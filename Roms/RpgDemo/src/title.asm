; Title scene: a pan down the landscape, dropping logo letters, walking heroes and the menu.

DEF TITLE_ROW0    EQU 14   ; top map row after the pan
DEF PAN_FRAMES    EQU 160
DEF PAN_END       EQU 112  ; SCY after the pan
DEF TITLE_SCX     EQU 252  ; the 19-tile logo, centred
DEF LOGO_LETTERS  EQU 10
DEF DROP_EVERY    EQU 7    ; frames between letter starts
DEF LETTER_SLOTS  EQU 3
DEF CHIBI_OAM     EQU LETTER_SLOTS * 6 ; first OAM entry of the walkers
DEF BOX_X EQU 3
DEF BOX_Y EQU 14
DEF DROP_TOUCH    EQU 11   ; DropTable frame of the first touch

DEF TP_PAN   EQU 0
DEF TP_LOGO  EQU 1
DEF TP_FLASH EQU 2
DEF TP_IDLE  EQU 3
DEF TP_MENU  EQU 4

SECTION "Title", ROM0

TitleInit::
    ld hl, TitleTiles
    ld de, VRAM_BG
    ld bc, TitleTiles.end - TitleTiles
    call Copy
    ld hl, LogoTiles
    ld de, VRAM_BG + TITLE_TILES * 16
    ld bc, LogoTiles.end - LogoTiles
    call Copy
    ld hl, LogoObjTiles
    ld de, VRAM_OBJ
    ld bc, LogoObjTiles.end - LogoObjTiles
    call Copy
    ; The whole map, 32 columns: the 20 columns of the picture, then its first 12 again.
    ld hl, TitleMap
    ld de, MAP0
    ld c, 32
.row:
    push bc
    push hl
    ld bc, 20
    call Copy
    pop hl
    ld bc, 12
    call Copy
    ld bc, 8
    add hl, bc
    pop bc
    dec c
    jr nz, .row
    ; Shadow = map rows 14-31 as loaded (nothing pending).
    ld de, MAP0 + TITLE_ROW0 * 32
    xor a
    call ResetShadow
    ld hl, TitleMap + TITLE_ROW0 * 20
    ld de, wMapShadow
    ld bc, MAP_W * MAP_H
    call Copy
    ld a, TITLE_SCX
    ldh [hSCX], a
    xor a
    ldh [hSCY], a
    ld [wTitleTimer], a
    ld [wLettersStarted], a
    ld [wLettersDone], a
    ld [wCursor], a
    ld hl, wLetterSlots
    ld bc, LETTER_SLOTS * 2
    ld a, $FF
    call Fill
    ld a, %11_11_11_11
    ldh [hBGP], a
    ld a, TP_PAN
    ld [wPhase], a
    call InitWalkers
    ld a, LCDC_ON
    call LcdOn
    ld a, SONG_TITLE
    jp PlaySong

TitleUpdate::
    ld a, [wPhase]
    ld hl, .phases
    jp JumpTable
.phases:
    dw TitlePan, TitleLogo, TitleFlash, TitleIdle, TitleMenu

; Skips the pan and the drop when START or A is pressed. Returns NZ if it did.
TitleSkip:
    ldh a, [hJoyPressed]
    and (1 << PAD_START) | (1 << PAD_A)
    ret z
    ld a, PAN_END
    ldh [hSCY], a
    ld a, BGP_NORMAL
    ldh [hBGP], a
    ld hl, wShadowOam
    ld b, CHIBI_OAM
    call HideObjects
    ld c, 0
.stamp:
    push bc
    ld a, c
    call StampLetter
    pop bc
    inc c
    ld a, c
    cp LOGO_LETTERS
    jr c, .stamp
    call StartFlash
    or 1
    ret

TitlePan:
    call TitleSkip
    ret nz
    ld a, [wTitleTimer]
    inc a
    ld [wTitleTimer], a
    ; Fade in from black over the first 16 frames.
    cp 16
    jr nc, .scroll
    srl a
    srl a
    ld hl, FadeFromBlack
    ld c, a
    add a
    add c
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ld a, [hl]
    ldh [hBGP], a
.scroll:
    ld a, [wTitleTimer]
    ld hl, PanTable
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ld a, [hl]
    ldh [hSCY], a
    ld a, [wTitleTimer]
    cp PAN_FRAMES
    ret c
    ld a, BGP_NORMAL
    ldh [hBGP], a
    xor a
    ld [wTitleTimer], a
    ld a, TP_LOGO
    ld [wPhase], a
    ret

TitleLogo:
    call TitleSkip
    ret nz
    ; Start a letter every DROP_EVERY frames.
    ld a, [wTitleTimer]
    inc a
    ld [wTitleTimer], a
    cp DROP_EVERY
    jr c, .move
    xor a
    ld [wTitleTimer], a
    ld a, [wLettersStarted]
    cp LOGO_LETTERS
    jr nc, .move
    call FreeLetterSlot ; HL = slot, or carry if none
    jr c, .move
    ld a, [wLettersStarted]
    ld [hl+], a ; letter
    xor a
    ld [hl], a  ; frame in DropTable
    ld hl, wLettersStarted
    inc [hl]
.move:
    ld c, 0
.slot:
    push bc
    call MoveLetter
    pop bc
    inc c
    ld a, c
    cp LETTER_SLOTS
    jr c, .slot
    ld a, [wLettersDone]
    cp LOGO_LETTERS
    ret c
    jp StartFlash

; Returns HL = a free slot (letter $FF), or carry.
FreeLetterSlot:
    ld hl, wLetterSlots
    ld b, LETTER_SLOTS
.next:
    ld a, [hl]
    cp $FF
    ret z
    inc hl
    inc hl
    dec b
    jr nz, .next
    scf
    ret

; Advances slot C's letter one frame and draws its six objects, or stamps it when landed.
MoveLetter:
    ld a, c
    add a
    ld hl, wLetterSlots
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ld a, [hl+]
    cp $FF
    jp z, .hide
    ld [wTemp], a ; letter
    ld a, [hl]
    inc [hl]
    ; offset = DropTable[frame]
    ld de, DropTable
    add e
    ld e, a
    adc d
    sub e
    ld d, a
    ld a, [de]
    cp $80
    jp z, .landed
    ld b, a ; signed offset
    ; the thud on the first touch (offset 0 at frame DROP_TOUCH)
    ld a, [hl]
    cp DROP_TOUCH + 1
    jr nz, .draw
    push bc
    ld a, SFX_THUD
    call PlaySfx
    pop bc
.draw:
    ; letter info: shape, column, row
    push bc
    ld a, [wTemp]
    ld hl, LetterInfo
    ld b, 3
    call IndexRecord
    pop bc
    ld a, [hl+]
    ld [wTemp2], a ; shape
    ld a, [hl+]
    ld e, a ; column
    ld a, [hl] ; row
    add a
    add a
    add a ; target y in pixels
    add b ; + drop offset
    add 16
    ld d, a ; OAM Y of the top objects
    ; OAM entries: slot * 6
    ld a, c
    add a
    add c
    add a
    add a
    add a ; slot * 24 bytes
    ld hl, wShadowOam
    add l
    ld l, a
    ; X = column * 8 + 12
    ld a, e
    add a
    add a
    add a
    add 12
    ld c, a
    ; tile = (shape * 3) * 4
    ld a, [wTemp2]
    ld b, a
    add a
    add b
    add a
    add a
    ld e, a
    REPT 3
        ld a, d
        ld [hl+], a
        ld a, c
        ld [hl+], a
        ld a, e
        ld [hl+], a
        ld a, $10 ; OBP1
        ld [hl+], a
        ld a, d
        add 16
        ld [hl+], a
        ld a, c
        ld [hl+], a
        ld a, e
        add 2
        ld [hl+], a
        ld a, $10
        ld [hl+], a
        ld a, c
        add 8
        ld c, a
        ld a, e
        add 4
        ld e, a
    ENDR
    ret
.landed:
    dec hl
    ld [hl], $FF
    push bc
    ld a, [wTemp]
    call StampLetter
    ld hl, wLettersDone
    inc [hl]
    pop bc
.hide:
    ld a, c
    add a
    add c
    add a
    add a
    add a
    ld hl, wShadowOam
    add l
    ld l, a
    ld b, 6
    jp HideObjects

; A = letter 0-9: writes its 3 x 3 tiles into the shadow map.
StampLetter:
    ld hl, LetterInfo
    ld b, 3
    call IndexRecord
    ld a, [hl+] ; shape
    ld b, a
    add a
    add b ; 3 * shape: logo map column
    ld e, a
    ld a, [hl+]
    ld b, a ; x
    ld a, [hl]
    ld c, a ; y
    ld hl, LogoMap
    ld d, 0
    add hl, de
    ld d, 3
.row:
    push hl
    push bc
    REPT 3
        ld a, [hl+]
        call SetTile
        inc b
    ENDR
    pop bc
    pop hl
    ld a, 24
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    inc c
    dec d
    jr nz, .row
    ret

StartFlash:
    xor a
    ld [wTitleTimer], a
    ldh [hBGP], a
    ld a, SFX_SHINE
    call PlaySfx
    ld a, TP_FLASH
    ld [wPhase], a
    ret

TitleFlash:
    ld a, [wTitleTimer]
    inc a
    ld [wTitleTimer], a
    cp 4
    ret c
    ld a, BGP_NORMAL
    ldh [hBGP], a
    ; The walkers' tiles replace the logo objects; they appear when the stream is done.
    ld hl, HeroWalkTiles
    ld de, VRAM_OBJ
    ld bc, HeroWalkTiles.end - HeroWalkTiles
    call StartStream
    call DrawPushStart
    xor a
    ld [wTitleTimer], a
    ld a, TP_IDLE
    ld [wPhase], a
    ret

DrawPushStart:
    ld b, BOX_X
    ld c, BOX_Y
    ld d, 14
    ld e, 4
    call DrawBox
    ld b, BOX_X + 2
    ld c, BOX_Y + 1
    ld hl, TextPushStart
    jp PrintAt

TitleIdle:
    call UpdateWalkers
    ; Blink: shown 48 frames, hidden 16.
    ld a, [wFrame]
    and 63
    cp 48
    ld hl, TextPushStart
    jr c, .show
    ld hl, TextBlank10
.show:
    ld b, BOX_X + 2
    ld c, BOX_Y + 1
    call PrintAt
    ldh a, [hJoyPressed]
    and (1 << PAD_START) | (1 << PAD_A)
    ret z
    ld a, SFX_CONFIRM
    call PlaySfx
    ld a, TP_MENU
    ld [wPhase], a
    xor a
    ld [wCursor], a
    ; fall through
DrawTitleMenu:
    ld b, BOX_X + 1
    ld c, BOX_Y + 1
    ld hl, TextTitleMenu
    call PrintAt
    ld a, [wCursor]
    add BOX_Y + 1
    ld c, a
    ld b, BOX_X + 1
    ld a, '>' + TILE_FONT
    jp SetTile

TitleMenu:
    call UpdateWalkers
    ldh a, [hJoyRepeat]
    and (1 << PAD_UP) | (1 << PAD_DOWN) | (1 << PAD_SELECT)
    jr z, .noMove
    ld a, [wCursor]
    xor 1
    ld [wCursor], a
    ld a, SFX_CURSOR
    call PlaySfx
    call DrawTitleMenu
.noMove:
    ldh a, [hJoyPressed]
    bit PAD_B, a
    jr nz, .back
    and (1 << PAD_START) | (1 << PAD_A)
    ret z
    ld a, SFX_CONFIRM
    call PlaySfx
    call FadeOutWhite
    ld a, [wCursor]
    and a
    ld a, SCENE_SELECT
    jr z, .go
    ld a, SCENE_KEYTEST
.go:
    ld [wNextScene], a
    ret
.back:
    ld a, SFX_CANCEL
    call PlaySfx
    ld a, TP_IDLE
    ld [wPhase], a
    call DrawPushStart
    ret

; Walkers: heroes 0-3 go right on the back ledge, 4-7 left on the front one, 64 pixels apart.
InitWalkers:
    ld hl, wWalkerX
    ld a, 0
    ld b, 4
.back:
    ld [hl+], a
    add 64
    dec b
    jr nz, .back
    ld a, 32
    ld b, 4
.front:
    ld [hl+], a
    add 64
    dec b
    jr nz, .front
    ret

UpdateWalkers:
    ld a, [wStreamLeft]
    ld b, a
    ld a, [wStreamLeft + 1]
    or b
    ret nz
    ld a, [wFrame]
    rra
    jr nc, .draw
    ld hl, wWalkerX
    REPT 4
        inc [hl]
        inc hl
    ENDR
    REPT 4
        dec [hl]
        inc hl
    ENDR
.draw:
    ld c, 0
.walker:
    push bc
    call DrawWalker
    pop bc
    inc c
    ld a, c
    cp HERO_COUNT
    jr c, .walker
    ret

; Draws walker C as two objects at OAM entry CHIBI_OAM + 2 * C.
DrawWalker:
    ld hl, wWalkerX
    ld b, 0
    add hl, bc
    ld a, [hl]
    ld [wTemp], a ; X
    ; frame = ((frame >> 3) + hero) & 1; sprite = 2 * hero + frame; tile = 4 * sprite
    ld a, [wFrame]
    rra
    rra
    rra
    add c
    and 1
    ld b, a
    ld a, c
    add a
    add b
    add a
    add a
    ld e, a ; left tile when facing right
    ; OAM address
    ld a, c
    add a
    add CHIBI_OAM
    add a
    add a
    ld hl, wShadowOam
    add l
    ld l, a
    ld a, c
    cp 4
    jr nc, .left
    ; facing right, back ledge
    ld d, 88 ; Y
    ld a, d
    ld [hl+], a
    ld a, [wTemp]
    ld [hl+], a
    ld a, e
    ld [hl+], a
    xor a
    ld [hl+], a
    ld a, [wTemp]
    add 8
    jr c, .hideRight
    ld b, a
    ld a, d
    ld [hl+], a
    ld a, b
    ld [hl+], a
    ld a, e
    add 2
    ld [hl+], a
    xor a
    ld [hl], a
    ret
.left:
    ld d, 112
    ld a, d
    ld [hl+], a
    ld a, [wTemp]
    ld [hl+], a
    ld a, e
    add 2
    ld [hl+], a
    ld a, $20 ; X flip
    ld [hl+], a
    ld a, [wTemp]
    add 8
    jr c, .hideRight
    ld b, a
    ld a, d
    ld [hl+], a
    ld a, b
    ld [hl+], a
    ld a, e
    ld [hl+], a
    ld a, $20
    ld [hl], a
    ret
.hideRight:
    xor a
    ld [hl], a
    ret

SECTION "Title tables", ROM0
; SCY during the pan, easing out.
PanTable:
FOR F, PAN_FRAMES + 1
    db PAN_END - (PAN_END * (PAN_FRAMES - F) * (PAN_FRAMES - F)) / (PAN_FRAMES * PAN_FRAMES)
ENDR

; A letter's height above its place per frame: a fall, a touch, a small bounce; $80 ends.
DropTable:
    db -56, -56, -54, -52, -48, -44, -38, -32, -24, -16, -6, 0, -3, -4, -4, -3, 0, $80

; Per letter: shape (index in "USAGIQET"), shadow column, shadow row.
LetterInfo:
    db 0, 0, 1 ; U
    db 1, 4, 1 ; S
    db 2, 8, 1 ; A
    db 3, 12, 1 ; G
    db 4, 16, 1 ; I
    db 5, 0, 5 ; Q
    db 0, 4, 5 ; U
    db 6, 8, 5 ; E
    db 1, 12, 5 ; S
    db 7, 16, 5 ; T

; Ten characters with a space column on each side inside the box, so the blink spares the frame.
TextPushStart: db "PUSH START", 0
TextBlank10:   db "          ", 0
TextTitleMenu: db "  GAME START", TX_LINE, "  KEY TEST  ", 0

SECTION "Title variables", WRAM0
wTitleTimer::     db
wLettersStarted:: db
wLettersDone::    db
wLetterSlots::    ds LETTER_SLOTS * 2 ; letter ($FF free), frame
wWalkerX::        ds HERO_COUNT
