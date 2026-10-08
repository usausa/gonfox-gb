; Finds the M-cycle of each data access of the memory instructions by aiming it at TIMA, or at IF, across one timer step.

INCLUDE "hardware.inc"
INCLUDE "report.inc"

DEF SWEEP EQU 16 ; Timer step positions tried per case, one M-cycle apart.
DEF FLAGS_IMAGE EQU $DD90 ; F then A, loaded by POP AF at the start of a run.
DEF POST_STACK EQU $DD80 ; Stack top for Post.
DEF QUIET_STACK EQU $DD00 ; SP of the cases that do not aim the stack at the timer.

DEF OBS_A EQU 0 ; Observation: A after the run.
DEF OBS_F EQU 1 ; Observation: F after the run.
DEF OBS_B EQU 2 ; Observation: B after the run.
DEF OBS_C EQU 3 ; Observation: C after the run.
DEF OBS_D EQU 4 ; Observation: D after the run (the RET landing code counts in D).
DEF OBS_E EQU 5 ; Observation: E after the run.
DEF OBS_H EQU 6 ; Observation: H after the run.
DEF OBS_L EQU 7 ; Observation: L after the run.
DEF OBS_TIMA EQU 8 ; Observation: TIMA read after the run.
DEF OBS_IF EQU 9 ; Observation: IF read after the run.

DEF KIND_READ EQU 0 ; Result: the cycle of a read of TIMA.
DEF KIND_WRITE EQU 1 ; Result: the cycle of a write to TIMA.
DEF KIND_MODIFY EQU 2 ; Results: the cycles of the read and of the write of TIMA.
DEF KIND_IF EQU 3 ; Result: the cycle of a write to IF, against the IF bit that a TIMA overflow sets.

DEF SP_LOW EQU $FF05 ; POP/RET: the low byte comes from TIMA.
DEF SP_HIGH EQU $FF04 ; POP/RET: the high byte comes from TIMA.
DEF SP_PUSH_HIGH EQU $FF06 ; PUSH/CALL/RST: the high byte goes to TIMA (the low byte to DIV).
DEF SP_PUSH_LOW EQU $FF07 ; PUSH/CALL/RST: the low byte goes to TIMA (the high byte to TMA).
DEF READ_START EQU $20 ; TIMA before the step in the read cases.
DEF MODIFY_START EQU $41 ; TIMA before the step in the read-modify-write cases (odd, so every result differs).
DEF VALUE EQU $40 ; Value written to TIMA by the stores.
DEF RET_PAGE EQU $D4 ; TIMA before the step when RET takes its high byte from TIMA.
DEF PAD_PAGE EQU $D6 ; TMA when RET takes its low byte from TIMA.
DEF PAD_LOW EQU $10 ; TIMA before the step when RET takes its low byte from TIMA.
DEF FZ EQU $80 ; Zero flag.
DEF FC EQU $10 ; Carry flag.

; Case entry: length, opcode bytes (3), AF, BC, DE, HL, SP, TIMA start, TMA, observation, kind.
MACRO Case
    db \1, \2, \3, \4
    dw \5, \6, \7, \8, \9
    SHIFT 9
    db \1, \2, \3, \4
ENDM

SECTION "Variables", WRAM0[$D010]
wSavedSp: ds 2 ; SP of the caller of RunOnce.
wLength: ds 1 ; Instruction length of the case.
wCaseTima: ds 1 ; TIMA value before the timer step.
wCaseTma: ds 1 ; TMA during the case.
wObserveIndex: ds 1 ; Which saved value the case looks at.
wKind: ds 1 ; KIND_ of the case.
wPosition: ds 1 ; Sweep position of the current run.
wOffsetRead: ds 1 ; Run length minus cycle for reads of TIMA.
wOffsetWrite: ds 1 ; Run length minus cycle for writes to TIMA.
wOffsetIf: ds 1 ; Run length minus cycle for writes to IF.
wObserved: ds 10 ; A, F, B, C, D, E, H, L, TIMA and IF after a run.
wProfile: ds SWEEP ; The observation at each sweep position.

; Each RST vector ends the run like the code after the instruction.
SECTION "Rst 00", ROM0[$00]
    jp Post
SECTION "Rst 08", ROM0[$08]
    jp Post
SECTION "Rst 10", ROM0[$10]
    jp Post
SECTION "Rst 18", ROM0[$18]
    jp Post
SECTION "Rst 20", ROM0[$20]
    jp Post
SECTION "Rst 28", ROM0[$28]
    jp Post
SECTION "Rst 30", ROM0[$30]
    jp Post
SECTION "Rst 38", ROM0[$38]
    jp Post

SECTION "Run template", ROM0
; Run code: loads the registers, runs 0-15 NOPs and the instruction, then ends in Post.
RunTemplate:
LOAD "Run code", WRAM0[$D200]
RunCode:
    ld sp, FLAGS_IMAGE
    pop af
RunSp:
    ld sp, 0
RunBc:
    ld bc, 0
RunDe:
    ld de, 0
RunHl:
    ld hl, 0
RunSledJump:
    jp RunSled
RunSled:
    ds SWEEP - 1, 0
RunInstruction:
    ds 3 + 3, 0
ENDL
RunTemplateEnd:

SECTION "Pad template", ROM0
; Landing code for RET with SP at SP_HIGH: page RET_PAGE adds 2 or 1 to D, page RET_PAGE + 1 leaves D alone.
PadHighTemplate:
LOAD "Pad high", WRAM0[RET_PAGE << 8]
    inc d
    inc d
    jp Post
ENDL
PadHighNextTemplate:
LOAD "Pad high next", WRAM0[(RET_PAGE + 1) << 8]
    nop
    nop
    jp Post
ENDL
; Landing code for RET with SP at SP_LOW: PAD_LOW adds 1 to D, PAD_LOW + 1 leaves D alone.
PadLowTemplate:
LOAD "Pad low", WRAM0[(PAD_PAGE << 8) | PAD_LOW]
    inc d
    jp Post
ENDL
PadTemplateEnd:

SECTION "Main", ROM0
; Copies the run and landing code, calibrates on LD A,(HL) and on LD (HL),A to TIMA and to IF, then measures every case.
Main::
    ld a, $07
    ldh [rTAC], a
    ld hl, RunTemplate
    ld de, RunCode
    ld bc, RunTemplateEnd - RunTemplate
    call Copy
    ld hl, PadHighTemplate
    ld de, RET_PAGE << 8
    ld bc, PadHighNextTemplate - PadHighTemplate
    call Copy
    ld hl, PadHighNextTemplate
    ld de, (RET_PAGE + 1) << 8
    ld bc, PadLowTemplate - PadHighNextTemplate
    call Copy
    ld hl, PadLowTemplate
    ld de, (PAD_PAGE << 8) | PAD_LOW
    ld bc, PadTemplateEnd - PadLowTemplate
    call Copy
    ld hl, CalibrationRead
    call Measure
    ld a, b
    sub 2
    ld [wOffsetRead], a
    ld hl, CalibrationWrite
    call Measure
    ld a, b
    sub 2
    ld [wOffsetWrite], a
    ld hl, CalibrationIf
    call Measure
    ld a, b
    sub 2
    ld [wOffsetIf], a
    ld hl, Cases
.case:
    ld a, [hl]
    and a
    ret z
    call Measure
    push hl
    call EmitCase
    pop hl
    jr .case

; Prepares the case at HL, sweeps it and counts its runs into B and C; returns HL after the entry.
Measure:
    call Prepare
    push hl
    call Sweep
    call CountRuns
    pop hl
    ret

; Emits the access cycles of the measured case from B and C as its kind decides.
EmitCase:
    ld a, [wKind]
    cp KIND_WRITE
    jr z, .write
    cp KIND_IF
    jr z, .if
    ld a, [wOffsetRead]
    ld d, a
    ld a, b
    sub d
    call Emit
    ld a, [wKind]
    cp KIND_MODIFY
    ret nz
    ld a, [wOffsetWrite]
    ld d, a
    ld a, c
    sub d
    jp Emit
.write:
    ld a, [wOffsetWrite]
    ld d, a
    ld a, b
    sub d
    jp Emit
.if:
    ld a, [wOffsetIf]
    ld d, a
    ld a, b
    sub d
    jp Emit

; Loads the case at HL into the run code and the case variables; returns HL after the entry.
Prepare:
    ld a, [hl+]
    ld [wLength], a
    ld de, RunInstruction
    ld a, [hl+]
    ld [de], a
    inc de
    ld a, [hl+]
    ld [de], a
    inc de
    ld a, [hl+]
    ld [de], a
    push hl
    ld a, [wLength]
    add LOW(RunInstruction)
    ld l, a
    ld h, HIGH(RunInstruction)
    ld a, $C3
    ld [hl+], a
    ld a, LOW(Post)
    ld [hl+], a
    ld [hl], HIGH(Post)
    pop hl
    ld a, [hl+]
    ld [FLAGS_IMAGE], a
    ld a, [hl+]
    ld [FLAGS_IMAGE + 1], a
    ld de, RunBc + 1
    call CopyWord
    ld de, RunDe + 1
    call CopyWord
    ld de, RunHl + 1
    call CopyWord
    ld de, RunSp + 1
    call CopyWord
    ld a, [hl+]
    ld [wCaseTima], a
    ld a, [hl+]
    ld [wCaseTma], a
    ld a, [hl+]
    ld [wObserveIndex], a
    ld a, [hl+]
    ld [wKind], a
    ret

; Copies two bytes from HL to DE, advancing HL.
CopyWord:
    ld a, [hl+]
    ld [de], a
    inc de
    ld a, [hl+]
    ld [de], a
    ret

; Runs the case at every sweep position and keeps the chosen observation of each run in wProfile.
Sweep:
    xor a
.run:
    ld [wPosition], a
    call RunOnce
    ld a, [wObserveIndex]
    ld e, a
    ld d, 0
    ld hl, wObserved
    add hl, de
    ld b, [hl]
    ld a, [wPosition]
    ld e, a
    ld hl, wProfile
    add hl, de
    ld [hl], b
    ld a, [wPosition]
    inc a
    cp SWEEP
    jr nz, .run
    ret

; Counts equal observations from the last sweep position down into B, and B plus the next run of equal ones into C.
CountRuns:
    ld hl, wProfile + SWEEP - 1
    ld d, [hl]
    ld b, 0
.first:
    ld a, [hl]
    cp d
    jr nz, .firstEnd
    inc b
    dec hl
    ld a, b
    cp SWEEP
    jr nz, .first
.firstEnd:
    ld c, b
    ld a, b
    cp SWEEP
    ret z
    ld d, [hl]
.second:
    ld a, [hl]
    cp d
    ret nz
    inc c
    dec hl
    ld a, c
    cp SWEEP
    jr nz, .second
    ret

; Runs the prepared case once with the timer step A M-cycles earlier relative to the instruction (0-15).
RunOnce:
    ld b, a
    ld a, LOW(RunSled + SWEEP - 1)
    sub b
    ld [RunSledJump + 1], a
    ld [wSavedSp], sp
    ldh [rDIV], a
    xor a
    ldh [rTIMA], a
    ldh [rIF], a
    ld a, [wCaseTma]
    ldh [rTMA], a
    ld a, [wCaseTima]
    ldh [rDIV], a
    ldh [rTIMA], a
    ld b, 5
.wait:
    dec b
    jr nz, .wait
    jp RunCode

; Saves the registers, TIMA and IF after the instruction, restores the caller's SP and returns to the caller of RunOnce.
Post:
    ld sp, POST_STACK
    push af
    ldh a, [rTIMA]
    ld [wObserved + OBS_TIMA], a
    ldh a, [rIF]
    ld [wObserved + OBS_IF], a
    ld a, b
    ld [wObserved + OBS_B], a
    ld a, c
    ld [wObserved + OBS_C], a
    ld a, d
    ld [wObserved + OBS_D], a
    ld a, e
    ld [wObserved + OBS_E], a
    ld a, h
    ld [wObserved + OBS_H], a
    ld a, l
    ld [wObserved + OBS_L], a
    pop hl
    ld a, h
    ld [wObserved + OBS_A], a
    ld a, l
    ld [wObserved + OBS_F], a
    ld hl, wSavedSp
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    ld sp, hl
    di
    ret

SECTION "Cases", ROM0
; Calibration: LD A,(HL) reads in M-cycle 2.
CalibrationRead:
    Case 1, $7E, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
; Calibration: LD (HL),A writes TIMA in M-cycle 2.
CalibrationWrite:
    Case 1, $77, 0, 0, VALUE << 8, 0, 0, rTIMA, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
; Calibration: LD (HL),A clears IF in M-cycle 2, racing the IF bit of a TIMA overflow.
CalibrationIf:
    Case 1, $77, 0, 0, $0000, 0, 0, rIF, QUIET_STACK, $FF, 0, OBS_IF, KIND_IF

; Measured cases in result order.
Cases:
; LD (BC),A, then LD (a16),SP with the low byte of SP to TIMA and with the high byte ($D0) to IF, then LD A,(BC).
    Case 1, $02, 0, 0, VALUE << 8, rTIMA, 0, 0, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 3, $08, LOW(rTIMA), HIGH(rTIMA), $0000, 0, 0, 0, $D020, 0, 0, OBS_TIMA, KIND_WRITE
    Case 3, $08, LOW(rIF - 1), HIGH(rIF - 1), $0000, 0, 0, 0, $D020, $FF, 0, OBS_IF, KIND_IF
    Case 1, $0A, 0, 0, $0000, rTIMA, 0, 0, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
; LD (DE),A and LD A,(DE).
    Case 1, $12, 0, 0, VALUE << 8, 0, rTIMA, 0, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $1A, 0, 0, $0000, 0, rTIMA, 0, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
; LD (HL+),A and LD A,(HL+).
    Case 1, $22, 0, 0, VALUE << 8, 0, 0, rTIMA, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $2A, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
; LD (HL-),A, INC (HL), DEC (HL), LD (HL),n and LD A,(HL-).
    Case 1, $32, 0, 0, VALUE << 8, 0, 0, rTIMA, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $34, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, MODIFY_START, 0, OBS_TIMA, KIND_MODIFY
    Case 1, $35, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, MODIFY_START, 0, OBS_TIMA, KIND_MODIFY
    Case 2, $36, VALUE, 0, $0000, 0, 0, rTIMA, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $3A, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
; LD r,(HL) for B, C, D, E, H and L: the byte read lands in r.
    Case 1, $46, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_B, KIND_READ
    Case 1, $4E, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_C, KIND_READ
    Case 1, $56, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_D, KIND_READ
    Case 1, $5E, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_E, KIND_READ
    Case 1, $66, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_H, KIND_READ
    Case 1, $6E, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_L, KIND_READ
; LD (HL),r for B, C, D, E, H ($FF, which overflows TIMA at the step) and L ($05).
    Case 1, $70, 0, 0, $0000, VALUE << 8, 0, rTIMA, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $71, 0, 0, $0000, VALUE, 0, rTIMA, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $72, 0, 0, $0000, 0, VALUE << 8, rTIMA, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $73, 0, 0, $0000, 0, VALUE, rTIMA, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $74, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $75, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
; LD (HL),A and LD A,(HL).
    Case 1, $77, 0, 0, VALUE << 8, 0, 0, rTIMA, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $7E, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
; ADD, ADC, SUB, SBC, AND, XOR and OR A,(HL) with A chosen so that A shows the byte read; CP (HL) shows it in F.
    Case 1, $86, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
    Case 1, $8E, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
    Case 1, $96, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
    Case 1, $9E, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
    Case 1, $A6, 0, 0, $FF00, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
    Case 1, $AE, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
    Case 1, $B6, 0, 0, $0000, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
    Case 1, $BE, 0, 0, READ_START << 8, 0, 0, rTIMA, QUIET_STACK, READ_START, 0, OBS_F, KIND_READ
; RET NZ taken (low byte read, high byte read), POP BC (low, high), CALL NZ taken (high write, low write), PUSH BC, RST 00.
    Case 1, $C0, 0, 0, $0000, 0, 0, 0, SP_LOW, PAD_LOW, PAD_PAGE, OBS_D, KIND_READ
    Case 1, $C0, 0, 0, $0000, 0, 0, 0, SP_HIGH, RET_PAGE, 0, OBS_D, KIND_READ
    Case 1, $C1, 0, 0, $0000, 0, 0, 0, SP_LOW, READ_START, 0, OBS_C, KIND_READ
    Case 1, $C1, 0, 0, $0000, 0, 0, 0, SP_HIGH, READ_START, 0, OBS_B, KIND_READ
    Case 3, $C4, LOW(RunInstruction + 3), HIGH(RunInstruction + 3), $0000, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 3, $C4, LOW(RunInstruction + 3), HIGH(RunInstruction + 3), $0000, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $C5, 0, 0, $0000, $1122, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $C5, 0, 0, $0000, $1122, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $C7, 0, 0, $0000, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $C7, 0, 0, $0000, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
; RET Z taken, RET, CALL Z taken, CALL, RST 08.
    Case 1, $C8, 0, 0, FZ, 0, 0, 0, SP_LOW, PAD_LOW, PAD_PAGE, OBS_D, KIND_READ
    Case 1, $C8, 0, 0, FZ, 0, 0, 0, SP_HIGH, RET_PAGE, 0, OBS_D, KIND_READ
    Case 1, $C9, 0, 0, $0000, 0, 0, 0, SP_LOW, PAD_LOW, PAD_PAGE, OBS_D, KIND_READ
    Case 1, $C9, 0, 0, $0000, 0, 0, 0, SP_HIGH, RET_PAGE, 0, OBS_D, KIND_READ
    Case 3, $CC, LOW(RunInstruction + 3), HIGH(RunInstruction + 3), FZ, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 3, $CC, LOW(RunInstruction + 3), HIGH(RunInstruction + 3), FZ, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
    Case 3, $CD, LOW(RunInstruction + 3), HIGH(RunInstruction + 3), $0000, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 3, $CD, LOW(RunInstruction + 3), HIGH(RunInstruction + 3), $0000, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $CF, 0, 0, $0000, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $CF, 0, 0, $0000, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
; RET NC taken, POP DE, CALL NC taken, PUSH DE, RST 10.
    Case 1, $D0, 0, 0, $0000, 0, 0, 0, SP_LOW, PAD_LOW, PAD_PAGE, OBS_D, KIND_READ
    Case 1, $D0, 0, 0, $0000, 0, 0, 0, SP_HIGH, RET_PAGE, 0, OBS_D, KIND_READ
    Case 1, $D1, 0, 0, $0000, 0, 0, 0, SP_LOW, READ_START, 0, OBS_E, KIND_READ
    Case 1, $D1, 0, 0, $0000, 0, 0, 0, SP_HIGH, READ_START, 0, OBS_D, KIND_READ
    Case 3, $D4, LOW(RunInstruction + 3), HIGH(RunInstruction + 3), $0000, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 3, $D4, LOW(RunInstruction + 3), HIGH(RunInstruction + 3), $0000, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $D5, 0, 0, $0000, 0, $3344, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $D5, 0, 0, $0000, 0, $3344, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $D7, 0, 0, $0000, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $D7, 0, 0, $0000, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
; RET C taken, RETI, CALL C taken, RST 18.
    Case 1, $D8, 0, 0, FC, 0, 0, 0, SP_LOW, PAD_LOW, PAD_PAGE, OBS_D, KIND_READ
    Case 1, $D8, 0, 0, FC, 0, 0, 0, SP_HIGH, RET_PAGE, 0, OBS_D, KIND_READ
    Case 1, $D9, 0, 0, $0000, 0, 0, 0, SP_LOW, PAD_LOW, PAD_PAGE, OBS_D, KIND_READ
    Case 1, $D9, 0, 0, $0000, 0, 0, 0, SP_HIGH, RET_PAGE, 0, OBS_D, KIND_READ
    Case 3, $DC, LOW(RunInstruction + 3), HIGH(RunInstruction + 3), FC, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 3, $DC, LOW(RunInstruction + 3), HIGH(RunInstruction + 3), FC, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $DF, 0, 0, $0000, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $DF, 0, 0, $0000, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
; LDH (n),A, POP HL, LD (C),A, PUSH HL, RST 20, LD (a16),A, RST 28.
    Case 2, $E0, LOW(rTIMA), 0, VALUE << 8, 0, 0, 0, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $E1, 0, 0, $0000, 0, 0, 0, SP_LOW, READ_START, 0, OBS_L, KIND_READ
    Case 1, $E1, 0, 0, $0000, 0, 0, 0, SP_HIGH, READ_START, 0, OBS_H, KIND_READ
    Case 1, $E2, 0, 0, VALUE << 8, LOW(rTIMA), 0, 0, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $E5, 0, 0, $0000, 0, 0, $5566, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $E5, 0, 0, $0000, 0, 0, $5566, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $E7, 0, 0, $0000, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $E7, 0, 0, $0000, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
    Case 3, $EA, LOW(rTIMA), HIGH(rTIMA), VALUE << 8, 0, 0, 0, QUIET_STACK, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $EF, 0, 0, $0000, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $EF, 0, 0, $0000, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
; LDH A,(n), POP AF (the low byte shows in F, from $0F to $10), LD A,(C), PUSH AF, RST 30, LD A,(a16), RST 38.
    Case 2, $F0, LOW(rTIMA), 0, $0000, 0, 0, 0, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
    Case 1, $F1, 0, 0, $0000, 0, 0, 0, SP_LOW, $0F, 0, OBS_F, KIND_READ
    Case 1, $F1, 0, 0, $0000, 0, 0, 0, SP_HIGH, READ_START, 0, OBS_A, KIND_READ
    Case 1, $F2, 0, 0, $0000, LOW(rTIMA), 0, 0, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
    Case 1, $F5, 0, 0, $7700, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $F5, 0, 0, $7700, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $F7, 0, 0, $0000, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $F7, 0, 0, $0000, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
    Case 3, $FA, LOW(rTIMA), HIGH(rTIMA), $0000, 0, 0, 0, QUIET_STACK, READ_START, 0, OBS_A, KIND_READ
    Case 1, $FF, 0, 0, $0000, 0, 0, 0, SP_PUSH_HIGH, 0, 0, OBS_TIMA, KIND_WRITE
    Case 1, $FF, 0, 0, $0000, 0, 0, 0, SP_PUSH_LOW, 0, 0, OBS_TIMA, KIND_WRITE
; RLC, RRC, RL, RR, SLA, SRA, SWAP and SRL (HL): read and write of TIMA.
FOR OP, $06, $40, 8
    Case 2, $CB, OP, 0, $0000, 0, 0, rTIMA, QUIET_STACK, MODIFY_START, 0, OBS_TIMA, KIND_MODIFY
ENDR
; BIT n,(HL): Z shows bit n of the byte read, with TIMA stepping from one below the value that sets bit n.
FOR N, 8
    Case 2, $CB, $46 | (N << 3), 0, $0000, 0, 0, rTIMA, QUIET_STACK, (1 << N) - 1, 0, OBS_F, KIND_READ
ENDR
; RES n,(HL) and SET n,(HL): read and write of TIMA.
FOR OP, $86, $100, 8
    Case 2, $CB, OP, 0, $0000, 0, 0, rTIMA, QUIET_STACK, MODIFY_START, 0, OBS_TIMA, KIND_MODIFY
ENDR
    db 0
