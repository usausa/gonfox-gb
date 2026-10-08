; Measures the DMG OAM corruption bug: 16-bit INC/DEC with the register in OAM, LD A,(HL+)/(HL-), PUSH and POP with SP in OAM, at each M-cycle across Mode 2 of line 1.

INCLUDE "hardware.inc"
INCLUDE "report.inc"

DEF OAM_SIZE EQU 160
DEF OAM_POINTER EQU $FE58
DEF SLED_SIZE EQU 128
DEF CASE_SP_IN_OAM EQU 1

; Lists the NOP delays \1 to \2.
MACRO delays
    FOR N, \1, \2 + 1
        db N
    ENDR
ENDM

SECTION "OAM bug RAM", WRAM0[$D100]
wStart: ds 8 ; LDH [rLCDC],A, LD SP,nn, then JP into the sled; SP moves into OAM only after the LCD is on.
wSled: ds SLED_SIZE ; NOPs.
wInstruction: ds 4 ; The instruction under test, then JP PositionDone.

SECTION "OAM bug HRAM", HRAM
hListPtr: ds 2
hFlags: ds 1
hDelay: ds 1
hSavedSp: ds 2

SECTION "Main", ROM0
; Writes the timed RAM code, then runs every case at each of its NOP delays.
Main::
    ld hl, wStart
    ld a, $E0
    ld [hl+], a
    ld a, LOW(rLCDC)
    ld [hl+], a
    ld a, $31
    ld [hl+], a
    inc hl
    inc hl
    ld a, $C3
    ld [hl+], a
    inc hl
    inc hl
    ld c, SLED_SIZE
    xor a
.sled:
    ld [hl+], a
    dec c
    jr nz, .sled
    inc hl
    ld a, $C3
    ld [hl+], a
    ld a, LOW(PositionDone)
    ld [hl+], a
    ld [hl], HIGH(PositionDone)
    ld hl, Cases
.case:
    ld a, [hl+]
    and a
    ret z
    ld [wInstruction], a
    ld a, [hl+]
    ldh [hFlags], a
.delay:
    ld a, [hl+]
    and a
    jr z, .case
    ldh [hDelay], a
    ld a, l
    ldh [hListPtr], a
    ld a, h
    ldh [hListPtr + 1], a
    call RunPosition
    ldh a, [hListPtr]
    ld l, a
    ldh a, [hListPtr + 1]
    ld h, a
    jr .delay

; Runs the instruction hDelay NOPs after enabling the LCD, then emits the CRC-16 of OAM read back in VBlank.
RunPosition:
    ld hl, OamPattern
    ld de, _OAMRAM
    REPT OAM_SIZE
        ld a, [hl+]
        ld [de], a
        inc e
    ENDR
    ldh a, [hDelay]
    cpl
    inc a
    add LOW(wInstruction)
    ld [wStart + 6], a
    ld a, HIGH(wInstruction)
    ld [wStart + 7], a
    ld [hSavedSp], sp
    ld [wStart + 3], sp
    ld bc, OAM_POINTER
    ld d, b
    ld e, c
    ld h, b
    ld l, c
    ldh a, [hFlags]
    and CASE_SP_IN_OAM
    jr z, .enable
    ld a, c
    ld [wStart + 3], a
    ld a, b
    ld [wStart + 4], a
.enable:
    ld a, LCDCF_ON
    jp wStart
PositionDone:
    ldh a, [hSavedSp]
    ld l, a
    ldh a, [hSavedSp + 1]
    ld h, a
    ld sp, hl
.vblank:
    ldh a, [rLY]
    cp 144
    jr nz, .vblank
    xor a
    ldh [rLCDC], a
    ld de, $FFFF
    ld bc, _OAMRAM
    ld h, HIGH(CrcTables)
    REPT OAM_SIZE
        ld a, [bc]
        inc c
        xor d
        ld l, a
        ld a, [hl]
        xor e
        ld d, a
        inc h
        ld e, [hl]
        dec h
    ENDR
    ld h, d
    ld l, e
    jp EmitWord

; Instructions under test: opcode, SP in OAM, then NOP delays ending with 0 (delay 104 is line 1 Dot 0).
Cases:
    db $03, 0 ; INC BC
    delays 104, 124
    db 0
    db $0B, 0 ; DEC BC
    delays 104, 124
    db 0
    db $13, 0 ; INC DE
    delays 104, 124
    db 0
    db $1B, 0 ; DEC DE
    delays 104, 124
    db 0
    db $23, 0 ; INC HL
    delays 104, 124
    db 0
    db $2B, 0 ; DEC HL
    delays 104, 124
    db 0
    db $33, CASE_SP_IN_OAM ; INC SP
    delays 104, 124
    db 0
    db $3B, CASE_SP_IN_OAM ; DEC SP
    delays 104, 124
    db 0
    db $2A, 0 ; LD A,(HL+)
    delays 103, 125
    db 0
    db $3A, 0 ; LD A,(HL-)
    delays 103, 125
    db 0
    db $C5, CASE_SP_IN_OAM ; PUSH BC
    delays 101, 124
    db 0
    db $C1, CASE_SP_IN_OAM ; POP BC
    delays 102, 125
    db 0
    db 0

; OAM fill pattern: the high bytes of a 16-bit linear congruential sequence.
OamPattern:
DEF X = $1234
REPT OAM_SIZE
    DEF X = (X * 25173 + 13849) & $FFFF
    db HIGH(X)
ENDR

; CRC-16 (polynomial $1021) lookup tables: 256 high bytes, then 256 low bytes.
SECTION "CRC-16 tables", ROM0, ALIGN[8]
CrcTables:
FOR I, 256
    DEF CRC = I << 8
    REPT 8
        DEF CRC = ((CRC << 1) ^ ((CRC >> 15) * $1021)) & $FFFF
    ENDR
    db HIGH(CRC)
ENDR
FOR I, 256
    DEF CRC = I << 8
    REPT 8
        DEF CRC = ((CRC << 1) ^ ((CRC >> 15) * $1021)) & $FFFF
    ENDR
    db LOW(CRC)
ENDR
