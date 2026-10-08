; HALT: the HALT bug, EI before HALT, timer wake-ups with IME off and on, and requests IE masks.

INCLUDE "hardware.inc"
INCLUDE "report.inc"

DEF TAC_START_16 EQU %101
DEF OP_NOP EQU $00
DEF OP_INC_B EQU $04
DEF OP_HALT EQU $76
DEF OP_JP EQU $C3
DEF OP_RET EQU $C9
DEF OP_LDH_A EQU $F0
DEF OP_EI EQU $FB
DEF MAX_RETURNS EQU 4
DEF TIMED_TIMA EQU $FC
DEF TIMED_STEPS EQU 10
DEF READ_STEPS EQU 4

; Counts the entry in the list at \1 and keeps the return address on the stack; keeps every register.
MACRO record_return
    push af
    push hl
    push de
    ld hl, sp + 6
    ld e, [hl]
    inc hl
    ld d, [hl]
    ld hl, \1
    ld a, [hl]
    inc [hl]
    cp MAX_RETURNS
    jr nc, .full\@
    add a
    inc a
    add l
    ld l, a
    ld [hl], e
    inc l
    ld [hl], d
.full\@:
    pop de
    pop hl
    pop af
ENDM

SECTION "Rst 28", ROM0[$28]
    jp RstHandler

SECTION "Timer vector", ROM0[$50]
    jp wTimerVector

SECTION "Halt RAM", WRAM0[$D100]
wCode: ds 32 ; Timed code: IME op, NOP, delay NOPs, HALT, INC B, read-delay NOPs, LDH A,(TIMA), RET.
wTimerVector: ds 16 ; Timer interrupt code, written per case.
wTimerCount: ds 1 ; Timer interrupts taken, then their return addresses.
wTimerReturns: ds 2 * MAX_RETURNS
wRstCount: ds 1 ; RST $28 entries, then their return addresses.
wRstReturns: ds 2 * MAX_RETURNS
wListsEnd:

SECTION "Halt HRAM", HRAM
hImeOp: ds 1
hDelay: ds 1
hRead: ds 1
hCount: ds 1
hReturnLow: ds 1
hTimas: ds READ_STEPS

SECTION "Main", ROM0
; Runs the cases in order, then leaves the timer and interrupts off.
Main::
    call HaltBugCases
    call EiHaltCases
    ld a, OP_NOP
    call TimedSweep
    ld a, OP_EI
    call TimedSweep
    call NotEnabledCase
    call HaltAfterWakeCase
    di
    xor a
    ldh [rTAC], a
    ldh [rIE], a
    ldh [rIF], a
    ret

; HALT with IME off and an enabled request pending: the byte after HALT is read twice.
HaltBugCases:
    ; One-byte instruction: INC A runs twice.
    call ArmPending
    xor a
    halt
    inc a
    call Emit
    ; Two-byte instruction: LD A,n takes its own opcode $3E as the operand, then $3C runs as INC A.
    call ArmPending
    halt
    ld a, $3C
    call Emit
    ; Three-byte instruction: LD BC,nn takes $01,$04 as the operand, then $0C runs as INC C.
    call ArmPending
    halt
    ld bc, $0C04
    ld a, b
    call Emit
    ld a, c
    call Emit
    ; RST: the RST runs twice; the count and both return addresses relative to the HALT.
    call ClearLists
    call ArmPending
.rstHalt:
    halt
    rst $28
    ld hl, wRstCount
    ld b, LOW(.rstHalt)
    ld c, 2
    call EmitList
    ; HALT again: the doubled $06 forms LD B,$06, then the HALT at the next byte doubles INC B.
    call ArmPending
    ld b, 0
    halt
    db $06, OP_HALT, OP_INC_B
    ld a, b
    call Emit
    ; IE written by the instruction right before HALT while the request is pending: INC B count.
    di
    xor a
    ldh [rTAC], a
    ldh [rIE], a
    ld b, a
    ld a, IEF_TIMER
    ldh [rIF], a
    ldh [rIE], a
    halt
    inc b
    ld a, b
    call Emit
    ; IF written by the instruction right before HALT while the request is enabled: INC B count.
    xor a
    ldh [rIF], a
    ld b, a
    ld a, IEF_TIMER
    ldh [rIE], a
    ldh [rIF], a
    halt
    inc b
    ld a, b
    jp Emit

; EI before HALT: interrupt entries with a request pending, with RST $28 after HALT, and none pending.
EiHaltCases:
    ld hl, wTimerVector
    ld a, OP_JP
    ld [hl+], a
    ld a, LOW(CountingTimer)
    ld [hl+], a
    ld [hl], HIGH(CountingTimer)
    ; EI, HALT, NOP with a request pending.
    call ClearLists
    ld a, IEF_TIMER
    call ArmTimer
    ei
.halt:
    halt
    nop
    di
    ld hl, wTimerCount
    ld b, LOW(.halt)
    ld c, 2
    call EmitList
    ; EI, HALT, RST $28 with a request pending.
    call ClearLists
    ld a, IEF_TIMER
    call ArmTimer
    ei
.haltRst:
    halt
    rst $28
    di
    ld hl, wTimerCount
    ld b, LOW(.haltRst)
    ld c, 2
    call EmitList
    ld hl, wRstCount
    ld c, 1
    call EmitList
    ; EI, HALT, NOP with no request pending.
    call ClearLists
    xor a
    call ArmTimer
    ei
.haltIdle:
    halt
    nop
    di
    ld hl, wTimerCount
    ld b, LOW(.haltIdle)
    ld c, 1
    jp EmitList

; Sweeps HALT's position (hDelay) and the TIMA read delay (hRead) against a timer request; A: NOP/EI.
TimedSweep:
    ldh [hImeOp], a
    xor a
    ldh [hDelay], a
.delay:
    xor a
    ldh [hRead], a
.read:
    call BuildTimed
    call RunTimed
    ld d, a
    ldh a, [hRead]
    add LOW(hTimas)
    ld c, a
    ld a, d
    ldh [c], a
    ld a, b
    ldh [hCount], a
    ld a, l
    ldh [hReturnLow], a
    ldh a, [hRead]
    inc a
    ldh [hRead], a
    cp READ_STEPS
    jr nz, .read
    ldh a, [hImeOp]
    cp OP_EI
    jr z, .imeOn
    ldh a, [hCount]
    jr .emitFirst
.imeOn:
    ldh a, [hDelay]
    add LOW(wCode + 2)
    ld b, a
    ldh a, [hReturnLow]
    sub b
.emitFirst:
    call Emit
    ld c, LOW(hTimas)
.emitTimas:
    ldh a, [c]
    call Emit
    inc c
    ld a, c
    cp LOW(hTimas + READ_STEPS)
    jr nz, .emitTimas
    ldh a, [hDelay]
    inc a
    ldh [hDelay], a
    cp TIMED_STEPS
    jr nz, .delay
    ret

; Writes the timed code for hDelay and hRead, and the IME-on handler that reads TIMA after hRead.
BuildTimed:
    ld hl, wCode
    ldh a, [hImeOp]
    ld [hl+], a
    xor a
    ld [hl+], a
    ldh a, [hDelay]
    call PutNops
    ld a, OP_HALT
    ld [hl+], a
    ld a, OP_INC_B
    ld [hl+], a
    ldh a, [hRead]
    call PutNops
    ld a, OP_LDH_A
    ld [hl+], a
    ld a, LOW(rTIMA)
    ld [hl+], a
    ld [hl], OP_RET
    ld hl, wTimerVector
    ldh a, [hRead]
    call PutNops
    ld a, OP_LDH_A
    ld [hl+], a
    ld a, LOW(rTIMA)
    ld [hl+], a
    ld a, OP_JP
    ld [hl+], a
    ld a, LOW(TimedInterrupted)
    ld [hl+], a
    ld [hl], HIGH(TimedInterrupted)
    ret

; Writes A NOPs at HL.
PutNops:
    and a
    ret z
    ld c, a
    xor a
.loop:
    ld [hl+], a
    dec c
    jr nz, .loop
    ret

; Restarts DIV and the timer and runs the timed code; returns A = TIMA, B = INC B count, HL = return.
RunTimed:
    di
    xor a
    ldh [rTAC], a
    ldh [rTMA], a
    ld a, TIMED_TIMA
    ldh [rTIMA], a
    ld a, IEF_TIMER
    ldh [rIE], a
    xor a
    ldh [rIF], a
    ld b, a
    ld h, a
    ld l, a
    ldh [rDIV], a
    ld a, TAC_START_16
    ldh [rTAC], a
    call wCode
    di
    ret

; Ends the IME-on timed code from the timer handler, with the interrupt return address in HL.
TimedInterrupted:
    pop hl
    ret

; HALT with only a request that IE does not enable pending: the INC B count, TIMA after waking and IF.
NotEnabledCase:
    di
    xor a
    ldh [rTAC], a
    ldh [rTMA], a
    ld a, $F0
    ldh [rTIMA], a
    ld a, IEF_TIMER
    ldh [rIE], a
    ld a, IEF_SERIAL
    ldh [rIF], a
    xor a
    ld b, a
    ldh [rDIV], a
    ld a, TAC_START_16
    ldh [rTAC], a
    halt
    inc b
    ldh a, [rTIMA]
    ld c, a
    ldh a, [rIF]
    ld d, a
    ld a, b
    call Emit
    ld a, c
    call Emit
    ld a, d
    jp Emit

; A second HALT right after an IME-off wake-up finds the request still pending: INC A runs twice.
HaltAfterWakeCase:
    di
    xor a
    ldh [rTAC], a
    ldh [rTMA], a
    ld a, $F0
    ldh [rTIMA], a
    ld a, IEF_TIMER
    ldh [rIE], a
    xor a
    ldh [rIF], a
    ldh [rDIV], a
    ld a, TAC_START_16
    ldh [rTAC], a
    xor a
    halt
    halt
    inc a
    jp Emit

; Stops the timer and makes an enabled timer request pending with IME off.
ArmPending:
    di
    xor a
    ldh [rTAC], a
    ld a, IEF_TIMER
    ldh [rIE], a
    ldh [rIF], a
    ret

; Sets IE to the timer, IF to A and IME off; the next request comes ~1040 T later, then every 4096 T.
ArmTimer:
    di
    ld b, a
    xor a
    ldh [rTAC], a
    ldh [rTMA], a
    ld a, $C0
    ldh [rTIMA], a
    ld a, IEF_TIMER
    ldh [rIE], a
    ld a, b
    ldh [rIF], a
    xor a
    ldh [rDIV], a
    ld a, TAC_START_16
    ldh [rTAC], a
    ret

; Clears both lists of return addresses.
ClearLists:
    ld hl, wTimerCount
    ld c, wListsEnd - wTimerCount
    xor a
.loop:
    ld [hl+], a
    dec c
    jr nz, .loop
    ret

; Emits the count of the list at HL and the low bytes of its first C return addresses minus B.
EmitList:
    ld a, [hl+]
    call Emit
.loop:
    ld a, [hl+]
    sub b
    call Emit
    inc hl
    dec c
    jr nz, .loop
    ret

; RST $28 handler: records the entry.
RstHandler:
    record_return wRstCount
    ret

; Timer handler for the EI cases: records the entry.
CountingTimer:
    record_return wTimerCount
    reti
