target triple = "x86_64-pc-windows-msvc"
module asm ".text"
module asm ".globl __chkstk"
module asm "__chkstk:"
module asm "  pushq %rcx"
module asm "  pushq %rax"
module asm "  cmpq $0x1000, %rax"
module asm "  leaq 24(%rsp), %rcx"
module asm "  jb 2f"
module asm "1:"
module asm "  subq $0x1000, %rcx"
module asm "  orq $0, (%rcx)"
module asm "  subq $0x1000, %rax"
module asm "  cmpq $0x1000, %rax"
module asm "  ja 1b"
module asm "2:"
module asm "  subq %rax, %rcx"
module asm "  orq $0, (%rcx)"
module asm "  popq %rax"
module asm "  popq %rcx"
module asm "  retq"
declare void @ExitProcess(i32)
declare void @llvm.memset.p0.i64(ptr, i8, i64, i1)
@_fltused = global i32 0
define ptr @memset(ptr %d, i32 %c, i64 %n) #0 {
entry:
  %b = trunc i32 %c to i8
  br label %loop
loop:
  %i = phi i64 [ 0, %entry ], [ %i1, %body ]
  %done = icmp eq i64 %i, %n
  br i1 %done, label %exit, label %body
body:
  %p = getelementptr i8, ptr %d, i64 %i
  store volatile i8 %b, ptr %p
  %i1 = add i64 %i, 1
  br label %loop
exit:
  ret ptr %d
}
define i32 @big() {
  %buf = alloca [200000 x i8], align 16
  call void @llvm.memset.p0.i64(ptr %buf, i8 7, i64 200000, i1 false)
  %last = getelementptr [200000 x i8], ptr %buf, i64 0, i64 199999
  %v = load volatile i8, ptr %last
  %first = load volatile i8, ptr %buf
  %a = zext i8 %v to i32
  %b = zext i8 %first to i32
  %s = add i32 %a, %b
  %f = sitofp i32 %s to double
  %g = fmul double %f, 1.5
  %r = fptosi double %g to i32
  ret i32 %r
}
define void @_start() {
  %r = call i32 @big()
  call void @ExitProcess(i32 %r)
  unreachable
}
attributes #0 = { "no-builtins" }
