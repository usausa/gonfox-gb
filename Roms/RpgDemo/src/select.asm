; Select scene: choose the hero and the difficulty; changed pictures stream in during VBlanks.

DEF FACE_ID  EQU $00
DEF ENEMY_ID EQU $10
DEF FACE_BYTES  EQU 16 * 16
DEF ENEMY_BYTES EQU 36 * 16

SECTION "Select", ROM0

SelectInit::
    call FaceSource
    ld de, VRAM_BG + FACE_ID * 16
    ld bc, FACE_BYTES
    call Copy
    call EnemySource
    ld de, VRAM_BG + ENEMY_ID * 16
    ld bc, ENEMY_BYTES
    call Copy
    ld a, [wHero]
    ld [wFaceShown], a
    ld a, [wMode]
    ld [wEnemyShown], a
    ld de, MAP0
    ld a, SPACE
    call ResetShadow
    draw_box 0, 0, 20, 8
    draw_box 0, 8, 20, 10
    print_at 2, 1, TextChooseHero
    ld bc, (2 << 8) | 2
    ld de, (4 << 8) | 4
    ld a, FACE_ID
    call DrawSequence
    ld bc, (1 << 8) | 3
    ld a, '<' + TILE_FONT
    call SetTile
    ld b, 6
    ld a, '>' + TILE_FONT
    call SetTile
    call DrawHeroInfo
    print_at 2, 9, TextDifficulty
    print_at 3, 10, TextModes
    ld bc, (13 << 8) | 9
    ld de, (6 << 8) | 6
    ld a, ENEMY_ID
    call DrawSequence
    call DrawModeInfo
    print_at 1, 16, TextSelectHint
    call ShadowToVram
    xor a
    ldh [hBGP], a
    ldh [hOBP0], a
    ldh [hOBP1], a
    ld a, LCDC_ON
    call LcdOn
    ld a, SONG_SELECT
    jp PlaySong

SelectUpdate::
    ld a, [wPhase]
    and a
    jr nz, .running
    inc a
    ld [wPhase], a
    jp FadeInFromWhite
.running:
    call RefreshPicture
    ldh a, [hJoyRepeat]
    ld b, a
    ldh a, [hJoyPressed]
    or b
    ld b, a
    bit PAD_LEFT, b
    jr nz, .previousHero
    bit PAD_RIGHT, b
    jr nz, .nextHero
    bit PAD_SELECT, b
    jr nz, .nextHero
    bit PAD_UP, b
    jr nz, .easier
    bit PAD_DOWN, b
    jr nz, .harder
    ldh a, [hJoyPressed]
    bit PAD_B, a
    jr nz, .back
    and (1 << PAD_A) | (1 << PAD_START)
    ret z
    ld a, SFX_CONFIRM
    call PlaySfx
    call FadeOutWhite
    ld a, SCENE_BATTLE
    ld [wNextScene], a
    ret
.back:
    ld a, SFX_CANCEL
    call PlaySfx
    call FadeOutWhite
    ld a, SCENE_TITLE
    ld [wNextScene], a
    ret
.previousHero:
    ld a, [wHero]
    dec a
    jr .hero
.nextHero:
    ld a, [wHero]
    inc a
.hero:
    and HERO_COUNT - 1
    ld [wHero], a
    ld a, SFX_CURSOR
    call PlaySfx
    jp DrawHeroInfo
.easier:
    ld a, [wMode]
    and a
    ret z
    dec a
    jr .mode
.harder:
    ld a, [wMode]
    cp 2
    ret nc
    inc a
.mode:
    ld [wMode], a
    ld a, SFX_CURSOR
    call PlaySfx
    jp DrawModeInfo

; When no stream runs, starts the portrait or else the enemy that differs from the choice.
RefreshPicture:
    ld a, [wStreamLeft]
    ld c, a
    ld a, [wStreamLeft + 1]
    or c
    ret nz
    ld a, [wHero]
    ld hl, wFaceShown
    cp [hl]
    jr z, .enemy
    ld [hl], a
    call FaceSource
    ld de, VRAM_BG + FACE_ID * 16
    ld bc, FACE_BYTES
    jp StartStream
.enemy:
    ld a, [wMode]
    ld hl, wEnemyShown
    cp [hl]
    ret z
    ld [hl], a
    call EnemySource
    ld de, VRAM_BG + ENEMY_ID * 16
    ld bc, ENEMY_BYTES
    jp StartStream

; Returns HL = the portrait tiles of wHero.
FaceSource:
    ld a, [wHero]
    ld hl, HeroFaceTiles
    ld b, a
    ld de, FACE_BYTES
.loop:
    ld a, b
    and a
    ret z
    add hl, de
    dec b
    jr .loop

; Returns HL = the enemy tiles of wMode.
EnemySource::
    ld a, [wMode]
    ld hl, EnemyTiles
    ld b, a
    ld de, ENEMY_BYTES
.loop:
    ld a, b
    and a
    ret z
    add hl, de
    dec b
    jr .loop

DrawHeroInfo:
    ld bc, (8 << 8) | 2
    ld de, (11 << 8) | 4
    ld a, SPACE
    call FillRect
    ld a, [wHero]
    ld hl, HeroNames
    call ReadWord
    ld bc, (8 << 8) | 2
    call PrintAt
    print_at 8, 3, TextMagicLabel
    ld a, [wHero]
    ld hl, HeroMagic
    call ReadWord
    ld bc, (8 << 8) | 4
    call PrintAt
    print_at 8, 5, TextHeroNumber
    ld a, [wHero]
    add '1' + TILE_FONT
    ld bc, (13 << 8) | 5
    jp SetTile

DrawModeInfo:
    ; cursor
    ld bc, (2 << 8) | 10
    ld de, (1 << 8) | 5
    ld a, SPACE
    call FillRect
    ld a, [wMode]
    add a
    add 10
    ld c, a
    ld b, 2
    ld a, '>' + TILE_FONT
    call SetTile
    ; enemy name
    ld bc, (10 << 8) | 15
    ld de, (9 << 8) | 1
    ld a, SPACE
    call FillRect
    ld a, [wMode]
    ld hl, EnemyNames
    call ReadWord
    ld bc, (10 << 8) | 15
    jp PrintAt

TextChooseHero: db "CHOOSE YOUR HERO", 0
TextMagicLabel: db "MAGIC:", 0
TextHeroNumber: db "HERO  /8", 0
TextDifficulty: db "DIFFICULTY", 0
TextModes:      db "EASY", TX_LINE, TX_LINE, "NORMAL", TX_LINE, TX_LINE, "NIGHTMARE", 0
TextSelectHint: db "A:START  B:BACK", 0

SECTION "Select variables", WRAM0
wFaceShown::  db ; hero whose portrait is shown
wEnemyShown:: db ; difficulty whose enemy is shown
