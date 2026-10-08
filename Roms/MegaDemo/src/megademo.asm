; GonFox.GameBoy mega demo. DMG, 32 KiB ROM only, MIT licensed (see ../LICENSE).

INCLUDE "hardware.inc"

SECTION "VBlank vector", ROM0[$40]
    jp VBlankIsr

SECTION "STAT vector", ROM0[$48]
; HBlank: sets the next line's SCX and BGP from the front page (over the unused $50/$58 vectors).
StatIsr::
    push af
    ldh a, [rLY]
    push hl
    inc a
    ld l, a
    ldh a, [hFrontPage]
    ld h, a
    ld a, [hl]
.scx:
    ldh [rSCX], a
    inc h
    ld a, [hl]
.bgp:
    ldh [rBGP], a
    pop hl
    pop af
    reti
.end:

SECTION "Header", ROM0[$100]
    nop
    jp Start
    ds $150 - @, 0

SECTION "Start", ROM0[$150]
Start::
    di
    ld sp, wStackTop
    ; Wait for VBlank, then switch the LCD off and clear IE and IF.
.waitVBlank:
    ldh a, [rLY]
    cp 144
    jr c, .waitVBlank
    xor a
    ldh [rLCDC], a
    ldh [rIE], a
    ldh [rIF], a
    ; Clear all WRAM without calls (the stack is at its top), then HRAM, VRAM and OAM.
    ld hl, $C000
    ld c, $2000 / $1000
.clearWram:
    ld b, 0 ; 256 x 16 bytes
.wramBlocks:
    REPT 16
        ld [hl+], a
    ENDR
    dec b
    jr nz, .wramBlocks
    dec c
    jr nz, .clearWram
    ld hl, $FF80
    ld b, $7F
.clearHram:
    ld [hl+], a
    dec b
    jr nz, .clearHram
    ld hl, VRAM_TILES
    ld b, 0 ; 256 blocks: 4 KiB
    call FillBlocks
    ld b, 0
    call FillBlocks
    ld hl, $FE00
    ld b, 160 / 16
    call FillBlocks
    ; OAM DMA routine into HRAM, then all tiles, which stay fixed for the whole demo.
    ld hl, OamDmaSource
    ld de, hOamDma
    ld bc, hOamDma.end - hOamDma
    call Copy
    ld hl, FontTiles
    ld de, VRAM_TILES + TILE_FONT * 16
    ld b, (FontTiles.end - FontTiles) / 16
    call CopyBlocks
    ld hl, StarTiles
    ld de, VRAM_TILES + TILE_STAR * 16
    ld b, (StarTiles.end - StarTiles) / 16
    call CopyBlocks
    ld hl, WaveTiles
    ld de, VRAM_TILES + TILE_WAVE * 16
    ld b, (WaveTiles.end - WaveTiles) / 16
    call CopyBlocks
    ld hl, ObjectTiles
    ld de, VRAM_TILES + TILE_OBJECT * 16
    ld b, (ObjectTiles.end - ObjectTiles) / 16
    call CopyBlocks
    ; Parameters and fixed registers.
    ld hl, SceneDefaults
    ld de, wSceneParams
    ld bc, SCENE_COUNT * 4
    call Copy
    ld a, 1
    ld [wAuto], a
    ld a, STATUS_LINE
    ldh [rWY], a
    ld a, 7
    ldh [rWX], a
    ld a, OBJ_PALETTE0
    ldh [rOBP0], a
    ld a, OBJ_PALETTE1
    ldh [rOBP1], a
    ld a, STAT_HBLANK
    ldh [rSTAT], a
    call LoadParams
    call MusicInit
    call EnterScene
    ei
    jr MainLoop.build

; Per VBlank: handles input and music, then builds the next frame into the back buffers.
MainLoop::
    halt
    ldh a, [hVBlank]
    and a
    jr z, MainLoop
    xor a
    ldh [hVBlank], a
    ldh [hReady], a
    call ProcessInput
    call UpdateMusic
    ldh a, [hSwitch]
    and a
    jr nz, MainLoop
.build:
    call Animate
    call BuildFrame
    ld hl, wFrame
    inc [hl]
    jr nz, .counted
    inc hl
    inc [hl]
.counted:
    ld a, 1
    ldh [hReady], a
    jr MainLoop

; VBlank: reads the joypad, then shows the finished frame or loads a newly selected scene.
VBlankIsr:
    push af
    push bc
    push de
    push hl
    call ReadJoypad
    ldh a, [hSwitch]
    and a
    jr nz, .switch
    ldh a, [hReady]
    and a
    jr nz, .commit
    ld hl, wLagFrames
    inc [hl]
    jr .line0
.commit:
    xor a
    ldh [hReady], a
    call CommitFrame
.line0:
    call SetLine0
.done:
    ld a, 1
    ldh [hVBlank], a
    pop hl
    pop de
    pop bc
    pop af
.return:
    reti
; Scene change: switches the LCD off here at the start of VBlank and loads the new scene.
.switch:
    xor a
    ldh [hSwitch], a
    ld a, IE_VBLANK
    ldh [rIE], a
    xor a
    ldh [rLCDC], a
    call EnterScene
    jr .done

; Shows the back buffers: OAM, raster pages, SCY, both status rows and one pending tile chunk.
CommitFrame:
    ld a, HIGH(wShadowOam)
    call hOamDma
    ldh a, [hBackPage]
    ld b, a
    ldh a, [hFrontPage]
    ldh [hBackPage], a
    ld a, b
    ldh [hFrontPage], a
    ldh a, [hNextScy]
    ldh [rSCY], a
    ld a, [wBuiltAngle]
    ld [wShownAngle], a
    ld hl, wStatusText
    ld de, VRAM_MAP_WIN
    call CopyRow
    ld hl, wStatusText + 20
    ld de, VRAM_MAP_WIN + 32
    call CopyRow
; Copies one 64-byte chunk of a queued tile upload. The destination never crosses a 256-byte page.
UploadChunk:
    ld a, [wUpload]
    and a
    ret z
    dec a
    ld [wUpload], a
    ldh a, [hUploadSrc]
    ld l, a
    ldh a, [hUploadSrc + 1]
    ld h, a
    ldh a, [hUploadDst]
    ld e, a
    ldh a, [hUploadDst + 1]
    ld d, a
    REPT 64
        ld a, [hl+]
        ld [de], a
        inc e
    ENDR
    ld a, l
    ldh [hUploadSrc], a
    ld a, h
    ldh [hUploadSrc + 1], a
    ld a, e
    ldh [hUploadDst], a
    ret

; Copies 20 bytes from HL to the window map row at DE.
CopyRow:
    REPT 20
        ld a, [hl+]
        ld [de], a
        inc e
    ENDR
    ret

; Line 0 has no HBlank before it, so its SCX/BGP are set here.
SetLine0:
    ldh a, [hFrontPage]
    ld h, a
    ld l, 0
    ld a, [hl]
    ldh [rSCX], a
    inc h
    ld a, [hl]
    ldh [rBGP], a
    ret

; Stores the joypad in hHeldRaw: 1 = pressed, buttons in the high nibble.
ReadJoypad:
    ld a, $20 ; directions
    ldh [rP1], a
    ldh a, [rP1]
    ldh a, [rP1]
    cpl
    and $0F
    ld b, a
    ld a, $10 ; buttons
    ldh [rP1], a
    ldh a, [rP1]
    ldh a, [rP1]
    ldh a, [rP1]
    ldh a, [rP1]
    cpl
    and $0F
    swap a
    or b
    ldh [hHeldRaw], a
    ld a, $30
    ldh [rP1], a
    ret

SECTION "OAM DMA source", ROM0
OamDmaSource:
LOAD "OAM DMA", HRAM
; A = source page. Waits the 160 M-cycles of the transfer in HRAM.
hOamDma::
    ldh [rDMA], a
    ld a, 40
.wait:
    dec a
    jr nz, .wait
    ret
.end:
ENDL

SECTION "Input", ROM0
; Applies this frame's buttons; Select goes first, so the others act on the new scene.
ProcessInput:
    ldh a, [hHeldRaw]
    ld b, a
    ld a, [wHeld]
    cpl
    and b
    ld [wPressed], a
    ld c, a
    ld a, b
    ld [wHeld], a
    call DirectionSteps
    ld [wSteps], a
    ld a, [wPressed]
    bit PAD_SELECT, a
    call nz, NextScene
    ld hl, wSpeed
    ld a, [wSteps]
    bit PAD_RIGHT, a
    call nz, Increment
    ld a, [wSteps]
    bit PAD_LEFT, a
    call nz, Decrement
    ld hl, wAmount
    ld a, [wSteps]
    bit PAD_UP, a
    call nz, Increment
    ld a, [wSteps]
    bit PAD_DOWN, a
    call nz, Decrement
    ld a, [wPressed]
    bit PAD_A, a
    call nz, NextPattern
    ld a, [wPressed]
    bit PAD_B, a
    jr z, .start
    ld a, [wDirection]
    xor 1
    ld [wDirection], a
.start:
    ld a, [wPressed]
    bit PAD_START, a
    ret z
    ld a, [wAuto]
    xor 1
    ld [wAuto], a
    ret

; B = held, C = pressed: returns A = direction steps with auto-repeat; opposites cancel.
DirectionSteps:
    ld hl, wRepeat
    ld d, 0
    ld e, 1
.next:
    ld a, b
    and e
    jr z, .skip
    ld a, c
    and e
    jr z, .held
    ld [hl], REPEAT_DELAY
    jr .step
.held:
    dec [hl]
    jr nz, .skip
    ld [hl], REPEAT_INTERVAL
.step:
    ld a, d
    or e
    ld d, a
.skip:
    inc hl
    sla e
    bit 4, e
    jr z, .next
    ld a, b
    and %0011
    cp %0011
    jr nz, .vertical
    ld a, d
    and %1100
    ld d, a
.vertical:
    ld a, b
    and %1100
    cp %1100
    ld a, d
    ret nz
    and %0011
    ret

; Increments [HL] up to PARAM_MAX.
Increment:
    ld a, [hl]
    cp PARAM_MAX
    ret z
    inc [hl]
    ret

; Decrements [HL] down to 0.
Decrement:
    ld a, [hl]
    and a
    ret z
    dec [hl]
    ret

NextPattern:
    ld a, [wPattern]
    inc a
    and PATTERN_COUNT - 1
    ld [wPattern], a
    ld a, [wScene]
    cp SCENE_PLASMA
    ret nz
    ; Queue the 16 plasma tiles of the new pattern: four 64-byte chunks, one per VBlank.
    ld a, [wPattern]
    add HIGH(PlasmaTextures)
    ldh [hUploadSrc + 1], a
    xor a
    ldh [hUploadSrc], a
    ldh [hUploadDst], a
    ld a, HIGH(VRAM_TILES + TILE_PLASMA * 16)
    ldh [hUploadDst + 1], a
    ld a, 4
    ld [wUpload], a
    ret

; Advances the 16-bit phase by (speed + 1) * 128 when running, backwards when reversed.
Animate:
    ld a, [wAuto]
    and a
    ret z
    ld a, [wSpeed]
    inc a
    ld b, a
    xor a
    srl b
    rra
    ld c, a
    ld hl, wPhase
    ld a, [wDirection]
    and a
    jr nz, .reverse
    ld a, [hl]
    add c
    ld [hl+], a
    ld a, [hl]
    adc b
    ld [hl], a
    ret
.reverse:
    ld a, [hl]
    sub c
    ld [hl+], a
    ld a, [hl]
    sbc b
    ld [hl], a
    ret

SECTION "Scene control", ROM0
; Selects the next scene's parameters at once; the VBlank handler loads the scene itself.
NextScene:
    call StoreParams
    ld a, [wScene]
    inc a
    cp SCENE_COUNT
    jr c, .set
    xor a
.set:
    ld [wScene], a
    call LoadParams
    ld a, 1
    ldh [hSwitch], a
    ret

; With the LCD off: load the scene map, reset its phase, show its first frame and restart the LCD.
EnterScene:
    xor a
    ld [wPhase], a
    ld [wPhase + 1], a
    ld [wUpload], a
    ld hl, wShadowOam
    ld b, 160 / 16
    call FillBlocks
    ld a, [wScene]
    add a
    add LOW(SceneMaps)
    ld l, a
    adc HIGH(SceneMaps)
    sub l
    ld h, a
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    ld de, VRAM_MAP_BG
    ld b, 1024 / 16
    call CopyBlocks
    ld a, [wScene]
    cp SCENE_PLASMA
    jr nz, .pages
    ld a, [wPattern]
    add HIGH(PlasmaTextures)
    ld h, a
    ld l, 0
    ld de, VRAM_TILES + TILE_PLASMA * 16
    ld b, 256 / 16
    call CopyBlocks
.pages:
    ld h, HIGH(wRasterA)
    call InitPage
    ld h, HIGH(wRasterB)
    call InitPage
    ld a, HIGH(wRasterA)
    ldh [hFrontPage], a
    ld a, HIGH(wRasterB)
    ldh [hBackPage], a
    call BuildFrame
    call CommitFrame
    call SetLine0
    ; Turn the LCD on and drop any STAT request it raised before enabling interrupts.
    ld a, LCDC_ON
    ldh [rLCDC], a
    xor a
    ldh [rIF], a
    ld a, IE_VBLANK | IE_STAT
    ldh [rIE], a
    ret

; Presets the fixed lines of raster buffer H: the status bar, plus the orbit scene's palette.
InitPage:
    ld l, STATUS_LINE
    xor a
.scx:
    ld [hl], a
    inc l
    jr nz, .scx
    inc h
    ld a, [wScene]
    cp SCENE_ORBIT
    jr nz, .status
    ld a, $E4
.orbit:
    ld [hl], a
    inc l
    bit 7, l
    jr z, .orbit
.status:
    ld l, STATUS_LINE
    ld a, STATUS_PALETTE
.statusLines:
    ld [hl], a
    inc l
    jr nz, .statusLines
    ret

; Returns HL = this scene's slot in wSceneParams.
SceneParams:
    ld a, [wScene]
    add a
    add a
    add LOW(wSceneParams)
    ld l, a
    ld h, HIGH(wSceneParams)
    ret

StoreParams:
    call SceneParams
    ld de, wSpeed
    REPT 4
        ld a, [de]
        inc de
        ld [hl+], a
    ENDR
    ret

LoadParams:
    call SceneParams
    ld de, wSpeed
    REPT 4
        ld a, [hl+]
        ld [de], a
        inc de
    ENDR
    ret

; Fill B * 16 bytes at HL with A (B = 0: 4 KiB).
FillBlocks:
    REPT 16
        ld [hl+], a
    ENDR
    dec b
    jr nz, FillBlocks
    ret

; Copy B * 16 bytes from HL to DE.
CopyBlocks:
    REPT 16
        ld a, [hl+]
        ld [de], a
        inc de
    ENDR
    dec b
    jr nz, CopyBlocks
    ret

; Copy BC bytes from HL to DE.
Copy:
    ld a, [hl+]
    ld [de], a
    inc de
    dec bc
    ld a, b
    or c
    jr nz, Copy
    ret

; Builds the back buffers for the next frame from the current phase and parameters.
BuildFrame:
    ld a, [wPhase + 1]
    ld [wBuiltAngle], a
    call BuildStatusText
    ld a, [wScene]
    and a
    jr z, BuildWave
    dec a
    jp z, BuildPlasma
    jp BuildOrbit

; Wave scene: sine SCX per line, and the base palette with a dark and a bright bar swinging.
BuildWave:
    xor a
    ldh [hNextScy], a
    ldh a, [hBackPage]
    ld d, a
    ld e, 0
    ld a, [wAmount]
    add HIGH(SineTables)
    ld h, a
    ld a, [wPhase + 1]
    ld l, a
    ld b, RASTER_LINES
.scx:
    ld a, [hl]
    sra a
    ld [de], a
    inc e
    inc l
    inc l
    dec b
    jr nz, .scx
    inc d
    ld a, [wPattern]
    ld c, a
    add LOW(WaveBasePalettes)
    ld l, a
    ld h, HIGH(WaveBasePalettes)
    ld a, [hl]
    ld h, d
    ld l, 0
    ld b, RASTER_LINES / 8
.fill:
    REPT 8
        ld [hl+], a
    ENDR
    dec b
    jr nz, .fill
    ld a, [wPhase + 1]
    add a
    ld e, a
    ld b, 0
    call DrawBar
    ld a, e
    add 128
    ld e, a
    ld b, 1
    ; fall through

; Writes a 15-line bar into BGP page D at angle E; B = 0 dark / 1 bright, C = pattern.
DrawBar:
    ld h, HIGH(SineTables) + BAR_LEVEL
    ld l, e
    ld a, [hl]
    add BAR_CENTER - 7
    push de
    ld e, a
    ld a, c
    add a
    add b
    swap a
    add LOW(WaveBars)
    ld l, a
    ld h, HIGH(WaveBars)
    REPT 15
        ld a, [hl+]
        ld [de], a
        inc e
    ENDR
    pop de
    ret

; Plasma scene: sine SCX per line, sine SCY, and one palette set by the amount and the angle.
BuildPlasma:
    ldh a, [hBackPage]
    ld d, a
    ld e, 0
    ld a, [wPhase + 1]
    ld c, a
    add a
    ld l, a
    ld h, HIGH(SineTables) + 2
    ld b, RASTER_LINES
.scx:
    ld a, [hl]
    sra a
    sra a
    add c
    ld [de], a
    inc e
    ld a, l
    add 4
    ld l, a
    dec b
    jr nz, .scx
    ld h, HIGH(SineTables) + 3
    ld l, c
    ld a, [hl]
    ldh [hNextScy], a
    ld a, c
    swap a
    rrca
    and 3
    ld b, a
    ld a, [wAmount]
    add a
    add a
    add b
    add LOW(PlasmaPalettes)
    ld l, a
    ld h, HIGH(PlasmaPalettes)
    ld a, [hl]
    inc d
    ld h, d
    ld l, 0
    ld b, RASTER_LINES / 8
.fill:
    REPT 8
        ld [hl+], a
    ENDR
    dec b
    jr nz, .fill
    ret

; Orbit scene: star bands scrolling at 1x to 3x the angle, and five rings of eight objects.
BuildOrbit:
    xor a
    ldh [hNextScy], a
    ld a, [wPhase + 1]
    ld b, a
    add a
    ld c, a
    add b
    ld e, a
    ldh a, [hBackPage]
    ld h, a
    ld l, 0
    ld d, RASTER_LINES / 24
.bands:
    ld a, b
    REPT 8
        ld [hl+], a
    ENDR
    ld a, c
    REPT 8
        ld [hl+], a
    ENDR
    ld a, e
    REPT 8
        ld [hl+], a
    ENDR
    dec d
    jr nz, .bands
    ld a, b
    REPT 8
        ld [hl+], a
    ENDR
    ; Objects
    ld a, [wPattern]
    ld c, a
    xor a
    bit 0, c
    jr z, .noXFlip
    or %00100000
.noXFlip:
    bit 1, c
    jr z, .noYFlip
    or %01000000
.noYFlip:
    ldh [hRingFlip], a
    ld hl, wShadowOam
    ld a, [wPhase + 1]
    ld [wRingAngle], a
    xor a
    ld [wRing], a
.ring:
    ld a, [wAmount]
    ld b, a
    add a
    add a
    add b ; amount * 5
    ld b, a
    ld a, [wRing]
    add b
    add LOW(RingLevels)
    ld e, a
    ld d, HIGH(RingLevels)
    ld a, [de]
    add HIGH(SineTables)
    ld d, a
    ld a, [wRing]
    ld b, a
    ld a, [wPattern]
    add b
    and 3
    add TILE_OBJECT
    ldh [hRingTile], a
    xor a
    bit 0, b
    jr z, .palette
    or %00010000
.palette:
    ld c, a
    ld a, b
    cp 2
    ld a, c
    jr nz, .priority
    or %10000000
.priority:
    ldh [hRingFlags], a
    ld a, [wRingAngle]
    ld c, a
    ld b, RING_OBJECTS
.object:
    ld e, c
    ld a, [de]
    add ORBIT_Y
    ld [hl+], a
    ld a, c
    add 64
    ld e, a
    ld a, [de]
    add ORBIT_X
    ld [hl+], a
    ldh a, [hRingTile]
    ld [hl+], a
    ldh a, [hRingFlags]
    bit 0, b ; odd objects
    jr z, .flags
    ld e, a
    ldh a, [hRingFlip]
    xor e
.flags:
    ld [hl+], a
    ld a, c
    add 256 / RING_OBJECTS
    ld c, a
    dec b
    jr nz, .object
    ld a, [wPhase + 1]
    ld b, a
    ld a, [wRingAngle]
    add b
    ld [wRingAngle], a
    ld a, [wRing]
    inc a
    ld [wRing], a
    cp ORBIT_RINGS
    jr nz, .ring
    ret

; Status rows: scene template, then scene number, RUN/STOP, speed, amount, pattern and direction.
BuildStatusText:
    ld a, [wScene]
    ld b, a
    add a
    add a
    add b ; scene * 5
    add a
    add a
    add a ; scene * 40
    add LOW(StatusTemplates)
    ld l, a
    adc HIGH(StatusTemplates)
    sub l
    ld h, a
    ld de, wStatusText
    REPT 40
        ld a, [hl+]
        ld [de], a
        inc e
    ENDR
    ld a, b
    add '1'
    ld [wStatusText], a
    ld hl, RunText
    ld a, [wAuto]
    and a
    jr nz, .auto
    ld hl, StopText
.auto:
    ld de, wStatusText + 15
    REPT 4
        ld a, [hl+]
        ld [de], a
        inc e
    ENDR
    ld a, [wSpeed]
    add '0'
    ld [wStatusText + 23], a
    ld a, [wAmount]
    add '0'
    ld [wStatusText + 28], a
    ld a, [wPattern]
    add '0'
    ld [wStatusText + 33], a
    ld a, [wDirection]
    and a
    ld a, '+'
    jr z, .direction
    ld a, '-'
.direction:
    ld [wStatusText + 38], a
    ret

INCLUDE "graphics.asm"
INCLUDE "tables.asm"
INCLUDE "music.asm"

; Observation block at fixed addresses for debuggers and tests.
SECTION "Observation", WRAM0[$C000]
wScene::      db ; 0 wave, 1 plasma, 2 orbit
wSpeed::      db
wAmount::     db
wPattern::    db
wDirection::  db ; 0 forward, 1 reverse
wAuto::       db ; 1 running, 0 stopped
wPhase::      dw ; high byte = angle
wFrame::      dw
wHeld::       db
wPressed::    db
wSteps::      db
wLagFrames::  db
wUpload::     db ; tile chunks still queued
wShownAngle:: db
wSceneParams:: ds SCENE_COUNT * 4 ; speed, amount, pattern, direction
wRepeat::     ds 4 ; right, left, up, down
wBuiltAngle:: db
wRing::       db
wRingAngle::  db
    ds 5
wStatusText:: ds 40 ; two 20-character rows
    ASSERT HIGH(wStatusText) == HIGH(wStatusText + 39)

SECTION "Shadow OAM", WRAM0[$C100]
wShadowOam:: ds 160

; Two raster buffers, each an SCX page followed by a BGP page, indexed by line.
SECTION "Raster A", WRAM0[$C200]
wRasterA:: ds 512
SECTION "Raster B", WRAM0[$C400]
wRasterB:: ds 512

SECTION "Stack", WRAM0[$DF00]
    ds $100
wStackTop::

SECTION "HRAM variables", HRAM
hFrontPage:: db ; SCX page of the shown buffer
hBackPage::  db
hVBlank::    db
hReady::     db
hHeldRaw::   db
hNextScy::   db
hSwitch::    db ; scene change pending
hUploadSrc:: dw
hUploadDst:: dw
hRingTile::  db
hRingFlags:: db
hRingFlip::  db
