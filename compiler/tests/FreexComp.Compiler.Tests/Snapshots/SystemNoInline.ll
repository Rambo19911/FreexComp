; ModuleID = 'System'
source_filename = "System.pas"
target triple = "x86_64-pc-windows-msvc"

module asm "\09.text"
module asm "\09.globl\09__chkstk"
module asm "\09.p2align\094"
module asm "__chkstk:"
module asm "\09pushq\09%rcx"
module asm "\09pushq\09%rax"
module asm "\09cmpq\09$4096, %rax"
module asm "\09leaq\0924(%rsp), %rcx"
module asm "\09jb\091f"
module asm "2:"
module asm "\09subq\09$4096, %rcx"
module asm "\09testq\09%rcx, (%rcx)"
module asm "\09subq\09$4096, %rax"
module asm "\09cmpq\09$4096, %rax"
module asm "\09ja\092b"
module asm "1:"
module asm "\09subq\09%rax, %rcx"
module asm "\09testq\09%rcx, (%rcx)"
module asm "\09popq\09%rax"
module asm "\09popq\09%rcx"
module asm "\09retq"

@"System.Input" = global [624 x i8] zeroinitializer, align 8
@"System.Output" = global [624 x i8] zeroinitializer, align 8

define void @"System._Helper"(i32 %Code) #4 {
entry:
  %Code.addr = alloca i32, align 4
  store i32 %Code, ptr %Code.addr, align 4
  br label %exit
exit:
  ret void
}

define void @"System._Raise"(ptr %Obj, ptr %Addr) #4 {
entry:
  %Obj.addr = alloca ptr, align 8
  %Addr.addr = alloca ptr, align 8
  store ptr %Obj, ptr %Obj.addr, align 8
  store ptr %Addr, ptr %Addr.addr, align 8
  call void @"System._Helper"(i32 1)
  br label %exit
exit:
  ret void
}

define void @"System.Plain"() #0 {
entry:
  call void @"System._Helper"(i32 2)
  br label %exit
exit:
  ret void
}

define void @"System.$init"() #0 {
entry:
  br label %exit
exit:
  ret void
}

define void @"System.$final"() #0 {
entry:
  br label %exit
exit:
  ret void
}

attributes #0 = { uwtable "frame-pointer"="all" }
attributes #4 = { noinline uwtable "frame-pointer"="all" }
