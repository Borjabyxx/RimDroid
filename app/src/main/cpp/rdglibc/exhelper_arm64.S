// librdexhelper.so, the part that needs exact register use (see exhelper.c). MonoMod's entry stubs
// pass the real target in x9 and the target's own arguments in x0-x8 untouched.

    .text

// void** eh_get_exception_ptr(void): the calling thread's slot. Keeps x1-x8: the landing pad below
// holds the exception in x1 across the call, and eh_native_to_managed passes x1-x8 on to its target.
    .globl  eh_get_exception_ptr
    .type   eh_get_exception_ptr, %function
    .p2align 2
eh_get_exception_ptr:
.Lget_slot:
    .cfi_startproc
    stp     x29, x30, [sp, #-80]!
    .cfi_def_cfa_offset 80
    .cfi_offset x29, -80
    .cfi_offset x30, -72
    mov     x29, sp
    stp     x1, x2, [sp, #16]
    stp     x3, x4, [sp, #32]
    stp     x5, x6, [sp, #48]
    stp     x7, x8, [sp, #64]
    bl      rd_eh_slot
    ldp     x1, x2, [sp, #16]
    ldp     x3, x4, [sp, #32]
    ldp     x5, x6, [sp, #48]
    ldp     x7, x8, [sp, #64]
    ldp     x29, x30, [sp], #80
    ret
    .cfi_endproc
    .size   eh_get_exception_ptr, . - eh_get_exception_ptr

// Managed code calling native code (x9). A native exception unwinding out of the call lands in
// .Lm2n_catch (rd_eh_personality + the LSDA below): it is kept in the slot and 0 is returned.
    .globl  eh_managed_to_native
    .type   eh_managed_to_native, %function
    .p2align 2
eh_managed_to_native:
    .cfi_startproc
    .cfi_personality 0x1b, rd_eh_personality
    .cfi_lsda 0x1b, .Lm2n_lsda
    stp     x29, x30, [sp, #-16]!
    .cfi_def_cfa_offset 16
    .cfi_offset x29, -16
    .cfi_offset x30, -8
    mov     x29, sp
    blr     x9
    ldp     x29, x30, [sp], #16
    ret
.Lm2n_catch:                            // the frame is still set up here (the unwinder restored it)
    mov     x1, x0
    bl      .Lget_slot
    str     x1, [x0]
    mov     x0, xzr
    ldp     x29, x30, [sp], #16
    ret
    .cfi_endproc
    .size   eh_managed_to_native, . - eh_managed_to_native

// Native code calling managed code (x9). The slot is cleared first; a native exception managed code
// brought back (caught in eh_managed_to_native further down) is raised again from here.
    .globl  eh_native_to_managed
    .type   eh_native_to_managed, %function
    .p2align 2
eh_native_to_managed:
    .cfi_startproc
    stp     x20, x19, [sp, #-32]!
    .cfi_def_cfa_offset 32
    stp     x29, x30, [sp, #16]
    .cfi_offset x19, -24
    .cfi_offset x20, -32
    .cfi_offset x29, -16
    .cfi_offset x30, -8
    add     x29, sp, #16
    mov     x19, x9
    mov     x20, x0
    bl      .Lget_slot
    str     xzr, [x0]
    mov     x0, x20
    blr     x19
    mov     x20, x0
    bl      .Lget_slot
    ldr     x0, [x0]
    cbnz    x0, 1f
    mov     x0, x20
    ldp     x29, x30, [sp, #16]
    ldp     x20, x19, [sp], #32
    ret
1:  bl      _Unwind_RaiseException      // returns only if nothing catches it
    brk     #1
    .cfi_endproc
    .size   eh_native_to_managed, . - eh_native_to_managed

    .section .rodata, "a", %progbits
    .p2align 2
.Lm2n_lsda:
    .word   .Lm2n_catch - .

    .section .note.GNU-stack, "", %progbits
