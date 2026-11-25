; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 111, i16 0] }, align 16

declare void @"System._IntfAsg"(ptr, ptr)
declare void @"System._IntfClr"(ptr)
declare void @"System._UStrAsg"(ptr, ptr)
declare void @"System._UStrClr"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define i32 @"M.Run"(ptr %G, i32 %N) #0 personality ptr @__C_specific_handler {
entry:
  %G.addr = alloca ptr, align 8
  %N.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  %I.addr = alloca i32, align 4
  %Outer.addr = alloca ptr, align 8
  %S.addr = alloca ptr, align 8
  %H.addr = alloca ptr, align 8
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  %T.addr = alloca ptr, align 8
  store ptr %G, ptr %G.addr, align 8
  store i32 %N, ptr %N.addr, align 4
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %Outer.addr, align 8
  store ptr null, ptr %S.addr, align 8
  store ptr null, ptr %H.addr, align 8
  store ptr null, ptr %T.addr, align 8
  invoke void @"System._UStrAsg"(ptr %Outer.addr, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.0, i32 0, i32 5))
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  store i32 0, ptr %Result.addr, align 4
  %t1 = load i32, ptr %N.addr, align 4
  %t2 = icmp sgt i32 %t1, 0
  br i1 %t2, label %if.then.1, label %if.end.1
if.then.1:
  invoke void @llvm.seh.try.begin()
          to label %cont.3 unwind label %fin.pad.2
cont.3:
  %t3 = load ptr, ptr %Outer.addr, align 8
  invoke void @"System._UStrAsg"(ptr %S.addr, ptr %t3)
          to label %cont.4 unwind label %fin.pad.2
cont.4:
  %t4 = load ptr, ptr %G.addr, align 8
  invoke void @"System._IntfAsg"(ptr %H.addr, ptr %t4)
          to label %cont.5 unwind label %fin.pad.2
cont.5:
  %t5 = load ptr, ptr %H.addr, align 8
  %t6 = load ptr, ptr %t5, align 8
  %t7 = getelementptr inbounds i8, ptr %t6, i64 0
  %t8 = load ptr, ptr %t7, align 8
  invoke void %t8(ptr %t5)
          to label %cont.6 unwind label %fin.pad.2
cont.6:
  %t9 = load i32, ptr %N.addr, align 4
  %t10 = icmp sgt i32 %t9, 5
  br i1 %t10, label %if.then.3, label %if.end.3
if.then.3:
  invoke void @llvm.seh.try.end()
          to label %cont.7 unwind label %fin.pad.2
cont.7:
  invoke void @"System._IntfClr"(ptr %H.addr)
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  invoke void @"System._UStrClr"(ptr %S.addr)
          to label %cont.9 unwind label %cleanup.pad
cont.9:
  br label %cleanup
if.end.3:
  invoke void @llvm.seh.try.end()
          to label %cont.10 unwind label %fin.pad.2
cont.10:
  invoke void @"System._IntfClr"(ptr %H.addr)
          to label %cont.11 unwind label %cleanup.pad
cont.11:
  invoke void @"System._UStrClr"(ptr %S.addr)
          to label %cont.12 unwind label %cleanup.pad
cont.12:
  br label %try.end.2
fin.pad.2:
  %cl.2 = cleanuppad within none []
  invoke void @"System._IntfClr"(ptr %H.addr) [ "funclet"(token %cl.2) ]
          to label %cont.13 unwind label %cleanup.pad
cont.13:
  invoke void @"System._UStrClr"(ptr %S.addr) [ "funclet"(token %cl.2) ]
          to label %cont.14 unwind label %cleanup.pad
cont.14:
  cleanupret from %cl.2 unwind label %cleanup.pad
try.end.2:
  br label %if.end.1
if.end.1:
  store i32 1, ptr %$tmp1.addr, align 4
  %t11 = load i32, ptr %N.addr, align 4
  store i32 %t11, ptr %$tmp2.addr, align 4
  %t12 = load i32, ptr %$tmp1.addr, align 4
  %t13 = load i32, ptr %$tmp2.addr, align 4
  %t14 = icmp sle i32 %t12, %t13
  br i1 %t14, label %if.then.4, label %if.end.4
if.then.4:
  %t15 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t15, ptr %I.addr, align 4
  br label %for.body.5
for.body.5:
  invoke void @llvm.seh.try.begin()
          to label %cont.15 unwind label %fin.pad.6
cont.15:
  %t16 = load ptr, ptr %Outer.addr, align 8
  invoke void @"System._UStrAsg"(ptr %T.addr, ptr %t16)
          to label %cont.16 unwind label %fin.pad.6
cont.16:
  %t17 = load i32, ptr %I.addr, align 4
  %t18 = icmp eq i32 %t17, 3
  br i1 %t18, label %if.then.7, label %if.end.7
if.then.7:
  invoke void @llvm.seh.try.end()
          to label %cont.17 unwind label %fin.pad.6
cont.17:
  invoke void @"System._UStrClr"(ptr %T.addr)
          to label %cont.18 unwind label %cleanup.pad
cont.18:
  br label %for.end.5
if.end.7:
  invoke void @llvm.seh.try.end()
          to label %cont.19 unwind label %fin.pad.6
cont.19:
  invoke void @"System._UStrClr"(ptr %T.addr)
          to label %cont.20 unwind label %cleanup.pad
cont.20:
  br label %try.end.6
fin.pad.6:
  %cl.6 = cleanuppad within none []
  invoke void @"System._UStrClr"(ptr %T.addr) [ "funclet"(token %cl.6) ]
          to label %cont.21 unwind label %cleanup.pad
cont.21:
  cleanupret from %cl.6 unwind label %cleanup.pad
try.end.6:
  br label %for.inc.5
for.inc.5:
  %t19 = load i32, ptr %I.addr, align 4
  %t20 = load i32, ptr %$tmp2.addr, align 4
  %t21 = icmp eq i32 %t19, %t20
  br i1 %t21, label %for.end.5, label %if.end.8
if.end.8:
  %t22 = load i32, ptr %I.addr, align 4
  %t23 = add i32 %t22, 1
  store i32 %t23, ptr %I.addr, align 4
  br label %for.body.5
for.end.5:
  br label %if.end.4
if.end.4:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.22 unwind label %cleanup.pad
cont.22:
  call void @"System._UStrClr"(ptr %Outer.addr)
  br label %exit
exit:
  %t24 = load i32, ptr %Result.addr, align 4
  ret i32 %t24
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %Outer.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
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

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
