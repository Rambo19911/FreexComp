; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.Run.$frame" = type { ptr, i32, i32 }

declare ptr @"System._BeginCatch"()
declare void @"System._EndCatch"()
declare i32 @"System._SehFilter"(ptr, ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.Step"(i32 %N) #0 {
entry:
  %N.addr = alloca i32, align 4
  store i32 %N, ptr %N.addr, align 4
  br label %exit
exit:
  ret void
}

define i32 @"M.Run"(i32 %N) #0 personality ptr @__C_specific_handler {
entry:
  %$frame = alloca %"M.Run.$frame", align 8
  %N.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  store volatile ptr null, ptr %$frame, align 8
  %Result.addr = getelementptr inbounds %"M.Run.$frame", ptr %$frame, i32 0, i32 1
  %I.addr = getelementptr inbounds %"M.Run.$frame", ptr %$frame, i32 0, i32 2
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
          to label %cont.1 unwind label %fin.pad.3
cont.1:
  %t6 = load volatile i32, ptr %I.addr, align 4
  invoke void @"M.Step"(i32 %t6)
          to label %cont.2 unwind label %fin.pad.3
cont.2:
  %t7 = load volatile i32, ptr %I.addr, align 4
  %t8 = icmp eq i32 %t7, 2
  br i1 %t8, label %if.then.4, label %if.end.4
if.then.4:
  invoke void @llvm.seh.try.end()
          to label %cont.3 unwind label %fin.pad.3
cont.3:
  call void @"M.Run.$fin1"(ptr %$frame)
  br label %for.inc.2
if.end.4:
  %t9 = load volatile i32, ptr %I.addr, align 4
  %t10 = icmp eq i32 %t9, 3
  br i1 %t10, label %if.then.5, label %if.end.5
if.then.5:
  invoke void @llvm.seh.try.end()
          to label %cont.4 unwind label %fin.pad.3
cont.4:
  call void @"M.Run.$fin1"(ptr %$frame)
  br label %for.end.2
if.end.5:
  %t11 = load volatile i32, ptr %I.addr, align 4
  %t12 = icmp eq i32 %t11, 4
  br i1 %t12, label %if.then.6, label %if.end.6
if.then.6:
  invoke void @llvm.seh.try.end()
          to label %cont.5 unwind label %fin.pad.3
cont.5:
  call void @"M.Run.$fin1"(ptr %$frame)
  br label %exit
if.end.6:
  invoke void @llvm.seh.try.end()
          to label %cont.6 unwind label %fin.pad.3
cont.6:
  call void @"M.Run.$fin1"(ptr %$frame)
  br label %try.end.3
fin.pad.3:
  %cl.3 = cleanuppad within none []
  call void @"M.Run.$fin1"(ptr %$frame) [ "funclet"(token %cl.3) ]
  cleanupret from %cl.3 unwind to caller
try.end.3:
  br label %for.inc.2
for.inc.2:
  %t13 = load volatile i32, ptr %I.addr, align 4
  %t14 = load volatile i32, ptr %$tmp2.addr, align 4
  %t15 = icmp eq i32 %t13, %t14
  br i1 %t15, label %for.end.2, label %if.end.7
if.end.7:
  %t16 = load volatile i32, ptr %I.addr, align 4
  %t17 = add i32 %t16, 1
  store volatile i32 %t17, ptr %I.addr, align 4
  br label %for.body.2
for.end.2:
  br label %if.end.1
if.end.1:
  invoke void @llvm.seh.try.begin()
          to label %cont.7 unwind label %fin.pad.8
cont.7:
  invoke void @"M.Step"(i32 0)
          to label %cont.8 unwind label %fin.pad.8
cont.8:
  invoke void @llvm.seh.try.end()
          to label %cont.9 unwind label %fin.pad.8
cont.9:
  call void @"M.Run.$fin2"(ptr %$frame)
  br label %try.end.8
fin.pad.8:
  %cl.8 = cleanuppad within none []
  call void @"M.Run.$fin2"(ptr %$frame) [ "funclet"(token %cl.8) ]
  cleanupret from %cl.8 unwind to caller
try.end.8:
  br label %exit
exit:
  %t18 = load volatile i32, ptr %Result.addr, align 4
  ret i32 %t18
}

define internal void @"M.Run.$fin1"(ptr %$link) #3 {
entry:
  %t1 = getelementptr inbounds %"M.Run.$frame", ptr %$link, i32 0, i32 1
  %t2 = load i32, ptr %t1, align 4
  %t3 = getelementptr inbounds %"M.Run.$frame", ptr %$link, i32 0, i32 2
  %t4 = load i32, ptr %t3, align 4
  %t5 = add i32 %t2, %t4
  %t6 = getelementptr inbounds %"M.Run.$frame", ptr %$link, i32 0, i32 1
  store i32 %t5, ptr %t6, align 4
  br label %exit
exit:
  ret void
}

define internal void @"M.Run.$fin2"(ptr %$link) #3 personality ptr @__C_specific_handler {
entry:
  %$tmp1.addr = alloca ptr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %ex.dispatch.1
cont.1:
  %t1 = getelementptr inbounds %"M.Run.$frame", ptr %$link, i32 0, i32 1
  %t2 = load volatile i32, ptr %t1, align 4
  invoke void @"M.Step"(i32 %t2)
          to label %cont.2 unwind label %ex.dispatch.1
cont.2:
  invoke void @llvm.seh.try.end()
          to label %cont.3 unwind label %ex.dispatch.1
cont.3:
  br label %try.end.1
ex.dispatch.1:
  %cs.1 = catchswitch within none [label %ex.pad.1] unwind to caller
ex.pad.1:
  %cp.1 = catchpad within %cs.1 [ptr @"System._SehFilter"]
  catchret from %cp.1 to label %ex.body.1
ex.body.1:
  %t3 = call ptr @"System._BeginCatch"()
  store volatile ptr %t3, ptr %$tmp1.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.4 unwind label %ex.cleanup.1
cont.4:
  invoke void @llvm.seh.try.end()
          to label %cont.5 unwind label %ex.cleanup.1
cont.5:
  call void @"System._EndCatch"()
  br label %try.end.1
ex.cleanup.1:
  %cl.1 = cleanuppad within none []
  call void @"System._EndCatch"() [ "funclet"(token %cl.1) ]
  cleanupret from %cl.1 unwind to caller
try.end.1:
  br label %exit
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
attributes #3 = { noinline optnone uwtable "frame-pointer"="all" }

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
