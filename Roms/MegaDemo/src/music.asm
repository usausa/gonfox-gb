; Music: a 64-step loop of melody (CH1), arpeggio (CH2), bass (CH3, wave) and drums (CH4, noise).

DEF TEMPO_BASE      EQU 24 ; 1/256 steps per frame at speed 0
DEF TEMPO_PER_SPEED EQU 6
DEF SONG_STEPS      EQU 64
DEF EFFECT_FRAMES   EQU 8  ; frames the blip owns CH2
DEF MUSIC_PANNING   EQU %0111_1101 ; left: CH3 CH2 CH1, right: CH4 CH3 CH1

; Note bytes: 0 holds the channel, 1 silences it, n >= 2 plays MIDI note n + 34.
DEF NOTE_OFFSET EQU 34
DEF __  EQU NOTE_OFFSET     ; hold, or no drum
DEF OFF EQU NOTE_OFFSET + 1 ; silence
DEF NOTE_FIRST EQU 2

; MIDI numbers of the notes the song uses (C4 = 60).
DEF C2 EQU 36
DEF G2 EQU 43
DEF A2 EQU 45
DEF C3 EQU 48
DEF D3 EQU 50
DEF E3 EQU 52
DEF F2 EQU 41
DEF F3 EQU 53
DEF G3 EQU 55
DEF A3 EQU 57
DEF C4 EQU 60
DEF E4 EQU 64
DEF F4 EQU 65
DEF G4 EQU 67
DEF A4 EQU 69
DEF B4 EQU 71
DEF C5 EQU 72
DEF D5 EQU 74
DEF E5 EQU 76
DEF F5 EQU 77
DEF G5 EQU 79
DEF A5 EQU 81
DEF B5 EQU 83
DEF C6 EQU 84
DEF D6 EQU 86

DEF KICK  EQU 1
DEF SNARE EQU 2
DEF HAT   EQU 3

; One sixteenth: melody, harmony, bass and drum; the bass is stored an octave up for CH3.
MACRO step
    db (\1) - NOTE_OFFSET, (\2) - NOTE_OFFSET
    IF (\3) <= OFF
        db (\3) - NOTE_OFFSET
    ELSE
        db (\3) + 12 - NOTE_OFFSET
    ENDC
    IF (\4) == __
        db 0
    ELSE
        db \4
    ENDC
ENDM

SECTION "Song", ROM0, ALIGN[8]
; The song: chords Am, F, C, G, one bar each, in one page so a step indexes it directly.
Song::
    step E5,    A4,     A2,  KICK
    step __,    __,     __,  __
    step __,    C5,     __,  HAT
    step __,    __,     __,  __
    step C5,    E5,     A3,  SNARE
    step __,    __,     __,  __
    step E5,    C5,     __,  HAT
    step __,    __,     __,  __
    step A5,    A4,     A2,  KICK
    step __,    __,     __,  __
    step __,    C5,     __,  HAT
    step __,    __,     __,  __
    step G5,    E5,     E3,  SNARE
    step __,    __,     __,  __
    step E5,    C5,     __,  HAT
    step __,    __,     __,  __
    step F5,    F4,     F2,  KICK
    step __,    __,     __,  __
    step __,    A4,     __,  HAT
    step __,    __,     __,  __
    step A5,    C5,     F3,  SNARE
    step __,    __,     __,  __
    step C6,    A4,     __,  HAT
    step __,    __,     __,  __
    step A5,    F4,     F2,  KICK
    step __,    __,     __,  __
    step __,    A4,     __,  HAT
    step __,    __,     __,  __
    step G5,    C5,     C3,  SNARE
    step __,    __,     __,  __
    step F5,    A4,     __,  HAT
    step __,    __,     __,  __
    step E5,    C5,     C3,  KICK
    step __,    __,     __,  __
    step G5,    E5,     __,  HAT
    step __,    __,     __,  __
    step C6,    G5,     C4,  SNARE
    step __,    __,     __,  __
    step __,    E5,     __,  HAT
    step __,    __,     __,  __
    step B5,    C5,     C3,  KICK
    step __,    __,     __,  __
    step G5,    E5,     __,  HAT
    step __,    __,     __,  __
    step E5,    G5,     G2,  SNARE
    step __,    __,     __,  __
    step D5,    E5,     __,  HAT
    step __,    __,     __,  __
    step D5,    G4,     G2,  KICK
    step __,    __,     __,  __
    step __,    B4,     __,  HAT
    step __,    __,     __,  __
    step G5,    D5,     G3,  SNARE
    step __,    __,     __,  __
    step B5,    B4,     __,  HAT
    step __,    __,     __,  __
    step D6,    G4,     G2,  KICK
    step __,    __,     __,  __
    step __,    B4,     __,  HAT
    step __,    __,     __,  __
    step B5,    D5,     D3,  SNARE
    step __,    __,     __,  __
    step G5,    B4,     OFF, SNARE
    step OFF,   OFF,    __,  SNARE
    ASSERT @ - Song == SONG_STEPS * 4

SECTION "Music tables", ROM0
; Pulse periods 2048 - 131072 / f for MIDI notes 36-96 (C2-C7), indexed by note byte - NOTE_FIRST.
NoteTable::
    dw   44,  157,  263,  363,  457,  547,  631,  711,  786,  856,  923,  986 ; octave 2
    dw 1046, 1102, 1155, 1205, 1253, 1297, 1339, 1379, 1417, 1452, 1486, 1517 ; octave 3
    dw 1547, 1575, 1602, 1627, 1650, 1673, 1694, 1714, 1732, 1750, 1767, 1783 ; octave 4
    dw 1798, 1812, 1825, 1837, 1849, 1860, 1871, 1881, 1890, 1899, 1907, 1915 ; octave 5
    dw 1923, 1930, 1936, 1943, 1949, 1954, 1959, 1964, 1969, 1974, 1978, 1982 ; octave 6
    dw 1985                                                                   ; octave 7
.end:

; The amount blip: C5 D5 E5 G5 A5 C6 D6 E6 for amounts 0-7.
EffectPeriods::
    dw 1798, 1825, 1849, 1881, 1899, 1923, 1936, 1949

; Per scene: NR12 melody envelope, NR21 harmony duty, NR22 harmony envelope, NR32 bass level.
Instruments::
    db $A3, $40, $62, $20 ; wave
    db $87, $80, $53, $40 ; plasma
    db $C2, $00, $71, $40 ; orbit

; Per scene: 32 four-bit samples for the bass.
WaveTables::
    db $01, $23, $45, $67, $89, $AB, $CD, $EF, $FE, $DC, $BA, $98, $76, $54, $32, $10 ; triangle
    db $00, $11, $22, $33, $44, $55, $66, $77, $88, $99, $AA, $BB, $CC, $DD, $EE, $FF ; sawtooth
    db $FF, $FF, $FF, $FF, $FF, $FF, $FF, $FF, $00, $00, $00, $00, $00, $00, $00, $00 ; square

; Per scene: envelope (NR42) and LFSR control (NR43) of the kick, snare and hi-hat.
DrumKits::
    db $F1, $54, $C2, $33, $81, $01 ; wave
    db $F1, $55, $B2, $34, $51, $01 ; plasma
    db $F2, $64, $C2, $33, $71, $09 ; orbit

SECTION "Music code", ROM0
; Powers the APU on from a clean state and plays step 0.
MusicInit::
    xor a
    ldh [rNR52], a ; APU off clears all registers
    ld a, $80
    ldh [rNR52], a
    ld a, $77
    ldh [rNR50], a
    ld a, MUSIC_PANNING
    ldh [rNR51], a
    xor a
    ld [wSongStep], a
    ld [wTempoAcc], a
    ld [wEffectFrames], a
    ld [wMelodyNote], a
    ld [wHarmonyNote], a
    ld [wBassNote], a
    inc a
    ld [wMusicOn], a
    call LoadInstrument
    jp PlayStep

; Per shown frame from the main loop: stop/run, instrument, blip and tempo, then PlayStep.
UpdateMusic::
    ld a, [wAuto]
    ld hl, wMusicOn
    cp [hl]
    jr z, .running
    ld [hl], a
    and a
    jr nz, .resume
    ldh [rNR51], a ; route nothing
    ret
.resume:
    ld a, MUSIC_PANNING
    ldh [rNR51], a
    call SoundHeldNotes
.running:
    ld a, [wMusicOn]
    and a
    ret z
    ld a, [wScene]
    ld hl, wInstrument
    cp [hl]
    call nz, LoadInstrument
    ld a, [wSteps]
    and (1 << PAD_UP) | (1 << PAD_DOWN)
    call nz, PlayEffect
    ld hl, wEffectFrames
    ld a, [hl]
    and a
    jr z, .tempo
    dec [hl]
.tempo:
    ld a, [wSpeed]
    ld b, a
    add a
    add b
    add a ; 6 * speed
    ASSERT TEMPO_PER_SPEED == 6
    add TEMPO_BASE
    ld b, a
    ld a, [wTempoAcc]
    add b
    ld [wTempoAcc], a
    ret nc
    ld a, [wSongStep]
    inc a
    and SONG_STEPS - 1
    ld [wSongStep], a
; Plays the notes and the drum of step wSongStep.
PlayStep:
    ld a, [wSongStep]
    add a
    add a
    ld l, a
    ld h, HIGH(Song)
    ld a, [hl+]
    ld b, a
    ld a, [hl+]
    ld c, a
    ld a, [hl+]
    ld d, a
    ld e, [hl]
    push de
    push bc
    ld a, b
    and a
    call nz, PlayMelody
    pop bc
    ld a, c
    and a
    call nz, PlayHarmony
    pop de
    push de
    ld a, d
    and a
    call nz, PlayBass
    pop de
    ld a, e
    and a
    ret z
; Plays drum A (1-3) from the scene's kit on CH4.
PlayDrum:
    dec a
    add a
    ld b, a
    ld a, [wInstrument]
    ld c, a
    add a
    add c
    add a ; 6 bytes per kit
    add b
    add LOW(DrumKits)
    ld l, a
    adc HIGH(DrumKits)
    sub l
    ld h, a
    xor a
    ldh [rNR41], a
    ld a, [hl+]
    ldh [rNR42], a
    ld a, [hl]
    ldh [rNR43], a
    ld a, $80
    ldh [rNR44], a
    ret

; Plays note byte A (not 0); each Play routine keeps its note for SoundHeldNotes.
PlayMelody:
    ld [wMelodyNote], a
SoundMelody:
    cp OFF - NOTE_OFFSET
    jr z, .off
    call NotePeriod
    ld a, [wPattern]
    rrca
    rrca ; duty in bits 7-6
    ldh [rNR11], a
    ld a, [wMelodyEnvelope]
    ldh [rNR12], a
    ld a, e
    ldh [rNR13], a
    ld a, d
    or $80
    ldh [rNR14], a
    ret
.off:
    xor a
    ldh [rNR12], a ; DAC off stops the channel
    ret

PlayHarmony:
    ld [wHarmonyNote], a
SoundHarmony:
    ld b, a
    ld a, [wEffectFrames]
    and a
    ret nz
    ld a, b
    cp OFF - NOTE_OFFSET
    jr z, .off
    call NotePeriod
    ld a, [wHarmonyDuty]
    ldh [rNR21], a
    ld a, [wHarmonyEnvelope]
    ldh [rNR22], a
    ld a, e
    ldh [rNR23], a
    ld a, d
    or $80
    ldh [rNR24], a
    ret
.off:
    xor a
    ldh [rNR22], a
    ret

PlayBass:
    ld [wBassNote], a
SoundBass:
    cp OFF - NOTE_OFFSET
    jr z, .off
    call NotePeriod
    ; Stop CH3 first: on a DMG, a retrigger that meets a Wave RAM read corrupts Wave RAM.
    xor a
    ldh [rNR30], a
    ld a, $80
    ldh [rNR30], a
    ld a, [wBassLevel]
    ldh [rNR32], a
    ld a, e
    ldh [rNR33], a
    ld a, d
    or $80
    ldh [rNR34], a
    ret
.off:
    xor a
    ldh [rNR30], a
    ret

; On resume, sounds the notes held at the saved step again (not the drums).
SoundHeldNotes:
    ld a, [wMelodyNote]
    cp NOTE_FIRST
    call nc, SoundMelody
    ld a, [wHarmonyNote]
    cp NOTE_FIRST
    call nc, SoundHarmony
    ld a, [wBassNote]
    cp NOTE_FIRST
    ret c
    jr SoundBass

; Returns in DE the period of note byte A (>= NOTE_FIRST).
NotePeriod:
    sub NOTE_FIRST
    add a
    add LOW(NoteTable)
    ld l, a
    adc HIGH(NoteTable)
    sub l
    ld h, a
    ld a, [hl+]
    ld e, a
    ld d, [hl]
    ret

; Loads wScene's instrument set, writing Wave RAM with CH3 off, then restarts a held bass.
LoadInstrument:
    ld a, [wScene]
    ld [wInstrument], a
    add a
    add a
    add LOW(Instruments)
    ld l, a
    adc HIGH(Instruments)
    sub l
    ld h, a
    ld de, wMelodyEnvelope
    REPT 4
        ld a, [hl+]
        ld [de], a
        inc de
    ENDR
    xor a
    ldh [rNR30], a
    ld a, [wScene]
    swap a ; 16 bytes per wave
    add LOW(WaveTables)
    ld l, a
    adc HIGH(WaveTables)
    sub l
    ld h, a
    ld c, LOW(rWAVE)
    REPT 16
        ld a, [hl+]
        ldh [c], a
        inc c
    ENDR
    ld a, [wBassNote]
    cp NOTE_FIRST
    ret c
    jp SoundBass

; Plays the amount blip on CH2, pitched by the new amount; the arpeggio pauses meanwhile.
PlayEffect:
    ld a, EFFECT_FRAMES
    ld [wEffectFrames], a
    ld a, [wAmount]
    add a
    add LOW(EffectPeriods)
    ld l, a
    adc HIGH(EffectPeriods)
    sub l
    ld h, a
    ld a, $80 ; 50% duty
    ldh [rNR21], a
    ld a, $F1 ; volume 15, fading
    ldh [rNR22], a
    ld a, [hl+]
    ldh [rNR23], a
    ld a, [hl]
    or $80
    ldh [rNR24], a
    ret

; Music state at fixed addresses for debuggers and tests.
SECTION "Music observation", WRAM0[$C050]
wSongStep::        db
wTempoAcc::        db ; 1/256 step fraction
wMusicOn::         db ; 1 playing, 0 stopped
wEffectFrames::    db
wInstrument::      db ; scene of the loaded set
wMelodyNote::      db ; 0 none, 1 off, else a note
wHarmonyNote::     db
wBassNote::        db
wMelodyEnvelope::  db
wHarmonyDuty::     db
wHarmonyEnvelope:: db
wBassLevel::       db
