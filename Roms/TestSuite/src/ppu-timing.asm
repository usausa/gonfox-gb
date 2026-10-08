; DMG PPU timing in M-cycles after LCD on: LY, STAT modes, LY=LYC, STAT/VBlank interrupts, VRAM/OAM.

INCLUDE "hardware.inc"
INCLUDE "report.inc"

DEF LCDC_BASE EQU LCDCF_ON | LCDCF_BG8000 | LCDCF_BGON
DEF LCDC_OBJ EQU LCDC_BASE | LCDCF_OBJON | LCDCF_OBJ16
DEF LCDC_OBJ_OFF EQU LCDC_BASE | LCDCF_OBJ16
DEF LCDC_WIN EQU LCDC_BASE | LCDCF_WINON
DEF LCDC_WIN_OBJ EQU LCDC_WIN | LCDCF_OBJON | LCDCF_OBJ16
DEF VRAM_PROBE EQU $8FFF ; Tile data no line uses; address bit 12 clear.
DEF VRAM_VALUE EQU $5A
DEF OAM_VALUE EQU $A5 ; OAM is filled with it when no objects are wanted: Y = 165 is below the screen.
DEF NO_LYC EQU $FF
DEF LINE EQU 114 ; M-cycles per line; line L (L >= 1) starts at M-cycle L * LINE - 1 after the LCD-enable write.
DEF FRAME EQU 154 * LINE
DEF PROFILE_TOP EQU 39 ; Y of the first object group in profile passes (lines 23-38, 39-54, 55-70).
DEF END_TOP EQU 19 ; Y of the first object group in end passes (region r: lines 3-18 + 16r).
DEF END_START EQU 58 ; Line offset of the first STAT read of an end pass region.
DEF END_SLED EQU 50 ; Line offset where the interrupt sled of an end pass region starts.

DEF END_NO_IRQ EQU $80 ; End pass flag (with the region count): the interrupt bytes are not emitted.
DEF END_VRAM EQU $40 ; End pass flag: VRAM_PROBE is read instead of STAT.
DEF END_OAM EQU $20 ; End pass flag: OAM is read instead of STAT.

DEF F_OAM EQU 1 ; Profile flag: the first slider reads OAM and its changes are emitted.
DEF F_RAW EQU 2 ; Profile flag: the three profiles are also emitted raw.

DEF TT = 0 ; Timed position: M-cycle from the LCD-enable write at which the next timed instruction starts.

; Moves the timed position by \1 M-cycles.
MACRO t_add
    DEF TT += \1
ENDM

; Spends exactly \1 M-cycles (E and flags change) without moving the timed position.
MACRO t_spend
    DEF _SPEND = \1
    ASSERT _SPEND >= 0, "negative delay"
    REPT _SPEND / 1025
        ld e, 0
:       dec e
        jr nz, :-
    ENDR
    DEF _SPEND = _SPEND % 1025
    IF _SPEND >= 5
        DEF _LOOPS = (_SPEND - 1) / 4
        ld e, _LOOPS
:       dec e
        jr nz, :-
        DEF _SPEND -= _LOOPS * 4 + 1
    ENDC
    REPT _SPEND
        nop
    ENDR
ENDM

; Spends \1 M-cycles and moves the timed position.
MACRO t_wait
    t_spend \1
    t_add \1
ENDM

; Waits until the timed position is \1.
MACRO t_until
    ASSERT (\1) >= TT, "timed position already passed"
    t_wait (\1) - TT
ENDM

; Switches the LCD on with LCDC = A and starts the timed position, shifted by \1 M-cycles (the phase).
MACRO t_start
    ldh [rLCDC], a
    DEF TT = 1
    REPT \1
        nop
    ENDR
ENDM

; Reads [BC] \2 times, 4 M-cycles apart, into [HL+]; the first read is at M-cycle \1.
MACRO t_window
    t_until (\1) - 1
    REPT \2
        ld a, [bc]
        ld [hl+], a
    ENDR
    t_add 4 * (\2)
ENDM

; Reads [BC] into [HL+] at 114 successive M-cycles from \1: six per line, 19 lines, each 1 later.
MACRO t_slider
    t_until (\1) - 3
    ld d, 19
    t_add 2
.line\@:
    REPT 5
        ld a, [bc]
        ld [hl+], a
        t_spend 15
    ENDR
    ld a, [bc]
    ld [hl+], a
    t_spend 12
    dec d
    jr nz, .line\@
    t_add 19 * 115 - 1
ENDM

; Writes STAT = 0 at 114 successive M-cycles from \1 as t_slider, storing IF 3 M-cycles after each.
MACRO t_glitch_slider
    t_until (\1) - 7
    ld d, 19
    t_add 2
.line\@:
    REPT 5
        xor a
        ldh [rIF], a
        ldh [c], a
        ldh a, [rIF]
        ld [hl+], a
        t_spend 8
    ENDR
    xor a
    ldh [rIF], a
    ldh [c], a
    ldh a, [rIF]
    ld [hl+], a
    t_spend 5
    dec d
    jr nz, .line\@
    t_add 19 * 115 - 1
ENDM

; Writes LYC = D (counting up) at M-cycles \1 + 115k (k = 0-15), storing STAT 2 M-cycles after each.
MACRO t_lyc_sweep
    t_until (\1) - 5
    ld b, 16
    t_add 2
.loop\@:
    ld a, d
    ldh [rLYC], a
    ldh a, [c]
    ld [hl+], a
    inc d
    t_spend 115 - 13
    dec b
    jr nz, .loop\@
    t_add 16 * 115 - 1
ENDM

; Writes STAT = \2 at M-cycle \1, 4 M-cycles after clearing IF; stores IF 3 M-cycles after the write.
MACRO t_glitch
    t_until (\1) - 7
    xor a
    ldh [rIF], a
    ld a, \2
    ldh [c], a
    ldh a, [rIF]
    ld [hl+], a
    t_add 13
ENDM

; Writes LYC = \2 at M-cycle \1, stores STAT (C = $41) read 2 M-cycles later and sets LYC back to \3.
MACRO t_lyc_write
    t_until (\1) - 5
    ld a, \2
    ldh [rLYC], a
    ldh a, [c]
    ld [hl+], a
    ld a, \3
    ldh [rLYC], a
    t_add 14
ENDM

SECTION "Null address", ROM0[$0000]
; Keeps address 0 free of lists, so that an object list pointer of 0 can mean no objects.
    db 0

SECTION "VBlank vector", ROM0[$40]
    jp IrqEntry

SECTION "STAT vector", ROM0[$48]
    jp IrqEntry

SECTION "PPU variables", WRAM0[$D010]
wIrqPc: ds 2 ; Address pushed by the last interrupt.
wIrqCont: ds 2 ; Where IrqEntry resumes.
wLcdc: ds 1 ; LCDC value for the next pass.
wFlags: ds 1 ; Flags of the current profile configuration.
wProbe: ds 2 ; Address read by the first slider of a profile pass.
wIrqIndex: ds 1 ; Sled index of the last measured interrupt.
wChange: ds 3 ; Change positions found by FindChanges.
wChangeCount: ds 1
wTable: ds 2 ; Next region of the running end pass.
wRegionCount: ds 1
wEndFlags: ds 1 ; Flags of the running end pass.
wEndProbe: ds 2 ; Address the running end pass reads.
wSave: ds 2 ; HL kept across an interrupt.

SECTION "PPU phase buffers", WRAM0[$D100]
wPhase0: ds 256
wPhase1: ds 256
wPhase2: ds 256
wPhase3: ds 256

SECTION "PPU slider buffers", WRAM0[$D500]
wSlider: ds 3 * LINE ; Slider reads in loop order.
wOrdered: ds 3 * LINE ; The same in time order.

SECTION "PPU region buffer", WRAM0[$DA00]
wRegions: ds 8 * 64 ; End pass: per region a 64-byte slot with 44 STAT reads in time order and the interrupt index.

SECTION "PPU HRAM", HRAM
hRegions: ds 1 ; Regions left in the running end pass.
hStep: ds 1 ; Lines between object groups.
hStride: ds 1 ; Group count of Reorder.

SECTION "Main", ROM0
; Runs every group of measurements in order.
Main::
    ld a, VRAM_VALUE
    ld [VRAM_PROBE], a
    call LyWindows
    call LineZeroProfiles
    call LycWindows
    call Profiles
    call EndPasses
    call GlitchProbes
    call FrameProbes
    call LycSweeps
    call IrqTimings
    ret

; Keeps the address an interrupt pushed in wIrqPc and resumes at wIrqCont (22 M-cycles).
IrqEntry:
    pop hl
    ld a, l
    ld [wIrqPc], a
    ld a, h
    ld [wIrqPc + 1], a
    ld hl, wIrqCont
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    jp hl

; Resets scroll, window, LYC, STAT, interrupts, palettes and OAM ($A5: no visible objects); LCD off.
SetupDefaults:
    xor a
    ldh [rSCX], a
    ldh [rSCY], a
    ldh [rWY], a
    ldh [rWX], a
    ldh [rSTAT], a
    ldh [rIE], a
    ld a, NO_LYC
    ldh [rLYC], a
    ld a, %11100100
    ldh [rBGP], a
    ldh [rOBP0], a
    ldh [rOBP1], a
    push hl
    ld hl, _OAMRAM
    ld b, $A0
    ld a, OAM_VALUE
.oam:
    ld [hl+], a
    dec b
    jr nz, .oam
    pop hl
    ld a, LCDC_BASE
    ld [wLcdc], a
    xor a
    ldh [rIF], a
    ret

; Clears OAM and places B groups of the objects at DE, group 0 at Y = C, each next hStep lines lower.
PlaceGroups:
    push bc
    ld hl, _OAMRAM
    ld b, $A0
    xor a
.clear:
    ld [hl+], a
    dec b
    jr nz, .clear
    pop bc
    ld hl, _OAMRAM
.group:
    push de
    ld a, [de]
    inc de
    push bc
    ld b, a
.object:
    ld a, c
    ld [hl+], a
    ld a, [de]
    inc de
    ld [hl+], a
    xor a
    ld [hl+], a
    ld [hl+], a
    dec b
    jr nz, .object
    pop bc
    pop de
    ldh a, [hStep]
    add c
    ld c, a
    dec b
    jr nz, .group
    ret

; Emits the four phase buffers' samples in time order, window by window; HL = window sizes, 0-ended.
EmitWindows:
    ld de, wPhase0
.window:
    ld a, [hl+]
    and a
    ret z
    ld b, a
.sample:
    REPT 3
        ld a, [de]
        call Emit
        inc d
    ENDR
    ld a, [de]
    call Emit
    ld a, d
    sub 3
    ld d, a
    inc e
    dec b
    jr nz, .sample
    jr .window

; Copies B samples of the four phase buffers into DE in time order.
Interleave:
    ld hl, wPhase0
.sample:
    REPT 3
        ld a, [hl]
        ld [de], a
        inc de
        inc h
    ENDR
    ld a, [hl]
    ld [de], a
    inc de
    ld a, h
    sub 3
    ld h, a
    inc l
    dec b
    jr nz, .sample
    ret

; Copies C interleaved groups of B samples from HL to DE, turning slider loop order into time order.
Reorder:
    ld a, c
    ldh [hStride], a
.group:
    push hl
    push bc
.sample:
    ld a, [hl]
    ld [de], a
    inc de
    ldh a, [hStride]
    add l
    ld l, a
    jr nc, .noCarry
    inc h
.noCarry:
    dec b
    jr nz, .sample
    pop bc
    pop hl
    inc hl
    dec c
    jr nz, .group
    ret

; Emits B bytes from HL.
EmitBytes:
    ld a, [hl+]
    call Emit
    dec b
    jr nz, EmitBytes
    ret

; Emits B bytes from HL taking every C-th byte.
EmitStrided:
    ld a, [hl]
    call Emit
    ld a, l
    add c
    ld l, a
    jr nc, .noCarry
    inc h
.noCarry:
    dec b
    jr nz, EmitStrided
    ret

; Finds where the B samples at HL change under mask C: first three positions and the change count.
FindChanges:
    ld a, $FF
    ld [wChange], a
    ld [wChange + 1], a
    ld [wChange + 2], a
    xor a
    ld [wChangeCount], a
    ld a, [hl+]
    and c
    ld d, a
    ld e, 1
    dec b
.loop:
    ld a, [hl+]
    and c
    cp d
    jr z, .same
    ld d, a
    push hl
    ld a, [wChangeCount]
    cp 3
    jr nc, .counted
    add LOW(wChange)
    ld l, a
    ld a, HIGH(wChange)
    adc 0
    ld h, a
    ld [hl], e
.counted:
    ld hl, wChangeCount
    inc [hl]
    pop hl
.same:
    inc e
    dec b
    jr nz, .loop
    ret

; Emits the first three change positions of the B samples at HL under mask C and the change count.
EmitChanges:
    call FindChanges
    ld hl, wChange
    ld b, 4
    jr EmitBytes

; LY at lines 0-1 after LCD on, 1-2, 143-144 and 152-153-0; OAM and VRAM at the next line 0.
MACRO ly_pass
    ld bc, rLY
    ld hl, wPhase0 + (\1) * 256
    ld a, LCDC_BASE
    t_start \1
    t_window 104, 4
    t_window 218, 4
    t_window 16406, 4
    t_window 17434, 4
    ld bc, _OAMRAM
    t_add 3
    t_window 17548, 4
    ld bc, VRAM_PROBE
    t_add 3
    t_window 17568, 4
    ld bc, rLY
    t_add 3
    t_window 17660, 4
ENDM

; [BC] over the first line after LCD enable and the start of line 1.
MACRO line0_pass
    ld hl, wPhase0 + (\1) * 256
    ld a, [wLcdc]
    t_start \1
    t_window 2, 30
ENDM

; STAT around lines 0-1 and 1-2.
MACRO early_stat_pass
    ld bc, rSTAT
    ld hl, wPhase0 + (\1) * 256
    ld a, LCDC_BASE
    t_start \1
    t_window 104, 4
    t_window 218, 4
ENDM

; STAT around lines 143-144.
MACRO vblank_stat_pass
    ld bc, rSTAT
    ld hl, wPhase0 + (\1) * 256
    ld a, LCDC_BASE
    t_start \1
    t_window 16406, 4
ENDM

; STAT around lines 152-153-0.
MACRO late_stat_pass
    ld bc, rSTAT
    ld hl, wPhase0 + (\1) * 256
    ld a, LCDC_BASE
    t_start \1
    t_window 17434, 8
ENDM

; STAT around lines 152-153-0 and around lines 0-1 of the next frame.
MACRO next_stat_pass
    late_stat_pass \1
    t_window 17660, 4
ENDM

SECTION "Window passes", ROM0
; Each window pass in four phases: the routine of phase P samples everything P M-cycles later.
FOR PH, 4
LyPass{d:PH}:
    ly_pass PH
    ret
Line0Pass{d:PH}:
    line0_pass PH
    ret
EarlyStatPass{d:PH}:
    early_stat_pass PH
    ret
VblankStatPass{d:PH}:
    vblank_stat_pass PH
    ret
LateStatPass{d:PH}:
    late_stat_pass PH
    ret
NextStatPass{d:PH}:
    next_stat_pass PH
    ret
ENDR

; Runs the four phases of window pass \1 with the setup routine \2 before each.
MACRO run_phases
    FOR RP, 4
        call LcdOff
        call \2
        call \1{d:RP}
    ENDR
    call LcdOff
ENDM

SECTION "Window groups", ROM0
; LY, OAM and VRAM windows (7 x 16 bytes).
LyWindows:
    run_phases LyPass, SetupDefaults
    ld hl, .sizes
    jp EmitWindows
.sizes:
    db 4, 4, 4, 4, 4, 4, 4, 0

; First line after LCD enable: STAT raw with LYC = 0, then changes of STAT with SCX = 7, VRAM and OAM.
LineZeroProfiles:
    run_phases Line0Pass, SetupLine0Stat
    ld hl, .size
    call EmitWindows
    run_phases Line0Pass, SetupLine0Scx7
    ld c, 3
    call .changes
    run_phases Line0Pass, SetupLine0Vram
    ld c, $FF
    call .changes
    run_phases Line0Pass, SetupLine0Oam
    ld c, $FF
; Emits the changes of the 120 interleaved samples under mask C.
.changes:
    push bc
    ld de, wOrdered
    ld b, 30
    call Interleave
    pop bc
    ld hl, wOrdered
    ld b, 120
    jp EmitChanges
.size:
    db 30, 0

; First-line pass setup: STAT read with LYC = 0.
SetupLine0Stat:
    call SetupDefaults
    xor a
    ldh [rLYC], a
    ld bc, rSTAT
    ret

; First-line pass setup: STAT read with SCX = 7.
SetupLine0Scx7:
    call SetupDefaults
    ld a, 7
    ldh [rSCX], a
    ld bc, rSTAT
    ret

; First-line pass setup: VRAM read.
SetupLine0Vram:
    call SetupDefaults
    ld bc, VRAM_PROBE
    ret

; First-line pass setup: OAM read.
SetupLine0Oam:
    call SetupDefaults
    ld bc, _OAMRAM
    ret

; STAT windows: LYC = 1 at lines 0-2, 144 at 143-144, 153 and 0 at 152-153-0 and the next 0-1.
LycWindows:
    run_phases EarlyStatPass, SetupLyc1
    ld hl, .early
    call EmitWindows
    run_phases VblankStatPass, SetupLyc144
    ld hl, .vblank
    call EmitWindows
    run_phases LateStatPass, SetupLyc153
    ld hl, .late
    call EmitWindows
    run_phases NextStatPass, SetupLyc0
    ld hl, .next
    jp EmitWindows
.early:
    db 4, 4, 0
.vblank:
    db 4, 0
.late:
    db 8, 0
.next:
    db 8, 4, 0

; Pass setup with LYC = 1.
SetupLyc1:
    call SetupDefaults
    ld a, 1
    ldh [rLYC], a
    ret

; Pass setup with LYC = 144.
SetupLyc144:
    call SetupDefaults
    ld a, 144
    ldh [rLYC], a
    ret

; Pass setup with LYC = 153.
SetupLyc153:
    call SetupDefaults
    ld a, 153
    ldh [rLYC], a
    ret

; Pass setup with LYC = 0.
SetupLyc0:
    call SetupDefaults
    xor a
    ldh [rLYC], a
    ret

SECTION "Profile pass", ROM0
; Reads OAM (or STAT), STAT and VRAM over whole lines and times line 68's mode 0 interrupt.
ProfilePass:
    ld a, [wProbe]
    ld c, a
    ld a, [wProbe + 1]
    ld b, a
    ld hl, wSlider
    ld a, LOW(.cont)
    ld [wIrqCont], a
    ld a, HIGH(.cont)
    ld [wIrqCont + 1], a
    ld a, [wLcdc]
    t_start 0
    t_slider 2 * LINE - 5
    ld bc, rSTAT
    t_add 3
    t_slider 24 * LINE - 5
    ld bc, VRAM_PROBE
    t_add 3
    t_slider 46 * LINE - 5
    t_until 68 * LINE - 1 + END_SLED - 5
    xor a
    ldh [rIF], a
    ei
    t_add 5
.sled:
    REPT 64
        nop
    ENDR
    di
    ld a, $FF
    jr .done
.cont:
    ld a, [wIrqPc]
    sub LOW(.sled)
.done:
    ld [wIrqIndex], a
    ret

; Applies the profile record at HL (LCDC, SCX, WX, flags, object list) and advances HL past it.
ApplyProfile:
    call SetupDefaults
    ld a, [hl+]
    ld [wLcdc], a
    ld a, [hl+]
    ldh [rSCX], a
    ld a, [hl+]
    ldh [rWX], a
    ld a, [hl+]
    ld [wFlags], a
    ld a, [hl+]
    ld e, a
    ld a, [hl+]
    ld d, a
    push hl
    ld hl, wProbe
    ld a, [wFlags]
    and F_OAM
    jr z, .noOam
    ld a, LOW(_OAMRAM)
    ld [hl+], a
    ld [hl], HIGH(_OAMRAM)
    jr .probeSet
.noOam:
    ld a, LOW(rSTAT)
    ld [hl+], a
    ld [hl], HIGH(rSTAT)
.probeSet:
    ld a, d
    or e
    jr z, .noObjects
    ld a, 16
    ldh [hStep], a
    ld b, 3
    ld c, PROFILE_TOP
    call PlaceGroups
.noObjects:
    ld a, STATF_MODE0
    ldh [rSTAT], a
    ld a, IEF_STAT
    ldh [rIE], a
    xor a
    ldh [rIF], a
    pop hl
    ret

; Emits the profile: raw if flagged, changes of STAT, VRAM and (if flagged) OAM, then the interrupt.
EmitProfile:
    ld hl, wSlider
    ld de, wOrdered
    ld bc, 19 << 8 | 6
    call Reorder
    ld hl, wSlider + LINE
    ld de, wOrdered + LINE
    ld bc, 19 << 8 | 6
    call Reorder
    ld hl, wSlider + 2 * LINE
    ld de, wOrdered + 2 * LINE
    ld bc, 19 << 8 | 6
    call Reorder
    ld a, [wFlags]
    and F_RAW
    jr z, .noRaw
    ld hl, wOrdered + LINE
    ld b, LINE
    call EmitBytes
    ld hl, wOrdered + 2 * LINE
    ld b, LINE
    call EmitBytes
    ld hl, wOrdered
    ld b, LINE
    call EmitBytes
.noRaw:
    ld hl, wOrdered + LINE
    ld b, LINE
    ld c, 3
    call EmitChanges
    ld hl, wOrdered + 2 * LINE
    ld b, LINE
    ld c, $FF
    call EmitChanges
    ld a, [wFlags]
    and F_OAM
    jr z, .noOam
    ld hl, wOrdered
    ld b, LINE
    ld c, $FF
    call EmitChanges
.noOam:
    ld a, [wIrqIndex]
    cp $FF
    jp z, Emit
    add END_SLED
    jp Emit

; Runs a profile pass for every record in ProfileTable.
Profiles:
    ld hl, ProfileTable
.next:
    ld a, [hl]
    and a
    ret z
    push hl
    call LcdOff
    pop hl
    call ApplyProfile
    push hl
    call ProfilePass
    call LcdOff
    call EmitProfile
    pop hl
    jr .next

; One profile record: LCDC, SCX, WX, flags, object list (0 = none).
MACRO profile
    db \1, \2, \3, \4
    dw \5
ENDM

SECTION "Profile table", ROM0
; Whole-line profiles: plain (also raw), SCX = 7, window WX = 7, one object and ten objects.
ProfileTable:
    profile LCDC_BASE, 0, 0, F_OAM | F_RAW, 0
    profile LCDC_BASE, 7, 0, F_OAM, 0
    profile LCDC_WIN, 0, 7, F_OAM, 0
    profile LCDC_OBJ, 0, 0, 0, ObjAt8
    profile LCDC_OBJ, 3, 0, 0, Obj10At8
    db 0

SECTION "End pass", ROM0
; Per region (SCX, WX) at wTable: [wEndProbe] at line offsets 58-101 and the next mode 0 interrupt.
EndPass:
    ld a, [wEndProbe]
    ld c, a
    ld a, [wEndProbe + 1]
    ld b, a
    ld hl, wRegions
    ld a, LOW(.cont)
    ld [wIrqCont], a
    ld a, HIGH(.cont)
    ld [wIrqCont + 1], a
    ld a, [wLcdc]
    t_start 0
    t_until LINE + 95
.region:
    DEF REGION_T = TT
    push hl
    ld hl, wTable
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    ld a, [hl+]
    ldh [rSCX], a
    ld a, [hl+]
    ldh [rWX], a
    ld a, l
    ld [wTable], a
    ld a, h
    ld [wTable + 1], a
    pop hl
    t_add 35
    t_until REGION_T + 190 - 1 - 70 - 2
    ld d, 11
    t_add 2
.line:
    t_spend 70
    REPT 3
        ld a, [bc]
        ld [hl], a
        ld a, l
        add 11
        ld l, a
        t_spend 3
    ENDR
    ld a, [bc]
    ld [hl], a
    ld a, l
    sub 32
    ld l, a
    dec d
    jr nz, .line
    t_add 11 * 115 - 1
    ld a, l
    add 33
    ld l, a
    ld [wSave], a
    ld a, h
    ld [wSave + 1], a
    t_add 13
    t_until REGION_T - 96 + 13 * LINE + END_SLED - 5
    xor a
    ldh [rIF], a
    ei
    t_add 5
.sled:
    REPT 56
        nop
    ENDR
    di
    ld a, $FF
    ld [wIrqIndex], a
    t_spend 39
    jp .join
.cont:
    ld a, [wIrqPc]
    sub LOW(.sled)
    ld [wIrqIndex], a
    add LOW(.comp)
    ld l, a
    ld a, HIGH(.comp)
    adc 0
    ld h, a
    jp hl
.comp:
    REPT 56
        nop
    ENDR
.join:
    t_add 56 + 50
    ld a, [wSave]
    ld l, a
    ld a, [wSave + 1]
    ld h, a
    ld a, [wIrqIndex]
    ld [hl], a
    ld a, l
    add 20
    ld l, a
    ld a, h
    adc 0
    ld h, a
    t_add 24
    t_until REGION_T + 16 * LINE - 11
    ldh a, [hRegions]
    dec a
    ldh [hRegions], a
    jp nz, .region
    ret

; Emits per region the first mode change ($80 added if more follow) and the interrupt ($FF: none).
EmitEnd:
    ld hl, wRegions
    ld a, [wRegionCount]
    ld b, a
.region:
    push bc
    push hl
    ld b, 44
    ld c, 3
    call FindChanges
    ld a, [wChangeCount]
    and a
    ld a, $FF
    jr z, .emitMode
    ld a, [wChange]
    add END_START
    ld b, a
    ld a, [wChangeCount]
    dec a
    ld a, b
    jr z, .emitMode
    or $80
.emitMode:
    call Emit
    pop hl
    ld a, [wEndFlags]
    and END_NO_IRQ
    jr nz, .next
    push hl
    ld de, 44
    add hl, de
    ld a, [hl]
    pop hl
    cp $FF
    jr z, .emitIrq
    add END_SLED
.emitIrq:
    call Emit
.next:
    ld de, 64
    add hl, de
    pop bc
    dec b
    jr nz, .region
    ret

; Runs every end pass in EndTable.
EndPasses:
    ld hl, EndTable
.next:
    ld a, [hl]
    and a
    ret z
    push hl
    call LcdOff
    pop hl
    call SetupDefaults
    ld a, [hl+]
    ld [wLcdc], a
    ld a, [hl+]
    ld e, a
    ld a, [hl+]
    ld d, a
    ld a, [hl+]
    ld b, a
    and END_NO_IRQ | END_VRAM | END_OAM
    ld [wEndFlags], a
    ld a, b
    and 15
    ldh [hRegions], a
    ld [wRegionCount], a
    push hl
    ld hl, rSTAT
    ld a, [wEndFlags]
    bit 6, a
    jr z, .notVram
    ld hl, VRAM_PROBE
.notVram:
    bit 5, a
    jr z, .notOam
    ld hl, _OAMRAM
.notOam:
    ld a, l
    ld [wEndProbe], a
    ld a, h
    ld [wEndProbe + 1], a
    pop hl
    ld a, l
    ld [wTable], a
    ld a, h
    ld [wTable + 1], a
    ld a, d
    or e
    jr z, .noObjects
    ld a, 16
    ldh [hStep], a
    ld a, [wRegionCount]
    ld b, a
    ld c, END_TOP
    call PlaceGroups
.noObjects:
    ld a, STATF_MODE0
    ldh [rSTAT], a
    ld a, IEF_STAT
    ldh [rIE], a
    xor a
    ldh [rIF], a
    call EndPass
    call LcdOff
    call EmitEnd
    ld a, [wTable]
    ld l, a
    ld a, [wTable + 1]
    ld h, a
    jr .next

; End pass record: LCDC \1, object list \2 (0: none) of \3 objects, flags \4, then (SCX, WX) pairs.
MACRO end_pass
    db \1
    dw \2
    DEF _R = (_NARG - 4) / 2
    ASSERT _R >= 1 && _R <= 8 && (\3) * _R <= 40, "bad end pass"
    db _R | (\4)
    SHIFT 4
    REPT _R
        db \1, \2
        SHIFT 2
    ENDR
ENDM

; End pass with SCX 0-7 and WX \4: LCDC \1, object list \2 with \3 objects, flags \5 (optional).
MACRO end_scx
    IF _NARG > 4
        DEF _F = \5
    ELSE
        DEF _F = 0
    ENDC
    end_pass \1, \2, \3, _F, 0, \4, 1, \4, 2, \4, 3, \4, 4, \4, 5, \4, 6, \4, 7, \4
ENDM

SECTION "End table", ROM0
; End passes: no window or objects, then VRAM and OAM reads instead of STAT.
EndTable:
    end_scx LCDC_BASE, 0, 0, 0
    end_scx LCDC_BASE, 0, 0, 0, END_VRAM | END_NO_IRQ
    end_scx LCDC_BASE, 0, 0, 0, END_OAM | END_NO_IRQ
    end_scx LCDC_WIN, 0, 0, 7, END_VRAM | END_NO_IRQ
    end_scx LCDC_OBJ, ObjAt8, 1, 0, END_VRAM | END_NO_IRQ
; Window at various WX.
FOR X, 0, 3
    end_scx LCDC_WIN, 0, 0, X
ENDR
FOR X, 6, 9
    end_scx LCDC_WIN, 0, 0, X
ENDR
    end_scx LCDC_WIN, 0, 0, 15
    end_scx LCDC_WIN, 0, 0, 87
    end_scx LCDC_WIN, 0, 0, 160
    end_scx LCDC_WIN, 0, 0, 165
    end_scx LCDC_WIN, 0, 0, 166
    end_scx LCDC_WIN, 0, 0, 167
; One object at X = 0-9 and near the right edge; X = 167 leaves out the interrupt (SameBoy differs).
FOR X, 0, 10
    end_scx LCDC_OBJ, ObjX{d:X}, 1, 0
ENDR
    end_scx LCDC_OBJ, ObjX159, 1, 0
    end_scx LCDC_OBJ, ObjX160, 1, 0
    end_scx LCDC_OBJ, ObjX164, 1, 0
    end_scx LCDC_OBJ, ObjX166, 1, 0
    end_scx LCDC_OBJ, ObjX167, 1, 0, END_NO_IRQ
    end_scx LCDC_OBJ, ObjX168, 1, 0
; Two to ten objects at the same X.
    end_scx LCDC_OBJ, ObjSame2, 2, 0
    end_scx LCDC_OBJ, ObjSame3, 3, 0
    end_scx LCDC_OBJ, ObjSame4, 4, 0
    end_scx LCDC_OBJ, ObjSame5, 5, 0
    end_pass LCDC_OBJ, ObjSame6, 6, 0, 0, 0, 1, 0, 2, 0, 4, 0, 5, 0, 7, 0
    end_pass LCDC_OBJ, ObjSame7, 7, 0, 0, 0, 1, 0, 3, 0, 5, 0, 7, 0
    end_pass LCDC_OBJ, ObjSame8, 8, 0, 0, 0, 2, 0, 3, 0, 4, 0, 6, 0
    end_pass LCDC_OBJ, ObjSame9, 9, 0, 0, 0, 3, 0, 5, 0, 7, 0
    end_pass LCDC_OBJ, Obj10At8, 10, 0, 0, 0, 2, 0, 5, 0, 7, 0
; Objects in separate tiles, off-screen, beyond ten, disabled, sharing a tile and out of X order.
    end_scx LCDC_OBJ, ObjSpread2, 2, 0
    end_scx LCDC_OBJ, ObjSpread5, 5, 0
    end_pass LCDC_OBJ, ObjSpread10, 10, 0, 0, 0, 2, 0, 5, 0, 7, 0
    end_pass LCDC_OBJ, Obj10At0, 10, 0, 0, 0, 3, 0, 4, 0, 7, 0
    end_pass LCDC_OBJ, Obj10At167, 10, END_NO_IRQ, 0, 0, 3, 0, 4, 0, 7, 0
    end_pass LCDC_OBJ, Obj10At168Then8, 11, 0, 0, 0, 5, 0, 7, 0
    end_pass LCDC_OBJ, Obj11At8, 11, 0, 0, 0, 5, 0, 7, 0
    end_pass LCDC_OBJ_OFF, Obj10At8, 10, 0, 0, 0, 3, 0, 4, 0, 7, 0
    end_scx LCDC_OBJ, ObjPair0And8, 2, 0
    end_scx LCDC_OBJ, ObjPair8And9, 2, 0
    end_scx LCDC_OBJ, ObjPair8And15, 2, 0
    end_scx LCDC_OBJ, ObjPair1And9, 2, 0
    end_scx LCDC_OBJ, ObjReverse3, 3, 0
; Window and objects together.
    end_pass LCDC_WIN_OBJ, ObjAt8, 1, 0, 0, 7, 1, 7, 2, 7, 3, 7, 0, 8, 0, 9, 0, 10, 0, 16
    end_pass LCDC_WIN_OBJ, ObjAt50, 1, 0, 0, 50, 0, 51, 0, 52, 0, 53, 0, 54, 0, 55, 0, 56, 0, 57
    end_pass LCDC_WIN_OBJ, ObjX0, 1, 0, 0, 0, 3, 0, 0, 7, 5, 7, 0, 1, 0, 8, 7, 8, 0, 166
    end_pass LCDC_WIN_OBJ, Obj10At8, 10, 0, 0, 7, 2, 7, 5, 7, 7, 7
    end_pass LCDC_WIN_OBJ, ObjX166, 1, 0, 0, 165, 0, 166, 3, 166, 0, 167, 0, 159, 7, 160, 0, 7, 0, 0
    db 0

; Object list: count, then X positions in OAM order.
MACRO objects
    db _NARG
    REPT _NARG
        db \1
        SHIFT
    ENDR
ENDM

SECTION "Object lists", ROM0
; Object lists used by the profile and end passes.
FOR X, 0, 10
ObjX{d:X}:
    objects X
ENDR
ObjX159:
    objects 159
ObjX160:
    objects 160
ObjX164:
    objects 164
ObjX166:
    objects 166
ObjX167:
    objects 167
ObjX168:
    objects 168
ObjAt8:
    objects 8
ObjAt50:
    objects 50
ObjSame2:
    objects 8, 8
ObjSame3:
    objects 8, 8, 8
ObjSame4:
    objects 8, 8, 8, 8
ObjSame5:
    objects 8, 8, 8, 8, 8
ObjSame6:
    objects 8, 8, 8, 8, 8, 8
ObjSame7:
    objects 8, 8, 8, 8, 8, 8, 8
ObjSame8:
    objects 8, 8, 8, 8, 8, 8, 8, 8
ObjSame9:
    objects 8, 8, 8, 8, 8, 8, 8, 8, 8
Obj10At8:
    objects 8, 8, 8, 8, 8, 8, 8, 8, 8, 8
Obj11At8:
    objects 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8
ObjSpread2:
    objects 8, 24
ObjSpread5:
    objects 8, 24, 40, 56, 72
ObjSpread10:
    objects 8, 24, 40, 56, 72, 88, 104, 120, 136, 152
Obj10At0:
    objects 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
Obj10At167:
    objects 167, 167, 167, 167, 167, 167, 167, 167, 167, 167
Obj10At168Then8:
    objects 168, 168, 168, 168, 168, 168, 168, 168, 168, 168, 8
ObjPair0And8:
    objects 0, 8
ObjPair8And9:
    objects 8, 9
ObjPair8And15:
    objects 8, 15
ObjPair1And9:
    objects 1, 9
ObjReverse3:
    objects 30, 20, 10

SECTION "Glitch probes", ROM0
; STAT writes on DMG: a line profile with STAT = 0 written (IF bit 1), then single writes' IF.
GlitchProbes:
    call LcdOff
    call SetupDefaults
    call GlitchPass
    call LcdOff
    ld hl, wSlider
    ld de, wOrdered
    ld bc, 19 << 8 | 6
    call Reorder
    ld hl, wOrdered
    ld b, LINE
    ld c, IEF_STAT
    call EmitChanges
    ld hl, wSlider + LINE
    ld b, 6
    jp EmitBytes

; Timed body of GlitchProbes: the slider, then STAT writes in lines 50-51 (mode 3) and 60 (mode 0).
GlitchPass:
    ld c, LOW(rSTAT)
    ld hl, wSlider
    ld a, LCDC_BASE
    t_start 0
    t_glitch_slider 24 * LINE - 5
    ld a, 50
    ldh [rLYC], a
    t_add 5
    ld hl, wSlider + LINE
    t_add 3
    t_glitch 50 * LINE - 1 + 40, 0
    t_glitch 51 * LINE - 1 + 40, 0
    t_glitch 60 * LINE - 1 + 80, STATF_MODE0
    t_glitch 61 * LINE - 1 + 40, STATF_MODE0
    t_glitch 61 * LINE - 1 + 80, STATF_MODE0
    t_glitch 62 * LINE - 1 + 80, 0
    ret

SECTION "Frame probes", ROM0
; Over 12 frames, each 1 M-cycle later: STAT at lines 144 and 0, LYC = 153 at line 153's start.
FrameProbes:
    call LcdOff
    call SetupDefaults
    call FramePass
    call LcdOff
    ld hl, wSlider
    call .probe
    ld hl, wSlider + 1
    call .probe
    ld hl, wSlider + 2
; Emits one probe of each of the 12 frames.
.probe:
    ld b, 12
    ld c, 3
    jp EmitStrided

; Timed body of FrameProbes.
FramePass:
    ld c, LOW(rSTAT)
    ld hl, wSlider
    ld a, LCDC_BASE
    t_start 0
    t_until 144 * LINE - 1 - 30
    ld b, 12
    t_add 2
.frame:
    DEF FRAME_BASE = TT
    t_glitch 144 * LINE - 1 - 6, 0
    t_lyc_write 153 * LINE - 1 - 6, 153, NO_LYC
    t_glitch 154 * LINE - 1 - 6, 0
    t_until FRAME_BASE + FRAME + 1 - 5
    dec b
    jp nz, .frame
    t_add 11 * (FRAME + 1) - 1
    ret

SECTION "LYC sweeps", ROM0
; LYC written around lines 10-25 (new line) and 30-45 (old line) starting; STAT 2 M-cycles after.
LycSweeps:
    call LcdOff
    call SetupDefaults
    call LycSweepPass
    call LcdOff
    ld hl, wSlider
    ld b, 32
    jp EmitBytes

; Timed body of LycSweeps.
LycSweepPass:
    ld c, LOW(rSTAT)
    ld hl, wSlider
    ld a, LCDC_BASE
    t_start 0
    t_until 9 * LINE
    ld d, 10
    t_add 2
    t_lyc_sweep 10 * LINE - 1 - 8
    ld d, 29
    t_add 2
    t_lyc_sweep 30 * LINE - 1 - 8
    ret

SECTION "Interrupt timings", ROM0
; Points wIrqCont at .cont\1 (the continuation of sled_body \1).
MACRO set_cont
    ld a, LOW(.cont\1)
    ld [wIrqCont], a
    ld a, HIGH(.cont\1)
    ld [wIrqCont + 1], a
ENDM

; A NOP sled of \2 with label suffix \1; emits the dispatch's sled index, or $FF if none.
MACRO sled_body
.sled\1:
    REPT \2
        nop
    ENDR
    di
    ld a, $FF
    jr .done\1
.cont\1:
    ld a, [wIrqPc]
    sub LOW(.sled\1)
.done\1:
    call Emit
ENDM

; One interrupt case: LCDC \1, STAT \2, LYC \3, IE \4, SCX \5, WX \6; sled at M-cycle \7 of \8 NOPs.
MACRO irq_case
    ld a, \5
    ldh [rSCX], a
    ld a, \6
    ldh [rWX], a
    ld a, \3
    ldh [rLYC], a
    ld a, \2
    ldh [rSTAT], a
    ld a, \4
    ldh [rIE], a
    set_cont \@
    ld a, \1
    t_start 0
    t_until (\7) - 5
    xor a
    ldh [rIF], a
    ei
    t_add 5
    sled_body \@, \8
ENDM

; irq_case after turning the LCD off and resetting the registers.
MACRO irq_plain
    call LcdOff
    call SetupDefaults
    irq_case \#
ENDM

; irq_case with ten objects at X = 8 on lines 0-15.
MACRO irq_objects
    call LcdOff
    call SetupDefaults
    ld de, Obj10At8
    ld b, 1
    ld c, 16
    call PlaceGroups
    irq_case \#
ENDM

; LYC = \2 with STAT \1: interrupts on before the LCD-enable write, sled of \3 right after it.
MACRO irq_at_enable
    call LcdOff
    call SetupDefaults
    ld a, \2
    ldh [rLYC], a
    ld a, \1
    ldh [rSTAT], a
    ld a, IEF_STAT
    ldh [rIE], a
    set_cont \@
    xor a
    ldh [rIF], a
    ld a, LCDC_BASE
    ei
    t_start 0
    sled_body \@, \3
ENDM

; Register \2 = \3 written at M-cycle \1 (STAT \4, LYC \5, IE = STAT) with IME on; sled of \6 after.
MACRO irq_after_write
    call LcdOff
    call SetupDefaults
    ld a, \5
    ldh [rLYC], a
    ld a, \4
    ldh [rSTAT], a
    ld a, IEF_STAT
    ldh [rIE], a
    set_cont \@
    ld a, LCDC_BASE
    t_start 0
    t_until (\1) - 9
    xor a
    ldh [rIF], a
    ld a, \3
    ei
    ldh [\2], a
    t_add 10
    sled_body \@, \6
ENDM

; Interrupt dispatch times: sled index = dispatch M-cycle minus sled start.
IrqTimings:
; Mode 0 on the first line with SCX 0-7, with the window and with objects.
FOR X, 8
    irq_plain LCDC_BASE, STATF_MODE0, NO_LYC, IEF_STAT, X, 0, 40, 48
ENDR
    irq_plain LCDC_WIN, STATF_MODE0, NO_LYC, IEF_STAT, 0, 7, 40, 48
    irq_plain LCDC_WIN, STATF_MODE0, NO_LYC, IEF_STAT, 3, 0, 40, 48
    irq_objects LCDC_OBJ, STATF_MODE0, NO_LYC, IEF_STAT, 0, 0, 40, 48
; Mode 2 on lines 1, 2, 144 and the next line 0.
    irq_plain LCDC_BASE, STATF_MODE2, NO_LYC, IEF_STAT, 0, 0, 100, 32
    irq_plain LCDC_BASE, STATF_MODE2, NO_LYC, IEF_STAT, 0, 0, 214, 32
    irq_plain LCDC_BASE, STATF_MODE2, NO_LYC, IEF_STAT, 0, 0, 16400, 40
    irq_plain LCDC_BASE, STATF_MODE2, NO_LYC, IEF_STAT, 0, 0, 17540, 40
; Mode 1 and VBlank on line 144.
    irq_plain LCDC_BASE, STATF_MODE1, NO_LYC, IEF_STAT, 0, 0, 16400, 40
    irq_plain LCDC_BASE, 0, NO_LYC, IEF_VBLANK, 0, 0, 16400, 40
; LYC = 1, 2, 144, 153 and 0 (reached on line 153).
    irq_plain LCDC_BASE, STATF_LYC, 1, IEF_STAT, 0, 0, 100, 32
    irq_plain LCDC_BASE, STATF_LYC, 2, IEF_STAT, 0, 0, 214, 32
    irq_plain LCDC_BASE, STATF_LYC, 144, IEF_STAT, 0, 0, 16400, 40
    irq_plain LCDC_BASE, STATF_LYC, 153, IEF_STAT, 0, 0, 17428, 40
    irq_plain LCDC_BASE, STATF_LYC, 0, IEF_STAT, 0, 0, 17428, 40
; Line 2 start with the HBlank source also on: mode 2, then LYC = 2.
    irq_plain LCDC_BASE, STATF_MODE0 | STATF_MODE2, NO_LYC, IEF_STAT, 0, 0, 214, 96
    irq_plain LCDC_BASE, STATF_MODE0 | STATF_LYC, 2, IEF_STAT, 0, 0, 214, 96
; LYC = 0 when the LCD goes on, an LYC write and a DMG STAT write.
    irq_at_enable STATF_LYC, 0, 32
    irq_after_write 10 * LINE - 1 + 40, rLYC, 10, STATF_LYC, NO_LYC, 16
    irq_after_write 10 * LINE - 1 + 80, rSTAT, 0, 0, NO_LYC, 16
    ret
