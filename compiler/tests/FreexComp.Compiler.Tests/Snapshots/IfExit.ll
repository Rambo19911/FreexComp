; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

declare void @"System._Halt"(i32) #1
declare void @"System._RunError"(i32) #1

define i32 @"M.Sign"(i32 %x) #0 {
entry:
  %x.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %x, ptr %x.addr, align 4
  %t1 = load i32, ptr %x.addr, align 4
  %t2 = icmp slt i32 %t1, 0
  br i1 %t2, label %if.then.1, label %if.else.1
if.then.1:
  store i32 -1, ptr %Result.addr, align 4
  br label %exit
if.else.1:
  %t3 = load i32, ptr %x.addr, align 4
  %t4 = icmp eq i32 %t3, 0
  br i1 %t4, label %if.then.2, label %if.else.2
if.then.2:
  store i32 0, ptr %Result.addr, align 4
  br label %if.end.2
if.else.2:
  store i32 1, ptr %Result.addr, align 4
  br label %if.end.2
if.end.2:
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  %t5 = load i32, ptr %Result.addr, align 4
  ret i32 %t5
}

define void @"M.Check"(i32 %x) #0 {
entry:
  %x.addr = alloca i32, align 4
  store i32 %x, ptr %x.addr, align 4
  %t1 = load i32, ptr %x.addr, align 4
  %t2 = icmp eq i32 %t1, 0
  br i1 %t2, label %if.then.1, label %if.end.1
if.then.1:
  call void @"System._Halt"(i32 3) #1
  unreachable
if.end.1:
  %t3 = load i32, ptr %x.addr, align 4
  %t4 = icmp sgt i32 %t3, 0
  br i1 %t4, label %if.then.2, label %if.end.2
if.then.2:
  br label %exit
if.end.2:
  call void @"System._RunError"(i32 201) #1
  unreachable
exit:
  ret void
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
attributes #1 = { noreturn uwtable }
