; Tables built at assembly time from integer-only formulas (tests recompute them), MIT licensed.

; Sets ISIN = round(amplitude * sine(angle)), 256 units per turn, with Bhaskara I's approximation.
MACRO isin ; angle, amplitude
    DEF _H = (\1) & 127
    DEF _P = _H * (128 - _H)
    DEF _D = 81920 - 4 * _P
    DEF _V = (2 * (\2) * 16 * _P + _D) / (2 * _D)
    IF ((\1) & 128) != 0
        DEF ISIN = -_V
    ELSE
        DEF ISIN = _V
    ENDC
ENDM

; Builds a 2bpp tile row from colours computed into the variable COLOR by the macro named in \1.
MACRO tile_row ; colour macro, extra argument
    DEF _LO = 0
    DEF _HI = 0
    FOR PX, 8
        \1 PX, \2
        DEF _LO = _LO | ((COLOR & 1) << (7 - PX))
        DEF _HI = _HI | (((COLOR >> 1) & 1) << (7 - PX))
    ENDR
    db _LO, _HI
ENDM

SECTION "Sine tables", ROM0, ALIGN[8]
; Eight signed sine tables of amplitude 8 * level (0-56), one aligned page per level.
SineTables::
FOR LEVEL, 8
    FOR ANGLE, 256
        isin ANGLE, LEVEL * 8
        db LOW(ISIN)
    ENDR
ENDR

SECTION "Plasma textures", ROM0, ALIGN[8]
; Four 256-byte sets of 16 dithered intensity tiles, one dither layout per set.
MACRO plasma_color ; x (PY, TEX, LEVEL from the loops)
    IF TEX == 0
        DEF _T = (2 * ((\1) & 1)) ^ (3 * (PY & 1)) ; 2x2 ordered dither
    ELIF TEX == 1
        DEF _T = PY & 3 ; horizontal lines
    ELIF TEX == 2
        DEF _T = (\1) & 3 ; vertical lines
    ELSE
        DEF _T = ((\1) + PY) & 3 ; diagonals
    ENDC
    IF _T < (LEVEL & 3)
        DEF COLOR = ((LEVEL >> 2) + 1) & 3
    ELSE
        DEF COLOR = LEVEL >> 2
    ENDC
ENDM
PlasmaTextures::
FOR TEX, 4
    FOR LEVEL, 16
        FOR PY, 8
            tile_row plasma_color, 0
        ENDR
    ENDR
ENDR

SECTION "Wave tiles", ROM0
; Two diagonal ramps and two concentric diamonds, each using all four colours.
MACRO wave_color ; x, tile
    IF (\2) == 0
        DEF COLOR = (((\1) + PY) >> 1) & 3
    ELIF (\2) == 1
        DEF COLOR = (((\1) + 7 - PY) >> 1) & 3
    ELSE
        DEF _DX = 2 * (\1) - 7
        DEF _DY = 2 * PY - 7
        IF _DX < 0
            DEF _DX = -_DX
        ENDC
        IF _DY < 0
            DEF _DY = -_DY
        ENDC
        DEF COLOR = ((_DX + _DY) >> 2) & 3
        IF (\2) == 3
            DEF COLOR = 3 - COLOR
        ENDC
    ENDC
ENDM
WaveTiles::
FOR TILE, 4
    FOR PY, 8
        tile_row wave_color, TILE
    ENDR
ENDR
.end:

SECTION "Wave map", ROM0
; Ramps and diamonds in alternating bands, with two lines of text repeated every 8 rows.
WaveMap::
FOR Y, 32
    IF Y % 8 == 3
        db "  STUDY EMULATOR * MEGA DEMO *  "
    ELIF Y % 8 == 7
        db " SM83 * WAVE SCROLL * RASTER *  "
    ELSE
        FOR X, 32
            db TILE_WAVE + ((X + Y) & 1) + 2 * ((Y >> 2) & 1)
        ENDR
    ENDC
ENDR
    ASSERT @ - WaveMap == 1024

SECTION "Plasma map", ROM0
; Tile levels from four sines that each repeat within 32 tiles, so scrolling wraps without seams.
PlasmaMap::
FOR Y, 32
    FOR X, 32
        isin X * 16, 64
        DEF PV = ISIN
        isin Y * 24, 64
        DEF PV = PV + ISIN
        isin (3 * X + 2 * Y) * 8, 64
        DEF PV = PV + ISIN
        isin (X - Y) * 16, 64
        DEF PV = PV + ISIN
        db TILE_PLASMA + (((PV + 256) >> 5) & 15)
    ENDR
ENDR

SECTION "Star map", ROM0
; A fixed hash places one of four stars on 1/8 of the tiles; the rest use the blank font tile.
StarMap::
FOR Y, 32
    FOR X, 32
        DEF SH = ((X * 73 + Y * 151) ^ (X * Y * 29) ^ (Y * 7)) & 63
        IF SH < 8
            db TILE_STAR + (SH & 3)
        ELSE
            db ' '
        ENDC
    ENDR
ENDR

SECTION "Palette tables", ROM0, ALIGN[8]
; Wave base palettes: normal, inverted, soft, silhouette.
WaveBasePalettes::
    db $E4, $1B, $54, $FC
; Raster bars: a dark and a bright 15-line bar per base palette, strongest at the centre line.
    ds 16 - (@ - WaveBasePalettes), 0
WaveBars::
FOR BASE, 4
    IF BASE == 0
        DEF PAL = $E4
    ELIF BASE == 1
        DEF PAL = $1B
    ELIF BASE == 2
        DEF PAL = $54
    ELSE
        DEF PAL = $FC
    ENDC
    FOR BRIGHT, 2
        FOR I, 15
            DEF _D = I - 7
            IF _D < 0
                DEF _D = -_D
            ENDC
            DEF _K = (7 - _D) >> 1
            DEF OUT = 0
            FOR COL, 4
                DEF _S = (PAL >> (2 * COL)) & 3
                IF BRIGHT
                    DEF _S = _S - _K
                    IF _S < 0
                        DEF _S = 0
                    ENDC
                ELSE
                    DEF _S = _S + _K
                    IF _S > 3
                        DEF _S = 3
                    ENDC
                ENDC
                DEF OUT = OUT | (_S << (2 * COL))
            ENDR
            db OUT
        ENDR
        db 0
    ENDR
ENDR
; Plasma palettes [strength][rotation], from flat grey at strength 0 to full contrast at 7.
PlasmaPalettes::
FOR STRENGTH, 8
    FOR ROT, 4
        DEF OUT = 0
        FOR COL, 4
            DEF _V = (COL + ROT) & 3
            DEF OUT = OUT | (((21 + (2 * _V - 3) * STRENGTH + 7) / 14) << (2 * COL))
        ENDR
        db OUT
    ENDR
ENDR
; Orbit ring radius levels [spread][ring], growing with the spread and the ring.
RingLevels::
FOR STRENGTH, 8
    FOR RING, ORBIT_RINGS
        DEF _L = (STRENGTH * (RING + 2) + 3) / 6
        IF _L > 7
            DEF _L = 7
        ENDC
        db _L
    ENDR
ENDR
    ASSERT HIGH(@ - 1) == HIGH(WaveBasePalettes), "palette tables must share one page"

SECTION "Scene data", ROM0
; Speed, amount, pattern and direction of each scene at power-on.
SceneDefaults::
    db 3, 4, 0, 0 ; wave
    db 3, 6, 0, 0 ; plasma
    db 3, 5, 0, 0 ; orbit
SceneMaps::
    dw WaveMap, PlasmaMap, StarMap
; Two 20-character status rows per scene. Digits, RUN/STOP and the direction sign are patched.
StatusTemplates::
    db "1:WAVE         RUN  "
    db "SPD0 AMP0 PAL0 DIR+ "
    db "2:PLASMA       RUN  "
    db "SPD0 STR0 PAT0 DIR+ "
    db "3:ORBIT        RUN  "
    db "SPD0 SPR0 PAT0 DIR+ "
    ASSERT @ - StatusTemplates == SCENE_COUNT * 40
RunText::
    db "RUN "
StopText::
    db "STOP"
