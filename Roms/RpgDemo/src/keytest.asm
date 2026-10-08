; Key test scene: a pad whose parts show pressed while held, held names, a press count and tones.

DEF PRESSED_OFFSET EQU 36

SECTION "Key test", ROM0

KeyTestInit::
    ld hl, KeypadTiles
    ld de, VRAM_BG
    ld bc, KeypadTiles.end - KeypadTiles
    call Copy
    ld de, MAP0
    ld a, SPACE
    call ResetShadow
    draw_box 0, 0, 20, 18
    print_at 6, 1, TextKeyTest
    print_at 1, 3, TextPressButtons
    print_at 3, 12, TextSelectLabel
    print_at 10, 12, TextStartLabel
    print_at 16, 8, TextALabel
    print_at 13, 9, TextBLabel
    print_at 1, 16, TextExitHint
    print_at 1, 15, TextCount
    xor a
    ld [wKeyHeld], a
    ld [wPresses], a
    ld [wPresses + 1], a
    call DrawPad
    call DrawHeld
    call DrawCount
    call ShadowToVram
    xor a
    ldh [hBGP], a
    ldh [hOBP0], a
    ldh [hOBP1], a
    ld a, LCDC_ON
    call LcdOn
    ld a, SONG_NONE
    jp PlaySong

KeyTestUpdate::
    ld a, [wPhase]
    and a
    jr nz, .running
    inc a
    ld [wPhase], a
    jp FadeInFromWhite
.running:
    ; START + SELECT: back to the title
    ldh a, [hJoyHeld]
    and (1 << PAD_START) | (1 << PAD_SELECT)
    cp (1 << PAD_START) | (1 << PAD_SELECT)
    jr nz, .keys
    ld a, SFX_CANCEL
    call PlaySfx
    call FadeOutWhite
    ld a, SCENE_TITLE
    ld [wNextScene], a
    ret
.keys:
    ; a tone for the lowest new press, and every new press counted
    ldh a, [hJoyPressed]
    and a
    jr z, .held
    ld b, a
    ld c, SFX_KEY_A
.lowest:
    rr b
    jr c, .tone
    inc c
    jr .lowest
.tone:
    ld a, c
    call PlaySfx
    ldh a, [hJoyPressed]
.count:
    and a
    jr z, .counted
    ld b, a
    ld hl, wPresses
    inc [hl]
    jr nz, .noCarry
    inc hl
    inc [hl]
.noCarry:
    ld a, b
    dec a
    and b ; clears the lowest set bit
    jr .count
.counted:
    call DrawCount
.held:
    ldh a, [hJoyHeld]
    ld hl, wKeyHeld
    cp [hl]
    ret z
    ld [hl], a
    call DrawPad
    jp DrawHeld

; Draws the pad pieces, plain or pressed by wKeyHeld.
DrawPad:
    ld hl, PadPieces
.piece:
    ld a, [hl+]
    cp $FF
    ret z
    ld b, a ; x
    ld a, [hl+]
    ld c, a ; y
    ld a, [hl+]
    ld e, a ; plain tile
    ld a, [hl+] ; button mask ($00: never pressed)
    push hl
    ld d, a
    ld a, [wKeyHeld]
    and d
    ld a, e
    jr z, .plain
    add PRESSED_OFFSET
.plain:
    call SetTile
    pop hl
    jr .piece

; Writes the names of the held buttons, and dots for the others.
DrawHeld:
    ld hl, HeldNames
    ld bc, (1 << 8) | 14
.name:
    ld a, [hl+]
    cp $FF
    ret z
    ld d, a ; mask
    ld a, [hl+]
    ld e, a ; length
    ld a, [wKeyHeld]
    and d
    ld a, d
    ld [wTemp2], a
.char:
    ld a, [wKeyHeld]
    push hl
    ld hl, wTemp2
    and [hl]
    pop hl
    ld a, [hl+]
    jr nz, .shown
    ld a, '.'
.shown:
    add TILE_FONT
    call SetTile
    inc b
    dec e
    jr nz, .char
    inc b ; a space between names
    jr .name

; Draws the press count in five digits.
DrawCount:
    ld a, [wPresses]
    ld l, a
    ld a, [wPresses + 1]
    ld h, a
    ld bc, (8 << 8) | 15
    ld de, 10000
    call .digit
    ld de, 1000
    call .digit
    ld de, 100
    call .digit
    ld de, 10
    call .digit
    ld a, l
    add '0' + TILE_FONT
    jp SetTile
.digit:
    ld a, '0' - 1
.subtract:
    inc a
    push af
    ld a, l
    sub e
    ld l, a
    ld a, h
    sbc d
    ld h, a
    jr c, .done
    pop af
    jr .subtract
.done:
    add hl, de
    pop af
    add TILE_FONT
    call SetTile
    inc b
    ret

SECTION "Key test tables", ROM0
; Pad pieces: x, y, plain tile (keypad.png, 12 tiles a row), the button that presses it.
MACRO piece ; x, y, tile column, tile row, mask
    db \1, \2, (\4) * 12 + (\3), \5
ENDM
PadPieces:
    piece 3, 6, 0, 0, 0
    piece 4, 6, 1, 0, 1 << PAD_UP
    piece 5, 6, 2, 0, 0
    piece 3, 7, 0, 1, 1 << PAD_LEFT
    piece 4, 7, 1, 1, 0
    piece 5, 7, 2, 1, 1 << PAD_RIGHT
    piece 3, 8, 0, 2, 0
    piece 4, 8, 1, 2, 1 << PAD_DOWN
    piece 5, 8, 2, 2, 0
    piece 12, 7, 3, 0, 1 << PAD_B
    piece 13, 7, 4, 0, 1 << PAD_B
    piece 12, 8, 3, 1, 1 << PAD_B
    piece 13, 8, 4, 1, 1 << PAD_B
    piece 15, 6, 5, 0, 1 << PAD_A
    piece 16, 6, 6, 0, 1 << PAD_A
    piece 15, 7, 5, 1, 1 << PAD_A
    piece 16, 7, 6, 1, 1 << PAD_A
    piece 5, 11, 7, 0, 1 << PAD_SELECT
    piece 6, 11, 8, 0, 1 << PAD_SELECT
    piece 11, 11, 9, 0, 1 << PAD_START
    piece 12, 11, 10, 0, 1 << PAD_START
    db $FF

; Held names: mask, length, characters.
HeldNames: ; 17 characters in all
    db 1 << PAD_UP, 1, "U"
    db 1 << PAD_DOWN, 1, "D"
    db 1 << PAD_LEFT, 1, "L"
    db 1 << PAD_RIGHT, 1, "R"
    db 1 << PAD_A, 1, "A"
    db 1 << PAD_B, 1, "B"
    db 1 << PAD_SELECT, 2, "SE"
    db 1 << PAD_START, 2, "ST"
    db $FF

TextKeyTest:      db "KEY TEST", 0
TextPressButtons: db "Press the buttons.", 0
TextSelectLabel:  db "SELECT", 0
TextStartLabel:   db "START", 0
TextALabel:       db "A", 0
TextBLabel:       db "B", 0
TextCount:        db "COUNT:", 0
TextExitHint:     db "START+SELECT: EXIT", 0
