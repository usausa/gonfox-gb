; Shared start-up and reporting: results collect in WRAM and end up on screen as 255 tiles.

DEF STATUS_DONE EQU $01
DEF RESULT_CAPACITY EQU 4078 ; The length word and the results fill 255 tiles; tile 255 shows the four colours.
DEF CALIBRATION_TILE EQU 255

SECTION "Report state", WRAM0[$C000]
wStatus:: ds 1 ; STATUS_DONE once the results are complete.
wResultPointer:: ds 2
    ds 11
wResultLength:: ds 2 ; Shown first, ahead of the results.
wResults:: ds RESULT_CAPACITY

SECTION "Stack", WRAM0[$DF00]
    ds $100
wStackTop::

SECTION "Header", ROM0[$100]
    nop
    jp Start
    ds $150 - @, 0

SECTION "Start", ROM0[$150]
; Switches the LCD off, clears memory and the APU, then runs the test's Main.
Start::
    di
    ld sp, wStackTop
    call LcdOff
    xor a
    ldh [rIE], a
    ldh [rIF], a
    ld hl, $C000
    ld bc, $1F00
    call Fill
    ld hl, _VRAM
    ld bc, $2000
    call Fill
    ld hl, _OAMRAM
    ld bc, $A0
    call Fill
    ld hl, _HRAM
    ld c, $7F
.clearHram:
    ld [hl+], a
    dec c
    jr nz, .clearHram
    ldh [rNR52], a
    ld a, LOW(wResults)
    ld [wResultPointer], a
    ld a, HIGH(wResults)
    ld [wResultPointer + 1], a
    call Main
    jp Finish

SECTION "Report", ROM0
; Waits for VBlank if the LCD is on, then switches it off.
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

; Fills BC bytes from HL with A.
Fill::
    ld d, a
.loop:
    ld a, d
    ld [hl+], a
    dec bc
    ld a, b
    or c
    jr nz, .loop
    ld a, d
    ret

; Copies BC bytes from HL to DE.
Copy::
    ld a, [hl+]
    ld [de], a
    inc de
    dec bc
    ld a, b
    or c
    jr nz, Copy
    ret

; Appends A to the results, keeping every register.
Emit::
    push hl
    push af
    ld hl, wResultPointer
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    pop af
    ld [hl+], a
    push af
    ld a, l
    ld [wResultPointer], a
    ld a, h
    ld [wResultPointer + 1], a
    pop af
    pop hl
    ret

; Appends HL, low byte first, keeping every register.
EmitWord::
    push af
    ld a, l
    call Emit
    ld a, h
    call Emit
    pop af
    ret

; Shows the length and the results as tile pixels, marks them complete and halts for good.
Finish::
    di
    call LcdOff
    ld hl, wResultPointer
    ld a, [hl+]
    ld h, [hl]
    sub LOW(wResults)
    ld [wResultLength], a
    ld a, h
    sbc HIGH(wResults)
    ld [wResultLength + 1], a
    ld hl, wResultLength
    ld de, _VRAM
    ld bc, 2 + RESULT_CAPACITY
    call Copy
    ld hl, _VRAM + CALIBRATION_TILE * 16 ; Colours 0, 1, 2 and 3, two columns each.
    ld b, 8
.calibration:
    ld a, %00110011
    ld [hl+], a
    ld a, %00001111
    ld [hl+], a
    dec b
    jr nz, .calibration
    ld hl, _SCRN0
    ld e, 0
    ld c, 13 ; Rows 0-12 hold tiles 0-254 in reading order.
.row:
    ld b, 20
.column:
    ld a, e
    ld [hl+], a
    inc e
    dec b
    jr nz, .column
    push bc
    ld bc, 12
    add hl, bc
    pop bc
    dec c
    jr nz, .row
    ld hl, _SCRN0 + 17 * 32
    ld b, 20
.calibrationRow:
    ld a, CALIBRATION_TILE
    ld [hl+], a
    dec b
    jr nz, .calibrationRow
    ld a, %11_10_01_00
    ldh [rBGP], a
    xor a
    ldh [rSCX], a
    ldh [rSCY], a
    ld a, LCDCF_ON | LCDCF_BG8000 | LCDCF_BGON
    ldh [rLCDC], a
    ld b, 3 ; The first frame after enabling the LCD is not shown.
.frame:
    ldh a, [rLY]
    cp 144
    jr nz, .frame
.vblank:
    ldh a, [rLY]
    cp 144
    jr z, .vblank
    dec b
    jr nz, .frame
    ld a, STATUS_DONE
    ld [wStatus], a
    ld b, b
    xor a
    ldh [rIE], a
.halt:
    halt
    jr .halt
