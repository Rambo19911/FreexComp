; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 69, i16 65, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 69, i16 66, i16 0] }, align 16
@"M.EA.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 8, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), ptr null, ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.EB.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr @"M.EA.$vmt", i64 8, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.1, i32 0, i32 5), ptr null, ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8

declare ptr @"System._BeginCatch"()
declare void @"System._EndCatch"()
declare i8 @"System._IsClass"(ptr, ptr)
declare void @"System._Raise"(ptr, ptr) #1
declare void @"System._ReRaise"() #1
declare i32 @"System._SehFilter"(ptr, ptr)
declare i32 @__C_specific_handler(...)

define ptr @"M.EA.Create"(ptr %Self, i8 %Alloc) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %Alloc.addr = alloca i8, align 1
  store ptr %Self, ptr %Self.addr, align 8
  store i8 %Alloc, ptr %Alloc.addr, align 1
  br label %body.body.1
body.body.1:
  br label %body.end.1
body.end.1:
  br label %exit
exit:
  %t1 = load ptr, ptr %Self.addr, align 8
  ret ptr %t1
}

define void @"M.Fail"(i32 %N) #0 {
entry:
  %N.addr = alloca i32, align 4
  store i32 %N, ptr %N.addr, align 4
  %t1 = load i32, ptr %N.addr, align 4
  %t2 = icmp sgt i32 %t1, 0
  br i1 %t2, label %if.then.1, label %if.end.1
if.then.1:
  %t3 = call ptr @"M.EA.Create"(ptr @"M.EA.$vmt", i8 1)
  call void @"System._Raise"(ptr %t3, ptr null) #1
  unreachable
if.end.1:
  %t4 = load i32, ptr %N.addr, align 4
  %t5 = icmp slt i32 %t4, 0
  br i1 %t5, label %if.then.2, label %if.end.2
if.then.2:
  %t6 = call ptr @"M.EA.Create"(ptr @"M.EB.$vmt", i8 1)
  call void @"System._Raise"(ptr %t6, ptr null) #1
  unreachable
if.end.2:
  br label %exit
exit:
  ret void
}

define i32 @"M.Run"(i32 %N) #0 personality ptr @__C_specific_handler {
entry:
  %N.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  %X.addr = alloca ptr, align 8
  %$tmp1.addr = alloca ptr, align 8
  %E.addr = alloca ptr, align 8
  %$tmp2.addr = alloca ptr, align 8
  store i32 %N, ptr %N.addr, align 4
  store i32 0, ptr %Result.addr, align 4
  %t1 = load i32, ptr %N.addr, align 4
  invoke void @"M.Fail"(i32 %t1)
          to label %cont.1 unwind label %ex.dispatch.2
cont.1:
  br label %try.end.2
ex.dispatch.2:
  %cs.2 = catchswitch within none [label %ex.pad.2] unwind label %ex.dispatch.1
ex.pad.2:
  %cp.2 = catchpad within %cs.2 [ptr @"System._SehFilter"]
  catchret from %cp.2 to label %ex.body.2
ex.body.2:
  %t2 = call ptr @"System._BeginCatch"()
  store ptr %t2, ptr %$tmp1.addr, align 8
  %t3 = load ptr, ptr %$tmp1.addr, align 8
  %t4 = call i8 @"System._IsClass"(ptr %t3, ptr @"M.EB.$vmt")
  %t5 = icmp ne i8 %t4, 0
  br i1 %t5, label %if.then.3, label %if.else.3
if.then.3:
  %t6 = load ptr, ptr %$tmp1.addr, align 8
  store ptr %t6, ptr %E.addr, align 8
  %t7 = load ptr, ptr %E.addr, align 8
  store ptr %t7, ptr %X.addr, align 8
  br label %if.end.3
if.else.3:
  %t8 = load ptr, ptr %$tmp1.addr, align 8
  %t9 = call i8 @"System._IsClass"(ptr %t8, ptr @"M.EA.$vmt")
  %t10 = icmp ne i8 %t9, 0
  br i1 %t10, label %if.then.4, label %if.else.4
if.then.4:
  invoke void @"System._ReRaise"() #1
          to label %cont.2 unwind label %ex.cleanup.2
cont.2:
  unreachable
if.else.4:
  invoke void @"System._ReRaise"() #1
          to label %cont.3 unwind label %ex.cleanup.2
cont.3:
  unreachable
if.end.4:
  br label %if.end.3
if.end.3:
  invoke void @"System._EndCatch"()
          to label %cont.4 unwind label %ex.dispatch.1
cont.4:
  br label %try.end.2
ex.cleanup.2:
  %cl.2 = cleanuppad within none []
  invoke void @"System._EndCatch"() [ "funclet"(token %cl.2) ]
          to label %cont.5 unwind label %ex.dispatch.1
cont.5:
  cleanupret from %cl.2 unwind label %ex.dispatch.1
try.end.2:
  br label %try.end.1
ex.dispatch.1:
  %cs.1 = catchswitch within none [label %ex.pad.1] unwind to caller
ex.pad.1:
  %cp.1 = catchpad within %cs.1 [ptr @"System._SehFilter"]
  catchret from %cp.1 to label %ex.body.1
ex.body.1:
  %t11 = call ptr @"System._BeginCatch"()
  store ptr %t11, ptr %$tmp2.addr, align 8
  store i32 1, ptr %Result.addr, align 4
  call void @"System._EndCatch"()
  br label %try.end.1
ex.cleanup.1:
  %cl.1 = cleanuppad within none []
  call void @"System._EndCatch"() [ "funclet"(token %cl.1) ]
  cleanupret from %cl.1 unwind to caller
try.end.1:
  br label %exit
exit:
  %t12 = load i32, ptr %Result.addr, align 4
  ret i32 %t12
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
