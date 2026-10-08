; Interrupt, HALT, timer and serial timing: dispatch, IME, priority, TIMA reload, DIV/TAC edges.

INCLUDE "hardware.inc"
INCLUDE "report.inc"

; Emits code that runs exactly \1 M-cycles, counting with B.
MACRO DELAY
    DEF DLY_LEFT = (\1)
    ASSERT DLY_LEFT >= 0, "negative delay"
    DEF DLY_BIG = 0
    IF DLY_LEFT >= 1030
        DEF DLY_BIG = (DLY_LEFT - 5) / 1025
    ENDC
    REPT DLY_BIG
        ld b, 0
:       dec b
        jr nz, :-
    ENDR
    DEF DLY_LEFT = DLY_LEFT - DLY_BIG * 1025
    IF DLY_LEFT >= 5
        DEF DLY_N = (DLY_LEFT - 1) / 4
        IF DLY_N > 256
            DEF DLY_N = 256
        ENDC
        ld b, LOW(DLY_N)
:       dec b
        jr nz, :-
        DEF DLY_LEFT = DLY_LEFT - (4 * DLY_N + 1)
    ENDC
    REPT DLY_LEFT
        nop
    ENDR
ENDM

; Emits \1 NOPs.
MACRO SLED
    REPT \1
        nop
    ENDR
ENDM

; Resets DIV: the write is cycle W, and POS counts M-cycles from W to the next opcode fetch.
MACRO SYNC
    ldh [rDIV], a
    DEF POS = 1
ENDM

; Delays until the next opcode fetch falls on W+\1.
MACRO FETCH_AT
    ASSERT (\1) >= POS, "FETCH_AT is behind"
    DELAY (\1) - POS
    DEF POS = (\1)
ENDM

; Emits one instruction of \1 M-cycles (the remaining arguments) and advances POS.
MACRO TI
    DEF TI_CYCLES = (\1)
    SHIFT
    \#
    DEF POS = POS + TI_CYCLES
ENDM

; Writes A to the high register \1 with the write on W+\2.
MACRO WRITE_AT
    FETCH_AT (\2) - 2
    TI 3, ldh [\1], a
ENDM

; Writes C to [HL] with the write on W+\1.
MACRO WRITE_HL_AT
    FETCH_AT (\1) - 1
    TI 2, ld [hl], c
ENDM

; Reads the high register \1 into A with the read on W+\2.
MACRO READ_AT
    FETCH_AT (\2) - 2
    TI 3, ldh a, [\1]
ENDM

; Starts a case that may take an interrupt: saves SP, sets the resume label \1 and clears the record.
MACRO BEGIN_CASE
    di
    ld [wSavedSp], sp
    ld a, LOW(\1)
    ld [wContinue], a
    ld a, HIGH(\1)
    ld [wContinue + 1], a
    ld a, $FF
    ld [wIrqVector], a
    ld [wIrqTima], a
    ld [wIrqIf], a
    ld [wIrqIe], a
ENDM

SECTION "Vector 00", ROM0[$0000]
; A cancelled dispatch lands here.
    jp wStub00

SECTION "Vector 40", ROM0[$0040]
; Each interrupt vector jumps to its WRAM stub.
    jp wStub40

SECTION "Vector 48", ROM0[$0048]
    jp wStub48

SECTION "Vector 50", ROM0[$0050]
    jp wStub50

SECTION "Vector 58", ROM0[$0058]
    jp wStub58

SECTION "Vector 60", ROM0[$0060]
    jp wStub60

SECTION "Irq state", WRAM0[$D010]
wSavedSp: ds 2
wContinue: ds 2
wIrqTima: ds 1
wIrqVector: ds 1
wIrqIf: ds 1
wIrqIe: ds 1
wIrqReturn: ds 2
wScratch: ds 16

SECTION "Irq stubs", WRAM0[$D100]
wStub00: ds 16
wStub40: ds 16
wStub48: ds 16
wStub50: ds 16
wStub58: ds 16
wStub60: ds 16

SECTION "Irq common", ROM0
; Records TIMA (read by the stub), vector, IF, IE and the pushed word, then restores SP and resumes.
IrqCommon:
    ld [wIrqTima], a
    ld a, e
    ld [wIrqVector], a
    ldh a, [rIF]
    ld [wIrqIf], a
    ldh a, [rIE]
    ld [wIrqIe], a
    ld hl, sp + 0
    ld a, [hl+]
    ld [wIrqReturn], a
    ld a, [hl]
    ld [wIrqReturn + 1], a
    ld hl, wSavedSp
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    ld sp, hl
    ld hl, wContinue
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    jp hl

; Writes the six vector stubs: A NOPs, read TIMA, load the vector id into E, jump to IrqCommon.
SetStubs:
    ld c, a
    ld hl, wStub00
    ld d, $00
    call .stub
    ld hl, wStub40
    ld d, $40
    call .stub
    ld hl, wStub48
    ld d, $48
    call .stub
    ld hl, wStub50
    ld d, $50
    call .stub
    ld hl, wStub58
    ld d, $58
    call .stub
    ld hl, wStub60
    ld d, $60
.stub:
    ld b, c
    inc b
    xor a
    jr .count
.nop:
    ld [hl+], a
.count:
    dec b
    jr nz, .nop
    ld a, $F0
    ld [hl+], a
    ld a, LOW(rTIMA)
    ld [hl+], a
    ld a, $1E
    ld [hl+], a
    ld a, d
    ld [hl+], a
    ld a, $C3
    ld [hl+], a
    ld a, LOW(IrqCommon)
    ld [hl+], a
    ld a, HIGH(IrqCommon)
    ld [hl+], a
    ret

; Emits the low byte of the pushed return address minus DE, or $FF when no interrupt was taken.
EmitOffset:
    ld a, [wIrqVector]
    inc a
    jr z, .none
    ld a, [wIrqReturn]
    sub e
    jp Emit
.none:
    ld a, $FF
    jp Emit

; Clears IME, stops the timer and clears TIMA, TMA, IF and IE.
QuietTimer:
    di
    xor a
    ldh [rTAC], a
    ldh [rTIMA], a
    ldh [rTMA], a
    ldh [rIF], a
    ldh [rIE], a
    ret

; Stops any transfer, then clears SB.
SerialIdle:
    xor a
    ldh [rSC], a
    ldh [rSB], a
    ret

; Emits the recorded vector, then IF as the handler read it.
EmitVectorIf:
    ld a, [wIrqVector]
    call Emit
    ld a, [wIrqIf]
    jp Emit

SECTION "Main", ROM0
Main::
    xor a
    call SetStubs
    call TimerIrqCases
    call InstructionCases
    call IfWriteCases
    call StatCases
    call SerialIrqCases
    call EiCases
    call DiCases
    call LateRequestCases
    call PushCases
    call PriorityCases
    call HaltIme0Cases
    call HaltIme1Cases
    call HaltOtherCases
    call HaltBugCases
    call TimaReadCases
    call TimaWriteCases
    call TmaWriteCases
    call IfWindowCases
    call DivWriteCases
    call TacChangeCases
    call SerialReadCases
    ret

; Timer overflow (TIMA FD, TAC 05 from W+3, IF at the end of W+12) to its handler; TIMA 0-3 NOPs in.
TimerIrqCases:
    FOR K, 4
        ld a, K
        call SetStubs
        call TimerIrq
    ENDR
    xor a
    jp SetStubs

; One timer case: offset in a 24-NOP sled from W+5 and the TIMA read by the stub.
TimerIrq:
    call QuietTimer
    ld a, IEF_TIMER
    ldh [rIE], a
    ld a, $FD
    ldh [rTIMA], a
    BEGIN_CASE .done
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    TI 1, ei
.sled:
    SLED 24
.done:
    ld de, .sled
    call EmitOffset
    ld a, [wIrqTima]
    jp Emit

; Prepares a timer interrupt that becomes visible at W+13, with HL on scratch WRAM.
TimerInstrSetup:
    call QuietTimer
    ld a, IEF_TIMER
    ldh [rIE], a
    ld a, $FD
    ldh [rTIMA], a
    ld hl, wScratch
    ret

; Timer interrupt during a sled of one instruction (the remaining arguments), shifted by \1 NOPs.
MACRO INSTRUCTION_CASE
    call TimerInstrSetup
    BEGIN_CASE .done\@
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    ei
    SLED \1
    SHIFT
.sled\@:
    REPT 12
        \#
    ENDR
.done\@:
    ld de, .sled\@
    call EmitOffset
ENDM

; Return offsets for sleds of 2, 3, 4 and 5 M-cycle instructions, each shifted by 0-4 NOPs.
InstructionCases:
    FOR SHIFTED, 5
        INSTRUCTION_CASE SHIFTED, ld a, [hl]
    ENDR
    FOR SHIFTED, 5
        INSTRUCTION_CASE SHIFTED, ld bc, $0000
    ENDR
    FOR SHIFTED, 5
        INSTRUCTION_CASE SHIFTED, jp @ + 3
    ENDR
    FOR SHIFTED, 5
        INSTRUCTION_CASE SHIFTED, ld [wScratch], sp
    ENDR
    ret

; An IF write (serial) dispatches after the writing instruction, the write shifted by \1 NOPs.
MACRO IF_WRITE_CASE
    call QuietTimer
    ld a, IEF_SERIAL
    ldh [rIE], a
    BEGIN_CASE .done\@
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    ld a, IEF_SERIAL
    ei
    SLED \1
    ldh [rIF], a
.after\@:
    SLED 8
.done\@:
    ld de, .after\@
    call EmitOffset
    ld a, [wIrqTima]
    call Emit
ENDM

; IF written by LDH (shifted 0-3), by LD (HL) and by LD (a16): return offset and handler TIMA.
IfWriteCases:
    FOR SHIFTED, 4
        IF_WRITE_CASE SHIFTED
    ENDR
    call QuietTimer
    ld a, IEF_SERIAL
    ldh [rIE], a
    BEGIN_CASE .doneHl
    ld hl, rIF
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    ld a, IEF_SERIAL
    ei
    ld [hl], a
.afterHl:
    SLED 8
.doneHl:
    ld de, .afterHl
    call EmitOffset
    ld a, [wIrqTima]
    call Emit
    call QuietTimer
    ld a, IEF_SERIAL
    ldh [rIE], a
    BEGIN_CASE .doneAbs
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    ld a, IEF_SERIAL
    ei
    db $EA, LOW(rIF), HIGH(rIF)
.afterAbs:
    SLED 8
.doneAbs:
    ld de, .afterAbs
    call EmitOffset
    ld a, [wIrqTima]
    jp Emit

; LCD on at W+8 with STAT \1, LYC \2, IE \3; IF cleared and IME set \4 M-cycles later; NOP sled.
MACRO STAT_CASE
    call QuietTimer
    call LcdOff
    ld a, \1
    ldh [rSTAT], a
    ld a, \2
    ldh [rLYC], a
    ld a, \3
    ldh [rIE], a
    BEGIN_CASE .done\@
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    TI 2, ld a, LCDCF_ON | LCDCF_BGON
    WRITE_AT rLCDC, 8
    DELAY \4
    xor a
    ldh [rIF], a
    ei
.sled\@:
    SLED 64
.done\@:
    call LcdOff
    ld de, .sled\@
    call EmitOffset
    ld a, [wIrqIf]
    call Emit
    ld a, [wIrqTima]
    call Emit
ENDM

; STAT mode 2 and mode 0 (line 1), LYC=2, mode 1 and VBlank interrupts after the LCD is switched on.
StatCases:
    STAT_CASE STATF_MODE2, $FF, IEF_STAT, 88
    STAT_CASE STATF_MODE0, $FF, IEF_STAT, 151
    STAT_CASE STATF_LYC, 2, IEF_STAT, 202
    STAT_CASE STATF_MODE1, $FF, IEF_STAT, 16390
    STAT_CASE 0, $FF, IEF_VBLANK, 16390
    xor a
    ldh [rSTAT], a
    ret

; Serial transfer (internal clock) started at W+\1: completion interrupt in a NOP sled from W+1000.
MACRO SERIAL_PHASE_CASE
    call QuietTimer
    call SerialIdle
    ld a, IEF_SERIAL
    ldh [rIE], a
    BEGIN_CASE .done\@
    ld a, $81
    SYNC
    WRITE_AT rSC, \1
    FETCH_AT 999
    TI 1, ei
.sled\@:
    SLED 128
.done\@:
    ld de, .sled\@
    call EmitOffset
ENDM

; Completion for starts at W+61 to W+65 (DIV bit 7 falls at the end of W+63), then a restart at W+300.
SerialIrqCases:
    FOR START, 61, 66
        SERIAL_PHASE_CASE START
    ENDR
    call QuietTimer
    call SerialIdle
    ld a, IEF_SERIAL
    ldh [rIE], a
    BEGIN_CASE .doneRestart
    ld a, $81
    SYNC
    WRITE_AT rSC, 10
    WRITE_AT rSC, 300
    FETCH_AT 1255
    TI 1, ei
.sledRestart:
    SLED 64
.doneRestart:
    ld de, .sledRestart
    call EmitOffset
    call QuietTimer
    call SerialIdle
    ld a, $80
    SYNC
    WRITE_AT rSC, 10
    READ_AT rIF, 1100
    call Emit
    ldh a, [rSC]
    call Emit
    ldh a, [rSB]
    jp Emit

; EI with the timer interrupt becoming visible at W+13; EI on W+(4+\1); offset from the EI.
MACRO EI_SWEEP_CASE
    call TimerInstrSetup
    BEGIN_CASE .done\@
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    SLED \1
.ei\@:
    ei
    SLED 16
.done\@:
    ld de, .ei\@
    call EmitOffset
ENDM

; Prepares a timer request pending in IF with IME=0.
PendingSetup:
    call QuietTimer
    ld a, IEF_TIMER
    ldh [rIE], a
    ldh [rIF], a
    ret

; EI sweep, then a pending request after EI;NOP, EI;DI, EI;EI, EI;HALT and RETI.
EiCases:
    FOR SHIFTED, 5, 11
        EI_SWEEP_CASE SHIFTED
    ENDR
    call PendingSetup
    BEGIN_CASE .done1
.ei1:
    ei
    SLED 8
.done1:
    ld de, .ei1
    call EmitOffset
    call PendingSetup
    BEGIN_CASE .done2
.ei2:
    ei
    di
    SLED 8
.done2:
    ld de, .ei2
    call EmitOffset
    call PendingSetup
    BEGIN_CASE .done3
.ei3:
    ei
    ei
    SLED 8
.done3:
    ld de, .ei3
    call EmitOffset
    call PendingSetup
    BEGIN_CASE .done4
.ei4:
    ei
    halt
    SLED 8
.done4:
    ld de, .ei4
    call EmitOffset
    call PendingSetup
    BEGIN_CASE .done5
    ld hl, .target5
    push hl
    reti
.target5:
    SLED 8
.done5:
    ld de, .target5
    jp EmitOffset

; DI on W+(4+\1) with IME=1 and the timer interrupt visible at W+13: vector and offset from the DI.
MACRO DI_SWEEP_CASE
    call TimerInstrSetup
    BEGIN_CASE .done\@
    ld a, $05
    ei
    SYNC
    WRITE_AT rTAC, 3
    SLED \1
.di\@:
    di
    SLED 16
.done\@:
    ld a, [wIrqVector]
    call Emit
    ld de, .di\@
    call EmitOffset
ENDM

; DI sweep around the request.
DiCases:
    FOR SHIFTED, 6, 11
        DI_SWEEP_CASE SHIFTED
    ENDR
    ret

; Serial dispatch from an IF write at W+(9+\1) while a timer request shows at W+17: vector and IF.
MACRO LATE_REQUEST_CASE
    call QuietTimer
    ld a, IEF_TIMER | IEF_SERIAL
    ldh [rIE], a
    ld a, $FC
    ldh [rTIMA], a
    BEGIN_CASE .done\@
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    ld a, IEF_SERIAL
    ei
    SLED \1
    ldh [rIF], a
    SLED 16
.done\@:
    call EmitVectorIf
ENDM

; Late request sweep over the dispatch of the serial interrupt.
LateRequestCases:
    FOR SHIFTED, 8
        LATE_REQUEST_CASE SHIFTED
    ENDR
    ret

; Sets SP to HL, enables interrupts and writes A to IF; the return address $xx0C lands at SP.
MACRO PUSH_TRIGGER
    ld sp, hl
    ei
    ldh [rIF], a
    SLED 8
    ld e, $FF
    jp IrqCommon
ENDM

SECTION "Push trigger 02", ROM0[$0208]
PushTrigger02:
    PUSH_TRIGGER

SECTION "Push trigger 04", ROM0[$0408]
PushTrigger04:
    PUSH_TRIGGER

SECTION "Push trigger 08", ROM0[$0808]
PushTrigger08:
    PUSH_TRIGGER

SECTION "Push cases", ROM0
; Dispatch with SP \1, IE \2 and IF \3 through the trigger \4: vector, IF and IE seen by the handler.
MACRO PUSH_CASE
    call QuietTimer
    ld a, \2
    ldh [rIE], a
    BEGIN_CASE .done\@
    ld hl, \1
    ld a, \3
    jp \4
.done\@:
    call EmitVectorIf
    ld a, [wIrqIe]
    call Emit
ENDM

; Pushes into IE (SP 0000 and 0001) and IF (SP FF10 and FF11) during the dispatch.
PushCases:
    PUSH_CASE $0000, IEF_TIMER, IEF_TIMER, PushTrigger02
    PUSH_CASE $0000, IEF_TIMER, IEF_TIMER, PushTrigger04
    PUSH_CASE $0000, IEF_TIMER | IEF_SERIAL, IEF_TIMER | IEF_SERIAL, PushTrigger08
    PUSH_CASE $0001, IEF_TIMER, IEF_TIMER, PushTrigger02
    PUSH_CASE $FF10, IEF_TIMER, IEF_TIMER, PushTrigger02
    PUSH_CASE $FF10, IEF_TIMER, IEF_TIMER, PushTrigger04
    PUSH_CASE $FF11, IEF_TIMER, IEF_TIMER, PushTrigger04
    ret

; IE \1 and IF \2 written with IME=1: vector and IF (IF read directly when nothing was taken).
MACRO PRIORITY_CASE
    call QuietTimer
    BEGIN_CASE .done\@
    ld a, \1
    ldh [rIE], a
    ld a, \2
    ei
    ldh [rIF], a
    SLED 4
.done\@:
    ld a, [wIrqVector]
    call Emit
    ld a, [wIrqVector]
    inc a
    ld a, [wIrqIf]
    jr nz, .taken\@
    ldh a, [rIF]
.taken\@:
    call Emit
ENDM

; Vector choice for several IE/IF combinations.
PriorityCases:
    PRIORITY_CASE $1F, $1F
    PRIORITY_CASE $1F, $1E
    PRIORITY_CASE $1F, $1C
    PRIORITY_CASE $1F, $18
    PRIORITY_CASE $1F, $10
    PRIORITY_CASE $1E, $1F
    PRIORITY_CASE $1C, $1F
    PRIORITY_CASE $18, $1F
    PRIORITY_CASE $10, $1F
    PRIORITY_CASE $1F, $0A
    PRIORITY_CASE $1F, $14
    PRIORITY_CASE $1F, $12
    PRIORITY_CASE $1F, $05
    PRIORITY_CASE $0A, $15
    PRIORITY_CASE $E4, $04
    PRIORITY_CASE $FF, $E0
    ret

; HALT (IME=0) on W+(4+\1), timer request at the end of W+12; INC C after it, TIMA \2 NOPs later.
MACRO HALT_IME0_CASE
    call TimerInstrSetup
    ld c, 0
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    SLED \1
    halt
    inc c
    SLED \2
    ldh a, [rTIMA]
    ld b, a
    ld a, c
    call Emit
    ld a, b
    call Emit
ENDM

; HALT entry swept across the request, then the wake-up time with 0-3 NOPs before the TIMA read.
HaltIme0Cases:
    FOR SHIFTED, 4, 12
        HALT_IME0_CASE SHIFTED, 0
    ENDR
    FOR WAITED, 1, 4
        HALT_IME0_CASE 4, WAITED
    ENDR
    ret

; HALT (IME=1) on W+(5+\1), timer request at the end of W+12: offset from the HALT and handler TIMA.
MACRO HALT_IME1_CASE
    call TimerInstrSetup
    BEGIN_CASE .done\@
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    ei
    SLED \1
.halt\@:
    halt
    SLED 8
.done\@:
    ld de, .halt\@
    call EmitOffset
    ld a, [wIrqTima]
    call Emit
ENDM

; HALT entry swept across the request, then the stub reading TIMA after 1-3 NOPs.
HaltIme1Cases:
    FOR SHIFTED, 3, 9
        HALT_IME1_CASE SHIFTED
    ENDR
    FOR K, 1, 4
        ld a, K
        call SetStubs
        HALT_IME1_CASE 3
    ENDR
    xor a
    jp SetStubs

; HALT (IME=0), STAT \1, LYC \2, IE \3, IF cleared at W+\4: TIMA \5 NOPs after waking (LY if 0).
MACRO HALT_STAT_IME0_CASE
    call QuietTimer
    call LcdOff
    ld a, \1
    ldh [rSTAT], a
    ld a, \2
    ldh [rLYC], a
    ld a, \3
    ldh [rIE], a
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    TI 2, ld a, LCDCF_ON | LCDCF_BGON
    WRITE_AT rLCDC, 8
    FETCH_AT \4
    xor a
    ldh [rIF], a
    halt
    SLED \5
    ldh a, [rTIMA]
    ld c, a
    ldh a, [rLY]
    ld b, a
    call LcdOff
    ld a, c
    call Emit
    IF \5 == 0
        ld a, b
        call Emit
    ENDC
ENDM

; HALT (IME=1), STAT \1, LYC \2, IE \3, LCD on at W+8, IF cleared at W+\4: HALT offset, handler TIMA.
MACRO HALT_STAT_IME1_CASE
    call QuietTimer
    call LcdOff
    ld a, \1
    ldh [rSTAT], a
    ld a, \2
    ldh [rLYC], a
    ld a, \3
    ldh [rIE], a
    BEGIN_CASE .done\@
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    TI 2, ld a, LCDCF_ON | LCDCF_BGON
    WRITE_AT rLCDC, 8
    FETCH_AT \4
    xor a
    ldh [rIF], a
    ei
.halt\@:
    halt
    SLED 8
.done\@:
    call LcdOff
    ld de, .halt\@
    call EmitOffset
    ld a, [wIrqTima]
    call Emit
ENDM

; HALT (IME=0) woken by a transfer started at W+10; TIMA (from 80, TMA 00) read \1 NOPs after.
MACRO HALT_SERIAL_CASE
    call QuietTimer
    call SerialIdle
    ld a, $80
    ldh [rTIMA], a
    ld a, IEF_SERIAL
    ldh [rIE], a
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    TI 2, ld a, $81
    WRITE_AT rSC, 10
    halt
    SLED \1
    ldh a, [rTIMA]
    call Emit
ENDM

; Wake-ups from HALT by LYC, mode 0 and VBlank (IME=0), LYC and mode 2 (IME=1), and serial.
HaltOtherCases:
    FOR WAITED, 4
        HALT_STAT_IME0_CASE STATF_LYC, 2, IEF_STAT, 9, WAITED
    ENDR
    FOR WAITED, 4
        HALT_STAT_IME0_CASE STATF_MODE0, $FF, IEF_STAT, 130, WAITED
    ENDR
    FOR WAITED, 4
        HALT_STAT_IME0_CASE 0, $FF, IEF_VBLANK, 16000, WAITED
    ENDR
    FOR K, 4
        ld a, K
        call SetStubs
        HALT_STAT_IME1_CASE STATF_LYC, 2, IEF_STAT, 9
    ENDR
    FOR K, 4
        ld a, K
        call SetStubs
        HALT_STAT_IME1_CASE STATF_MODE2, $FF, IEF_STAT, 20
    ENDR
    xor a
    call SetStubs
    xor a
    ldh [rSTAT], a
    FOR WAITED, 4
        HALT_SERIAL_CASE WAITED
    ENDR
    ret

; HALT bug with IME=0 and a request pending: INC C, and LD A,$14 read as LD A,$3E; INC D.
HaltBugCases:
    call PendingSetup
    ld c, 0
    halt
    inc c
    ld a, c
    call Emit
    call PendingSetup
    ld d, 0
    halt
    ld a, $14
    call Emit
    ld a, d
    jp Emit

; Reads \2 on W+\1 with TIMA FE, TMA A5 and TAC 05 from W+3 (overflow at the end of W+7).
MACRO TIMA_READ_CASE
    call QuietTimer
    ld a, $FE
    ldh [rTIMA], a
    ld a, $A5
    ldh [rTMA], a
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    READ_AT \2, \1
    call Emit
ENDM

; TIMA, then IF, read on W+6 to W+13 across the overflow and reload.
TimaReadCases:
    FOR CYCLE, 6, 14
        TIMA_READ_CASE CYCLE, rTIMA
    ENDR
    FOR CYCLE, 6, 14
        TIMA_READ_CASE CYCLE, rIF
    ENDR
    ret

; Writes C to [HL] on W+\1 around the overflow at the end of W+7; TIMA on W+20, IF on W+24 (if \2).
MACRO WINDOW_WRITE_CASE
    call QuietTimer
    ld a, $FE
    ldh [rTIMA], a
    ld a, $A5
    ldh [rTMA], a
    ld a, $05
    SYNC
    WRITE_AT rTAC, 3
    WRITE_HL_AT \1
    READ_AT rTIMA, 20
    ld c, a
    DEF POS = POS + 1
    READ_AT rIF, 24
    ld b, a
    ld a, c
    call Emit
    IF \2
        ld a, b
        call Emit
    ENDC
ENDM

; TIMA write of 33 on W+5 to W+11: TIMA and IF after (no IF for the reload cycle W+8).
TimaWriteCases:
    FOR CYCLE, 5, 12
        ld hl, rTIMA
        ld c, $33
        WINDOW_WRITE_CASE CYCLE, CYCLE != 8
    ENDR
    ret

; TMA write of 5A on W+5 to W+11: TIMA and IF afterwards.
TmaWriteCases:
    FOR CYCLE, 5, 12
        ld hl, rTMA
        ld c, $5A
        WINDOW_WRITE_CASE CYCLE, 1
    ENDR
    ret

; IF write of 00 on W+5 to W+11 against the timer request at the end of W+8: TIMA and IF afterwards.
IfWindowCases:
    FOR CYCLE, 5, 12
        ld hl, rIF
        ld c, 0
        WINDOW_WRITE_CASE CYCLE, 1
    ENDR
    ret

; TAC \1 from W+3 with TIMA 00, a second DIV write on W+\2, TIMA read on W+(\2+3).
MACRO DIV_WRITE_CASE
    call QuietTimer
    ld a, \1
    SYNC
    WRITE_AT rTAC, 3
    WRITE_AT rDIV, \2
    READ_AT rTIMA, (\2) + 3
    call Emit
ENDM

; DIV writes on W+8 to W+15 with TAC 05, then high and low phases of the other three rates.
DivWriteCases:
    FOR CYCLE, 8, 16
        DIV_WRITE_CASE $05, CYCLE
    ENDR
    DIV_WRITE_CASE $04, 200
    DIV_WRITE_CASE $04, 100
    DIV_WRITE_CASE $06, 26
    DIV_WRITE_CASE $06, 20
    DIV_WRITE_CASE $07, 40
    DIV_WRITE_CASE $07, 20
    ret

; TAC \1 from W+3 with TIMA 00, TAC \2 written on W+\3, TIMA read on W+(\3+3).
MACRO TAC_CHANGE_CASE
    call QuietTimer
    ld hl, rTAC
    ld c, \2
    ld a, \1
    SYNC
    WRITE_AT rTAC, 3
    WRITE_HL_AT \3
    READ_AT rTIMA, (\3) + 3
    call Emit
ENDM

; TAC 05 to 01 on W+8 to W+15, 05 to 06 on W+12 to W+19, 01 to 05 and 05 to 04 on W+10 and W+12.
TacChangeCases:
    FOR CYCLE, 8, 16
        TAC_CHANGE_CASE $05, $01, CYCLE
    ENDR
    FOR CYCLE, 12, 20
        TAC_CHANGE_CASE $05, $06, CYCLE
    ENDR
    TAC_CHANGE_CASE $01, $05, 10
    TAC_CHANGE_CASE $01, $05, 12
    TAC_CHANGE_CASE $05, $04, 10
    TAC_CHANGE_CASE $05, $04, 12
    ret

; Reads \2 on W+\1 during a transfer of 00 started on W+10.
MACRO SERIAL_READ_CASE
    call QuietTimer
    call SerialIdle
    ld a, $81
    SYNC
    WRITE_AT rSC, 10
    READ_AT \2, \1
    call Emit
ENDM

; SB around the second shift (end of W+255), SC and IF around the completion (end of W+1023).
SerialReadCases:
    FOR CYCLE, 254, 258
        SERIAL_READ_CASE CYCLE, rSB
    ENDR
    FOR CYCLE, 1022, 1026
        SERIAL_READ_CASE CYCLE, rSC
    ENDR
    FOR CYCLE, 1022, 1026
        SERIAL_READ_CASE CYCLE, rIF
    ENDR
    ret
