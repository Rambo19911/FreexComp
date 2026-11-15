; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

declare void @llvm.x86.sse.ldmxcsr(ptr)
declare void @llvm.x86.sse.stmxcsr(ptr)

define void @"M.Setup"() #0 {
entry:
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  %$tmp3.addr = alloca i32, align 4
  store i32 8064, ptr %$tmp1.addr, align 4
  call void @llvm.x86.sse.ldmxcsr(ptr %$tmp1.addr)
  call void @llvm.x86.sse.stmxcsr(ptr %$tmp3.addr)
  %t1 = load i32, ptr %$tmp3.addr, align 4
  %t2 = or i32 %t1, 64
  store i32 %t2, ptr %$tmp2.addr, align 4
  call void @llvm.x86.sse.ldmxcsr(ptr %$tmp2.addr)
  br label %exit
exit:
  ret void
}

define i32 @"M.Current"() #0 {
entry:
  %Result.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  call void @llvm.x86.sse.stmxcsr(ptr %$tmp1.addr)
  %t1 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t1, ptr %Result.addr, align 4
  br label %exit
exit:
  %t2 = load i32, ptr %Result.addr, align 4
  ret i32 %t2
}

define void @"M.$init"() #0 {
entry:
  br label %exit
exit:
  ret void
}

define void @"M.$final"() #0 {
entry:
  br label %exit
exit:
  ret void
}

attributes #0 = { uwtable "frame-pointer"="all" }
