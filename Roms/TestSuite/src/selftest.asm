; Checks the reporting path: every byte value, an ADD result with its flags, and the word order.

INCLUDE "hardware.inc"
INCLUDE "report.inc"

SECTION "Main", ROM0
Main::
    ld c, 0
.pattern:
    ld a, c
    call Emit
    inc c
    jr nz, .pattern
    ld a, $12
    add $34
    call Emit
    push af
    pop hl
    ld a, l
    call Emit
    ld hl, $BEEF
    call EmitWord
    ret
