; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@"System.Input" = external global [624 x i8], align 8

declare void @"System.SetInOutRes"(i32)
declare i8 @"System._Eof"(ptr)
declare i8 @"System._Eoln"(ptr)
declare void @"System._IOCheck"()
declare void @"System._ReadEnd"(ptr)
declare double @"System._ReadFloat"(ptr)
declare i64 @"System._ReadInt"(ptr)
declare void @"System._ReadLn"(ptr)
declare i64 @"System._ReadUInt"(ptr)
declare void @"System._ReadUStr"(ptr, ptr)
declare i16 @"System._ReadWChar"(ptr)
declare void @"System._UStrClr"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.Scan"(ptr %t) #0 personality ptr @__C_specific_handler {
entry:
  %n.addr = alloca i32, align 4
  %w.addr = alloca i16, align 2
  %q.addr = alloca i64, align 8
  %g.addr = alloca float, align 4
  %c.addr = alloca i16, align 2
  %s.addr = alloca ptr, align 8
  %$tmp1.addr = alloca i64, align 8
  %$tmp2.addr = alloca i64, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %s.addr, align 8
  br label %while.cond.1
while.cond.1:
  %t1 = invoke i8 @"System._Eof"(ptr @"System.Input")
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t2 = icmp ne i8 %t1, 0
  %t3 = xor i1 %t2, true
  br i1 %t3, label %while.body.1, label %while.end.1
while.body.1:
  %t4 = invoke i64 @"System._ReadInt"(ptr @"System.Input")
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  store i64 %t4, ptr %$tmp1.addr, align 8
  %t5 = load i64, ptr %$tmp1.addr, align 8
  %t6 = icmp slt i64 %t5, -2147483648
  br i1 %t6, label %or.end.2, label %or.rhs.2
or.rhs.2:
  %t7 = load i64, ptr %$tmp1.addr, align 8
  %t8 = icmp sgt i64 %t7, 2147483647
  br label %or.end.2
or.end.2:
  %t9 = phi i1 [ true, %cont.3 ], [ %t8, %or.rhs.2 ]
  br i1 %t9, label %if.then.3, label %if.end.3
if.then.3:
  invoke void @"System.SetInOutRes"(i32 106)
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  br label %if.end.3
if.end.3:
  %t10 = load i64, ptr %$tmp1.addr, align 8
  %t11 = trunc i64 %t10 to i32
  store i32 %t11, ptr %n.addr, align 4
  %t12 = invoke i64 @"System._ReadInt"(ptr @"System.Input")
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  store i64 %t12, ptr %$tmp2.addr, align 8
  %t13 = load i64, ptr %$tmp2.addr, align 8
  %t14 = icmp slt i64 %t13, 0
  br i1 %t14, label %or.end.4, label %or.rhs.4
or.rhs.4:
  %t15 = load i64, ptr %$tmp2.addr, align 8
  %t16 = icmp sgt i64 %t15, 65535
  br label %or.end.4
or.end.4:
  %t17 = phi i1 [ true, %cont.5 ], [ %t16, %or.rhs.4 ]
  br i1 %t17, label %if.then.5, label %if.end.5
if.then.5:
  invoke void @"System.SetInOutRes"(i32 106)
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  br label %if.end.5
if.end.5:
  %t18 = load i64, ptr %$tmp2.addr, align 8
  %t19 = trunc i64 %t18 to i16
  store i16 %t19, ptr %w.addr, align 2
  %t20 = invoke i64 @"System._ReadUInt"(ptr @"System.Input")
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  store i64 %t20, ptr %q.addr, align 8
  %t21 = invoke double @"System._ReadFloat"(ptr @"System.Input")
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  %t22 = fptrunc double %t21 to float
  store float %t22, ptr %g.addr, align 4
  %t23 = invoke i16 @"System._ReadWChar"(ptr @"System.Input")
          to label %cont.9 unwind label %cleanup.pad
cont.9:
  store i16 %t23, ptr %c.addr, align 2
  invoke void @"System._ReadUStr"(ptr @"System.Input", ptr %s.addr)
          to label %cont.10 unwind label %cleanup.pad
cont.10:
  invoke void @"System._ReadLn"(ptr @"System.Input")
          to label %cont.11 unwind label %cleanup.pad
cont.11:
  invoke void @"System._ReadEnd"(ptr @"System.Input")
          to label %cont.12 unwind label %cleanup.pad
cont.12:
  invoke void @"System._IOCheck"()
          to label %cont.13 unwind label %cleanup.pad
cont.13:
  br label %while.cond.1
while.end.1:
  %t24 = invoke i8 @"System._Eoln"(ptr %t)
          to label %cont.14 unwind label %cleanup.pad
cont.14:
  %t25 = icmp ne i8 %t24, 0
  br i1 %t25, label %if.then.6, label %if.end.6
if.then.6:
  invoke void @"System._ReadUStr"(ptr %t, ptr %s.addr)
          to label %cont.15 unwind label %cleanup.pad
cont.15:
  invoke void @"System._ReadEnd"(ptr %t)
          to label %cont.16 unwind label %cleanup.pad
cont.16:
  br label %if.end.6
if.end.6:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.17 unwind label %cleanup.pad
cont.17:
  call void @"System._UStrClr"(ptr %s.addr)
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %s.addr) [ "funclet"(token %cl) ]
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
