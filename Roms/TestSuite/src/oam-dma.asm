; OAM DMA: the OAM block, CPU reads and fetches on the source bus, restarts, sources and other buses.

INCLUDE "hardware.inc"
INCLUDE "report.inc"

; HRAM locations, probe values and the WRAM and VRAM pages the cases use.
DEF hCode EQU $FF80 ; Routines copied to HRAM run here (up to $FFDF).
DEF hPage EQU $FFE0
DEF hTarget EQU $FFE1
DEF hValue EQU $FFE3
DEF hSecond EQU $FFE4
DEF hHramProbe EQU $FFF0
DEF hWriteProbe EQU $FFF8

DEF OAM_FILL EQU $E7
DEF ROM0_PROBE EQU $11
DEF ROMX_PROBE EQU $22
DEF VRAM_PROBE EQU $33
DEF WRAM_PROBE EQU $44
DEF HRAM_PROBE EQU $55
DEF TMA_PROBE EQU $66
DEF INC_C EQU $0C

DEF wPatternD1 EQU $D100
DEF wWramProbe EQU $D200
DEF wIncPage EQU $D300
DEF wSlideCode EQU $D400
DEF wPatternDD EQU $DD00
DEF wPatternDE EQU $DE00
DEF wPatternDF EQU $DF00 ; Low part of the stack page, which the shallow stack never reaches.
DEF vPattern80 EQU $8000
DEF vVramProbe EQU $8800
DEF vSlideCode EQU $8C00
DEF vIncPage EQU $8D00
DEF vPattern9F EQU $9F00

; Emits code that runs exactly \1 M-cycles, counting with B.
MACRO DELAY
    DEF DLY_LEFT = (\1)
    ASSERT DLY_LEFT >= 0, "negative delay"
    IF DLY_LEFT >= 5
        DEF DLY_N = (DLY_LEFT - 1) / 4
        ASSERT DLY_N <= 256, "delay too long"
        ld b, LOW(DLY_N)
:       dec b
        jr nz, :-
        DEF DLY_LEFT = DLY_LEFT - (4 * DLY_N + 1)
    ENDC
    REPT DLY_LEFT
        nop
    ENDR
ENDM

; Emits the 160 bytes 3*i+\1, all different within the page.
MACRO PATTERN
    FOR I, 160
        db LOW(3 * I + (\1))
    ENDR
ENDM

SECTION "ROM0 probe", ROM0[$3D00]
Rom0Probe:
    db ROM0_PROBE

SECTION "INC C page", ROM0[$3E00]
    ds 160, INC_C

SECTION "Pattern 3F", ROM0[$3F00]
    PATTERN $3F

SECTION "ROMX probe", ROM0[$5000]
RomxProbe:
    db ROMX_PROBE

SECTION "Pattern 7F", ROM0[$7F00]
    PATTERN $7F

SECTION "Routines", ROM0
; Copies the HRAM routine from HL (BC bytes) to hCode.
Install:
    ld de, hCode
    jp Copy

; Writes the pattern 3*i+C to the 160 bytes at HL.
WritePattern:
    ld b, 160
    ld a, c
.loop:
    ld [hl+], a
    add 3
    dec b
    jr nz, .loop
    ret

; Fills OAM with OAM_FILL (the LCD is off).
FillOam:
    ld hl, _OAMRAM
    ld bc, 160
    ld a, OAM_FILL
    jp Fill

; Emits the sum and XOR of the 160 OAM bytes, then OAM[0], OAM[1] and OAM[159].
EmitOamDigest:
    ld hl, _OAMRAM
    ld bc, 160 << 8
    ld d, c
.loop:
    ld a, [hl+]
    ld e, a
    add c
    ld c, a
    ld a, e
    xor d
    ld d, a
    dec b
    jr nz, .loop
    ld a, c
    call Emit
    ld a, d
    call Emit
    ld a, [_OAMRAM]
    call Emit
    ld a, [_OAMRAM + 1]
    call Emit
    ld a, [_OAMRAM + 159]
    jp Emit

; Writes the source patterns, the probe bytes and the two copies of the fetch slide.
Setup:
    ld hl, vPattern80
    ld c, $80
    call WritePattern
    ld hl, vPattern9F
    ld c, $9F
    call WritePattern
    ld hl, wPatternD1
    ld c, $D1
    call WritePattern
    ld hl, wPatternDD
    ld c, $DD
    call WritePattern
    ld hl, wPatternDE
    ld c, $DE
    call WritePattern
    ld hl, wPatternDF
    ld c, $DF
    call WritePattern
    ld hl, wIncPage
    ld bc, 160
    ld a, INC_C
    call Fill
    ld hl, vIncPage
    ld bc, 160
    call Fill
    ld a, VRAM_PROBE
    ld [vVramProbe], a
    ld a, WRAM_PROBE
    ld [wWramProbe], a
    ld a, HRAM_PROBE
    ldh [hHramProbe], a
    ld a, TMA_PROBE
    ldh [rTMA], a
    ld hl, FetchSlide
    ld de, vSlideCode
    ld bc, FetchSlideEnd - FetchSlide
    call Copy
    ld hl, FetchSlide
    ld de, wSlideCode
    ld bc, FetchSlideEnd - FetchSlide
    jp Copy

; Starts a transfer from page A and runs 176 NOPs, in which conflicting fetches run the source bytes.
FetchSlide:
    ldh [rDMA], a
    REPT 176
        nop
    ENDR
    ret
FetchSlideEnd:

; HRAM routine: transfer from hPage, read of hTarget on W+2+\1 into hValue, then a wait past the end.
MACRO PROBE_CODE
    ldh a, [hTarget]
    ld l, a
    ldh a, [hTarget + 1]
    ld h, a
    ldh a, [hPage]
    ldh [rDMA], a
    DELAY \1
    ld a, [hl]
    ldh [hValue], a
    ld a, 45
:   dec a
    jr nz, :-
    ret
ENDM

; HRAM routine: transfer from hPage, restart from hSecond on R=W+\1+4, read hTarget on R+2+\2, wait.
MACRO RESTART_CODE
    ldh a, [hTarget]
    ld l, a
    ldh a, [hTarget + 1]
    ld h, a
    ldh a, [hSecond]
    ld e, a
    ldh a, [hPage]
    ldh [rDMA], a
    DELAY \1
    ld a, e
    ldh [rDMA], a
    DELAY \2
    ld a, [hl]
    ldh [hValue], a
    ld a, 45
:   dec a
    jr nz, :-
    ret
ENDM

; HRAM routine: transfer from hPage, write of hSecond to hTarget on W+2+\1, wait.
MACRO WRITE_CODE
    ldh a, [hTarget]
    ld l, a
    ldh a, [hTarget + 1]
    ld h, a
    ldh a, [hSecond]
    ld c, a
    ldh a, [hPage]
    ldh [rDMA], a
    DELAY \1
    ld [hl], c
    ld a, 45
:   dec a
    jr nz, :-
    ret
ENDM

; Defines the HRAM routine \1 from the code macro \2 with the remaining arguments.
MACRO HRAM_ROUTINE
\1:
    DEF HR_NAME EQUS "\1"
    SHIFT
    \#
{HR_NAME}End:
    PURGE HR_NAME
ENDM

; The HRAM routine variants the cases install.
    HRAM_ROUTINE Probe0, PROBE_CODE 0
    HRAM_ROUTINE Probe1, PROBE_CODE 1
    HRAM_ROUTINE Probe10, PROBE_CODE 10
    HRAM_ROUTINE Probe158, PROBE_CODE 158
    HRAM_ROUTINE Probe159, PROBE_CODE 159
    HRAM_ROUTINE Probe160, PROBE_CODE 160
    HRAM_ROUTINE Probe161, PROBE_CODE 161
    HRAM_ROUTINE Restart0, RESTART_CODE 40, 0
    HRAM_ROUTINE Restart1, RESTART_CODE 40, 1
    HRAM_ROUTINE Restart158, RESTART_CODE 40, 158
    HRAM_ROUTINE Restart159, RESTART_CODE 40, 159
    HRAM_ROUTINE Restart160, RESTART_CODE 40, 160
    HRAM_ROUTINE Write10, WRITE_CODE 10
    HRAM_ROUTINE Write48, WRITE_CODE 48
    HRAM_ROUTINE Write159, WRITE_CODE 159
    HRAM_ROUTINE Write160, WRITE_CODE 160

; Copies the HRAM routine \1 to hCode.
MACRO INSTALL
    ld hl, \1
    ld bc, \1End - \1
    call Install
ENDM

; Runs the installed routine with source page \1, target \2 and second value \3.
MACRO RUN
    ld a, \1
    ldh [hPage], a
    ld a, LOW(\2)
    ldh [hTarget], a
    ld a, HIGH(\2)
    ldh [hTarget + 1], a
    ld a, \3
    ldh [hSecond], a
    call hCode
ENDM

; Runs the installed routine with source page \1, target \2 and second value \3, then emits hValue.
MACRO RUN_EMIT
    RUN \1, \2, \3
    ldh a, [hValue]
    call Emit
ENDM

SECTION "Main", ROM0
Main::
    INSTALL Probe0
    call SourceC0Case
    call Setup
    call SourceCases
    call ConflictCases
    call WindowCases
    call FetchCases
    call RestartCases
    call OamWriteCases
    call OtherBusWriteCases
    call RegisterCases
    ret

; Transfer from C000 before anything is emitted: the reporter's state and zeros reach OAM.
SourceC0Case:
    call FillOam
    RUN $C0, hValue, 0
    jp EmitOamDigest

; Transfer from page \1 over a filled OAM, then the OAM digest.
MACRO SOURCE_CASE
    call FillOam
    RUN \1, hValue, 0
    call EmitOamDigest
ENDM

; Digests for ROM (3F, 7F), VRAM (80, 9F), WRAM (D1), echo (F1, FD) and pages FE, FF (WRAM DE, DF).
SourceCases:
    SOURCE_CASE $3F
    SOURCE_CASE $7F
    SOURCE_CASE $80
    SOURCE_CASE $9F
    SOURCE_CASE $D1
    SOURCE_CASE $F1
    SOURCE_CASE $FD
    SOURCE_CASE $FE
    SOURCE_CASE $FF
    ret

; Reads on W+12 from ROM0, ROMX, VRAM, WRAM, echo, HRAM, TMA, OAM and FEA0 during a transfer from \1.
MACRO CONFLICT_ROW
    RUN_EMIT \1, Rom0Probe, 0
    RUN_EMIT \1, RomxProbe, 0
    RUN_EMIT \1, vVramProbe, 0
    RUN_EMIT \1, wWramProbe, 0
    RUN_EMIT \1, wWramProbe + $2000, 0
    RUN_EMIT \1, hHramProbe, 0
    RUN_EMIT \1, rTMA, 0
    RUN_EMIT \1, _OAMRAM + $10, 0
    RUN_EMIT \1, $FEA0, 0
ENDM

; The byte each bus gives the CPU while the transfer reads ROM, VRAM, WRAM or echo RAM.
ConflictCases:
    INSTALL Probe10
    CONFLICT_ROW $3F
    CONFLICT_ROW $7F
    CONFLICT_ROW $80
    CONFLICT_ROW $9F
    CONFLICT_ROW $D1
    CONFLICT_ROW $F1
    ret

; Reads of \2 on W+2, W+3 and W+160 to W+163 during a transfer from page \1.
MACRO WINDOW_ROW
    INSTALL Probe0
    RUN_EMIT \1, \2, 0
    INSTALL Probe1
    RUN_EMIT \1, \2, 0
    INSTALL Probe158
    RUN_EMIT \1, \2, 0
    INSTALL Probe159
    RUN_EMIT \1, \2, 0
    INSTALL Probe160
    RUN_EMIT \1, \2, 0
    INSTALL Probe161
    RUN_EMIT \1, \2, 0
ENDM

; Start and end of the OAM block (VRAM source), the main-bus conflict (WRAM) and the VRAM conflict.
WindowCases:
    call FillOam
    WINDOW_ROW $80, _OAMRAM
    WINDOW_ROW $D1, Rom0Probe
    WINDOW_ROW $80, vVramProbe
    ret

; Runs the fetch slide at \1 with a transfer from page \2 and emits the count of executed INC C bytes.
MACRO FETCH_CASE
    ld c, 0
    ld a, \2
    call \1
    ld a, c
    call Emit
ENDM

; Fetches from ROM, WRAM and VRAM during transfers from INC C pages in ROM (3E), WRAM (D3), VRAM (8D).
FetchCases:
    FETCH_CASE FetchSlide, HIGH($3E00)
    FETCH_CASE FetchSlide, HIGH(wIncPage)
    FETCH_CASE FetchSlide, HIGH(vIncPage)
    FETCH_CASE wSlideCode, HIGH($3E00)
    FETCH_CASE wSlideCode, HIGH(vIncPage)
    FETCH_CASE vSlideCode, HIGH(vIncPage)
    FETCH_CASE vSlideCode, HIGH($3E00)
    ret

; Restart 40 M-cycles in: OAM reads on R+2 and R+160 to R+162, digest, ROM0 reads on R+2 and R+3.
RestartCases:
    call FillOam
    INSTALL Restart0
    RUN_EMIT $80, _OAMRAM, $9F
    INSTALL Restart158
    RUN_EMIT $80, _OAMRAM, $9F
    INSTALL Restart159
    RUN_EMIT $80, _OAMRAM, $9F
    INSTALL Restart160
    RUN_EMIT $80, _OAMRAM, $9F
    call FillOam
    INSTALL Restart0
    RUN $80, hValue, $9F
    call EmitOamDigest
    RUN_EMIT $D1, Rom0Probe, $3F
    INSTALL Restart1
    RUN_EMIT $D1, Rom0Probe, $3F
    ret

; OAM writes on W+50 and W+161 are ignored, one on W+162 lands: OAM[10], OAM[20], OAM[20] after.
OamWriteCases:
    INSTALL Write48
    RUN $80, _OAMRAM + $10, $5A
    ld a, [_OAMRAM + $10]
    call Emit
    INSTALL Write159
    RUN $80, _OAMRAM + $20, $5A
    ld a, [_OAMRAM + $20]
    call Emit
    INSTALL Write160
    RUN $80, _OAMRAM + $20, $5A
    ld a, [_OAMRAM + $20]
    jp Emit

; Writes on W+12 to the bus the transfer does not use, to HRAM and to TMA, read back afterwards.
OtherBusWriteCases:
    INSTALL Write10
    RUN $80, wWramProbe, $77
    ld a, [wWramProbe]
    call Emit
    RUN $3F, vVramProbe, $88
    ld a, [vVramProbe]
    call Emit
    RUN $3F, hWriteProbe, $5A
    ldh a, [hWriteProbe]
    call Emit
    RUN $3F, rTMA, $C3
    ldh a, [rTMA]
    jp Emit

; The DMA register reads back the last page written; FEA0 and FEFF read outside a transfer.
RegisterCases:
    ldh a, [rDMA]
    call Emit
    ld a, [$FEA0]
    call Emit
    ld a, [$FEFF]
    jp Emit
