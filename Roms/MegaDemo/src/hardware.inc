; DMG registers and demo-wide constants. Defined here so the build needs no external include files.

DEF rP1   EQU $FF00
DEF rIF   EQU $FF0F
DEF rNR11 EQU $FF11
DEF rNR12 EQU $FF12
DEF rNR13 EQU $FF13
DEF rNR14 EQU $FF14
DEF rNR21 EQU $FF16
DEF rNR22 EQU $FF17
DEF rNR23 EQU $FF18
DEF rNR24 EQU $FF19
DEF rNR30 EQU $FF1A
DEF rNR32 EQU $FF1C
DEF rNR33 EQU $FF1D
DEF rNR34 EQU $FF1E
DEF rNR41 EQU $FF20
DEF rNR42 EQU $FF21
DEF rNR43 EQU $FF22
DEF rNR44 EQU $FF23
DEF rNR50 EQU $FF24
DEF rNR51 EQU $FF25
DEF rNR52 EQU $FF26
DEF rWAVE EQU $FF30
DEF rLCDC EQU $FF40
DEF rSTAT EQU $FF41
DEF rSCY  EQU $FF42
DEF rSCX  EQU $FF43
DEF rLY   EQU $FF44
DEF rDMA  EQU $FF46
DEF rBGP  EQU $FF47
DEF rOBP0 EQU $FF48
DEF rOBP1 EQU $FF49
DEF rWY   EQU $FF4A
DEF rWX   EQU $FF4B
DEF rIE   EQU $FFFF

DEF VRAM_TILES  EQU $8000
DEF VRAM_MAP_BG EQU $9800 ; 32x32 background map
DEF VRAM_MAP_WIN EQU $9C00 ; window map: status bar rows 0-1

; LCD on, window map 9C00, window on, tile data 8000, BG map 9800, OBJ 8x8, OBJ on, BG on.
DEF LCDC_ON     EQU %11110011
DEF STAT_HBLANK EQU %00001000 ; HBlank as the only STAT source
DEF IE_VBLANK   EQU %00000001
DEF IE_STAT     EQU %00000010

; Joypad bits in wHeld/wPressed: buttons in the high nibble, directions in the low nibble.
DEF PAD_RIGHT  EQU 0
DEF PAD_LEFT   EQU 1
DEF PAD_UP     EQU 2
DEF PAD_DOWN   EQU 3
DEF PAD_A      EQU 4
DEF PAD_B      EQU 5
DEF PAD_SELECT EQU 6
DEF PAD_START  EQU 7

DEF SCENE_WAVE   EQU 0
DEF SCENE_PLASMA EQU 1
DEF SCENE_ORBIT  EQU 2
DEF SCENE_COUNT  EQU 3

DEF PARAM_MAX       EQU 7  ; speed and amount range 0-7
DEF PATTERN_COUNT   EQU 4
DEF REPEAT_DELAY    EQU 20 ; frames to the first repeat
DEF REPEAT_INTERVAL EQU 6  ; frames between later repeats

DEF STATUS_LINE    EQU 128 ; status bar on lines 128-143
DEF RASTER_LINES   EQU 128 ; lines with per-line SCX and BGP
DEF STATUS_PALETTE EQU $1B ; light text on a dark bar
DEF OBJ_PALETTE0   EQU $E4
DEF OBJ_PALETTE1   EQU $6C

; Tile numbers (tile data at 8000). Font tiles hold ASCII 32-95, so strings index them directly.
DEF TILE_FONT   EQU 32
DEF TILE_PLASMA EQU 96  ; 16 intensity levels
DEF TILE_STAR   EQU 112 ; 4 star shapes
DEF TILE_WAVE   EQU 116 ; 4 background tiles
DEF TILE_OBJECT EQU 128 ; 4 sprite shapes

DEF BAR_CENTER  EQU 64 ; raster bars swing around this line
DEF BAR_LEVEL   EQU 6  ; sine level of the bar swing
DEF ORBIT_RINGS EQU 5
DEF RING_OBJECTS EQU 8 ; 5 x 8 = all 40 OAM entries
DEF ORBIT_X     EQU 84 ; orbit centre in OAM coordinates
DEF ORBIT_Y     EQU 76
