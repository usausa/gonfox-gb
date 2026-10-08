; Battle message box: two 18-character lines, typed one character per frame.

DEF MSG_X     EQU 1
DEF MSG_LINE1 EQU 14
DEF MSG_LINE2 EQU 16
DEF MORE_MARK EQU '|' + TILE_FONT

SECTION "Text", ROM0

ClearMessageText::
    ld b, 1
    ld c, 13
    ld d, 18
    ld e, 4
    ld a, SPACE
    jp FillRect

; Types message HL (A or B held: at once); TX_WAIT waits for a press, then clears the box.
ShowMessage::
    ld a, l
    ld [wMsgPtr], a
    ld a, h
    ld [wMsgPtr + 1], a
    xor a
    ld [wMsgSub], a
    call ClearMessageText
.home:
    ld a, MSG_X
    ld [wMsgX], a
    ld a, MSG_LINE1
    ld [wMsgY], a
.next:
    call FetchChar
    and a
    ret z
    cp TX_LINE
    jr z, .line
    cp TX_WAIT
    jr z, .wait
    add TILE_FONT
    ld hl, wMsgX
    ld b, [hl]
    inc [hl]
    ld hl, wMsgY
    ld c, [hl]
    call SetTile
    ldh a, [hJoyHeld]
    and (1 << PAD_A) | (1 << PAD_B)
    jr nz, .next
    call NextFrame
    jr .next
.line:
    ld a, MSG_X
    ld [wMsgX], a
    ld a, MSG_LINE2
    ld [wMsgY], a
    jr .next
.wait:
    call WaitButton
    call ClearMessageText
    jr .home

; Shows the more mark (blinking) and waits for a new press of A or B.
WaitButton::
    call NextFrame
    ld b, 18
    ld c, MSG_LINE2
    ld a, [wFrame]
    and 16
    ld a, MORE_MARK
    jr z, .show
    ld a, SPACE
.show:
    call SetTile
    ldh a, [hJoyPressed]
    and (1 << PAD_A) | (1 << PAD_B)
    jr z, WaitButton
    ld b, 18
    ld c, MSG_LINE2
    ld a, SPACE
    call SetTile
    ld a, SFX_CLICK
    jp PlaySfx

; Prints string HL at (B, C) at once with the codes expanded; TX_WAIT or 0 ends it.
PrintExpanded::
    ld a, l
    ld [wMsgPtr], a
    ld a, h
    ld [wMsgPtr + 1], a
    xor a
    ld [wMsgSub], a
.next:
    push bc
    call FetchChar
    pop bc
    and a
    ret z
    cp TX_WAIT
    ret z
    cp TX_LINE
    jr z, .line
    add TILE_FONT
    call SetTile
    inc b
    jr .next
.line:
    inc c
    jr .next

; Returns the next message character in A, expanding the name and number codes.
FetchChar:
    ld a, [wMsgSub]
    and a
    jr z, .main
    ld hl, wMsgSubPtr
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    ld a, [hl+]
    and a
    jr z, .subEnd
    push af
    ld a, l
    ld [wMsgSubPtr], a
    ld a, h
    ld [wMsgSubPtr + 1], a
    pop af
    ret
.subEnd:
    xor a
    ld [wMsgSub], a
.main:
    ld hl, wMsgPtr
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    ld a, [hl+]
    push af
    ld a, l
    ld [wMsgPtr], a
    ld a, h
    ld [wMsgPtr + 1], a
    pop af
    cp TX_HERO
    jr z, .hero
    cp TX_ENEMY
    jr z, .enemy
    cp TX_NUM
    jr z, .number
    cp TX_MAGIC
    jr z, .magic
    cp TX_MOVE
    jr z, .move
    ret
.hero:
    ld a, [wHero]
    ld hl, HeroNames
    call ReadWord
    jr .startSub
.magic:
    ld a, [wHero]
    ld hl, HeroMagic
    call ReadWord
    jr .startSub
.enemy:
    ld a, [wMode]
    ld hl, EnemyNames
    call ReadWord
    jr .startSub
.move:
    ld hl, wMovePtr
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    jr .startSub
.number:
    ld a, [wNumber]
    call NumberToText
    ld hl, wNumberText
.startSub:
    ld a, l
    ld [wMsgSubPtr], a
    ld a, h
    ld [wMsgSubPtr + 1], a
    ld a, 1
    ld [wMsgSub], a
    jp FetchChar

SECTION "Text variables", WRAM0
wMsgPtr::    dw
wMsgSubPtr:: dw
wMsgSub::    db
wMsgX::      db
wMsgY::      db
wMovePtr::   dw
