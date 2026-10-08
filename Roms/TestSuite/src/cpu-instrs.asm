; Runs every CPU instruction but HALT, STOP and undefined ones over fixed states; a CRC-16 per opcode.

INCLUDE "hardware.inc"
INCLUDE "report.inc"

DEF BASE_STATES EQU 32
DEF CB_STATES EQU 16
DEF STATE_SIZE EQU 16
DEF RESULT_BYTES EQU 12

DEF LEN_MASK EQU %11
DEF OPK_MASK EQU %11100
DEF OPK_N8 EQU 1 << 2
DEF OPK_N16 EQU 2 << 2
DEF OPK_JR EQU 3 << 2
DEF OPK_PAD16 EQU 4 << 2
DEF OPK_MEM16 EQU 5 << 2
DEF OPK_HRAM8 EQU 6 << 2
DEF OVR_MASK EQU %111
DEF OVR_HL_MEM EQU 1
DEF ATTR_SP EQU 3
DEF ATTR_PAD EQU 4

DEF JR_FORWARD EQU 4
DEF JR_BACKWARD EQU -15 & $FF

; Folds A into the CRC-16 in DE (D high) with the lookup tables at page H.
MACRO crc_step
    xor d
    ld l, a
    ld a, [hl]
    xor e
    ld d, a
    inc h
    ld e, [hl]
    dec h
ENDM

; Stores the registers and SP after the instruction, then hands the path code to CaptureCommon.
MACRO capture
    ld [hCap + 8], sp
    ld sp, hCap + 8
    push hl
    push de
    push bc
    push af
    ld a, \1
    jp CaptureCommon
ENDM

SECTION "Capture", HRAM[$FF80]
hCap: ds 10 ; F, A, C, B, E, D, L, H and SP after the instruction.
hTestMem: ds 4 ; Two test bytes (the path code XORed into the second) and the stack word; SP = hTestMem + 2.

SECTION "Harness HRAM", HRAM
hStatePtr: ds 2
hStatesLeft: ds 1
hStateCount: ds 1
hAttr0: ds 1
hAttr1: ds 1
hOpcode: ds 1
hCrc: ds 2
hHarnessSp: ds 2
hDaaA: ds 1
hDaaF: ds 1

SECTION "Exec", WRAM0[$D100]
wBackPad: ds 3 ; Target of backward relative jumps.
wExec: ds 16 ; POP AF/BC/DE/HL, a register override, LD SP,nn, the instruction at +10, then a jump to CaptureFall.
wPadTaken: ds 3 ; Target of taken jumps, calls and returns.

; RST vectors, each leading to its own capture entry.
FOR V, 0, 64, 8
SECTION "Rst {02X:V}", ROM0[V]
    jp CaptureRst{02X:V}
ENDR

SECTION "Main", ROM0
; Tests the base opcodes, then the CB opcodes, then DAA over every input.
Main::
    call InitExec
    ld a, BASE_STATES
    ldh [hStateCount], a
    xor a
    ldh [hOpcode], a
.base:
    ldh a, [hOpcode]
    ld l, a
    ld h, 0
    add hl, hl
    ld de, BaseAttributes
    add hl, de
    ld a, [hl+]
    ldh [hAttr0], a
    ld b, a
    ld a, [hl]
    ldh [hAttr1], a
    ld a, b
    and LEN_MASK
    jr z, .nextBase
    ldh a, [hOpcode]
    ld [wExec + 10], a
    call BuildExec
    call RunStates
.nextBase:
    ldh a, [hOpcode]
    inc a
    ldh [hOpcode], a
    jr nz, .base
    ld a, CB_STATES
    ldh [hStateCount], a
.cb:
    ld a, 2
    ldh [hAttr0], a
    ldh a, [hOpcode]
    and 7
    cp 6
    ld a, 0
    jr nz, .cbOverride
    ld a, OVR_HL_MEM
.cbOverride:
    ldh [hAttr1], a
    ld a, $CB
    ld [wExec + 10], a
    ldh a, [hOpcode]
    ld [wExec + 11], a
    call BuildExec
    call RunStates
    ldh a, [hOpcode]
    inc a
    ldh [hOpcode], a
    jr nz, .cb
    jp DaaSweep

; Writes the parts of the RAM code that every opcode shares.
InitExec:
    ld hl, wExec
    ld a, $F1
    ld [hl+], a
    ld a, $C1
    ld [hl+], a
    ld a, $D1
    ld [hl+], a
    ld a, $E1
    ld [hl+], a
    ld a, $31
    ld [wExec + 7], a
    ld hl, wPadTaken
    ld a, $C3
    ld [hl+], a
    ld a, LOW(CaptureTaken)
    ld [hl+], a
    ld a, HIGH(CaptureTaken)
    ld [hl], a
    ld hl, wBackPad
    ld a, $C3
    ld [hl+], a
    ld a, LOW(CaptureBack)
    ld [hl+], a
    ld a, HIGH(CaptureBack)
    ld [hl], a
    ret

; Writes the register override, test SP, fixed operands and fall-through jump at wExec + 10.
BuildExec:
    ldh a, [hAttr1]
    and OVR_MASK
    ld c, a
    add a
    add c
    ld e, a
    ld d, 0
    ld hl, Overrides
    add hl, de
    ld de, wExec + 4
    ld b, 3
.override:
    ld a, [hl+]
    ld [de], a
    inc de
    dec b
    jr nz, .override
    ld a, LOW(hTestMem + 2)
    ld [wExec + 8], a
    ld a, HIGH(hTestMem + 2)
    ld [wExec + 9], a
    ldh a, [hAttr0]
    and OPK_MASK
    ld hl, wPadTaken
    cp OPK_PAD16
    jr z, .address
    ld hl, hTestMem
    cp OPK_MEM16
    jr z, .address
    cp OPK_HRAM8
    jr nz, .fallJump
    ld a, l
    ld [wExec + 11], a
    jr .fallJump
.address:
    ld a, l
    ld [wExec + 11], a
    ld a, h
    ld [wExec + 12], a
.fallJump:
    ldh a, [hAttr0]
    and LEN_MASK
    add LOW(wExec + 10)
    ld l, a
    ld h, HIGH(wExec)
    ld a, $C3
    ld [hl+], a
    ld a, LOW(CaptureFall)
    ld [hl+], a
    ld a, HIGH(CaptureFall)
    ld [hl+], a
.clear:
    ld a, l
    cp LOW(wPadTaken)
    ret z
    xor a
    ld [hl+], a
    jr .clear

; Register overrides: none, HL, BC or DE at the test bytes, C at the test bytes, HL at the taken pad.
Overrides:
    db $00, $00, $00
    db $21, LOW(hTestMem), HIGH(hTestMem)
    db $01, LOW(hTestMem), HIGH(hTestMem)
    db $11, LOW(hTestMem), HIGH(hTestMem)
    db $0E, LOW(hTestMem), $00
    db $21, LOW(wPadTaken), HIGH(wPadTaken)

; Runs the first hStateCount input states; CaptureCommon loops and emits the CRC-16, low byte first.
RunStates:
    ld [hHarnessSp], sp
    ld a, $FF
    ldh [hCrc], a
    ldh [hCrc + 1], a
    ldh a, [hStateCount]
    ldh [hStatesLeft], a
    ld hl, States
.state:
    ld a, l
    ldh [hStatePtr], a
    ld a, h
    ldh [hStatePtr + 1], a
    ld a, [hl+]
    ld c, a
    ld a, [hl+]
    ld b, a
    ld a, [hl+]
    ldh [hTestMem], a
    ld a, [hl+]
    ldh [hTestMem + 1], a
    ld a, [hl+]
    ld d, a
    ld a, [hl+]
    ld e, a
    ld a, [hl+]
    ldh [hTestMem + 2], a
    ld a, [hl+]
    ldh [hTestMem + 3], a
    ldh a, [hAttr1]
    bit ATTR_SP, a
    jr z, .stackWord
    ld a, c
    ld [wExec + 8], a
    ld a, b
    ld [wExec + 9], a
    ldh a, [hAttr1]
.stackWord:
    bit ATTR_PAD, a
    jr z, .operand
    ld a, LOW(wPadTaken)
    ldh [hTestMem + 2], a
    ld a, HIGH(wPadTaken)
    ldh [hTestMem + 3], a
.operand:
    ldh a, [hAttr0]
    and OPK_MASK
    jr z, .go
    cp OPK_N8
    jr z, .n8
    cp OPK_N16
    jr z, .n16
    cp OPK_JR
    jr nz, .go
    ld a, l
    and STATE_SIZE
    ld a, JR_FORWARD
    jr z, .jr
    ld a, JR_BACKWARD
.jr:
    ld [wExec + 11], a
    jr .go
.n16:
    ld a, e
    ld [wExec + 12], a
.n8:
    ld a, d
    ld [wExec + 11], a
.go:
    ld sp, hl
    jp wExec

; Folds the results into a CRC-8 and that into the CRC-16, then runs the next state or emits.
CaptureCommon:
    di
    ld b, a
    ldh a, [hTestMem + 1]
    xor b
    ldh [hTestMem + 1], a
    ldh a, [hHarnessSp]
    ld l, a
    ldh a, [hHarnessSp + 1]
    ld h, a
    ld sp, hl
    ld bc, LOW(hCap)
    ld h, HIGH(Crc8Table)
    REPT RESULT_BYTES
        ldh a, [c]
        inc c
        xor b
        ld l, a
        ld b, [hl]
    ENDR
    ldh a, [hCrc]
    ld d, a
    ldh a, [hCrc + 1]
    ld e, a
    ld h, HIGH(CrcTables)
    ld a, b
    crc_step
    ld a, d
    ldh [hCrc], a
    ld a, e
    ldh [hCrc + 1], a
    ldh a, [hStatePtr]
    add STATE_SIZE
    ld l, a
    ldh a, [hStatePtr + 1]
    adc 0
    ld h, a
    ldh a, [hStatesLeft]
    dec a
    ldh [hStatesLeft], a
    jp nz, RunStates.state
    ld h, d
    ld l, e
    jp EmitWord

; Runs DAA on every A and flag combination; emits the CRC-16 of the results, low byte first.
DaaSweep:
    ld de, $FFFF
    ld bc, 0
.loop:
    push bc
    pop af
    daa
    ldh [hDaaA], a
    push af
    pop hl
    ld a, l
    ldh [hDaaF], a
    ld h, HIGH(CrcTables)
    ldh a, [hDaaA]
    crc_step
    ldh a, [hDaaF]
    crc_step
    ld a, c
    add $10
    ld c, a
    jr nc, .loop
    inc b
    jr nz, .loop
    ld h, d
    ld l, e
    jp EmitWord

SECTION "Capture entries", ROM0
; Capture entries for the paths: fall-through, taken, backward, then RST $00-$38.
CaptureFall:
    capture 0
CaptureTaken:
    capture 1
CaptureBack:
    capture 2
FOR V, 0, 64, 8
CaptureRst{02X:V}:
    capture 3 + V / 8
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

; CRC-8 (polynomial $07) lookup table.
SECTION "CRC-8 table", ROM0, ALIGN[8]
Crc8Table:
FOR I, 256
    DEF CRC = I
    REPT 8
        DEF CRC = ((CRC << 1) ^ ((CRC >> 7) * $07)) & $FF
    ENDR
    db CRC
ENDR

; Input states (16 bytes): SP, test bytes, operand bytes 1-2, stack word, then F, A, C, B, E, D, L, H.
SECTION "States", ROM0, ALIGN[8]
States:
    db $FF, $FF, $81, $62, $F0, $17, $B6, $10, $00, $00, $0F, $01, $1F, $10, $80, $7F
    db $00, $80, $80, $80, $C3, $69, $87, $73, $50, $01, $81, $1F, $A5, $3C, $10, $00
    db $CB, $ED, $7F, $61, $08, $D4, $3E, $C2, $A0, $0F, $5A, $81, $80, $01, $00, $C3
    db $00, $00, $1F, $93, $4B, $27, $C3, $C5, $F0, $10, $01, $3C, $F8, $F0, $A5, $80
    db $00, $F0, $10, $35, $1F, $78, $65, $15, $40, $7F, $80, $A5, $F0, $F8, $3C, $01
    db $34, $12, $0F, $C8, $81, $86, $7A, $17, $90, $80, $C3, $00, $01, $80, $81, $5A
    db $01, $00, $01, $20, $3C, $AA, $32, $A5, $E0, $FF, $00, $10, $3C, $A5, $1F, $81
    db $FF, $0F, $00, $21, $F1, $5B, $69, $12, $30, $99, $7F, $80, $10, $1F, $01, $0F
    db $08, $00, $F8, $87, $00, $5B, $2B, $F6, $80, $9A, $3C, $FF, $5A, $C3, $08, $A5
    db $FE, $FF, $08, $E1, $10, $E9, $DB, $AA, $D0, $A0, $F8, $5A, $7F, $0F, $C3, $F0
    db $00, $FF, $A5, $AC, $80, $8E, $5E, $C0, $20, $09, $1F, $F8, $08, $FF, $F0, $10
    db $F8, $FF, $5A, $78, $DF, $19, $1D, $24, $70, $0A, $FF, $0F, $81, $00, $7F, $08
    db $00, $0F, $C3, $77, $5A, $3E, $54, $08, $C0, $90, $08, $7F, $00, $81, $0F, $FF
    db $FF, $00, $3C, $B7, $F8, $D9, $C1, $E5, $10, $66, $10, $F0, $FF, $08, $F8, $1F
    db $FF, $7F, $FF, $3D, $0F, $99, $66, $8D, $60, $06, $F0, $C3, $0F, $7F, $5A, $F8
    db $F0, $00, $F0, $25, $5F, $85, $52, $02, $B0, $60, $A5, $08, $C3, $5A, $FF, $3C
    db $FF, $FF, $81, $88, $F0, $01, $30, $9A, $30, $F0, $0F, $01, $1F, $10, $80, $7F
    db $00, $80, $80, $FB, $C3, $75, $BA, $65, $A0, $1F, $81, $1F, $A5, $3C, $10, $00
    db $CB, $ED, $7F, $F1, $08, $11, $C5, $C4, $10, $3C, $5A, $81, $80, $01, $00, $C3
    db $00, $00, $1F, $87, $39, $D6, $02, $05, $80, $C3, $01, $3C, $F8, $F0, $A5, $80
    db $00, $F0, $10, $DC, $1F, $4C, $0B, $EC, $F0, $55, $80, $A5, $F0, $F8, $3C, $01
    db $34, $12, $0F, $94, $81, $4D, $EE, $C1, $60, $AA, $C3, $00, $01, $80, $81, $5A
    db $01, $00, $01, $82, $3C, $71, $14, $9C, $D0, $81, $00, $10, $3C, $A5, $1F, $81
    db $FF, $0F, $00, $E7, $31, $3E, $6F, $83, $40, $FE, $7F, $80, $10, $1F, $01, $0F
    db $08, $00, $F8, $0E, $00, $40, $17, $AE, $B0, $19, $3C, $FF, $5A, $C3, $08, $A5
    db $FE, $FF, $08, $81, $10, $7F, $25, $37, $20, $91, $F8, $5A, $7F, $0F, $C3, $F0
    db $00, $FF, $A5, $A5, $80, $2B, $69, $00, $90, $F9, $1F, $F8, $08, $FF, $F0, $10
    db $F8, $FF, $5A, $A6, $3D, $42, $56, $E9, $00, $FA, $FF, $0F, $81, $00, $7F, $08
    db $00, $0F, $C3, $30, $5A, $A8, $11, $F9, $70, $E8, $08, $7F, $00, $81, $0F, $FF
    db $FF, $00, $3C, $7F, $F8, $F6, $6F, $35, $E0, $18, $10, $F0, $FF, $08, $F8, $1F
    db $FF, $7F, $FF, $E1, $0F, $78, $B1, $3A, $50, $42, $F0, $C3, $0F, $7F, $5A, $F8
    db $F0, $00, $F0, $47, $B9, $73, $11, $91, $C0, $BD, $A5, $08, $C3, $5A, $FF, $3C

; Two bytes per base opcode: length, kind (2-4); override (0-2), SP from state (3), taken pad (4).
SECTION "Base attributes", ROM0
BaseAttributes:
    db $01, $00 ; $00 NOP
    db $0B, $00 ; $01 LD BC,n16
    db $01, $02 ; $02 LD (BC),A
    db $01, $00 ; $03 INC BC
    db $01, $00 ; $04 INC B
    db $01, $00 ; $05 DEC B
    db $06, $00 ; $06 LD B,n8
    db $01, $00 ; $07 RLCA
    db $17, $08 ; $08 LD (a16),SP
    db $01, $00 ; $09 ADD HL,BC
    db $01, $02 ; $0A LD A,(BC)
    db $01, $00 ; $0B DEC BC
    db $01, $00 ; $0C INC C
    db $01, $00 ; $0D DEC C
    db $06, $00 ; $0E LD C,n8
    db $01, $00 ; $0F RRCA
    db 0, 0 ; $10 not tested
    db $0B, $00 ; $11 LD DE,n16
    db $01, $03 ; $12 LD (DE),A
    db $01, $00 ; $13 INC DE
    db $01, $00 ; $14 INC D
    db $01, $00 ; $15 DEC D
    db $06, $00 ; $16 LD D,n8
    db $01, $00 ; $17 RLA
    db $0E, $00 ; $18 JR e8
    db $01, $00 ; $19 ADD HL,DE
    db $01, $03 ; $1A LD A,(DE)
    db $01, $00 ; $1B DEC DE
    db $01, $00 ; $1C INC E
    db $01, $00 ; $1D DEC E
    db $06, $00 ; $1E LD E,n8
    db $01, $00 ; $1F RRA
    db $0E, $00 ; $20 JR NZ,e8
    db $0B, $00 ; $21 LD HL,n16
    db $01, $01 ; $22 LD (HL+),A
    db $01, $00 ; $23 INC HL
    db $01, $00 ; $24 INC H
    db $01, $00 ; $25 DEC H
    db $06, $00 ; $26 LD H,n8
    db $01, $00 ; $27 DAA
    db $0E, $00 ; $28 JR Z,e8
    db $01, $00 ; $29 ADD HL,HL
    db $01, $01 ; $2A LD A,(HL+)
    db $01, $00 ; $2B DEC HL
    db $01, $00 ; $2C INC L
    db $01, $00 ; $2D DEC L
    db $06, $00 ; $2E LD L,n8
    db $01, $00 ; $2F CPL
    db $0E, $00 ; $30 JR NC,e8
    db $0B, $00 ; $31 LD SP,n16
    db $01, $01 ; $32 LD (HL-),A
    db $01, $08 ; $33 INC SP
    db $01, $01 ; $34 INC (HL)
    db $01, $01 ; $35 DEC (HL)
    db $06, $01 ; $36 LD (HL),n8
    db $01, $00 ; $37 SCF
    db $0E, $00 ; $38 JR C,e8
    db $01, $08 ; $39 ADD HL,SP
    db $01, $01 ; $3A LD A,(HL-)
    db $01, $08 ; $3B DEC SP
    db $01, $00 ; $3C INC A
    db $01, $00 ; $3D DEC A
    db $06, $00 ; $3E LD A,n8
    db $01, $00 ; $3F CCF
    db $01, $00 ; $40 LD B,B
    db $01, $00 ; $41 LD B,C
    db $01, $00 ; $42 LD B,D
    db $01, $00 ; $43 LD B,E
    db $01, $00 ; $44 LD B,H
    db $01, $00 ; $45 LD B,L
    db $01, $01 ; $46 LD B,(HL)
    db $01, $00 ; $47 LD B,A
    db $01, $00 ; $48 LD C,B
    db $01, $00 ; $49 LD C,C
    db $01, $00 ; $4A LD C,D
    db $01, $00 ; $4B LD C,E
    db $01, $00 ; $4C LD C,H
    db $01, $00 ; $4D LD C,L
    db $01, $01 ; $4E LD C,(HL)
    db $01, $00 ; $4F LD C,A
    db $01, $00 ; $50 LD D,B
    db $01, $00 ; $51 LD D,C
    db $01, $00 ; $52 LD D,D
    db $01, $00 ; $53 LD D,E
    db $01, $00 ; $54 LD D,H
    db $01, $00 ; $55 LD D,L
    db $01, $01 ; $56 LD D,(HL)
    db $01, $00 ; $57 LD D,A
    db $01, $00 ; $58 LD E,B
    db $01, $00 ; $59 LD E,C
    db $01, $00 ; $5A LD E,D
    db $01, $00 ; $5B LD E,E
    db $01, $00 ; $5C LD E,H
    db $01, $00 ; $5D LD E,L
    db $01, $01 ; $5E LD E,(HL)
    db $01, $00 ; $5F LD E,A
    db $01, $00 ; $60 LD H,B
    db $01, $00 ; $61 LD H,C
    db $01, $00 ; $62 LD H,D
    db $01, $00 ; $63 LD H,E
    db $01, $00 ; $64 LD H,H
    db $01, $00 ; $65 LD H,L
    db $01, $01 ; $66 LD H,(HL)
    db $01, $00 ; $67 LD H,A
    db $01, $00 ; $68 LD L,B
    db $01, $00 ; $69 LD L,C
    db $01, $00 ; $6A LD L,D
    db $01, $00 ; $6B LD L,E
    db $01, $00 ; $6C LD L,H
    db $01, $00 ; $6D LD L,L
    db $01, $01 ; $6E LD L,(HL)
    db $01, $00 ; $6F LD L,A
    db $01, $01 ; $70 LD (HL),B
    db $01, $01 ; $71 LD (HL),C
    db $01, $01 ; $72 LD (HL),D
    db $01, $01 ; $73 LD (HL),E
    db $01, $01 ; $74 LD (HL),H
    db $01, $01 ; $75 LD (HL),L
    db 0, 0 ; $76 not tested
    db $01, $01 ; $77 LD (HL),A
    db $01, $00 ; $78 LD A,B
    db $01, $00 ; $79 LD A,C
    db $01, $00 ; $7A LD A,D
    db $01, $00 ; $7B LD A,E
    db $01, $00 ; $7C LD A,H
    db $01, $00 ; $7D LD A,L
    db $01, $01 ; $7E LD A,(HL)
    db $01, $00 ; $7F LD A,A
    db $01, $00 ; $80 ADD A,B
    db $01, $00 ; $81 ADD A,C
    db $01, $00 ; $82 ADD A,D
    db $01, $00 ; $83 ADD A,E
    db $01, $00 ; $84 ADD A,H
    db $01, $00 ; $85 ADD A,L
    db $01, $01 ; $86 ADD A,(HL)
    db $01, $00 ; $87 ADD A,A
    db $01, $00 ; $88 ADC A,B
    db $01, $00 ; $89 ADC A,C
    db $01, $00 ; $8A ADC A,D
    db $01, $00 ; $8B ADC A,E
    db $01, $00 ; $8C ADC A,H
    db $01, $00 ; $8D ADC A,L
    db $01, $01 ; $8E ADC A,(HL)
    db $01, $00 ; $8F ADC A,A
    db $01, $00 ; $90 SUB B
    db $01, $00 ; $91 SUB C
    db $01, $00 ; $92 SUB D
    db $01, $00 ; $93 SUB E
    db $01, $00 ; $94 SUB H
    db $01, $00 ; $95 SUB L
    db $01, $01 ; $96 SUB (HL)
    db $01, $00 ; $97 SUB A
    db $01, $00 ; $98 SBC A,B
    db $01, $00 ; $99 SBC A,C
    db $01, $00 ; $9A SBC A,D
    db $01, $00 ; $9B SBC A,E
    db $01, $00 ; $9C SBC A,H
    db $01, $00 ; $9D SBC A,L
    db $01, $01 ; $9E SBC A,(HL)
    db $01, $00 ; $9F SBC A,A
    db $01, $00 ; $A0 AND B
    db $01, $00 ; $A1 AND C
    db $01, $00 ; $A2 AND D
    db $01, $00 ; $A3 AND E
    db $01, $00 ; $A4 AND H
    db $01, $00 ; $A5 AND L
    db $01, $01 ; $A6 AND (HL)
    db $01, $00 ; $A7 AND A
    db $01, $00 ; $A8 XOR B
    db $01, $00 ; $A9 XOR C
    db $01, $00 ; $AA XOR D
    db $01, $00 ; $AB XOR E
    db $01, $00 ; $AC XOR H
    db $01, $00 ; $AD XOR L
    db $01, $01 ; $AE XOR (HL)
    db $01, $00 ; $AF XOR A
    db $01, $00 ; $B0 OR B
    db $01, $00 ; $B1 OR C
    db $01, $00 ; $B2 OR D
    db $01, $00 ; $B3 OR E
    db $01, $00 ; $B4 OR H
    db $01, $00 ; $B5 OR L
    db $01, $01 ; $B6 OR (HL)
    db $01, $00 ; $B7 OR A
    db $01, $00 ; $B8 CP B
    db $01, $00 ; $B9 CP C
    db $01, $00 ; $BA CP D
    db $01, $00 ; $BB CP E
    db $01, $00 ; $BC CP H
    db $01, $00 ; $BD CP L
    db $01, $01 ; $BE CP (HL)
    db $01, $00 ; $BF CP A
    db $01, $10 ; $C0 RET NZ
    db $01, $00 ; $C1 POP BC
    db $13, $00 ; $C2 JP NZ,a16
    db $13, $00 ; $C3 JP a16
    db $13, $00 ; $C4 CALL NZ,a16
    db $01, $00 ; $C5 PUSH BC
    db $06, $00 ; $C6 ADD A,n8
    db $01, $00 ; $C7 RST $00
    db $01, $10 ; $C8 RET Z
    db $01, $10 ; $C9 RET
    db $13, $00 ; $CA JP Z,a16
    db 0, 0 ; $CB not tested
    db $13, $00 ; $CC CALL Z,a16
    db $13, $00 ; $CD CALL a16
    db $06, $00 ; $CE ADC A,n8
    db $01, $00 ; $CF RST $08
    db $01, $10 ; $D0 RET NC
    db $01, $00 ; $D1 POP DE
    db $13, $00 ; $D2 JP NC,a16
    db 0, 0 ; $D3 not tested
    db $13, $00 ; $D4 CALL NC,a16
    db $01, $00 ; $D5 PUSH DE
    db $06, $00 ; $D6 SUB n8
    db $01, $00 ; $D7 RST $10
    db $01, $10 ; $D8 RET C
    db $01, $10 ; $D9 RETI
    db $13, $00 ; $DA JP C,a16
    db 0, 0 ; $DB not tested
    db $13, $00 ; $DC CALL C,a16
    db 0, 0 ; $DD not tested
    db $06, $00 ; $DE SBC A,n8
    db $01, $00 ; $DF RST $18
    db $1A, $00 ; $E0 LDH (a8),A
    db $01, $00 ; $E1 POP HL
    db $01, $04 ; $E2 LD (C),A
    db 0, 0 ; $E3 not tested
    db 0, 0 ; $E4 not tested
    db $01, $00 ; $E5 PUSH HL
    db $06, $00 ; $E6 AND n8
    db $01, $00 ; $E7 RST $20
    db $06, $08 ; $E8 ADD SP,e8
    db $01, $05 ; $E9 JP HL
    db $17, $00 ; $EA LD (a16),A
    db 0, 0 ; $EB not tested
    db 0, 0 ; $EC not tested
    db 0, 0 ; $ED not tested
    db $06, $00 ; $EE XOR n8
    db $01, $00 ; $EF RST $28
    db $1A, $00 ; $F0 LDH A,(a8)
    db $01, $00 ; $F1 POP AF
    db $01, $04 ; $F2 LD A,(C)
    db $01, $00 ; $F3 DI
    db 0, 0 ; $F4 not tested
    db $01, $00 ; $F5 PUSH AF
    db $06, $00 ; $F6 OR n8
    db $01, $00 ; $F7 RST $30
    db $06, $08 ; $F8 LD HL,SP+e8
    db $01, $00 ; $F9 LD SP,HL
    db $17, $00 ; $FA LD A,(a16)
    db $01, $00 ; $FB EI
    db 0, 0 ; $FC not tested
    db 0, 0 ; $FD not tested
    db $06, $00 ; $FE CP n8
    db $01, $00 ; $FF RST $38
