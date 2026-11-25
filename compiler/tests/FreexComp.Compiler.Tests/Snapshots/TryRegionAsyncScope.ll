; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 120, i16 0] }, align 16

declare ptr @"System._BeginCatch"()
declare void @"System._EndCatch"()
declare i32 @"System._SehFilter"(ptr, ptr)
declare void @"System._UStrAsg"(ptr, ptr)
declare void @"System._UStrClr"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.Step"() #0 {
entry:
  br label %exit
exit:
  ret void
}

define i32 @"M.Plain"(ptr %P) #0 {
entry:
  %P.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  store ptr %P, ptr %P.addr, align 8
  %t1 = load ptr, ptr %P.addr, align 8
  %t2 = load i32, ptr %t1, align 4
  store i32 %t2, ptr %Result.addr, align 4
  br label %exit
exit:
  %t3 = load i32, ptr %Result.addr, align 4
  ret i32 %t3
}

define i32 @"M.Managed"(ptr %P) #0 personality ptr @__C_specific_handler {
entry:
  %P.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  %S.addr = alloca ptr, align 8
  store ptr %P, ptr %P.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %S.addr, align 8
  invoke void @"System._UStrAsg"(ptr %S.addr, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.0, i32 0, i32 5))
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t1 = load ptr, ptr %P.addr, align 8
  %t2 = load i32, ptr %t1, align 4
  %t3 = load ptr, ptr %S.addr, align 8
  %t4 = icmp eq ptr %t3, null
  br i1 %t4, label %len.end.1, label %len.load.1
len.load.1:
  %t5 = getelementptr inbounds i8, ptr %t3, i64 -12
  %t6 = load i32, ptr %t5, align 4
  br label %len.end.1
len.end.1:
  %t7 = phi i32 [ 0, %cont.2 ], [ %t6, %len.load.1 ]
  %t8 = add i32 %t2, %t7
  store i32 %t8, ptr %Result.addr, align 4
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  call void @"System._UStrClr"(ptr %S.addr)
  br label %exit
exit:
  %t9 = load i32, ptr %Result.addr, align 4
  ret i32 %t9
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %S.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define i32 @"M.Caught"(ptr %P) #0 personality ptr @__C_specific_handler {
entry:
  %P.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  %$tmp1.addr = alloca ptr, align 8
  store volatile ptr %P, ptr %P.addr, align 8
  store volatile i32 1, ptr %Result.addr, align 4
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %ex.dispatch.1
cont.1:
  invoke void @llvm.seh.try.begin()
          to label %cont.2 unwind label %fin.pad.2
cont.2:
  store volatile i32 2, ptr %Result.addr, align 4
  %t1 = load volatile ptr, ptr %P.addr, align 8
  store volatile i32 7, ptr %t1, align 4
  invoke void @llvm.seh.try.end()
          to label %cont.3 unwind label %fin.pad.2
cont.3:
  invoke void @"M.Caught.$fin1"(ptr null)
          to label %cont.4 unwind label %ex.dispatch.1
cont.4:
  br label %try.end.2
fin.pad.2:
  %cl.2 = cleanuppad within none []
  invoke void @"M.Caught.$fin1"(ptr null) [ "funclet"(token %cl.2) ]
          to label %cont.5 unwind label %ex.dispatch.1
cont.5:
  cleanupret from %cl.2 unwind label %ex.dispatch.1
try.end.2:
  invoke void @llvm.seh.try.end()
          to label %cont.6 unwind label %ex.dispatch.1
cont.6:
  br label %try.end.1
ex.dispatch.1:
  %cs.1 = catchswitch within none [label %ex.pad.1] unwind to caller
ex.pad.1:
  %cp.1 = catchpad within %cs.1 [ptr @"System._SehFilter"]
  catchret from %cp.1 to label %ex.body.1
ex.body.1:
  %t2 = call ptr @"System._BeginCatch"()
  store volatile ptr %t2, ptr %$tmp1.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.7 unwind label %ex.cleanup.1
cont.7:
  %t3 = load volatile i32, ptr %Result.addr, align 4
  %t4 = load volatile ptr, ptr %P.addr, align 8
  %t5 = load volatile i32, ptr %t4, align 4
  %t6 = add i32 %t3, %t5
  store volatile i32 %t6, ptr %Result.addr, align 4
  invoke void @llvm.seh.try.end()
          to label %cont.8 unwind label %ex.cleanup.1
cont.8:
  call void @"System._EndCatch"()
  br label %try.end.1
ex.cleanup.1:
  %cl.1 = cleanuppad within none []
  call void @"System._EndCatch"() [ "funclet"(token %cl.1) ]
  cleanupret from %cl.1 unwind to caller
try.end.1:
  br label %exit
exit:
  %t7 = load volatile i32, ptr %Result.addr, align 4
  ret i32 %t7
}

define internal void @"M.Caught.$fin1"(ptr %$link) #3 {
entry:
  call void @"M.Step"()
  br label %exit
exit:
  ret void
}

define i32 @"M.Leaves"(ptr %P, i32 %N) #0 personality ptr @__C_specific_handler {
entry:
  %P.addr = alloca ptr, align 8
  %N.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  %I.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  %$tmp3.addr = alloca ptr, align 8
  store volatile ptr %P, ptr %P.addr, align 8
  store volatile i32 %N, ptr %N.addr, align 4
  store volatile i32 0, ptr %Result.addr, align 4
  store volatile i32 1, ptr %$tmp1.addr, align 4
  %t1 = load volatile i32, ptr %N.addr, align 4
  store volatile i32 %t1, ptr %$tmp2.addr, align 4
  %t2 = load volatile i32, ptr %$tmp1.addr, align 4
  %t3 = load volatile i32, ptr %$tmp2.addr, align 4
  %t4 = icmp sle i32 %t2, %t3
  br i1 %t4, label %if.then.1, label %if.end.1
if.then.1:
  %t5 = load volatile i32, ptr %$tmp1.addr, align 4
  store volatile i32 %t5, ptr %I.addr, align 4
  br label %for.body.2
for.body.2:
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %ex.dispatch.3
cont.1:
  %t6 = load volatile i32, ptr %I.addr, align 4
  %t7 = icmp eq i32 %t6, 2
  br i1 %t7, label %if.then.4, label %if.end.4
if.then.4:
  invoke void @llvm.seh.try.end()
          to label %cont.2 unwind label %ex.dispatch.3
cont.2:
  br label %for.inc.2
if.end.4:
  %t8 = load volatile i32, ptr %I.addr, align 4
  %t9 = icmp eq i32 %t8, 3
  br i1 %t9, label %if.then.5, label %if.end.5
if.then.5:
  invoke void @llvm.seh.try.end()
          to label %cont.3 unwind label %ex.dispatch.3
cont.3:
  br label %for.end.2
if.end.5:
  %t10 = load volatile i32, ptr %I.addr, align 4
  %t11 = icmp eq i32 %t10, 4
  br i1 %t11, label %if.then.6, label %if.end.6
if.then.6:
  invoke void @llvm.seh.try.end()
          to label %cont.4 unwind label %ex.dispatch.3
cont.4:
  br label %exit
if.end.6:
  %t12 = load volatile ptr, ptr %P.addr, align 8
  %t13 = load volatile i32, ptr %t12, align 4
  store volatile i32 %t13, ptr %Result.addr, align 4
  invoke void @llvm.seh.try.end()
          to label %cont.5 unwind label %ex.dispatch.3
cont.5:
  br label %try.end.3
ex.dispatch.3:
  %cs.3 = catchswitch within none [label %ex.pad.3] unwind to caller
ex.pad.3:
  %cp.3 = catchpad within %cs.3 [ptr @"System._SehFilter"]
  catchret from %cp.3 to label %ex.body.3
ex.body.3:
  %t14 = call ptr @"System._BeginCatch"()
  store volatile ptr %t14, ptr %$tmp3.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.6 unwind label %ex.cleanup.3
cont.6:
  %t15 = load volatile i32, ptr %I.addr, align 4
  %t16 = icmp eq i32 %t15, 5
  br i1 %t16, label %if.then.7, label %if.end.7
if.then.7:
  invoke void @llvm.seh.try.end()
          to label %cont.7 unwind label %ex.cleanup.3
cont.7:
  call void @"System._EndCatch"()
  br label %for.end.2
if.end.7:
  invoke void @llvm.seh.try.end()
          to label %cont.8 unwind label %ex.cleanup.3
cont.8:
  call void @"System._EndCatch"()
  br label %try.end.3
ex.cleanup.3:
  %cl.3 = cleanuppad within none []
  call void @"System._EndCatch"() [ "funclet"(token %cl.3) ]
  cleanupret from %cl.3 unwind to caller
try.end.3:
  br label %for.inc.2
for.inc.2:
  %t17 = load volatile i32, ptr %I.addr, align 4
  %t18 = load volatile i32, ptr %$tmp2.addr, align 4
  %t19 = icmp eq i32 %t17, %t18
  br i1 %t19, label %for.end.2, label %if.end.8
if.end.8:
  %t20 = load volatile i32, ptr %I.addr, align 4
  %t21 = add i32 %t20, 1
  store volatile i32 %t21, ptr %I.addr, align 4
  br label %for.body.2
for.end.2:
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  %t22 = load volatile i32, ptr %Result.addr, align 4
  ret i32 %t22
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
attributes #3 = { noinline optnone uwtable "frame-pointer"="all" }

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
