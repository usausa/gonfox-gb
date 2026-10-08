; APU behaviour seen through its registers: read-back, power, length, sweep, DAC, Wave RAM and timing.

INCLUDE "hardware.inc"
INCLUDE "report.inc"

DEF WAVE_MARK EQU $00 ; Written into Wave RAM while CH3 plays; the test pattern never holds it.

SECTION "APU test state", WRAM0[$D010]
wCounts: ds 8 ; DIV-APU step counts of CH1-CH4, low byte first.
wLimit: ds 2

SECTION "APU test HRAM", HRAM
hK: ds 1 ; Probe index of the running sweep.
hCount: ds 1
hBody: ds 2
hMask: ds 1
hEmitMask: ds 1

; Waits exactly \1 M-cycles, using A only.
MACRO DELAY
    REPT (\1) / 1025
        ld a, 0
:
        dec a
        jr nz, :-
    ENDR
    IF (\1) % 1025 >= 5
        ld a, ((\1) % 1025 - 1) / 4
:
        dec a
        jr nz, :-
        REPT ((\1) % 1025 - 1) % 4
            nop
        ENDR
    ELSE
        REPT (\1) % 1025
            nop
        ENDR
    ENDC
ENDM

; Powers the APU off, writes DIV in cycle W, powers on in W + 5: DIV-APU step 0 falls in W + 2047.
MACRO APU_RESET
    xor a
    ldh [rNR52], a
    ldh [rDIV], a
    ld a, $80
    ldh [rNR52], a
ENDM

; Sets HL to \1 - k, the slide entry that delays the access after \1 by k cycles.
MACRO SLIDE_ENTRY
    ldh a, [hK]
    SLIDE_ENTRY_A \1
ENDM

; Sets HL to \1 - A.
MACRO SLIDE_ENTRY_A
    ld l, a
    ld a, LOW(\1)
    sub l
    ld l, a
    ld a, HIGH(\1)
    sbc 0
    ld h, a
ENDM

SECTION "Main", ROM0
; Runs every case in order.
Main::
    call TestRegisters
    call TestWaveRam
    call TestPowerChannels
    call TestPowerRewrite
    call TestLengthWhileOff
    call TestLengthPowerCycle
    call TestLengthDisabled
    call TestLengthEnableFirstHalf
    call TestLengthWhileStopped
    call TestLengthRewrite
    call TestLengthReload
    call TestDivWrite
    call TestLengthEdge
    call TestExtraClockEdge
    call TestPowerOnSkip
    call TestTriggerStart
    call TestSweepTrigger
    call TestSweepTriggerDelay
    call TestSweepSteps
    call TestSweepNegate
    call TestSweepClockTiming
    call TestDac
    call TestDacTrigger
    call TestEnvelopeZero
    call TestWaveRead
    call TestWaveWrite
    call TestWaveRetrigger
    xor a
    ldh [rNR52], a
    ret

SECTION "Helpers", ROM0
; Waits for the next DIV-APU step (DIV bit 4 falling), then 33 cycles for delayed events to settle.
WaitStep:
.high:
    ldh a, [rDIV]
    and $10
    jr z, .high
.low:
    ldh a, [rDIV]
    and $10
    jr nz, .low
    ld a, 8
.settle:
    dec a
    jr nz, .settle
    ret

; Counts DIV-APU steps (at most BC) until each channel of A clears in NR52; emits each 16-bit count.
CountSteps:
    ldh [hMask], a
    ldh [hEmitMask], a
    ld a, c
    ld [wLimit], a
    ld a, b
    ld [wLimit + 1], a
    ld hl, wCounts
    xor a
    REPT 8
        ld [hl+], a
    ENDR
.loop:
    ldh a, [rNR52]
    ld b, a
    ldh a, [hMask]
    and b
    ldh [hMask], a
    jr z, .done
    ld hl, wLimit
    ld a, [hl+]
    or [hl]
    jr z, .done
    dec hl
    ld a, [hl]
    sub 1
    ld [hl+], a
    ld a, [hl]
    sbc 0
    ld [hl], a
    ldh a, [hMask]
    ld c, a
    ld hl, wCounts
    ld b, 4
.channel:
    srl c
    jr nc, .next
    inc [hl]
    jr nz, .next
    inc hl
    inc [hl]
    dec hl
.next:
    inc hl
    inc hl
    dec b
    jr nz, .channel
    call WaitStep
    jr .loop
.done:
    ldh a, [hEmitMask]
    ld c, a
    ld hl, wCounts
    ld b, 4
.emit:
    srl c
    jr nc, .skip
    ld a, [hl+]
    call Emit
    ld a, [hl-]
    call Emit
.skip:
    inc hl
    inc hl
    dec b
    jr nz, .emit
    ret

; Runs the probe routine at HL for k = 0 .. B-1, with k in hK; each probe emits its own results.
Sweep:
    ld a, l
    ldh [hBody], a
    ld a, h
    ldh [hBody + 1], a
    ld a, b
    ldh [hCount], a
    xor a
    ldh [hK], a
.loop:
    ldh a, [hBody]
    ld l, a
    ldh a, [hBody + 1]
    ld h, a
    call JumpHl
    ldh a, [hK]
    inc a
    ldh [hK], a
    ld b, a
    ldh a, [hCount]
    cp b
    jr nz, .loop
    ret

; Switches the DACs of all four channels on.
DacsOn:
    ld a, $F0
    ldh [rNR12], a
    ldh [rNR22], a
    ldh [rNR42], a
    ld a, $80
    ldh [rNR30], a
    ret

; Switches the DACs of all four channels off.
DacsOff:
    xor a
    ldh [rNR12], a
    ldh [rNR22], a
    ldh [rNR42], a
    ldh [rNR30], a
    ret

; Writes A to NR14, NR24, NR34 and NR44.
WriteNrx4:
    ldh [rNR14], a
    ldh [rNR24], a
    ldh [rNR34], a
    ldh [rNR44], a
    ret

; Writes length A to NR11, NR21 and NR41 and length B to NR31.
WriteLengths:
    ldh [rNR11], a
    ldh [rNR21], a
    ldh [rNR41], a
    ld a, b
    ldh [rNR31], a
    ret

; Returns in A the all-ones value written to register C: $7F for NRx4 (no trigger), else $FF.
AllOnes:
    ld a, c
    cp LOW(rNR14)
    jr z, .nrx4
    cp LOW(rNR24)
    jr z, .nrx4
    cp LOW(rNR34)
    jr z, .nrx4
    cp LOW(rNR44)
    jr z, .nrx4
    ld a, $FF
    ret
.nrx4:
    ld a, $7F
    ret

; Emits $FF10-$FF2F as read.
EmitApuRegs:
    ld c, LOW(rNR10)
.loop:
    ldh a, [c]
    call Emit
    inc c
    ld a, c
    cp $30
    jr nz, .loop
    ret

; Copies 16 bytes from HL to Wave RAM.
LoadWave:
    ld de, _AUD3WAVERAM
    ld bc, 16
    jp Copy

; Emits the 16 bytes of Wave RAM.
EmitWave:
    ld hl, _AUD3WAVERAM
    ld b, 16
.loop:
    ld a, [hl+]
    call Emit
    dec b
    jr nz, .loop
    ret

; Returns in A the index of the first Wave RAM byte equal to WAVE_MARK, or $FF.
FindMark:
    ld hl, _AUD3WAVERAM
    ld b, 0
.loop:
    ld a, [hl+]
    cp WAVE_MARK
    jr z, .found
    inc b
    ld a, b
    cp 16
    jr nz, .loop
    ld a, $FF
    ret
.found:
    ld a, b
    ret

; Distinct Wave RAM bytes, none equal to WAVE_MARK or $FF.
WavePatternA:
    db $0F, $1E, $2D, $3C, $4B, $5A, $69, $78, $87, $96, $A5, $B4, $C3, $D2, $E1, $F1
WavePatternB:
    db $F0, $E1, $D2, $C3, $B4, $A5, $96, $87, $78, $69, $5A, $4B, $3C, $2D, $1E, $0E

SECTION "Slides", ROM0
; Entering a slide k bytes before its End label delays the access that follows the label by k cycles.
ProbeSlide:
    REPT 64
        nop
    ENDR
ProbeRead:
    ld a, [bc]
    ret
WriteReadSlide:
    REPT 64
        nop
    ENDR
WriteReadEnd:
    ld [de], a
    ld a, [bc]
    ret
WriteMarkSlide:
    REPT 64
        nop
    ENDR
WriteMarkEnd:
    ld a, WAVE_MARK
    ld [bc], a
    ret
WriteAgainSlide:
    REPT 64
        nop
    ENDR
WriteAgainEnd:
    ld [de], a
    ret

; Writes A to [DE] (cycle T0 = call + 7) and continues at HL in T0 + 1.
WriteJump:
    ld [de], a
    jp hl

; Continues at HL; a call to it reaches HL in cycle call + 6.
JumpHl:
    jp hl

SECTION "Register tests", ROM0
; Read-back of $FF10-$FF2F with the APU on, after power-off, while off and after power-on.
TestRegisters:
    APU_RESET
    ld c, LOW(rNR10)
.on:
    ld a, c
    cp LOW(rNR52)
    jr z, .onNext
    xor a
    ldh [c], a
    ldh a, [c]
    call Emit
    call AllOnes
    ldh [c], a
    ldh a, [c]
    call Emit
.onNext:
    inc c
    ld a, c
    cp $30
    jr nz, .on
    ldh a, [rNR52]
    call Emit
    ld a, $FF
    ldh [rNR52], a
    ldh a, [rNR52]
    call Emit
    xor a
    ldh [rNR52], a
    call EmitApuRegs
    ld c, LOW(rNR10)
.off:
    ld a, c
    cp LOW(rNR52)
    ld a, $FF
    jr z, .offNext
    ldh [c], a
.offNext:
    inc c
    ld a, c
    cp $30
    jr nz, .off
    ld a, $7F
    ldh [rNR52], a
    call EmitApuRegs
    ld a, $80
    ldh [rNR52], a
    jp EmitApuRegs

; Wave RAM with CH3 stopped: written on, read after power-off, written while off, read after power-on.
TestWaveRam:
    APU_RESET
    ld hl, WavePatternA
    call LoadWave
    call EmitWave
    xor a
    ldh [rNR52], a
    call EmitWave
    ld hl, WavePatternB
    call LoadWave
    call EmitWave
    ld a, $80
    ldh [rNR52], a
    jp EmitWave

; NR52 with all channels playing, after power-off and power-on, and after retriggering them.
TestPowerChannels:
    APU_RESET
    call DacsOn
    ld a, $80
    call WriteNrx4
    DELAY 8
    ldh a, [rNR52]
    call Emit
    xor a
    ldh [rNR52], a
    ldh a, [rNR52]
    call Emit
    ld a, $80
    ldh [rNR52], a
    ldh a, [rNR52]
    call Emit
    ld a, $80
    call WriteNrx4
    DELAY 8
    ldh a, [rNR52]
    jp Emit

; NR52 = $80 rewritten while on keeps the frame sequencer: steps until CH2 (length 2) stops.
TestPowerRewrite:
    APU_RESET
    ld a, $F0
    ldh [rNR22], a
    ld a, $3E
    ldh [rNR21], a
    ld a, $C0
    ldh [rNR24], a
    call WaitStep
    ld a, $80
    ldh [rNR52], a
    ld a, $02
    ld bc, 8
    jp CountSteps

SECTION "Length tests", ROM0
; Lengths 2, 4, 6, 8 written while off, then power on and trigger: steps until each channel stops.
TestLengthWhileOff:
    xor a
    ldh [rNR52], a
    ld a, $3E
    ldh [rNR11], a
    ld a, $3C
    ldh [rNR21], a
    ld a, $FA
    ldh [rNR31], a
    ld a, $38
    ldh [rNR41], a
    call WaitStep
    call WaitStep
    APU_RESET
    call DacsOn
    ld a, $C0
    call WriteNrx4
    ld a, $0F
    ld bc, 40
    jp CountSteps

; Lengths 4, 6, 8, 10 written while on, power cycled, then trigger: steps until each channel stops.
TestLengthPowerCycle:
    APU_RESET
    ld a, $3C
    ldh [rNR11], a
    ld a, $3A
    ldh [rNR21], a
    ld a, $F8
    ldh [rNR31], a
    ld a, $36
    ldh [rNR41], a
    APU_RESET
    call DacsOn
    ld a, $C0
    call WriteNrx4
    ld a, $0F
    ld bc, 24
    jp CountSteps

; Length 2 without enable: after 6 steps, NRx4 = $40 in a period's second half counts 2 more.
TestLengthDisabled:
    APU_RESET
    ld a, $3E
    ld b, $FE
    call WriteLengths
    call DacsOn
    ld a, $80
    call WriteNrx4
    ld a, $0F
    ld bc, 6
    call CountSteps
    ld a, $40
    call WriteNrx4
    ld a, $0F
    ld bc, 8
    jp CountSteps

; Lengths 1, 2, 1, 2 enabled right after step 0 (first half) clock once at once; NR52, then steps.
TestLengthEnableFirstHalf:
    APU_RESET
    ld a, $3F
    ldh [rNR11], a
    ld a, $3E
    ldh [rNR21], a
    ldh [rNR41], a
    ld a, $FF
    ldh [rNR31], a
    call DacsOn
    ld a, $80
    call WriteNrx4
    call WaitStep
    ld a, $40
    call WriteNrx4
    ldh a, [rNR52]
    call Emit
    ld a, $0F
    ld bc, 8
    jp CountSteps

; Length 4 keeps counting while the DACs stop the channels; NR52, then steps after retriggering.
TestLengthWhileStopped:
    APU_RESET
    ld a, $3C
    ld b, $FC
    call WriteLengths
    call DacsOn
    ld a, $C0
    call WriteNrx4
    call DacsOff
    ldh a, [rNR52]
    call Emit
    REPT 4
        call WaitStep
    ENDR
    call DacsOn
    ld a, $C0
    call WriteNrx4
    ld a, $0F
    ld bc, 12
    jp CountSteps

; Length 63 (255 on CH3) counting for 4 steps, then length 1 written: steps until each stops.
TestLengthRewrite:
    APU_RESET
    ld a, $01
    ld b, $01
    call WriteLengths
    call DacsOn
    ld a, $C0
    call WriteNrx4
    REPT 4
        call WaitStep
    ENDR
    ld a, $3F
    ld b, $FF
    call WriteLengths
    ld a, $0F
    ld bc, 8
    jp CountSteps

; Length 1 stops each channel at step 0; steps after retriggering at length 0 in each half.
TestLengthReload:
    APU_RESET
    ld a, $3F
    ld b, $FF
    call WriteLengths
    call DacsOn
    ld a, $C0
    call WriteNrx4
    ld a, $0F
    ld bc, 8
    call CountSteps
    ld a, $C0
    call WriteNrx4
    ld a, $0F
    ld bc, 600
    call CountSteps
    call WaitStep
    ld a, $C0
    call WriteNrx4
    ld a, $0F
    ld bc, 600
    jp CountSteps

SECTION "Timing tests", ROM0
; DIV written in cycle W + 1020 + k (k = 0-7) with CH2 at length 1 enabled; NR52 two cycles later.
TestDivWrite:
    ld hl, DivWriteProbe
    ld b, 8
    jp Sweep

DivWriteProbe:
    SLIDE_ENTRY WriteReadEnd
    ld bc, rNR52
    ld de, rDIV
    APU_RESET
    ld a, $F0
    ldh [rNR22], a
    ld a, $3F
    ldh [rNR21], a
    ld a, $C0
    ldh [rNR24], a
    DELAY 991
    call JumpHl
    jp Emit

; All channels at length 1 enabled; NR52 read in cycle W + 2045 + k (k = 0-7), around step 0.
TestLengthEdge:
    ld hl, LengthEdgeProbe
    ld b, 8
    jp Sweep

LengthEdgeProbe:
    SLIDE_ENTRY ProbeRead
    ld bc, rNR52
    APU_RESET
    ld a, $3F
    ldh [rNR11], a
    ldh [rNR21], a
    ldh [rNR41], a
    ld a, $FF
    ldh [rNR31], a
    ld a, $F0
    ldh [rNR12], a
    ldh [rNR22], a
    ldh [rNR42], a
    ld a, $80
    ldh [rNR30], a
    ld a, $C0
    ldh [rNR14], a
    ldh [rNR24], a
    ldh [rNR34], a
    ldh [rNR44], a
    DELAY 1985
    call JumpHl
    jp Emit

; CH2 at length 1, NR24 = $40 written in cycle W + 4092 + k (around step 1); NR52 two cycles later.
TestExtraClockEdge:
    ld hl, ExtraClockProbe
    ld b, 8
    jp Sweep

ExtraClockProbe:
    SLIDE_ENTRY WriteReadEnd
    ld bc, rNR52
    ld de, rNR24
    APU_RESET
    ld a, $F0
    ldh [rNR22], a
    ld a, $3F
    ldh [rNR21], a
    ld a, $80
    ldh [rNR24], a
    DELAY 4061
    ld a, $40
    call JumpHl
    jp Emit

; DIV written in W, power-on in W + 1019-1022 or 1024-1027, CH2 length 2: NR52 after the 2nd edge.
TestPowerOnSkip:
    ld hl, PowerOnProbe
    ld b, 8
    jp Sweep

PowerOnProbe:
    ldh a, [hK]
    cp 4
    jr c, .early
    inc a
.early:
    SLIDE_ENTRY_A WriteReadEnd
    ld bc, rNR52
    ld de, rNR52
    xor a
    ldh [rNR52], a
    ldh [rDIV], a
    ld a, $3E
    ldh [rNR21], a
    DELAY 1003
    ld a, $80
    call JumpHl
    ld a, $F0
    ldh [rNR22], a
    ld a, $C0
    ldh [rNR24], a
    DELAY 3900
    ldh a, [rNR52]
    jp Emit

; CH4 triggered an odd (\1 = 0) or even cycle after power-on; NR52 read 2 + \2 cycles later.
MACRO NOISE_START
    ld hl, rNR44
    ld bc, rNR52
    APU_RESET
    ld a, $F0
    ldh [rNR42], a
    REPT \1
        nop
    ENDR
    ld a, $80
    ld [hl], a
    IF \2 == 0
        ld a, [bc]
    ELSE
        ldh a, [rNR52]
    ENDC
    call Emit
ENDM

; Channel with NRx4 \1 triggered with DAC register \2 = \3 at an odd cycle; NR52 2 cycles later.
MACRO TRIGGER_START
    ld hl, \1
    ld bc, rNR52
    APU_RESET
    ld a, \3
    ldh [\2], a
    ld a, $80
    ld [hl], a
    ld a, [bc]
    call Emit
ENDM

; Trigger delays in NR52: CH1-CH3 right after a trigger, CH4 for both cycle parities.
TestTriggerStart:
    TRIGGER_START rNR14, rNR12, $F0
    TRIGGER_START rNR24, rNR22, $F0
    TRIGGER_START rNR34, rNR30, $80
    NOISE_START 0, 0
    NOISE_START 0, 1
    NOISE_START 1, 0
    NOISE_START 1, 1
    ret

SECTION "Sweep tests", ROM0
; CH1 triggered after power-on with sweep register \1 and frequency \2; NR52 read 32 cycles later.
MACRO SWEEP_TRIGGER
    APU_RESET
    ld a, \1
    ldh [rNR10], a
    ld a, $F0
    ldh [rNR12], a
    ld a, LOW(\2)
    ldh [rNR13], a
    ld a, $80 | HIGH(\2)
    ldh [rNR14], a
    DELAY 32
    ldh a, [rNR52]
    call Emit
ENDM

; Overflow check at trigger for several sweep shifts, directions and frequencies.
TestSweepTrigger:
    SWEEP_TRIGGER $01, $7FF
    SWEEP_TRIGGER $07, $7FF
    SWEEP_TRIGGER $01, $555
    SWEEP_TRIGGER $01, $556
    SWEEP_TRIGGER $0F, $7FF
    SWEEP_TRIGGER $00, $7FF
    SWEEP_TRIGGER $70, $7FF
    SWEEP_TRIGGER $07, $7F0
    ret

; Probe k: CH1 triggered from off at $7FF with sweep \1; NR52 read 3 + k cycles after the trigger.
MACRO SWEEP_DELAY_PROBE
    SLIDE_ENTRY ProbeRead
    ld bc, rNR52
    ld de, rNR14
    APU_RESET
    ld a, \1
    ldh [rNR10], a
    ld a, $F0
    ldh [rNR12], a
    ld a, $FF
    ldh [rNR13], a
    ld a, $87
    call WriteJump
    jp Emit
ENDM

; Probe k: CH1 playing, sweep \1 written, retriggered at $7FF; NR52 read 3 + k cycles later.
MACRO SWEEP_RETRIGGER_PROBE
    SLIDE_ENTRY ProbeRead
    ld bc, rNR52
    ld de, rNR14
    APU_RESET
    ld a, $F0
    ldh [rNR12], a
    ld a, $FF
    ldh [rNR13], a
    ld a, $87
    ldh [rNR14], a
    DELAY 16
    ld a, \1
    ldh [rNR10], a
    ld a, $87
    call WriteJump
    jp Emit
ENDM

SweepDelayProbe1:
    SWEEP_DELAY_PROBE $01
SweepDelayProbe2:
    SWEEP_DELAY_PROBE $02
SweepDelayProbe7:
    SWEEP_DELAY_PROBE $07
SweepRetriggerProbe2:
    SWEEP_RETRIGGER_PROBE $02
SweepRetriggerProbe7:
    SWEEP_RETRIGGER_PROBE $07

; How long an overflowing trigger keeps CH1 on: shifts 1, 2, 7 from off, 2 and 7 from playing.
TestSweepTriggerDelay:
    ld hl, SweepDelayProbe1
    ld b, 12
    call Sweep
    ld hl, SweepDelayProbe2
    ld b, 12
    call Sweep
    ld hl, SweepDelayProbe7
    ld b, 12
    call Sweep
    ld hl, SweepRetriggerProbe2
    ld b, 12
    call Sweep
    ld hl, SweepRetriggerProbe7
    ld b, 12
    jp Sweep

; CH1 triggered with sweep \1 and frequency \2: DIV-APU steps until NR52 bit 0 clears (max 32).
MACRO SWEEP_STEPS
    APU_RESET
    ld a, \1
    ldh [rNR10], a
    ld a, $F0
    ldh [rNR12], a
    ld a, LOW(\2)
    ldh [rNR13], a
    ld a, $80 | HIGH(\2)
    ldh [rNR14], a
    ld a, $01
    ld bc, 32
    call CountSteps
ENDM

; Overflow at sweep clocks for several periods, shifts and frequencies, then period 0 and shift 0.
TestSweepSteps:
    SWEEP_STEPS $11, $500
    SWEEP_STEPS $21, $500
    SWEEP_STEPS $11, $200
    SWEEP_STEPS $12, $555
    SWEEP_STEPS $17, $7F0
    SWEEP_STEPS $19, $400
    SWEEP_STEPS $71, $500
    SWEEP_STEPS $01, $500
    SWEEP_STEPS $00, $7FF
    SWEEP_STEPS $10, $400
    SWEEP_STEPS $10, $3FF
    SWEEP_STEPS $18, $400
    ret

; CH1 with sweep \1 at \2 triggered, \3 steps waited: NR52 before and after NR10 = \4.
MACRO SWEEP_NEGATE
    APU_RESET
    ld a, \1
    ldh [rNR10], a
    ld a, $F0
    ldh [rNR12], a
    ld a, LOW(\2)
    ldh [rNR13], a
    ld a, $80 | HIGH(\2)
    ldh [rNR14], a
    DELAY 32
    REPT \3
        call WaitStep
    ENDR
    ldh a, [rNR52]
    call Emit
    ld a, \4
    ldh [rNR10], a
    ldh a, [rNR52]
    call Emit
ENDM

; NR10 writes that leave or keep negate after negate calculations, and after adds to 2047 and 2046.
TestSweepNegate:
    SWEEP_NEGATE $09, $400, 0, $01
    SWEEP_NEGATE $08, $400, 0, $00
    SWEEP_NEGATE $18, $400, 3, $10
    SWEEP_NEGATE $19, $400, 0, $1A
    SWEEP_NEGATE $01, $555, 0, $01
    SWEEP_NEGATE $01, $554, 0, $01
    ret

; Probe k: CH1 sweep period 1, shift \1, frequency \2; NR52 read in W + \3 + k (first sweep clock).
MACRO SWEEP_CLOCK_PROBE
    SLIDE_ENTRY ProbeRead
    ld bc, rNR52
    APU_RESET
    ld a, $10 | (\1)
    ldh [rNR10], a
    ld a, $F0
    ldh [rNR12], a
    ld a, LOW(\2)
    ldh [rNR13], a
    ld a, $80 | HIGH(\2)
    ldh [rNR14], a
    DELAY (\3) - 34
    call JumpHl
    jp Emit
ENDM

SweepClockProbe0:
    SWEEP_CLOCK_PROBE 0, $400, 6141
SweepClockProbe1:
    SWEEP_CLOCK_PROBE 1, $500, 6141
SweepClockProbe7:
    SWEEP_CLOCK_PROBE 7, $7F0, 6145

; When an overflow at the first sweep clock stops CH1, for shifts 0 and 1 and for shift 7.
TestSweepClockTiming:
    ld hl, SweepClockProbe0
    ld b, 12
    call Sweep
    ld hl, SweepClockProbe1
    ld b, 12
    call Sweep
    ld hl, SweepClockProbe7
    ld b, 12
    jp Sweep

SECTION "DAC tests", ROM0
; NRx2 values written after a trigger with NRx2 = $F0.
DacValues:
    db $00, $01, $07, $08, $0F, $10, $80, $F8
; NR30 values written after a trigger with NR30 = $80.
Nr30Values:
    db $00, $7F, $80, $FF

; NR52 after each DAC value on CH1, CH2 and CH4 (NRx2) and CH3 (NR30).
TestDac:
    ld c, LOW(rNR12)
    call DacChannel
    ld c, LOW(rNR22)
    call DacChannel
    ld c, LOW(rNR42)
    call DacChannel
    ld hl, Nr30Values
    ld b, 4
.wave:
    APU_RESET
    ld a, $80
    ldh [rNR30], a
    ldh [rNR34], a
    DELAY 8
    ld a, [hl+]
    ldh [rNR30], a
    ldh a, [rNR52]
    call Emit
    dec b
    jr nz, .wave
    ret

; For the NRx2 at C: trigger with $F0, write each DacValues entry 8 cycles later, emit NR52.
DacChannel:
    ld hl, DacValues
    ld b, 8
.loop:
    APU_RESET
    ld a, $F0
    ldh [c], a
    inc c
    inc c
    ld a, $80
    ldh [c], a
    dec c
    dec c
    DELAY 8
    ld a, [hl+]
    ldh [c], a
    ldh a, [rNR52]
    call Emit
    dec b
    jr nz, .loop
    ret

; Per channel: NR52 after a trigger with the DAC off, and after DAC off and on while playing.
TestDacTrigger:
    ld c, LOW(rNR12)
    call DacTriggerChannel
    ld c, LOW(rNR22)
    call DacTriggerChannel
    ld c, LOW(rNR42)
    call DacTriggerChannel
    APU_RESET
    ld a, $80
    ldh [rNR34], a
    DELAY 8
    ldh a, [rNR52]
    call Emit
    ld a, $80
    ldh [rNR30], a
    ldh [rNR34], a
    DELAY 8
    xor a
    ldh [rNR30], a
    ld a, $80
    ldh [rNR30], a
    ldh a, [rNR52]
    jp Emit

; For the NRx2 at C: trigger with $07, then with $F0 followed by $00 and $F0; NR52 after each.
DacTriggerChannel:
    APU_RESET
    ld a, $07
    ldh [c], a
    inc c
    inc c
    ld a, $80
    ldh [c], a
    DELAY 8
    ldh a, [rNR52]
    call Emit
    dec c
    dec c
    ld a, $F0
    ldh [c], a
    inc c
    inc c
    ld a, $80
    ldh [c], a
    dec c
    dec c
    DELAY 8
    xor a
    ldh [c], a
    ld a, $F0
    ldh [c], a
    ldh a, [rNR52]
    jp Emit

; Envelopes reaching volume 0 keep their channels on: steps until NR52 clears (at most 24).
TestEnvelopeZero:
    APU_RESET
    ld a, $11
    ldh [rNR12], a
    ldh [rNR42], a
    ld a, $08
    ldh [rNR22], a
    ld a, $80
    ldh [rNR14], a
    ldh [rNR24], a
    ldh [rNR44], a
    ld a, $0B
    ld bc, 24
    jp CountSteps

SECTION "Wave tests", ROM0
; Probe k: CH3 at $7FB with pattern A; the Wave RAM byte at \1 read 3 + k cycles after the trigger.
MACRO WAVE_READ_PROBE
    APU_RESET
    ld hl, WavePatternA
    call LoadWave
    SLIDE_ENTRY ProbeRead
    ld bc, \1
    ld de, rNR34
    ld a, $80
    ldh [rNR30], a
    ld a, $20
    ldh [rNR32], a
    ld a, $FB
    ldh [rNR33], a
    ld a, $87
    call WriteJump
    jp Emit
ENDM

WaveReadProbe30:
    WAVE_READ_PROBE $FF30
WaveReadProbe3F:
    WAVE_READ_PROBE $FF3F

; Wave RAM reads while CH3 plays: $FF30 for k = 0-23 and $FF3F for k = 0-11.
TestWaveRead:
    ld hl, WaveReadProbe30
    ld b, 24
    call Sweep
    ld hl, WaveReadProbe3F
    ld b, 12
    jp Sweep

; Probe k: CH3 at $7FB; WAVE_MARK written 5 + k cycles after the trigger; index it reached or $FF.
WaveWriteProbe:
    APU_RESET
    ld hl, WavePatternA
    call LoadWave
    SLIDE_ENTRY WriteMarkEnd
    ld bc, _AUD3WAVERAM
    ld de, rNR34
    ld a, $80
    ldh [rNR30], a
    ld a, $20
    ldh [rNR32], a
    ld a, $FB
    ldh [rNR33], a
    ld a, $87
    call WriteJump
    xor a
    ldh [rNR30], a
    call FindMark
    jp Emit

; Wave RAM writes while CH3 plays (k = 0-15).
TestWaveWrite:
    ld hl, WaveWriteProbe
    ld b, 16
    jp Sweep

; Probe k: CH3 at $7FC retriggered 3 + k cycles after its trigger; Wave RAM bytes 0-3 after stopping.
WaveRetriggerProbe:
    APU_RESET
    ld hl, WavePatternA
    call LoadWave
    SLIDE_ENTRY WriteAgainEnd
    ld de, rNR34
    ld a, $80
    ldh [rNR30], a
    ld a, $20
    ldh [rNR32], a
    ld a, $FC
    ldh [rNR33], a
    ld a, $87
    call WriteJump
    xor a
    ldh [rNR30], a
    ld hl, _AUD3WAVERAM
    ld b, 4
.emit:
    ld a, [hl+]
    call Emit
    dec b
    jr nz, .emit
    ret

; Wave RAM corruption by retriggering CH3 while it plays (k = 0-47).
TestWaveRetrigger:
    ld hl, WaveRetriggerProbe
    ld b, 48
    jp Sweep
