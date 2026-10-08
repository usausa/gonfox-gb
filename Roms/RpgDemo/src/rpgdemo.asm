; USAGI QUEST, the GonFox.GameBoy RPG demo. DMG, 32 KiB ROM only, MIT licensed (see ../LICENSE).

INCLUDE "hardware.inc"
INCLUDE "constants.inc"
INCLUDE "art.inc"

DEF MAP_W EQU 20
DEF MAP_H EQU 18
DEF MAP_ROWS_PER_FRAME EQU 2
DEF STREAM_TILES_PER_FRAME EQU 2 ; 1 if map rows were copied

; Palettes: BG normal; objects 0 white/light/black, objects 1 white/light/dark.
DEF BGP_NORMAL  EQU %11_10_01_00
DEF OBP0_NORMAL EQU %11_01_00_00
DEF OBP1_NORMAL EQU %10_01_00_00

DEF SPACE EQU ' ' + TILE_FONT

MACRO print_at ; x, y, string label
    ld bc, ((\1) << 8) | (\2)
    ld hl, \3
    call PrintAt
ENDM

MACRO draw_box ; x, y, width, height
    ld bc, ((\1) << 8) | (\2)
    ld de, ((\3) << 8) | (\4)
    call DrawBox
ENDM

SECTION "VBlank vector", ROM0[$40]
    jp VBlankIsr

SECTION "Header", ROM0[$100]
    nop
    jp Start
    ds $150 - @, 0

SECTION "Start", ROM0[$150]
Start::
    di
    ld sp, wStackTop
.waitVBlank:
    ldh a, [rLY]
    cp 144
    jr c, .waitVBlank
    xor a
    ldh [rLCDC], a
    ldh [rIE], a
    ldh [rIF], a
    ; Clear WRAM (no calls: the stack lives there), then HRAM, VRAM and OAM.
    ld hl, $C000
    ld bc, $2000
.clearWram:
    xor a
    ld [hl+], a
    dec c
    jr nz, .clearWram
    dec b
    jr nz, .clearWram
    ld hl, $FF80
    ld c, $7F
.clearHram:
    ld [hl+], a
    dec c
    jr nz, .clearHram
    ld hl, $8000
    ld bc, $2000
    xor a
    call Fill
    ld hl, $FE00
    ld bc, 160
    xor a
    call Fill
    ; OAM DMA routine into HRAM; the font and UI tiles stay for the whole demo.
    ld hl, OamDmaSource
    ld de, hOamDma
    ld bc, OamDmaSource.end - OamDmaSource
    call Copy
    ld hl, FontTiles
    ld de, VRAM_FONT
    ld bc, FontTiles.end - FontTiles
    call Copy
    ld hl, UiTiles
    ld de, VRAM_UI
    ld bc, UiTiles.end - UiTiles
    call Copy
    ld a, LOW(RANDOM_SEED)
    ldh [hRandom], a
    ld a, HIGH(RANDOM_SEED)
    ldh [hRandom + 1], a
    ld a, SCENE_NONE
    ld [wNextScene], a
    call ResetParty
    call MusicInit
    ld a, SCENE_TITLE
    call ChangeScene
    ld a, IEF_VBLANK
    ldh [rIE], a
    xor a
    ldh [rIF], a
    ei
MainLoop:
    call NextFrame
    ld a, [wScene]
    ld hl, SceneUpdates
    call JumpTable
    ld a, [wNextScene]
    cp SCENE_NONE
    jr z, MainLoop
    ld b, a
    ld a, SCENE_NONE
    ld [wNextScene], a
    ld a, b
    call ChangeScene
    jr MainLoop

DEF RANDOM_SEED EQU $E1AC

SceneUpdates:
    dw TitleUpdate, SelectUpdate, BattleUpdate, KeyTestUpdate
SceneInits:
    dw TitleInit, SelectInit, BattleInit, KeyTestInit

; A = scene. Loads it with the LCD off; the init routine turns the LCD on again.
ChangeScene::
    ld [wScene], a
    push af
    di
    call LcdOff
    call ClearOam
    xor a
    ld [wPhase], a
    ld [wStreamLeft], a
    ld [wStreamLeft + 1], a
    ld [wMapAny], a
    ldh [hSCX], a
    ldh [hSCY], a
    ld hl, wMapDirty
    ld bc, MAP_H * 2 ; dirty and pending
    call Fill
    ld a, BGP_NORMAL
    ldh [hBGP], a
    ld a, OBP0_NORMAL
    ldh [hOBP0], a
    ld a, OBP1_NORMAL
    ldh [hOBP1], a
    pop af
    ld hl, SceneInits
    call JumpTable
    xor a
    ldh [rIF], a
    ei
    ret

; Jumps to entry A of the routine table at HL.
JumpTable::
    add a
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    jp hl

; Commits the changed rows, waits for VBlank, then reads the joypad and steps the random state.
NextFrame::
    call CommitMap
    xor a
    ldh [hVBlankDone], a
.wait:
    halt
    ldh a, [hVBlankDone]
    and a
    jr z, .wait
    ld hl, wFrame
    inc [hl]
    call ReadJoypad
    jp NextRandom

; Waits B frames.
WaitFrames::
    push bc
    call NextFrame
    pop bc
    dec b
    jr nz, WaitFrames
    ret

LcdOff::
    ldh a, [rLCDC]
    bit 7, a
    ret z
.wait:
    ldh a, [rLY]
    cp 144
    jr c, .wait
    xor a
    ldh [rLCDC], a
    ret

; A = LCDC value. Writes the registers the scene set, then turns the LCD on.
LcdOn::
    push af
    ldh a, [hSCX]
    ldh [rSCX], a
    ldh a, [hSCY]
    ldh [rSCY], a
    ldh a, [hBGP]
    ldh [rBGP], a
    ldh a, [hOBP0]
    ldh [rOBP0], a
    ldh a, [hOBP1]
    ldh [rOBP1], a
    ld a, HIGH(wShadowOam)
    call hOamDma
    pop af
    ldh [rLCDC], a
    ret

; VBlank: everything that touches VRAM, OAM and the display registers, then the music (APU only).
VBlankIsr:
    push af
    push bc
    push de
    push hl
    ld a, HIGH(wShadowOam)
    call hOamDma
    ldh a, [hSCX]
    ldh [rSCX], a
    ldh a, [hSCY]
    ldh [rSCY], a
    ldh a, [hBGP]
    ldh [rBGP], a
    ldh a, [hOBP0]
    ldh [rOBP0], a
    ldh a, [hOBP1]
    ldh [rOBP1], a
    ld b, MAP_ROWS_PER_FRAME
    ld a, [wMapAny]
    and a
    call nz, FlushMap
    ld a, b
    cp MAP_ROWS_PER_FRAME
    ld b, STREAM_TILES_PER_FRAME
    jr z, .stream
    dec b
.stream:
    call FlushStream
.vramDone:
    ld a, 1
    ldh [hVBlankDone], a
    call MusicUpdate
    pop hl
    pop de
    pop bc
    pop af
    reti

; Copies up to B committed shadow rows to VRAM and returns B less the rows copied.
FlushMap:
    ld hl, wMapDirty
    ld c, MAP_H
.next:
    ld a, [hl+]
    and a
    jr nz, .dirty
.clean:
    dec c
    jr nz, .next
    xor a
    ld [wMapAny], a
    ret
.dirty:
    ld a, b
    and a
    ret z
    dec b
    dec hl
    ld [hl], 0
    inc hl
    push hl
    push bc
    ld a, MAP_H
    sub c
    ld c, a
    call CopyMapRow
    pop bc
    pop hl
    jr .clean

; C = shadow row. Copies its 20 tiles to the map row at wMapVram + 32 * C.
CopyMapRow:
    ld b, 0
    ld l, c
    ld h, b
    add hl, hl
    add hl, hl
    ld d, h
    ld e, l
    add hl, hl
    add hl, hl
    add hl, de ; 20 * C
    ld de, wMapShadow
    add hl, de
    push hl
    ld l, c
    ld h, b
    add hl, hl
    add hl, hl
    add hl, hl
    add hl, hl
    add hl, hl ; 32 * C
    ld a, [wMapVram]
    ld e, a
    ld a, [wMapVram + 1]
    ld d, a
    add hl, de
    ld d, h
    ld e, l
    pop hl
    REPT MAP_W
        ld a, [hl+]
        ld [de], a
        inc de
    ENDR
    ret

; Copies up to B (at least 1) 16-byte tiles of the tile stream to VRAM.
FlushStream:
    ld a, [wStreamLeft]
    ld c, a
    ld a, [wStreamLeft + 1]
    or c
    ret z
    ld hl, wStreamSrc
    ld a, [hl+]
    ld e, a
    ld a, [hl+]
    ld d, a
    ld a, [hl+]
    ld h, [hl]
    ld l, a
.tile:
    REPT 16
        ld a, [de]
        ld [hl+], a
        inc de
    ENDR
    ld a, [wStreamLeft]
    sub 16
    ld [wStreamLeft], a
    ld c, a
    ld a, [wStreamLeft + 1]
    sbc 0
    ld [wStreamLeft + 1], a
    or c
    jr z, .done
    dec b
    jr nz, .tile
.done:
    ld a, e
    ld [wStreamSrc], a
    ld a, d
    ld [wStreamSrc + 1], a
    ld a, l
    ld [wStreamDst], a
    ld a, h
    ld [wStreamDst + 1], a
    ret

; Starts a tile stream of BC bytes from HL to VRAM at DE, after any running stream ends.
StartStream::
    push hl
    push de
    push bc
    call WaitStream
    pop bc
    pop de
    pop hl
    di
    ld a, l
    ld [wStreamSrc], a
    ld a, h
    ld [wStreamSrc + 1], a
    ld a, e
    ld [wStreamDst], a
    ld a, d
    ld [wStreamDst + 1], a
    ld a, c
    ld [wStreamLeft], a
    ld a, b
    ld [wStreamLeft + 1], a
    ei
    ret

; Waits frames until the tile stream is done.
WaitStream::
    ld a, [wStreamLeft]
    ld c, a
    ld a, [wStreamLeft + 1]
    or c
    ret z
    call NextFrame
    jr WaitStream

OamDmaSource:
LOAD "OAM DMA", HRAM
hOamDma::
    ldh [rDMA], a
    ld a, 40
.wait:
    dec a
    jr nz, .wait
    ret
ENDL
.end:

; Reads the joypad into hJoyHeld, hJoyPressed and hJoyRepeat (presses plus repeated directions).
DEF REPEAT_FIRST EQU 20
DEF REPEAT_NEXT  EQU 6
ReadJoypad::
    ld a, $20 ; directions
    ldh [rP1], a
    ldh a, [rP1]
    ldh a, [rP1]
    cpl
    and $0F
    swap a
    ld b, a
    ld a, $10 ; buttons
    ldh [rP1], a
    REPT 6
        ldh a, [rP1]
    ENDR
    cpl
    and $0F
    or b
    ld b, a
    ld a, $30
    ldh [rP1], a
    ldh a, [hJoyHeld]
    cpl
    and b
    ldh [hJoyPressed], a
    ld c, a
    ld a, b
    ldh [hJoyHeld], a
    and $F0
    jr z, .idle
    ld a, c
    and $F0
    jr z, .held
    ld a, REPEAT_FIRST
    ldh [hRepeatTimer], a
    jr .plain
.held:
    ldh a, [hRepeatTimer]
    dec a
    ldh [hRepeatTimer], a
    jr nz, .plain
    ld a, REPEAT_NEXT
    ldh [hRepeatTimer], a
    ld a, b
    and $F0
    or c
    ldh [hJoyRepeat], a
    ret
.idle:
    xor a
    ldh [hRepeatTimer], a
.plain:
    ld a, c
    ldh [hJoyRepeat], a
    ret

; Steps the 16-bit xorshift (7, 9, 8) random state; returns its high byte in A, uses HL.
NextRandom::
    ldh a, [hRandom]
    ld l, a
    ldh a, [hRandom + 1]
    ld h, a
    rra
    ld a, l
    rra
    xor h
    ld h, a
    ld a, l
    rra
    ld a, h
    rra
    xor l
    ld l, a
    xor h
    ld h, a
    ld a, l
    ldh [hRandom], a
    ld a, h
    ldh [hRandom + 1], a
    ret

; A = n (1-255). Returns a random number 0..n-1 in A; uses E and HL.
RandomBelow::
    ld e, a
    call NextRandom
.reduce:
    cp e
    ret c
    sub e
    jr .reduce

; Memory helpers.
Copy:: ; HL = source, DE = destination, BC = bytes
    ld a, b
    or c
    ret z
.loop:
    ld a, [hl+]
    ld [de], a
    inc de
    dec bc
    ld a, b
    or c
    jr nz, .loop
    ret

Fill:: ; HL = destination, BC = bytes, A = value
    ld d, a
    ld a, b
    or c
    ret z
.loop:
    ld a, d
    ld [hl+], a
    dec bc
    ld a, b
    or c
    jr nz, .loop
    ret

ClearOam::
    ld hl, wShadowOam
    ld bc, 160
    xor a
    jp Fill

; Returns HL = word A of the word table at HL.
ReadWord::
    add a
    add l
    ld l, a
    adc h
    sub l
    ld h, a
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    ret

; HL = base, A = index, B = record size. Returns HL = base + index * size.
IndexRecord::
    ld e, a
    ld d, 0
.loop:
    ld a, b
    and a
    ret z
    add hl, de
    dec b
    jr .loop

; Shadow map of 20 x 18 tiles: changed rows are marked pending, committed, then copied in VBlank.

; Points the shadow map at VRAM DE and fills it with tile A.
ResetShadow::
    push af
    ld a, e
    ld [wMapVram], a
    ld a, d
    ld [wMapVram + 1], a
    pop af
    ld hl, wMapShadow
    ld bc, MAP_W * MAP_H
    jp Fill

; LCD off only: copies the whole shadow to VRAM now.
ShadowToVram::
    ld c, 0
.row:
    push bc
    call CopyMapRow
    pop bc
    inc c
    ld a, c
    cp MAP_H
    jr c, .row
    ld hl, wMapDirty
    ld bc, MAP_H * 2
    xor a
    jp Fill

CommitMap::
    ld hl, wMapPending
    ld de, wMapDirty
    ld c, MAP_H
    ld b, 0
.row:
    ld a, [hl]
    and a
    jr z, .skip
    ld [hl], 0
    ld a, 1
    ld [de], a
    ld b, a
.skip:
    inc hl
    inc de
    dec c
    jr nz, .row
    ld a, b
    and a
    ret z
    ld [wMapAny], a
    ret

; B = x, C = y. Returns HL = shadow address; marks the row pending. Keeps BC and DE.
ShadowAddress::
    push de
    ld e, c
    ld d, 0
    ld hl, wMapPending
    add hl, de
    ld [hl], 1
    ld l, c
    ld h, d
    add hl, hl
    add hl, hl
    ld d, h
    ld e, l
    add hl, hl
    add hl, hl
    add hl, de ; 20 * y
    ld e, b
    ld d, 0
    add hl, de
    ld de, wMapShadow
    add hl, de
    pop de
    ret

; Sets the shadow tile at (B, C) to A.
SetTile::
    push hl
    push af
    call ShadowAddress
    pop af
    ld [hl], a
    pop hl
    ret

; Returns A = the shadow tile at (B, C).
GetTile::
    push hl
    call ShadowAddress
    ld a, [hl]
    pop hl
    ret

; Fills the D x E rectangle at (B, C) with tile A (sizes 1-20); keeps BC and DE.
FillRect::
    push bc
    push de
    ld [wTemp], a
.row:
    push de
    call ShadowAddress
    ld a, [wTemp]
.col:
    ld [hl+], a
    dec d
    jr nz, .col
    pop de
    inc c
    dec e
    jr nz, .row
    pop de
    pop bc
    ret

; Fills the D x E rectangle at (B, C) with tiles A, A+1, ... row by row; keeps BC and DE.
DrawSequence::
    push bc
    push de
    ld [wTemp], a
.row:
    push de
    call ShadowAddress
    ld a, [wTemp]
.col:
    ld [hl+], a
    inc a
    dec d
    jr nz, .col
    ld [wTemp], a
    pop de
    inc c
    dec e
    jr nz, .row
    pop de
    pop bc
    ret

; Draws the D x E tile IDs at HL (row by row) to (B, C), adding A to each.
DrawTiles::
    ld [wTemp2], a
.row:
    push bc
    push hl
    call ShadowAddress
    ld b, h
    ld c, l
    pop hl
    push de
.col:
    ld a, [wTemp2]
    add [hl]
    inc hl
    ld [bc], a
    inc bc
    dec d
    jr nz, .col
    pop de
    pop bc
    inc c
    dec e
    jr nz, .row
    ret

; Draws a D x E window frame (both at least 3) at (B, C) with a blank inside.
DrawBox::
    ld a, b
    ld [wBoxX], a
    ld a, c
    ld [wBoxY], a
    ld a, d
    ld [wBoxW], a
    ld a, e
    ld [wBoxH], a
    ld a, SPACE
    call FillRect
    ; edges
    inc b
    dec d
    dec d
    ld e, 1
    ld a, TILE_UI + 1
    call FillRect
    ld a, [wBoxH]
    dec a
    add c
    ld c, a
    ld a, TILE_UI + 6
    call FillRect
    ld a, [wBoxX]
    ld b, a
    ld a, [wBoxY]
    inc a
    ld c, a
    ld a, [wBoxH]
    sub 2
    ld e, a
    ld d, 1
    ld a, TILE_UI + 3
    call FillRect
    ld a, [wBoxW]
    dec a
    add b
    ld b, a
    ld a, TILE_UI + 4
    call FillRect
    ; corners
    ld a, [wBoxY]
    ld c, a
    ld a, TILE_UI + 2
    call SetTile
    ld a, [wBoxX]
    ld b, a
    ld a, TILE_UI + 0
    call SetTile
    ld a, [wBoxH]
    dec a
    add c
    ld c, a
    ld a, TILE_UI + 5
    call SetTile
    ld a, [wBoxW]
    dec a
    add b
    ld b, a
    ld a, TILE_UI + 7
    jp SetTile

; Prints string HL at (B, C) at once (TX_LINE starts the next row, 0 ends); returns HL after it.
PrintAt::
    push bc
.line:
    push hl
    call ShadowAddress
    ld d, h
    ld e, l
    pop hl
.char:
    ld a, [hl+]
    and a
    jr z, .end
    cp TX_LINE
    jr z, .newline
    add TILE_FONT
    ld [de], a
    inc de
    jr .char
.newline:
    inc c
    jr .line
.end:
    pop bc
    ret

; Prints A at (B, C) in three places, with leading zeros as spaces.
PrintNumber3::
    push bc
    push af
    call ShadowAddress
    pop af
    ld e, 0 ; 1 once a digit was written
    ld b, 100
    call .digit
    ld b, 10
    call .digit
    add '0' + TILE_FONT
    ld [hl], a
    pop bc
    ret
.digit:
    ld c, -1
.sub:
    inc c
    sub b
    jr nc, .sub
    add b
    ld d, a
    ld a, c
    or e
    jr z, .space
    ld e, 1
    ld a, c
    add '0' + TILE_FONT
    ld [hl+], a
    ld a, d
    ret
.space:
    ld a, SPACE
    ld [hl+], a
    ld a, d
    ret

; A = value: writes up to three digits (no leading spaces) to wNumberText, ending with 0.
NumberToText::
    ld hl, wNumberText
    ld e, 0
    ld b, 100
    call .digit
    ld b, 10
    call .digit
    add '0'
    ld [hl+], a
    ld [hl], 0
    ret
.digit:
    ld c, -1
.sub:
    inc c
    sub b
    jr nc, .sub
    add b
    ld d, a
    ld a, c
    or e
    jr z, .skip
    ld e, 1
    ld a, c
    add '0'
    ld [hl+], a
.skip:
    ld a, d
    ret

; Fades: four steps of four frames each. Objects follow the background.
FadeOutWhite::
    ld hl, FadeToWhite
    jr Fade
FadeInFromWhite::
    ld hl, FadeFromWhite
    jr Fade
FadeInFromBlack::
    ld hl, FadeFromBlack
Fade:
    ld c, 4
.step:
    ld a, [hl+]
    ldh [hBGP], a
    ld a, [hl+]
    ldh [hOBP0], a
    ld a, [hl+]
    ldh [hOBP1], a
    push hl
    push bc
    ld b, 4
    call WaitFrames
    pop bc
    pop hl
    dec c
    jr nz, .step
    ret

FadeToWhite:   db %10_01_00_00, %01_00_00_00, %01_00_00_00,  %01_00_00_00, %00_00_00_00, %00_00_00_00,  0, 0, 0,  0, 0, 0
FadeFromWhite: db %01_00_00_00, %00_00_00_00, %00_00_00_00,  %10_01_00_00, %01_00_00_00, %01_00_00_00,  %11_10_01_00, %11_01_00_00, %10_01_00_00,  BGP_NORMAL, OBP0_NORMAL, OBP1_NORMAL
FadeFromBlack: db %11_11_11_11, %11_11_11_00, %11_11_11_00,  %11_11_10_10, %11_11_10_00, %11_11_10_00,  %11_10_10_01, %11_10_01_00, %10_10_01_00,  BGP_NORMAL, OBP0_NORMAL, OBP1_NORMAL

; Writes OAM entry HL from B = Y, C = X, D = tile, E = attributes; HL moves to the next entry.
PutObject::
    ld a, b
    ld [hl+], a
    ld a, c
    ld [hl+], a
    ld a, d
    ld [hl+], a
    ld a, e
    ld [hl+], a
    ret

; Hides B OAM entries from HL.
HideObjects::
    xor a
.loop:
    ld [hl+], a
    inc hl
    inc hl
    inc hl
    dec b
    jr nz, .loop
    ret

INCLUDE "text.asm"
INCLUDE "title.asm"
INCLUDE "select.asm"
INCLUDE "battle.asm"
INCLUDE "keytest.asm"
INCLUDE "music.asm"
INCLUDE "data.asm"

SECTION "Shadow OAM", WRAM0, ALIGN[8]
wShadowOam:: ds 160

SECTION "Observation", WRAM0[$C100]
; Fixed addresses for tests and debuggers.
wScene::      db ; 0 title, 1 select, 2 battle, 3 key test
wPhase::      db
wFrame::      db
wHero::       db
wMode::       db ; 0 easy, 1 normal, 2 nightmare
wCursor::     db
wEnemyHp::    db
wEnemyMaxHp:: db
wHeroHp::     ds 8
wHeroMp::     db ; party MP, one per magic
wTurn::       db
wNumber::     db ; last damage or healing shown
wGuard::      db ; 1 while guarding
wResult::     db ; 0 on, 1 won, 2 lost, 3 ran away
wPresses::    dw
wKeyHeld::    db
wSongId::     db
wNextScene::  db ; FF none

SECTION "Variables", WRAM0
wTemp::       db
wTemp2::      db
wBoxX::       db
wBoxY::       db
wBoxW::       db
wBoxH::       db
wMapAny::     db
wMapVram::    dw
wMapDirty::   ds MAP_H
wMapPending:: ds MAP_H
wMapShadow::  ds MAP_W * MAP_H
wStreamSrc::  dw
wStreamDst::  dw
wStreamLeft:: dw
wNumberText:: ds 4

SECTION "Stack", WRAM0
    ds 256
wStackTop::

SECTION "HRAM variables", HRAM
hVBlankDone::  db
hJoyHeld::     db
hJoyPressed::  db
hJoyRepeat::   db
hRepeatTimer:: db
hRandom::      dw
hSCX::         db
hSCY::         db
hBGP::         db
hOBP0::        db
hOBP1::        db
