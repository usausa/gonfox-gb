; Music and sound effects. Original compositions, MIT licensed with the rest of the demo.

; Note bytes: 0 holds the channel, 1 silences it, n >= 2 plays MIDI note n + 34.
DEF NOTE_OFFSET EQU 34
DEF __  EQU NOTE_OFFSET     ; hold (and "no drum")
DEF OFF EQU NOTE_OFFSET + 1 ; silence
DEF NOTE_FIRST EQU 2
DEF KICK  EQU 1
DEF SNARE EQU 2
DEF HAT   EQU 3
DEF MUSIC_PANNING EQU %1111_1111

; MIDI note numbers (C4 = 60), octaves 2-7; s = sharp.
FOR OCT, 2, 8
    DEF C{d:OCT}  EQU 12 * OCT + 12
    DEF Cs{d:OCT} EQU 12 * OCT + 13
    DEF D{d:OCT}  EQU 12 * OCT + 14
    DEF Ds{d:OCT} EQU 12 * OCT + 15
    DEF E{d:OCT}  EQU 12 * OCT + 16
    DEF F{d:OCT}  EQU 12 * OCT + 17
    DEF Fs{d:OCT} EQU 12 * OCT + 18
    DEF G{d:OCT}  EQU 12 * OCT + 19
    DEF Gs{d:OCT} EQU 12 * OCT + 20
    DEF A{d:OCT}  EQU 12 * OCT + 21
    DEF As{d:OCT} EQU 12 * OCT + 22
    DEF B{d:OCT}  EQU 12 * OCT + 23
ENDR

MACRO step ; melody, harmony, bass (as it sounds), drum
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

; Song header (loop: 1 repeats, 0 stops after the last pattern), followed by the pattern order.
MACRO song ; tempo, loop, nr11, nr12, nr21, nr22, nr32, wave, kit
    db \1, \2, \3, \4, \5, \6, \7, \8
    dw \9
ENDM

SECTION "Music code", ROM0

MusicInit::
    xor a
    ldh [rNR52], a
    ld a, $80
    ldh [rNR52], a
    ld a, $77
    ldh [rNR50], a
    ld a, MUSIC_PANNING
    ldh [rNR51], a
    xor a
    ld [wSongActive], a
    ld [wMusicPaused], a
    ld [wSfx2Frames], a
    ld [wSfx4Frames], a
    ld [wSongId], a
    ret

; Starts song A (SONG_*); SONG_NONE silences the music.
PlaySong::
    ld [wSongId], a
    push af
    xor a
    ld [wSongActive], a
    call SilenceSong
    pop af
    and a
    ret z
    ld hl, Songs
    call ReadWord ; HL = header
    ld a, [hl+]
    ld [wTempo], a
    ld a, [hl+]
    ld [wSongLoop], a
    ld de, wMelodyDuty
    ld c, 6
.instrument:
    ld a, [hl+]
    ld [de], a
    inc de
    dec c
    jr nz, .instrument
    ld a, [hl+]
    ld [wDrumKit], a
    ld a, [hl+]
    ld [wDrumKit + 1], a
    ; the order follows the header
    ld a, l
    ld [wOrderStart], a
    ld [wOrderPtr], a
    ld a, h
    ld [wOrderStart + 1], a
    ld [wOrderPtr + 1], a
    call LoadWave
    call LoadPattern
    xor a
    ld [wRow], a
    ld [wTempoAcc], a
    inc a
    ld [wSongActive], a
    jp PlayRow

; A = 1 pauses (the channels go silent, nothing advances), 0 resumes.
PauseMusic::
    ld [wMusicPaused], a
    and a
    ld a, MUSIC_PANNING
    jr z, .route
    xor a
.route:
    ldh [rNR51], a
    ret

SilenceSong:
    xor a
    ldh [rNR12], a
    ldh [rNR30], a
    ld a, [wSfx2Frames]
    and a
    jr nz, .keep2
    xor a
    ldh [rNR22], a
.keep2:
    ld a, [wSfx4Frames]
    and a
    ret nz
    xor a
    ldh [rNR42], a
    ret

LoadWave:
    xor a
    ldh [rNR30], a
    ld a, [wBassWave]
    swap a ; 16 bytes per wave
    ld hl, WaveTables
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ld c, LOW(rWAVE)
    REPT 16
        ld a, [hl+]
        ldh [c], a
        inc c
    ENDR
    ret

; Points wPattern at the next pattern of the order; at its end, loops or stops the song.
LoadPattern:
    ld hl, wOrderPtr
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    ld a, [hl+]
    ld e, a
    ld a, [hl+]
    ld d, a
    or e
    jr nz, .found
    ld a, [wSongLoop]
    and a
    jr z, .stop
    ld a, [wOrderStart]
    ld [wOrderPtr], a
    ld a, [wOrderStart + 1]
    ld [wOrderPtr + 1], a
    jr LoadPattern
.stop:
    xor a
    ld [wSongActive], a
    ret
.found:
    ld a, l
    ld [wOrderPtr], a
    ld a, h
    ld [wOrderPtr + 1], a
    ld a, e
    ld [wPattern], a
    ld a, d
    ld [wPattern + 1], a
    ret

; Once per VBlank: steps the effects and, on a tempo carry, the song.
MusicUpdate::
    ld a, [wMusicPaused]
    and a
    ret nz
    call UpdateSfx
    ld a, [wSongActive]
    and a
    ret z
    ld a, [wTempo]
    ld b, a
    ld a, [wTempoAcc]
    add b
    ld [wTempoAcc], a
    ret nc
    ld a, [wRow]
    inc a
    and 15
    ld [wRow], a
    jr nz, PlayRow
    call LoadPattern
    ld a, [wSongActive]
    and a
    ret z
; Plays the notes and the drum of row wRow.
PlayRow:
    ld a, [wRow]
    add a
    add a
    ld hl, wPattern
    ld e, [hl]
    inc hl
    ld d, [hl]
    add e
    ld l, a
    adc d
    sub l
    ld h, a
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
; Plays drum A (1-3) on CH4 unless a noise effect is playing.
PlayDrum:
    ld b, a
    ld a, [wSfx4Frames]
    and a
    ret nz
    ld a, b
    dec a
    add a
    ld hl, wDrumKit
    ld e, [hl]
    inc hl
    ld d, [hl]
    add e
    ld l, a
    adc d
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

PlayMelody:
    cp OFF - NOTE_OFFSET
    jr z, .off
    call NotePeriod
    ld a, [wMelodyDuty]
    ldh [rNR11], a
    ld a, [wMelodyEnv]
    ldh [rNR12], a
    ld a, e
    ldh [rNR13], a
    ld a, d
    or $80
    ldh [rNR14], a
    ret
.off:
    xor a
    ldh [rNR12], a
    ret

PlayHarmony:
    ld b, a
    ld a, [wSfx2Frames]
    and a
    ret nz
    ld a, b
    cp OFF - NOTE_OFFSET
    jr z, .off
    call NotePeriod
    ld a, [wHarmonyDuty]
    ldh [rNR21], a
    ld a, [wHarmonyEnv]
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

; Returns in DE the period of note byte A (>= NOTE_FIRST).
NotePeriod:
    sub NOTE_FIRST
    add a
    ld hl, NoteTable
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ld a, [hl+]
    ld e, a
    ld d, [hl]
    ret

; Starts sound effect A (SFX_*), which borrows CH2 or CH4 until it ends.
PlaySfx::
    push hl
    push de
    push bc
    ld hl, SfxTable
    call ReadWord
    ld a, [hl+]
    cp 4
    jr z, .noise
    ld a, [hl+]
    ld [wSfx2Duty], a
    ld a, [hl+]
    ld [wSfx2Env], a
    ld a, l
    ld [wSfx2Ptr], a
    ld a, h
    ld [wSfx2Ptr + 1], a
    ld a, 1
    ld [wSfx2Frames], a ; start at the next update
    jr .done
.noise:
    ld a, l
    ld [wSfx4Ptr], a
    ld a, h
    ld [wSfx4Ptr + 1], a
    ld a, 1
    ld [wSfx4Frames], a
.done:
    pop bc
    pop de
    pop hl
    ret

UpdateSfx:
    ld a, [wSfx2Frames]
    and a
    jr z, .noise
    dec a
    ld [wSfx2Frames], a
    jr nz, .noise
    ; next CH2 step
    ld hl, wSfx2Ptr
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    ld a, [hl+]
    and a
    jr z, .end2
    ld b, a
    ld a, [hl+]
    ld [wSfx2Frames], a
    ld a, l
    ld [wSfx2Ptr], a
    ld a, h
    ld [wSfx2Ptr + 1], a
    ld a, b
    call NotePeriod
    ld a, [wSfx2Duty]
    ldh [rNR21], a
    ld a, [wSfx2Env]
    ldh [rNR22], a
    ld a, e
    ldh [rNR23], a
    ld a, d
    or $80
    ldh [rNR24], a
    jr .noise
.end2:
    xor a
    ldh [rNR22], a
.noise:
    ld a, [wSfx4Frames]
    and a
    ret z
    dec a
    ld [wSfx4Frames], a
    ret nz
    ld hl, wSfx4Ptr
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    ld a, [hl+]
    and a
    jr z, .end4
    ld b, a
    ld a, [hl+]
    ld c, a
    ld a, [hl+]
    ld [wSfx4Frames], a
    ld a, l
    ld [wSfx4Ptr], a
    ld a, h
    ld [wSfx4Ptr + 1], a
    xor a
    ldh [rNR41], a
    ld a, c
    ldh [rNR42], a
    ld a, b
    ldh [rNR43], a
    ld a, $80
    ldh [rNR44], a
    ret
.end4:
    xor a
    ldh [rNR42], a
    ret

SECTION "Music tables", ROM0
; Pulse periods 2048 - 131072 / f for MIDI notes 36-96 (C2-C7), indexed by note byte - NOTE_FIRST.
NoteTable:
    dw   44,  157,  263,  363,  457,  547,  631,  711,  786,  856,  923,  986 ; octave 2
    dw 1046, 1102, 1155, 1205, 1253, 1297, 1339, 1379, 1417, 1452, 1486, 1517 ; octave 3
    dw 1547, 1575, 1602, 1627, 1650, 1673, 1694, 1714, 1732, 1750, 1767, 1783 ; octave 4
    dw 1798, 1812, 1825, 1837, 1849, 1860, 1871, 1881, 1890, 1899, 1907, 1915 ; octave 5
    dw 1923, 1930, 1936, 1943, 1949, 1954, 1959, 1964, 1969, 1974, 1978, 1982 ; octave 6
    dw 1985                                                                   ; octave 7

WaveTables:
    db $01, $23, $45, $67, $89, $AB, $CD, $EF, $FE, $DC, $BA, $98, $76, $54, $32, $10 ; 0 triangle
    db $FF, $FF, $FF, $FF, $FF, $FF, $FF, $FF, $00, $00, $00, $00, $00, $00, $00, $00 ; 1 square
    db $8A, $CD, $EE, $FF, $FF, $EE, $DC, $A8, $75, $32, $11, $00, $00, $11, $23, $57 ; 2 sine-like

; Drum kits: (NR42, NR43) for kick, snare, hat.
KitRock:  db $D1, $54, $B2, $33, $71, $01
KitMarch: db $D1, $55, $A3, $23, $61, $01
KitSoft:  db $91, $55, $72, $33, $41, $01

Songs:
    dw 0, SongTitle, SongSelect, SongBattle, SongVictory, SongGameOver

; Title: C major, about 119 BPM.
SongTitle:
    song 34, 1, $80, $A5, $40, $63, $20, 0, KitMarch
    dw TitleA, TitleB, TitleC, TitleD, TitleE, TitleF, TitleG, TitleH, 0

TitleA:
    step C5,  E4,  C2,  KICK
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
    step E5,  C5,  C3,  SNARE
    step __,  __,  __,  __
    step G5,  G4,  __,  HAT
    step __,  __,  __,  __
    step C6,  E4,  C2,  KICK
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
    step __,  C5,  C3,  SNARE
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
TitleB:
    step B5,  G4,  G2,  KICK
    step __,  __,  __,  __
    step __,  B4,  __,  HAT
    step __,  __,  __,  __
    step __,  D5,  G3,  SNARE
    step __,  __,  __,  __
    step A5,  B4,  __,  HAT
    step __,  __,  __,  __
    step G5,  G4,  G2,  KICK
    step __,  __,  __,  __
    step __,  B4,  __,  HAT
    step __,  __,  __,  __
    step __,  D5,  G3,  SNARE
    step __,  __,  __,  __
    step __,  B4,  __,  HAT
    step __,  __,  __,  __
TitleC:
    step A5,  A4,  A2,  KICK
    step __,  __,  __,  __
    step __,  C5,  __,  HAT
    step __,  __,  __,  __
    step C6,  E5,  A3,  SNARE
    step __,  __,  __,  __
    step __,  C5,  __,  HAT
    step __,  __,  __,  __
    step B5,  A4,  A2,  KICK
    step __,  __,  __,  __
    step __,  C5,  __,  HAT
    step __,  __,  __,  __
    step A5,  E5,  A3,  SNARE
    step __,  __,  __,  __
    step __,  C5,  __,  HAT
    step __,  __,  __,  __
TitleD:
    step G5,  A4,  F2,  KICK
    step __,  __,  __,  __
    step __,  C5,  __,  HAT
    step __,  __,  __,  __
    step __,  F5,  F3,  SNARE
    step __,  __,  __,  __
    step __,  C5,  __,  HAT
    step __,  __,  __,  __
    step __,  A4,  F2,  KICK
    step __,  __,  __,  __
    step __,  C5,  __,  HAT
    step __,  __,  __,  __
    step F5,  F5,  F3,  SNARE
    step __,  __,  __,  __
    step __,  C5,  __,  HAT
    step __,  __,  __,  __
TitleE:
    step E5,  E4,  C2,  KICK
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
    step G5,  C5,  C3,  SNARE
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
    step C6,  E4,  C2,  KICK
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
    step E6,  C5,  C3,  SNARE
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
TitleF:
    step D6,  G4,  G2,  KICK
    step __,  __,  __,  __
    step __,  B4,  __,  HAT
    step __,  __,  __,  __
    step __,  D5,  G3,  SNARE
    step __,  __,  __,  __
    step C6,  B4,  __,  HAT
    step __,  __,  __,  __
    step B5,  G4,  G2,  KICK
    step __,  __,  __,  __
    step __,  B4,  __,  HAT
    step __,  __,  __,  __
    step __,  D5,  G3,  SNARE
    step __,  __,  __,  __
    step __,  B4,  __,  HAT
    step __,  __,  __,  __
TitleG:
    step A5,  A4,  F2,  KICK
    step __,  __,  __,  __
    step __,  C5,  __,  HAT
    step __,  __,  __,  __
    step C6,  F5,  F3,  SNARE
    step __,  __,  __,  __
    step __,  C5,  __,  HAT
    step __,  __,  __,  __
    step B5,  B4,  G2,  KICK
    step __,  __,  __,  __
    step __,  D5,  __,  HAT
    step __,  __,  __,  __
    step D6,  G5,  G3,  SNARE
    step __,  __,  __,  __
    step __,  D5,  __,  HAT
    step __,  __,  __,  __
TitleH:
    step C6,  E4,  C2,  KICK
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
    step __,  C5,  C3,  SNARE
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
    step __,  E4,  G2,  KICK
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
    step G5,  B4,  G3,  SNARE
    step __,  __,  __,  SNARE
    step E5,  D5,  __,  SNARE
    step __,  __,  __,  SNARE

; Select: F major, about 94 BPM, gentle.
SongSelect:
    song 27, 1, $40, $83, $00, $41, $40, 2, KitSoft
    dw SelectA, SelectB, SelectC, SelectD, 0

SelectA:
    step A5,  F4,  F2,  KICK
    step __,  __,  __,  __
    step C6,  A4,  __,  HAT
    step __,  __,  __,  __
    step A5,  C5,  __,  __
    step __,  __,  __,  __
    step F5,  A4,  __,  HAT
    step __,  __,  __,  __
    step G5,  F4,  C3,  __
    step __,  __,  __,  __
    step __,  A4,  __,  HAT
    step __,  __,  __,  __
    step A5,  C5,  __,  __
    step __,  __,  __,  __
    step __,  A4,  __,  HAT
    step __,  __,  __,  __
SelectB:
    step F5,  D4,  D2,  KICK
    step __,  __,  __,  __
    step __,  F4,  __,  HAT
    step __,  __,  __,  __
    step D5,  A4,  __,  __
    step __,  __,  __,  __
    step __,  F4,  __,  HAT
    step __,  __,  __,  __
    step E5,  D4,  A2,  __
    step __,  __,  __,  __
    step __,  F4,  __,  HAT
    step __,  __,  __,  __
    step F5,  A4,  __,  __
    step __,  __,  __,  __
    step __,  F4,  __,  HAT
    step __,  __,  __,  __
SelectC:
    step D6,  D4,  As2, KICK
    step __,  __,  __,  __
    step __,  F4,  __,  HAT
    step __,  __,  __,  __
    step __,  As4, __,  __
    step __,  __,  __,  __
    step C6,  F4,  __,  HAT
    step __,  __,  __,  __
    step As5, D4,  F2,  __
    step __,  __,  __,  __
    step __,  F4,  __,  HAT
    step __,  __,  __,  __
    step A5,  As4, __,  __
    step __,  __,  __,  __
    step __,  F4,  __,  HAT
    step __,  __,  __,  __
SelectD:
    step G5,  E4,  C2,  KICK
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
    step __,  C5,  __,  __
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
    step E5,  E4,  G2,  __
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __
    step C5,  C5,  __,  __
    step __,  __,  __,  __
    step __,  G4,  __,  HAT
    step __,  __,  __,  __

; Battle: A minor, about 150 BPM.
SongBattle:
    song 43, 1, $80, $A3, $40, $82, $40, 1, KitRock
    dw BattleA, BattleB, BattleC, BattleD, BattleE, BattleF, BattleG, BattleH, 0

BattleA:
    step A5,  A4,  A2,  KICK
    step __,  __,  __,  __
    step A5,  E5,  A3,  HAT
    step G5,  __,  __,  __
    step A5,  C5,  A2,  SNARE
    step __,  __,  __,  __
    step C6,  E5,  A3,  KICK
    step __,  __,  __,  __
    step B5,  A4,  A2,  KICK
    step __,  __,  __,  __
    step A5,  E5,  A3,  HAT
    step __,  __,  __,  __
    step E5,  C5,  A2,  SNARE
    step __,  __,  __,  __
    step __,  E5,  A3,  HAT
    step __,  __,  __,  __
BattleB:
    step F5,  A4,  A2,  KICK
    step __,  __,  __,  __
    step G5,  E5,  A3,  HAT
    step __,  __,  __,  __
    step A5,  C5,  A2,  SNARE
    step __,  __,  __,  __
    step B5,  E5,  A3,  KICK
    step __,  __,  __,  __
    step C6,  A4,  A2,  KICK
    step __,  __,  __,  __
    step D6,  E5,  A3,  HAT
    step __,  __,  __,  __
    step E6,  C5,  A2,  SNARE
    step __,  __,  __,  __
    step __,  E5,  A3,  SNARE
    step __,  __,  __,  __
BattleC:
    step F6,  A4,  F2,  KICK
    step __,  __,  __,  __
    step __,  C5,  F3,  HAT
    step __,  __,  __,  __
    step E6,  F5,  F2,  SNARE
    step __,  __,  __,  __
    step C6,  C5,  F3,  KICK
    step __,  __,  __,  __
    step A5,  A4,  F2,  KICK
    step __,  __,  __,  __
    step __,  C5,  F3,  HAT
    step __,  __,  __,  __
    step C6,  F5,  F2,  SNARE
    step __,  __,  __,  __
    step __,  C5,  F3,  HAT
    step __,  __,  __,  __
BattleD:
    step D6,  B4,  G2,  KICK
    step __,  __,  __,  __
    step __,  D5,  G3,  HAT
    step __,  __,  __,  __
    step B5,  G5,  G2,  SNARE
    step __,  __,  __,  __
    step G5,  D5,  G3,  KICK
    step __,  __,  __,  __
    step B5,  B4,  G2,  KICK
    step __,  __,  __,  __
    step D6,  D5,  G3,  HAT
    step __,  __,  __,  __
    step G6,  G5,  G2,  SNARE
    step __,  __,  __,  __
    step __,  D5,  G3,  SNARE
    step __,  __,  __,  SNARE
BattleE:
    step E6,  A4,  A2,  KICK
    step __,  __,  __,  __
    step D6,  E5,  A3,  HAT
    step __,  __,  __,  __
    step C6,  C5,  A2,  SNARE
    step __,  __,  __,  __
    step B5,  E5,  A3,  KICK
    step __,  __,  __,  __
    step A5,  A4,  A2,  KICK
    step __,  __,  __,  __
    step __,  E5,  A3,  HAT
    step __,  __,  __,  __
    step E5,  C5,  A2,  SNARE
    step __,  __,  __,  __
    step A5,  E5,  A3,  HAT
    step __,  __,  __,  __
BattleF:
    step C6,  A4,  A2,  KICK
    step __,  __,  __,  __
    step B5,  E5,  A3,  HAT
    step __,  __,  __,  __
    step A5,  C5,  A2,  SNARE
    step __,  __,  __,  __
    step G5,  E5,  A3,  KICK
    step __,  __,  __,  __
    step A5,  A4,  A2,  KICK
    step __,  __,  __,  __
    step B5,  E5,  A3,  HAT
    step __,  __,  __,  __
    step C6,  C5,  A2,  SNARE
    step __,  __,  __,  __
    step D6,  E5,  A3,  HAT
    step __,  __,  __,  __
BattleG:
    step C6,  A4,  F2,  KICK
    step __,  __,  __,  __
    step __,  C5,  F3,  HAT
    step __,  __,  __,  __
    step A5,  F5,  F2,  SNARE
    step __,  __,  __,  __
    step __,  C5,  F3,  KICK
    step __,  __,  __,  __
    step F5,  A4,  F2,  KICK
    step __,  __,  __,  __
    step A5,  C5,  F3,  HAT
    step __,  __,  __,  __
    step C6,  F5,  F2,  SNARE
    step __,  __,  __,  __
    step F6,  C5,  F3,  HAT
    step __,  __,  __,  __
BattleH:
    step E6,  Gs4, E2,  KICK
    step __,  __,  __,  __
    step __,  B4,  E3,  HAT
    step __,  __,  __,  __
    step __,  E5,  E2,  SNARE
    step __,  __,  __,  __
    step D6,  B4,  E3,  KICK
    step __,  __,  __,  __
    step C6,  Gs4, E2,  KICK
    step __,  __,  __,  __
    step B5,  B4,  E3,  HAT
    step __,  __,  __,  __
    step Gs5, E5,  E2,  SNARE
    step __,  __,  __,  SNARE
    step __,  B4,  E3,  SNARE
    step __,  __,  __,  SNARE

; Victory: a short C major fanfare (does not loop).
SongVictory:
    song 40, 0, $80, $D4, $40, $A4, $20, 0, KitMarch
    dw VictoryA, VictoryB, 0

VictoryA:
    step C5,  E4,  C3,  SNARE
    step __,  __,  __,  __
    step E5,  G4,  __,  SNARE
    step __,  __,  __,  __
    step G5,  C5,  __,  SNARE
    step __,  __,  __,  __
    step C6,  E5,  C2,  KICK
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step G5,  C5,  G2,  SNARE
    step __,  __,  __,  __
    step C6,  E5,  C3,  KICK
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
VictoryB:
    step E6,  G5,  C3,  KICK
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step OFF, OFF, OFF, __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __

; Game over: a falling A minor phrase (does not loop).
SongGameOver:
    song 22, 0, $40, $A6, $00, $72, $40, 2, KitSoft
    dw GameOverA, GameOverB, 0

GameOverA:
    step E5,  C5,  A2,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step D5,  B4,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step C5,  A4,  F2,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step B4,  Gs4, E2,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
GameOverB:
    step A4,  E4,  A2,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step OFF, OFF, OFF, __
    step __,  __,  __,  __
    step __,  __,  __,  __
    step __,  __,  __,  __

; Sound effects: channel 2 with duty, envelope and tones, or 4 with (NR43, NR42, frames); 0 ends.
MACRO tone ; note, frames
    db (\1) - NOTE_OFFSET, \2
ENDM
SfxTable:
    dw 0, SfxCursor, SfxConfirm, SfxCancel, SfxClick, SfxThud, SfxShine, SfxHit, SfxMagic, SfxHurt, SfxHeal
    dw SfxFaint, SfxSwitch, SfxKeyA, SfxKeyB, SfxKeySelect, SfxKeyStart, SfxKeyRight, SfxKeyLeft, SfxKeyUp, SfxKeyDown

SfxCursor:  db 2, $80, $A1
    tone E6, 3
    db 0
SfxConfirm: db 2, $80, $C2
    tone A5, 3
    tone E6, 8
    db 0
SfxCancel:  db 2, $80, $A2
    tone E5, 3
    tone A4, 8
    db 0
SfxClick:   db 2, $40, $81
    tone C6, 2
    db 0
SfxThud:    db 4
    db $71, $C3, 8
    db 0
SfxShine:   db 2, $40, $F3
    tone C6, 3
    tone E6, 3
    tone G6, 3
    tone C7, 10
    db 0
SfxHit:     db 4
    db $51, $F2, 4
    db $63, $A2, 8
    db 0
SfxMagic:   db 2, $80, $D2
    tone C5, 2
    tone E5, 2
    tone G5, 2
    tone C6, 2
    tone E6, 2
    tone G6, 2
    tone C7, 8
    db 0
SfxHurt:    db 4
    db $45, $F3, 4
    db $70, $C4, 10
    db 0
SfxHeal:    db 2, $40, $A4
    tone C5, 4
    tone E5, 4
    tone G5, 4
    tone C6, 10
    db 0
SfxFaint:   db 2, $C0, $F4
    tone G5, 5
    tone E5, 5
    tone C5, 5
    tone G4, 5
    tone C4, 14
    db 0
SfxSwitch:  db 2, $80, $B2
    tone C6, 3
    tone G5, 3
    tone C6, 8
    db 0
SfxKeyA:      db 2, $80, $C2
    tone C6, 10
    db 0
SfxKeyB:      db 2, $80, $C2
    tone A5, 10
    db 0
SfxKeySelect: db 2, $40, $C2
    tone E5, 10
    db 0
SfxKeyStart:  db 2, $40, $C2
    tone G5, 10
    db 0
SfxKeyRight:  db 2, $00, $C2
    tone D6, 10
    db 0
SfxKeyLeft:   db 2, $00, $C2
    tone B5, 10
    db 0
SfxKeyUp:     db 2, $C0, $C2
    tone F6, 10
    db 0
SfxKeyDown:   db 2, $C0, $C2
    tone G4, 10
    db 0

SECTION "Music variables", WRAM0
wSongActive::  db
wMusicPaused:: db
wSongLoop::    db
wTempo::       db
wTempoAcc::    db
wRow::         db
wOrderStart::  dw
wOrderPtr::    dw
wPattern::     dw
wMelodyDuty::  db ; six bytes in header order
wMelodyEnv::   db
wHarmonyDuty:: db
wHarmonyEnv::  db
wBassLevel::   db
wBassWave::    db
wDrumKit::     dw
wSfx2Ptr::     dw
wSfx2Frames::  db
wSfx2Duty::    db
wSfx2Env::     db
wSfx4Ptr::     dw
wSfx4Frames::  db
