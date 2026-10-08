; Battle scene: the hero's commands, the enemy's moves, HP bars, messages and animations.

DEF EN_X EQU 13
DEF EN_Y EQU 0
DEF HB_X EQU 1
DEF HB_Y EQU 6
DEF EN_ID EQU $00
DEF HB_ID EQU $24
DEF PLATFORM_ID EQU $48
DEF SPRITE_BYTES EQU 36 * 16
DEF HERO_LEVEL EQU 10

DEF FX_BURST  EQU 0  ; effect tiles, 4 per picture
DEF FX_BURST2 EQU 4
DEF FX_STAR   EQU 8
DEF FX_STAR2  EQU 12
DEF FX_SHIELD EQU 16
DEF FX_HEAL   EQU 20
DEF ENEMY_CX  EQU 128 + 8 - 8 ; effect centred on the enemy
DEF ENEMY_CY  EQU 24 + 16 - 8
DEF HERO_CX   EQU 32 + 8 - 8
DEF HERO_CY   EQU 72 + 16 - 8
DEF BGP_INVERT EQU %00_01_10_11
DEF BGP_DARK   EQU %11_11_10_10

; EnemyStats record: level, max HP, attack base and range, special every N turns (0 never), EXP.
DEF ES_LEVEL   EQU 0
DEF ES_MAXHP   EQU 1
DEF ES_BASE    EQU 2
DEF ES_RANGE   EQU 3
DEF ES_SPECIAL EQU 4
DEF ES_EXP     EQU 5
DEF ES_SIZE    EQU 6

SECTION "Battle", ROM0

BattleInit::
    call EnemySource
    ld de, VRAM_BG + EN_ID * 16
    ld bc, SPRITE_BYTES
    call Copy
    call HeroBackSource
    ld de, VRAM_BG + HB_ID * 16
    ld bc, SPRITE_BYTES
    call Copy
    ld hl, BattleTiles
    ld de, VRAM_BG + PLATFORM_ID * 16
    ld bc, BattleTiles.end - BattleTiles
    call Copy
    ld hl, EffectTiles
    ld de, VRAM_OBJ
    ld bc, EffectTiles.end - EffectTiles
    call Copy
    call ResetParty
    ld c, ES_MAXHP
    call EnemyStat
    ld [wEnemyMaxHp], a
    ld [wEnemyHp], a
    xor a
    ld [wTurn], a
    ld [wGuard], a
    ld [wResult], a
    ld [wCursor], a
    ld de, MAP0
    ld a, SPACE
    call ResetShadow
    call DrawEnemy
    ld bc, (12 << 8) | 6
    ld de, (8 << 8) | 1
    ld a, PLATFORM_ID
    call DrawSequence
    inc c
    ld a, PLATFORM_ID + 16
    call DrawSequence
    call DrawHero
    call DrawEnemyPanel
    call DrawHeroPanel
    draw_box 0, 12, 20, 6
    call ShadowToVram
    xor a
    ldh [hBGP], a
    ldh [hOBP0], a
    ldh [hOBP1], a
    ld a, LCDC_ON
    call LcdOn
    ld a, SONG_BATTLE
    jp PlaySong

; Restores every hero's HP and the party's MP.
ResetParty::
    ld hl, wHeroHp
    ld bc, HERO_COUNT
    ld a, HERO_MAX_HP
    call Fill
    ld a, HERO_MAX_MP
    ld [wHeroMp], a
    ret

; Returns A = field C of the enemy record of wMode; keeps BC, DE and HL.
EnemyStat::
    push hl
    push de
    push bc
    ld a, [wMode]
    ld hl, EnemyStats
    ld b, ES_SIZE
    call IndexRecord
    pop bc
    push bc
    ld a, c
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ld a, [hl]
    pop bc
    pop de
    pop hl
    ret

; Returns HL = the back-view tiles of wHero.
HeroBackSource:
    ld a, [wHero]
    ld hl, HeroBackTiles
    ld b, a
    ld de, SPRITE_BYTES
.loop:
    ld a, b
    and a
    ret z
    add hl, de
    dec b
    jr .loop

DrawEnemy:
    ld bc, (EN_X << 8) | EN_Y
    ld de, (6 << 8) | 6
    ld a, EN_ID
    jp DrawSequence

DrawHero:
    ld bc, (HB_X << 8) | HB_Y
    ld de, (6 << 8) | 6
    ld a, HB_ID
    jp DrawSequence

ClearHero:
    ld bc, (HB_X << 8) | HB_Y
    ld de, (6 << 8) | 6
    ld a, SPACE
    jp FillRect

DrawEnemyPanel:
    ld a, [wMode]
    ld hl, EnemyNames
    call ReadWord
    ld bc, (1 << 8) | 1
    call PrintAt
    ld bc, (1 << 8) | 2
    ld a, TILE_UI + 24
    call SetTile
    ld c, ES_LEVEL
    call EnemyStat
    call NumberToText
    ld hl, wNumberText
    ld bc, (2 << 8) | 2
    call PrintAt
    ld bc, (1 << 8) | 3
    call DrawBarFrame
    ; fall through
DrawEnemyBar:
    ld a, [wEnemyHp]
    ld d, a
    ld a, [wEnemyMaxHp]
    ld e, a
    ld bc, (3 << 8) | 3
    jp DrawBar

DrawHeroPanel:
    ld bc, (10 << 8) | 8
    ld de, (10 << 8) | 4
    ld a, SPACE
    call FillRect
    ld a, [wHero]
    ld hl, HeroNames
    call ReadWord
    ld bc, (10 << 8) | 8
    call PrintAt
    ld bc, (16 << 8) | 8
    ld a, TILE_UI + 24
    call SetTile
    print_at 17, 8, TextHeroLevel
    ld bc, (10 << 8) | 9
    call DrawBarFrame
    ld bc, (14 << 8) | 10
    ld a, '/' + TILE_FONT
    call SetTile
    ld a, HERO_MAX_HP
    ld bc, (15 << 8) | 10
    call PrintNumber3
    print_at 11, 11, TextMp
    call DrawMp
    ; fall through
DrawHeroBar:
    call HeroHpAddress
    ld a, [hl]
    push af
    ld bc, (11 << 8) | 10
    call PrintNumber3
    pop af
    ld d, a
    ld e, HERO_MAX_HP
    ld bc, (12 << 8) | 9
    jp DrawBar

DrawMp:
    ld a, [wHeroMp]
    add '0' + TILE_FONT
    ld bc, (14 << 8) | 11
    jp SetTile

; Returns HL = the HP of wHero.
HeroHpAddress:
    ld a, [wHero]
    ld hl, wHeroHp
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ret

; Draws the HP label and bar caps at (B, C); DrawBar fills the bar at x + 2.
DrawBarFrame:
    ld a, TILE_UI + 8
    call SetTile
    inc b
    ld a, TILE_UI + 9
    call SetTile
    ld a, b
    add 7
    ld b, a
    ld a, TILE_UI + 19
    jp SetTile

; Draws a six-tile HP bar at (B, C) of D * 48 / E pixels, at least one while D > 0.
DrawBar:
    ld a, d
    and a
    jr z, .draw
    push bc
    ld l, d
    ld h, 0
    add hl, hl
    add hl, hl
    add hl, hl
    add hl, hl
    ld b, h
    ld c, l
    add hl, hl
    add hl, bc ; 48 * HP
    ld c, 0
.divide:
    ld a, h
    and a
    jr nz, .subtract
    ld a, l
    cp e
    jr c, .quotient
.subtract:
    ld a, l
    sub e
    ld l, a
    jr nc, .noBorrow
    dec h
.noBorrow:
    inc c
    jr .divide
.quotient:
    ld a, c
    pop bc
    and a
    jr nz, .draw
    inc a
.draw:
    ld d, a
    ld e, 6
.tile:
    ld a, d
    cp 8
    jr c, .partial
    sub 8
    ld d, a
    ld a, 8
    jr .put
.partial:
    ld d, 0
.put:
    add TILE_UI + 10
    call SetTile
    inc b
    dec e
    jr nz, .tile
    ret

BattleUpdate::
    ld a, [wPhase]
    and a
    ret nz
    inc a
    ld [wPhase], a
    call FadeInFromWhite
    call RunBattle
    call FadeOutWhite
    ld a, SCENE_TITLE
    ld [wNextScene], a
    ret

RunBattle:
    ld hl, MsgAppeared
    call ShowMessage
    ld hl, MsgGo
    call ShowMessage
    ld b, 30
    call WaitFrames
.turn:
    call CommandMenu
    ld hl, .actions
    call JumpTable
    ld a, [wResult]
    and a
    ret nz
    ld a, [wEnemyHp]
    and a
    jr z, .won
    call EnemyTurn
    ld a, [wResult]
    and a
    ret nz
    jr .turn
.won:
    jp Victory
.actions:
    dw DoFight, DoMagic, DoGuard, DoRun, DoSwitch

; Runs the command box until a choice; returns A = ACT_*.
CommandMenu:
    call DrawCommands
.loop:
    call NextFrame
    ldh a, [hJoyPressed]
    bit PAD_START, a
    jp nz, .pause
    bit PAD_SELECT, a
    jr nz, .switch
    ldh a, [hJoyRepeat]
    and (1 << PAD_UP) | (1 << PAD_DOWN)
    jr z, .noRow
    ld a, [wCursor]
    xor 2
    jr .moved
.noRow:
    ldh a, [hJoyRepeat]
    and (1 << PAD_LEFT) | (1 << PAD_RIGHT)
    jr z, .noColumn
    ld a, [wCursor]
    xor 1
.moved:
    ld [wCursor], a
    ld a, SFX_CURSOR
    call PlaySfx
    call DrawCursor
    jr .loop
.noColumn:
    ldh a, [hJoyPressed]
    bit PAD_A, a
    jr z, .loop
    ld a, SFX_CONFIRM
    call PlaySfx
    ld a, [wCursor]
    ret
.switch:
    call NextAliveHero
    cp $FF
    jr z, .loop
    ld a, ACT_SWITCH
    ret
.pause:
    ld a, 1
    call PauseMusic
    call ClearMessageText
    print_at 5, 14, TextPause
.paused:
    call NextFrame
    ldh a, [hJoyPressed]
    bit PAD_START, a
    jr z, .paused
    xor a
    call PauseMusic
    call DrawCommands
    jr .loop

DrawCommands:
    call ClearMessageText
    ld bc, (1 << 8) | 13
    ld hl, MsgCommandPrompt
    call PrintExpanded
    print_at 3, 15, TextCommands
    ; fall through
DrawCursor:
    ld bc, (2 << 8) | 15
    ld de, (1 << 8) | 2
    ld a, SPACE
    call FillRect
    ld b, 11
    call FillRect
    ld a, [wCursor]
    ld b, 2
    bit 0, a
    jr z, .column
    ld b, 11
.column:
    ld c, 15
    bit 1, a
    jr z, .row
    inc c
.row:
    ld a, '>' + TILE_FONT
    jp SetTile

; The hero's actions, one per command.
DoFight:
    ld hl, MsgAttacks
    call ShowMessage
    call AnimateEnemyHit
    ld a, 5
    call RandomBelow
    add 8
    ld [wNumber], a
    ld a, 8
    call RandomBelow
    and a
    jr nz, .normal
    ld a, [wNumber]
    add a
    ld [wNumber], a
    ld hl, MsgCritical
    call ShowMessage
    ld b, 20
    call WaitFrames
.normal:
    call DamageEnemy
    ld hl, MsgEnemyTook
    jp ShowMessage

DoMagic:
    ld a, [wHeroMp]
    and a
    jr nz, .cast
    ld hl, MsgNoMp
    call ShowMessage
    jp CommandMenuAgain
.cast:
    dec a
    ld [wHeroMp], a
    call DrawMp
    ld hl, MsgUsedMagic
    call ShowMessage
    call AnimateMagic
    ld a, 7
    call RandomBelow
    add 14
    ld [wNumber], a
    call DamageEnemy
    ld hl, MsgEnemyTook
    jp ShowMessage

DoGuard:
    ld a, 1
    ld [wGuard], a
    ld hl, MsgGuards
    call ShowMessage
    call AnimateGuard
    call HeroHpAddress
    ld a, [hl]
    add 4
    cp HERO_MAX_HP + 1
    jr c, .capped
    ld a, HERO_MAX_HP
.capped:
    ld b, a
    ld a, b
    sub [hl]
    ld [wNumber], a
    ld a, b
    call AnimateHeroHp
    ld hl, MsgRecovered
    jp ShowMessage

DoRun:
    ld a, [wMode]
    and a
    jr z, .away
    cp 2
    jr z, .fail
    ld a, 2
    call RandomBelow
    and a
    jr nz, .fail
.away:
    ld a, SFX_SWITCH
    call PlaySfx
    ld hl, MsgGotAway
    call ShowMessage
    ld a, RESULT_RAN
    ld [wResult], a
    ret
.fail:
    ld hl, MsgCantEscape
    jp ShowMessage

DoSwitch:
    ld hl, MsgComeBack
    call ShowMessage
    ld b, 16
    call WaitFrames
    call NextAliveHero
    jp SendOut

; Sends out hero A in place of the current one, whose picture is still shown.
SendOut:
    push af
    call ClearHero
    ld b, 8
    call WaitFrames
    pop af
    ld [wHero], a
    call HeroBackSource
    ld de, VRAM_BG + HB_ID * 16
    ld bc, SPRITE_BYTES
    call StartStream
    call WaitStream
    call DrawHero
    call DrawHeroPanel
    ld a, SFX_SWITCH
    call PlaySfx
    ld hl, MsgGo
    call ShowMessage
    ld b, 30
    jp WaitFrames

; After a choice that took no turn (no MP), shows the menu again and runs the new choice.
CommandMenuAgain:
    call CommandMenu
    ld hl, RunBattle.actions
    jp JumpTable

; Returns A = the next hero after wHero with HP left, or $FF.
NextAliveHero:
    ld a, [wHero]
    ld c, HERO_COUNT - 1
.next:
    inc a
    and HERO_COUNT - 1
    ld b, a
    ld hl, wHeroHp
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ld a, [hl]
    and a
    ld a, b
    ret nz
    dec c
    jr nz, .next
    ld a, $FF
    ret

; Deals wNumber damage to the enemy, running its HP bar down one point per frame.
DamageEnemy:
    ld a, [wNumber]
    ld b, a
    ld a, [wEnemyHp]
    sub b
    jr nc, .left
    xor a
.left:
    ld b, a
.step:
    ld a, [wEnemyHp]
    cp b
    ret z
    dec a
    ld [wEnemyHp], a
    push bc
    call DrawEnemyBar
    call NextFrame
    pop bc
    jr .step

; Moves the current hero's HP to A, one point per frame.
AnimateHeroHp:
    ld b, a
.step:
    call HeroHpAddress
    ld a, [hl]
    cp b
    ret z
    jr c, .up
    dec [hl]
    jr .shown
.up:
    inc [hl]
.shown:
    push bc
    call DrawHeroBar
    call NextFrame
    pop bc
    jr .step

EnemyTurn:
    ld hl, wTurn
    inc [hl]
    ; the special move every N turns
    ld c, ES_SPECIAL
    call EnemyStat
    and a
    jr z, .normal
    ld b, a
    ld a, [wTurn]
.modulo:
    sub b
    jr z, .special
    jr nc, .modulo
.normal:
    xor a
    ld [wTemp2], a
    ld a, [wMode]
    ld hl, EnemyMoves
    jr .move
.special:
    ld a, 1
    ld [wTemp2], a
    ld a, [wMode]
    ld hl, EnemySpecials
.move:
    call ReadWord
    ld a, l
    ld [wMovePtr], a
    ld a, h
    ld [wMovePtr + 1], a
    ld hl, MsgEnemyUsed
    call ShowMessage
    ld a, [wTemp2]
    and a
    call nz, AnimateDark
    call AnimateHeroHit
    ; damage = base + random(range), doubled for the special, halved when guarding
    ld c, ES_RANGE
    call EnemyStat
    call RandomBelow
    ld b, a
    ld c, ES_BASE
    call EnemyStat
    add b
    ld b, a
    ld a, [wTemp2]
    and a
    jr z, .notDoubled
    sla b
.notDoubled:
    ld a, [wGuard]
    and a
    jr z, .notGuarded
    srl b
    jr nz, .notGuarded
    inc b
.notGuarded:
    xor a
    ld [wGuard], a
    ld a, b
    ld [wNumber], a
    call HeroHpAddress
    ld a, [hl]
    sub b
    jr nc, .left
    xor a
.left:
    call AnimateHeroHp
    ld hl, MsgHeroTook
    call ShowMessage
    call HeroHpAddress
    ld a, [hl]
    and a
    ret nz
    ; the hero fainted
    call HeroFaint
    ld hl, MsgFainted
    call ShowMessage
    call NextAliveHero
    cp $FF
    jp nz, SendOut
    jp Defeat

Victory:
    call EnemyFaint
    ld hl, MsgDefeated
    call ShowMessage
    ld a, SONG_VICTORY
    call PlaySong
    ld c, ES_EXP
    call EnemyStat
    ld [wNumber], a
    ld hl, MsgWon
    call ShowMessage
    ld a, RESULT_WON
    ld [wResult], a
    ret

Defeat:
    ld a, SONG_GAMEOVER
    call PlaySong
    ld hl, MsgAllDown
    call ShowMessage
    ld a, RESULT_LOST
    ld [wResult], a
    ret

; Animations.

; HL = OAM entry, B = Y, C = X, D = first tile, E = attributes: writes a 16 x 16 picture.
PutSprite16:
    ld a, b
    ld [hl+], a
    ld a, c
    ld [hl+], a
    ld a, d
    ld [hl+], a
    ld a, e
    ld [hl+], a
    ld a, b
    ld [hl+], a
    ld a, c
    add 8
    ld [hl+], a
    ld a, d
    add 2
    ld [hl+], a
    ld a, e
    ld [hl+], a
    ret

AnimateEnemyHit:
    ld a, SFX_HIT
    call PlaySfx
    ld c, 0
.frame:
    push bc
    ld a, c
    and 4
    ld d, FX_BURST
    jr z, .big
    ld d, FX_BURST2
.big:
    ld hl, ShakeTable
    ld a, c
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ld a, [hl]
    ldh [hSCX], a
    ld b, ENEMY_CY
    ld a, c
    and 3
    add ENEMY_CX - 2
    ld c, a
    ld e, 0
    ld hl, wShadowOam
    call PutSprite16
    call NextFrame
    pop bc
    inc c
    ld a, c
    cp 16
    jr c, .frame
    xor a
    ldh [hSCX], a
    ld hl, wShadowOam
    ld b, 2
    jp HideObjects

AnimateHeroHit:
    ld a, SFX_HURT
    call PlaySfx
    ld c, 0
.frame:
    ld hl, ShakeTable
    ld a, c
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ld a, [hl]
    ldh [hSCX], a
    ldh [hSCY], a
    push bc
    call NextFrame
    pop bc
    inc c
    ld a, c
    cp 16
    jr c, .frame
    xor a
    ldh [hSCX], a
    ldh [hSCY], a
    ret

; Darkens the screen for a moment for the special move.
AnimateDark:
    ld a, BGP_DARK
    ldh [hBGP], a
    ld b, 20
    call WaitFrames
    ld a, BGP_NORMAL
    ldh [hBGP], a
    ret

; Four stars fly out from the enemy while the screen flashes.
AnimateMagic:
    ld a, SFX_MAGIC
    call PlaySfx
    ld c, 0
.frame:
    push bc
    ; flash for the first 12 frames
    ld a, c
    cp 12
    ld a, BGP_NORMAL
    jr nc, .palette
    bit 1, c
    jr z, .palette
    ld a, BGP_INVERT
.palette:
    ldh [hBGP], a
    ld a, c
    srl a
    ld [wTemp], a ; radius
    ld a, c
    and 4
    ld d, FX_STAR
    jr z, .tile
    ld d, FX_STAR2
.tile:
    ld e, 0
    ld hl, wShadowOam + 8
    ; right
    ld b, ENEMY_CY
    ld a, [wTemp]
    add ENEMY_CX
    ld c, a
    call PutSprite16
    ; left
    ld a, [wTemp]
    ld b, a
    ld a, ENEMY_CX
    sub b
    ld c, a
    ld b, ENEMY_CY
    call PutSprite16
    ; down
    ld a, [wTemp]
    add ENEMY_CY
    ld b, a
    ld c, ENEMY_CX
    call PutSprite16
    ; up
    ld a, [wTemp]
    ld b, a
    ld a, ENEMY_CY
    sub b
    ld b, a
    ld c, ENEMY_CX
    call PutSprite16
    call NextFrame
    pop bc
    inc c
    ld a, c
    cp 24
    jr c, .frame
    ld a, BGP_NORMAL
    ldh [hBGP], a
    ld hl, wShadowOam + 8
    ld b, 8
    jp HideObjects

; A shield over the hero, blinking, for 32 frames.
AnimateGuard:
    ld a, SFX_HEAL
    call PlaySfx
    ld c, 0
.frame:
    push bc
    ld hl, wShadowOam + 40
    ld b, HERO_CY
    ld a, c
    and 8
    jr z, .shown
    ld b, 0
.shown:
    ld c, HERO_CX
    ld d, FX_SHIELD
    ld e, 0
    call PutSprite16
    call NextFrame
    pop bc
    inc c
    ld a, c
    cp 32
    jr c, .frame
    ld hl, wShadowOam + 40
    ld b, 2
    jp HideObjects

; The enemy sinks out of sight: six steps of one tile row, four frames each.
EnemyFaint:
    ld a, SFX_FAINT
    call PlaySfx
    ld a, 1
.step:
    ld [wTemp2], a
    ; rows EN_Y .. EN_Y + k - 1 blank, then rows 0 .. 5 - k of the picture
    ld bc, (EN_X << 8) | EN_Y
    ld d, 6
    ld e, a
    ld a, SPACE
    call FillRect
    ld a, [wTemp2]
    cp 6
    jr z, .wait
    add c
    ld c, a
    ld a, 6
    ld hl, wTemp2
    sub [hl]
    ld e, a
    ld a, EN_ID
    call DrawSequence
.wait:
    ld b, 4
    call WaitFrames
    ld a, [wTemp2]
    inc a
    cp 7
    jr c, .step
    ret

; The hero sinks out of sight the same way.
HeroFaint:
    ld a, SFX_FAINT
    call PlaySfx
    ld a, 1
.step:
    ld [wTemp2], a
    ld bc, (HB_X << 8) | HB_Y
    ld d, 6
    ld e, a
    ld a, SPACE
    call FillRect
    ld a, [wTemp2]
    cp 6
    jr z, .wait
    add c
    ld c, a
    ld a, 6
    ld hl, wTemp2
    sub [hl]
    ld e, a
    ld a, HB_ID
    call DrawSequence
.wait:
    ld b, 4
    call WaitFrames
    ld a, [wTemp2]
    inc a
    cp 7
    jr c, .step
    ret

SECTION "Battle tables", ROM0
ShakeTable: db 2, -2, 3, -3, 2, -2, 1, -1, 1, -1, 0, 0, 0, 0, 0, 0

TextHeroLevel:    db "10", 0
TextMp:           db "MP  /3", 0
TextPause:        db "- PAUSE -", 0
TextCommands:     db "FIGHT    MAGIC", TX_LINE, "GUARD    RUN", 0

MsgCommandPrompt: db TX_HERO, " - command?", 0
MsgAppeared:      db "A wild ", TX_ENEMY, TX_LINE, "appeared!", TX_WAIT, 0
MsgGo:            db "Go, ", TX_HERO, "!", 0
MsgAttacks:       db TX_HERO, " attacks!", 0
MsgCritical:      db "A critical hit!", 0
MsgEnemyTook:     db TX_ENEMY, " took", TX_LINE, TX_NUM, " damage!", TX_WAIT, 0
MsgNoMp:          db "Not enough MP!", TX_WAIT, 0
MsgUsedMagic:     db TX_HERO, " used", TX_LINE, TX_MAGIC, "!", 0
MsgGuards:        db TX_HERO, " is", TX_LINE, "guarding!", 0
MsgRecovered:     db TX_HERO, " recovered", TX_LINE, TX_NUM, " HP!", TX_WAIT, 0
MsgGotAway:       db "Got away safely!", TX_WAIT, 0
MsgCantEscape:    db "Can't escape!", TX_WAIT, 0
MsgComeBack:      db TX_HERO, ", come back!", 0
MsgEnemyUsed:     db TX_ENEMY, " used", TX_LINE, TX_MOVE, "!", 0
MsgHeroTook:      db TX_HERO, " took", TX_LINE, TX_NUM, " damage!", TX_WAIT, 0
MsgFainted:       db TX_HERO, " fainted!", TX_WAIT, 0
MsgDefeated:      db TX_ENEMY, TX_LINE, "was defeated!", TX_WAIT, 0
MsgWon:           db TX_HERO, " won! Got", TX_LINE, TX_NUM, " EXP points!", TX_WAIT, 0
MsgAllDown:       db "Every hero is down", TX_LINE, "... GAME OVER", TX_WAIT, 0
