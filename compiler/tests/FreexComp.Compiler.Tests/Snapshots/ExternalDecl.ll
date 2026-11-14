; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@_counter = global i32 7, align 4
@_fltused = global i32 39029, align 4
@"M.Plain" = internal global i32 zeroinitializer, align 4

declare dllimport void @ExitProcess(i32)
declare dllimport i32 @GetCurrentProcessId()
declare i32 @Helper(i32)
declare dllimport void @Sleep(i32)
declare i32 @other_helper(i32)

define i32 @impl_sym(i32 %x) #0 {
entry:
  %x.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %x, ptr %x.addr, align 4
  %t1 = load i32, ptr %x.addr, align 4
  %t2 = load i32, ptr @_fltused, align 4
  %t3 = add i32 %t1, %t2
  %t4 = load i32, ptr @"M.Plain", align 4
  %t5 = add i32 %t3, %t4
  store i32 %t5, ptr %Result.addr, align 4
  br label %exit
exit:
  %t6 = load i32, ptr %Result.addr, align 4
  ret i32 %t6
}

define i32 @"M.Exported"(i32 %x) #0 {
entry:
  %x.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %x, ptr %x.addr, align 4
  %t1 = load i32, ptr %x.addr, align 4
  %t2 = call i32 @Helper(i32 %t1)
  %t3 = load i32, ptr %x.addr, align 4
  %t4 = call i32 @other_helper(i32 %t3)
  %t5 = add i32 %t2, %t4
  %t6 = load i32, ptr %x.addr, align 4
  %t7 = call i32 @impl_sym(i32 %t6)
  %t8 = add i32 %t5, %t7
  store i32 %t8, ptr %Result.addr, align 4
  br label %exit
exit:
  %t9 = load i32, ptr %Result.addr, align 4
  ret i32 %t9
}

define i32 @"M.Run"() #0 {
entry:
  %Result.addr = alloca i32, align 4
  call void @Sleep(i32 10)
  %t1 = load i32, ptr @_counter, align 4
  %t2 = call i32 @"M.Exported"(i32 %t1)
  store i32 %t2, ptr %Result.addr, align 4
  %t3 = call i32 @GetCurrentProcessId()
  %t4 = icmp ugt i32 %t3, 0
  br i1 %t4, label %if.then.1, label %if.end.1
if.then.1:
  %t5 = load i32, ptr %Result.addr, align 4
  %t6 = add i32 %t5, 1
  store i32 %t6, ptr %Result.addr, align 4
  br label %if.end.1
if.end.1:
  call void @ExitProcess(i32 0)
  br label %exit
exit:
  %t7 = load i32, ptr %Result.addr, align 4
  ret i32 %t7
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
