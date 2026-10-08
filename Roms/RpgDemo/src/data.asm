; Pictures (converted by Build.ps1 from art/*.png with rgbgfx) and the game's names and numbers.

SECTION "Font and UI", ROM0
FontTiles:: INCBIN "font.2bpp"
.end:
UiTiles:: INCBIN "ui.2bpp"
.end:

SECTION "Title pictures", ROM0
TitleTiles:: INCBIN "title.2bpp"
.end:
TitleMap:: INCBIN "title.tilemap" ; 20 x 32 tile IDs
LogoTiles:: INCBIN "logo.2bpp"
.end:
LogoMap:: INCBIN "logo.tilemap" ; 24 x 3 tile IDs, letters USAGIQET
LogoObjTiles:: INCBIN "logo-obj.2bpp" ; letters as objects, by column
.end:
HeroWalkTiles:: INCBIN "heroes-walk.2bpp" ; 8 heroes x 2 frames, 16 x 16
.end:

SECTION "Hero pictures", ROM0
HeroFaceTiles:: INCBIN "heroes-face.2bpp" ; 8 portraits of 4 x 4 tiles
HeroBackTiles:: INCBIN "heroes-back.2bpp" ; 8 back views of 6 x 6 tiles
EnemyTiles:: INCBIN "enemies.2bpp" ; 3 enemies of 6 x 6 tiles

SECTION "Scene pictures", ROM0
BattleTiles:: INCBIN "battle.2bpp" ; the platforms, 16 x 2 tiles
.end:
EffectTiles:: INCBIN "effects.2bpp" ; burst, star, shield, heal; 16 x 16
.end:
KeypadTiles:: INCBIN "keypad.2bpp" ; the pad, plain and pressed, 12 x 6 tiles
.end:

SECTION "Names", ROM0
HeroNames::
    dw NameSera, NamePina, NameShiro, NameKuro, NameYuki, NameAkane, NameYoru, NameHana
HeroMagic::
    dw MagicSera, MagicPina, MagicShiro, MagicKuro, MagicYuki, MagicAkane, MagicYoru, MagicHana
EnemyNames::
    dw NameSlimeCat, NameKingCat, NameShadowCat
EnemyMoves::
    dw MoveBounce, MoveRoyalPaw, MoveShadowBite
EnemySpecials::
    dw MoveBounce, MoveCrownDrop, MoveDarkDream

; Per difficulty: level, max HP, attack base and range, special every N turns (0 never), EXP.
EnemyStats::
    db 5, 30, 3, 4, 0, 40
    db 25, 60, 5, 5, 4, 120
    db 99, 99, 7, 6, 3, 250

NameSera:  db "SERA", 0
NamePina:  db "PINA", 0
NameShiro: db "SHIRO", 0
NameKuro:  db "KURO", 0
NameYuki:  db "YUKI", 0
NameAkane: db "AKANE", 0
NameYoru:  db "YORU", 0
NameHana:  db "HANA", 0

MagicSera:  db "HOLY LIGHT", 0
MagicPina:  db "HEART BEAM", 0
MagicShiro: db "MOON BLADE", 0
MagicKuro:  db "DARK WING", 0
MagicYuki:  db "ICE STORM", 0
MagicAkane: db "FOX FIRE", 0
MagicYoru:  db "NIGHT RAIN", 0
MagicHana:  db "PETAL DANCE", 0

NameSlimeCat:  db "SLIME CAT", 0
NameKingCat:   db "KING CAT", 0
NameShadowCat: db "SHADOWCAT", 0

MoveBounce:     db "BOUNCE", 0
MoveRoyalPaw:   db "ROYAL PAW", 0
MoveCrownDrop:  db "CROWN DROP", 0
MoveShadowBite: db "SHADOW BITE", 0
MoveDarkDream:  db "DARK DREAM", 0
