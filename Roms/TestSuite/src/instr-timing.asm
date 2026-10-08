; Measures the M-cycles of each documented opcode, using TIMA stepping every 4 M-cycles as the clock.

INCLUDE "hardware.inc"
INCLUDE "report.inc"

DEF SCRATCH EQU $DEA0 ; Byte that (HL) instructions read and write.
DEF DATA_WORD EQU $DEB0 ; 16-bit operand of the loads and stores that take an address.
DEF TEST_STACK EQU $DD00 ; SP during a run; the word on top is the address after the instruction.
DEF FLAGS_IMAGE EQU $DD80 ; F then A, loaded by POP AF at the start of a run.
DEF HRAM_BYTE EQU $80 ; Operand of LDH and the value of C, so that these access $FF80.
DEF SLED EQU 3 ; NOPs in front of the instruction; a run enters after 0 to 3 of them.

DEF K_LENGTH EQU %00000011 ; Table entry: instruction length, 0 for an opcode left out.
DEF K_TARGET EQU %00000100 ; Table entry: the 16-bit operand is the address after the instruction.
DEF K_HIGH EQU %00001000 ; Table entry: the 8-bit operand is HRAM_BYTE instead of 0.
DEF K_COND EQU %00010000 ; Table entry: conditional, measured not taken and then taken.
DEF K_HL EQU %00100000 ; Table entry: HL holds the address after the instruction.

DEF KN EQU 0 ; Left out: STOP, HALT, the CB prefix and the undefined opcodes.
DEF K1 EQU 1 ; One byte.
DEF K2 EQU 2 ; Two bytes with an operand of 0.
DEF K3 EQU 3 ; Three bytes with DATA_WORD as the operand.
DEF KH EQU 2 | K_HIGH ; LDH with HRAM_BYTE.
DEF KT EQU 3 | K_TARGET ; JP and CALL to the next instruction.
DEF KJ EQU 3 | K_TARGET | K_COND ; JP cc and CALL cc to the next instruction.
DEF KC EQU 2 | K_COND ; JR cc with a displacement of 0.
DEF KR EQU 1 | K_COND ; RET cc returning to the next instruction.
DEF KL EQU 1 | K_HL ; JP HL to the next instruction.

SECTION "Variables", WRAM0[$D010]
wSavedSp: ds 2 ; SP of the caller of RunOnce.
wTimaRead: ds 1 ; TIMA read right after the instruction.
wSum: ds 1 ; TIMA reads added over the four sled entries.
wReference: ds 1 ; The sum with no instruction in the run.
wOpcode: ds 1 ; Opcode being measured.
wKind: ds 1 ; Its table entry.
wFlags: ds 1 ; F during the run.
wLength: ds 1 ; Instruction length, 0 for the reference.
wBytes: ds 3 ; Instruction bytes.

; Each RST vector reads TIMA like the code after the instruction and ends the run.
SECTION "Rst 00", ROM0[$00]
    ldh a, [rTIMA]
    jp Post
SECTION "Rst 08", ROM0[$08]
    ldh a, [rTIMA]
    jp Post
SECTION "Rst 10", ROM0[$10]
    ldh a, [rTIMA]
    jp Post
SECTION "Rst 18", ROM0[$18]
    ldh a, [rTIMA]
    jp Post
SECTION "Rst 20", ROM0[$20]
    ldh a, [rTIMA]
    jp Post
SECTION "Rst 28", ROM0[$28]
    ldh a, [rTIMA]
    jp Post
SECTION "Rst 30", ROM0[$30]
    ldh a, [rTIMA]
    jp Post
SECTION "Rst 38", ROM0[$38]
    ldh a, [rTIMA]
    jp Post

SECTION "Run template", ROM0
; Run code: loads registers, restarts DIV and TIMA, runs 0-3 NOPs and the instruction, reads TIMA.
RunTemplate:
LOAD "Run code", WRAM0[$D200]
RunCode:
    ld sp, FLAGS_IMAGE
    pop af
    ld sp, TEST_STACK
    ld bc, $DE00 | HRAM_BYTE
    ld de, $DE90
RunHl:
    ld hl, SCRATCH
    ldh [rDIV], a
    ldh [rTIMA], a
RunSledJump:
    jp RunSled
RunSled:
    ds SLED, 0
RunInstruction:
    ds 3 + 5, 0
ENDL
RunTemplateEnd:

SECTION "Main", ROM0
; Measures an empty run, then emits each base opcode (conditional: not taken, taken) and CB opcode.
Main::
    ld a, $05
    ldh [rTAC], a
    ld hl, RunTemplate
    ld de, RunCode
    ld bc, RunTemplateEnd - RunTemplate
    call Copy
    xor a
    ld [wLength], a
    ld [wFlags], a
    ld hl, SCRATCH
    call SetHl
    call Build
    call Measure
    ld [wReference], a
    xor a
    ld [wOpcode], a
.base:
    call MeasureBase
    ld hl, wOpcode
    inc [hl]
    jr nz, .base
.cb:
    call MeasureCb
    ld hl, wOpcode
    inc [hl]
    jr nz, .cb
    ret

; Measures base opcode wOpcode as its table entry describes, both ways when it is conditional.
MeasureBase:
    ld a, [wOpcode]
    ld l, a
    ld h, 0
    ld de, OpcodeKinds
    add hl, de
    ld a, [hl]
    ld [wKind], a
    and K_LENGTH
    ret z
    ld [wLength], a
    ld a, [wOpcode]
    ld [wBytes], a
    xor a
    ld [wBytes + 1], a
    ld [wBytes + 2], a
    ld a, [wKind]
    and K_HIGH
    jr z, .notHigh
    ld a, HRAM_BYTE
    ld [wBytes + 1], a
.notHigh:
    ld a, [wLength]
    cp 3
    jr nz, .operandDone
    ld a, [wKind]
    and K_TARGET
    ld hl, DATA_WORD
    jr z, .word
    ld hl, RunInstruction + 3
.word:
    ld a, l
    ld [wBytes + 1], a
    ld a, h
    ld [wBytes + 2], a
.operandDone:
    ld a, [wKind]
    and K_HL
    ld hl, SCRATCH
    jr z, .hl
    ld hl, RunInstruction + 1
.hl:
    call SetHl
    ld a, [wKind]
    and K_COND
    jr nz, .conditional
    xor a
    jr MeasureWithFlags
.conditional:
    ld de, NotTakenFlags
    call ConditionFlags
    call MeasureWithFlags
    ld de, TakenFlags
    call ConditionFlags
    ; Falls through to measure the taken case.

; Measures the prepared instruction with F = A and emits its length in M-cycles.
MeasureWithFlags:
    ld [wFlags], a
    call Build
    call Measure
    ld hl, wReference
    sub [hl]
    jp Emit

; Returns in A the F from the four-entry table at DE for the condition in bits 3-4 of wOpcode.
ConditionFlags:
    ld a, [wOpcode]
    rrca
    rrca
    rrca
    and 3
    ld l, a
    ld h, 0
    add hl, de
    ld a, [hl]
    ret

; Measures CB opcode wOpcode with HL on the scratch byte.
MeasureCb:
    ld a, 2
    ld [wLength], a
    ld a, $CB
    ld [wBytes], a
    ld a, [wOpcode]
    ld [wBytes + 1], a
    ld hl, SCRATCH
    call SetHl
    xor a
    jr MeasureWithFlags

; Sets the HL that the run code loads.
SetHl:
    ld a, l
    ld [RunHl + 1], a
    ld a, h
    ld [RunHl + 2], a
    ret

; Writes the instruction, TIMA read and jump to Post into the run code, and the stack word and F.
Build:
    ld hl, wBytes
    ld de, RunInstruction
    ld a, [wLength]
    and a
    jr z, .tail
    ld c, a
.copy:
    ld a, [hl+]
    ld [de], a
    inc de
    dec c
    jr nz, .copy
.tail:
    ld a, e
    ld [TEST_STACK], a
    ld a, d
    ld [TEST_STACK + 1], a
    ld h, d
    ld l, e
    ld a, $F0
    ld [hl+], a
    ld a, LOW(rTIMA)
    ld [hl+], a
    ld a, $C3
    ld [hl+], a
    ld a, LOW(Post)
    ld [hl+], a
    ld [hl], HIGH(Post)
    ld a, [wFlags]
    ld [FLAGS_IMAGE], a
    xor a
    ld [FLAGS_IMAGE + 1], a
    ret

; Runs the code from each sled entry and returns in A the sum of the four TIMA reads.
Measure:
    xor a
    ld [wSum], a
    ld a, LOW(RunSled)
.entry:
    ld [RunSledJump + 1], a
    push af
    call RunOnce
    ld hl, wSum
    add [hl]
    ld [hl], a
    pop af
    inc a
    cp LOW(RunSled + SLED + 1)
    jr nz, .entry
    ld a, [wSum]
    ret

; Starts one run; it ends in Post, which returns the TIMA read in A to the caller of RunOnce.
RunOnce:
    xor a
    ldh [rTIMA], a
    ld [wSavedSp], sp
    jp RunCode

; Keeps the TIMA read, restores the caller's SP and interrupt state, and returns from RunOnce.
Post:
    ld [wTimaRead], a
    ld hl, wSavedSp
    ld a, [hl+]
    ld h, [hl]
    ld l, a
    ld sp, hl
    di
    ld a, [wTimaRead]
    ret

; F that makes NZ, Z, NC and C fail, then succeed.
NotTakenFlags: db $80, $00, $10, $00
TakenFlags: db $00, $80, $00, $10

; Entry per base opcode $00-$FF, built from the K_ bits above.
OpcodeKinds:
    db K1, K3, K1, K1, K1, K1, K2, K1, K3, K1, K1, K1, K1, K1, K2, K1
    db KN, K3, K1, K1, K1, K1, K2, K1, K2, K1, K1, K1, K1, K1, K2, K1
    db KC, K3, K1, K1, K1, K1, K2, K1, KC, K1, K1, K1, K1, K1, K2, K1
    db KC, K3, K1, K1, K1, K1, K2, K1, KC, K1, K1, K1, K1, K1, K2, K1
    db K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1
    db K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1
    db K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1
    db K1, K1, K1, K1, K1, K1, KN, K1, K1, K1, K1, K1, K1, K1, K1, K1
    db K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1
    db K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1
    db K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1
    db K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1, K1
    db KR, K1, KJ, KT, KJ, K1, K2, K1, KR, K1, KJ, KN, KJ, KT, K2, K1
    db KR, K1, KJ, KN, KJ, K1, K2, K1, KR, K1, KJ, KN, KJ, KN, K2, K1
    db KH, K1, K1, KN, KN, K1, K2, K1, K2, KL, K3, KN, KN, KN, K2, K1
    db KH, K1, K1, K1, KN, K1, K2, K1, K2, K1, K3, K1, KN, KN, K2, K1
