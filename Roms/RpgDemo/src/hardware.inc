; DMG registers used by the RPG demo. Defined here so the build needs no external include files.

DEF rP1   EQU $FF00
DEF rDIV  EQU $FF04
DEF rIF   EQU $FF0F
DEF rNR10 EQU $FF10
DEF rNR11 EQU $FF11
DEF rNR12 EQU $FF12
DEF rNR13 EQU $FF13
DEF rNR14 EQU $FF14
DEF rNR21 EQU $FF16
DEF rNR22 EQU $FF17
DEF rNR23 EQU $FF18
DEF rNR24 EQU $FF19
DEF rNR30 EQU $FF1A
DEF rNR31 EQU $FF1B
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
DEF rLYC  EQU $FF45
DEF rDMA  EQU $FF46
DEF rBGP  EQU $FF47
DEF rOBP0 EQU $FF48
DEF rOBP1 EQU $FF49
DEF rWY   EQU $FF4A
DEF rWX   EQU $FF4B
DEF rIE   EQU $FFFF

DEF IEF_VBLANK EQU %0000_0001

; LCDC: LCD on, window map 9C00 but off, BG tiles 8800 mode, BG map 9800, 8x16 objects on, BG on.
DEF LCDC_ON        EQU %1100_0111
DEF LCDC_WINDOW    EQU %0010_0000

; Joypad bits as the demo keeps them (hJoyHeld): low nibble buttons, high nibble directions.
DEF PAD_A      EQU 0
DEF PAD_B      EQU 1
DEF PAD_SELECT EQU 2
DEF PAD_START  EQU 3
DEF PAD_RIGHT  EQU 4
DEF PAD_LEFT   EQU 5
DEF PAD_UP     EQU 6
DEF PAD_DOWN   EQU 7

; VRAM areas (BG uses 8800 mode: IDs 00-7F at 9000, 80-FF at 8800; objects 00-FF at 8000).
DEF VRAM_OBJ   EQU $8000 ; object tiles 00-7F
DEF VRAM_FONT  EQU $8800 ; tile IDs 80-DF: the font
DEF VRAM_UI    EQU $8E00 ; tile IDs E0-FF: frames, HP bar
DEF VRAM_BG    EQU $9000 ; tile IDs 00-7F: the scene's pictures
DEF MAP0       EQU $9800
DEF MAP1       EQU $9C00

DEF TILE_FONT  EQU $80 - 32 ; tile ID = character + TILE_FONT
DEF TILE_UI    EQU $E0

; Text control codes inside strings (printable characters are ASCII 32-127).
DEF TX_END   EQU 0
DEF TX_LINE  EQU 1 ; next line
DEF TX_WAIT  EQU 2 ; wait for a press, then clear
DEF TX_HERO  EQU 3 ; hero name
DEF TX_ENEMY EQU 4 ; enemy name
DEF TX_NUM   EQU 5 ; wNumber in decimal
DEF TX_MAGIC EQU 6 ; the hero's magic
DEF TX_MOVE  EQU 7 ; the enemy's move (wMovePtr)
