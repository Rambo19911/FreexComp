; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 120, i16 0] }, align 16

declare void @"System._StrFloat"(ptr, double, i32, i32)
declare void @"System._StrInt"(ptr, i64, i32)
declare void @"System._UStrAsg"(ptr, ptr)
declare void @"System._UStrCatN"(ptr, ptr, i32)
declare void @"System._UStrClr"(ptr)
declare void @"System._UStrCopy"(ptr, ptr, i32, i32)
declare void @"System._UStrDelete"(ptr, i32, i32)
declare void @"System._UStrFromWChar"(ptr, i16)
declare void @"System._UStrInsert"(ptr, ptr, i32)
declare i32 @"System._UStrPos"(ptr, ptr, i32)
declare void @"System._UStrSetLen"(ptr, i32)
declare void @"System._UStrUnique"(ptr)
declare double @"System._ValFloat"(ptr, ptr)
declare i64 @"System._ValInt"(ptr, ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.Edit"(ptr %Result, ptr %s, ptr %t, i16 %c, i32 %i) #0 personality ptr @__C_specific_handler {
entry:
  %t.addr = alloca ptr, align 8
  %c.addr = alloca i16, align 2
  %i.addr = alloca i32, align 4
  %$tmp1.addr = alloca ptr, align 8
  %$tmp2.addr = alloca ptr, align 8
  %$tmp3.addr = alloca ptr, align 8
  %$tmp4.addr = alloca [3 x ptr], align 8
  store ptr %t, ptr %t.addr, align 8
  store i16 %c, ptr %c.addr, align 2
  store i32 %i, ptr %i.addr, align 4
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %$tmp1.addr, align 8
  store ptr null, ptr %$tmp2.addr, align 8
  store ptr null, ptr %$tmp3.addr, align 8
  %t1 = load i32, ptr %i.addr, align 4
  invoke void @"System._UStrSetLen"(ptr %s, i32 %t1)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t2 = load i32, ptr %i.addr, align 4
  invoke void @"System._UStrDelete"(ptr %s, i32 2, i32 %t2)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t3 = load ptr, ptr %t.addr, align 8
  %t4 = load i32, ptr %i.addr, align 4
  invoke void @"System._UStrInsert"(ptr %t3, ptr %s, i32 %t4)
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  %t5 = load i16, ptr %c.addr, align 2
  invoke void @"System._UStrFromWChar"(ptr %$tmp1.addr, i16 %t5)
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  %t6 = load ptr, ptr %$tmp1.addr, align 8
  invoke void @"System._UStrInsert"(ptr %t6, ptr %s, i32 1)
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  %t7 = load ptr, ptr %s, align 8
  %t8 = load i32, ptr %i.addr, align 4
  invoke void @"System._UStrCopy"(ptr %$tmp2.addr, ptr %t7, i32 %t8, i32 2)
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  %t9 = load ptr, ptr %$tmp2.addr, align 8
  %t10 = getelementptr inbounds [3 x ptr], ptr %$tmp4.addr, i64 0, i64 0
  store ptr %t9, ptr %t10, align 8
  %t11 = load ptr, ptr %t.addr, align 8
  %t12 = getelementptr inbounds [3 x ptr], ptr %$tmp4.addr, i64 0, i64 1
  store ptr %t11, ptr %t12, align 8
  %t13 = load i16, ptr %c.addr, align 2
  invoke void @"System._UStrFromWChar"(ptr %$tmp3.addr, i16 %t13)
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  %t14 = load ptr, ptr %$tmp3.addr, align 8
  %t15 = getelementptr inbounds [3 x ptr], ptr %$tmp4.addr, i64 0, i64 2
  store ptr %t14, ptr %t15, align 8
  invoke void @"System._UStrCatN"(ptr %Result, ptr %$tmp4.addr, i32 3)
          to label %cont.9 unwind label %cleanup.pad
cont.9:
  %t16 = load ptr, ptr %s, align 8
  invoke void @"System._UStrCopy"(ptr %s, ptr %t16, i32 2, i32 2147483647)
          to label %cont.10 unwind label %cleanup.pad
cont.10:
  %t17 = load ptr, ptr %Result, align 8
  %t18 = getelementptr inbounds i16, ptr %t17, i64 0
  %t19 = load i16, ptr %t18, align 2
  %t20 = icmp uge i16 %t19, 97
  %t21 = icmp ule i16 %t19, 122
  %t22 = and i1 %t20, %t21
  %t23 = zext i16 %t19 to i32
  %t24 = sub i32 %t23, 32
  %t25 = trunc i32 %t24 to i16
  %t26 = select i1 %t22, i16 %t25, i16 %t19
  invoke void @"System._UStrUnique"(ptr %Result)
          to label %cont.11 unwind label %cleanup.pad
cont.11:
  %t27 = load ptr, ptr %Result, align 8
  %t28 = getelementptr inbounds i16, ptr %t27, i64 0
  store i16 %t26, ptr %t28, align 2
  %t29 = load ptr, ptr %t.addr, align 8
  %t30 = load ptr, ptr %s, align 8
  %t31 = invoke i32 @"System._UStrPos"(ptr %t29, ptr %t30, i32 1)
          to label %cont.12 unwind label %cleanup.pad
cont.12:
  %t32 = load ptr, ptr %s, align 8
  %t33 = load i32, ptr %i.addr, align 4
  %t34 = invoke i32 @"System._UStrPos"(ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.0, i32 0, i32 5), ptr %t32, i32 %t33)
          to label %cont.13 unwind label %cleanup.pad
cont.13:
  %t35 = icmp sgt i32 %t31, %t34
  br i1 %t35, label %if.then.1, label %if.end.1
if.then.1:
  %t36 = load ptr, ptr %t.addr, align 8
  invoke void @"System._UStrAsg"(ptr %Result, ptr %t36)
          to label %cont.14 unwind label %cleanup.pad
cont.14:
  br label %if.end.1
if.end.1:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.15 unwind label %cleanup.pad
cont.15:
  call void @"System._UStrClr"(ptr %$tmp1.addr)
  call void @"System._UStrClr"(ptr %$tmp2.addr)
  call void @"System._UStrClr"(ptr %$tmp3.addr)
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %$tmp1.addr) [ "funclet"(token %cl) ]
  call void @"System._UStrClr"(ptr %$tmp2.addr) [ "funclet"(token %cl) ]
  call void @"System._UStrClr"(ptr %$tmp3.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define i8 @"M.Numbers"(ptr %s, i32 %i, double %d, ptr %code) #0 {
entry:
  %i.addr = alloca i32, align 4
  %d.addr = alloca double, align 8
  %Result.addr = alloca i8, align 1
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i64, align 8
  %$tmp3.addr = alloca i32, align 4
  %$tmp4.addr = alloca double, align 8
  store i32 %i, ptr %i.addr, align 4
  store double %d, ptr %d.addr, align 8
  %t1 = load i32, ptr %i.addr, align 4
  %t2 = sext i32 %t1 to i64
  call void @"System._StrInt"(ptr %s, i64 %t2, i32 0)
  %t3 = load i32, ptr %i.addr, align 4
  %t4 = sext i32 %t3 to i64
  call void @"System._StrInt"(ptr %s, i64 %t4, i32 5)
  %t5 = load double, ptr %d.addr, align 8
  call void @"System._StrFloat"(ptr %s, double %t5, i32 23, i32 -1)
  %t6 = load double, ptr %d.addr, align 8
  call void @"System._StrFloat"(ptr %s, double %t6, i32 10, i32 3)
  %t7 = load ptr, ptr %s, align 8
  %t8 = call i64 @"System._ValInt"(ptr %t7, ptr %$tmp1.addr)
  store i64 %t8, ptr %$tmp2.addr, align 8
  %t9 = load i32, ptr %$tmp1.addr, align 4
  %t10 = icmp eq i32 %t9, 0
  %t11 = load i64, ptr %$tmp2.addr, align 8
  %t12 = icmp slt i64 %t11, 0
  %t13 = load i64, ptr %$tmp2.addr, align 8
  %t14 = icmp sgt i64 %t13, 255
  %t15 = or i1 %t12, %t14
  %t16 = and i1 %t10, %t15
  br i1 %t16, label %if.then.1, label %if.end.1
if.then.1:
  %t17 = load ptr, ptr %s, align 8
  %t18 = icmp eq ptr %t17, null
  br i1 %t18, label %len.end.2, label %len.load.2
len.load.2:
  %t19 = getelementptr inbounds i8, ptr %t17, i64 -12
  %t20 = load i32, ptr %t19, align 4
  br label %len.end.2
len.end.2:
  %t21 = phi i32 [ 0, %if.then.1 ], [ %t20, %len.load.2 ]
  store i32 %t21, ptr %$tmp1.addr, align 4
  store i64 0, ptr %$tmp2.addr, align 8
  br label %if.end.1
if.end.1:
  %t22 = load i64, ptr %$tmp2.addr, align 8
  %t23 = trunc i64 %t22 to i8
  store i8 %t23, ptr %Result.addr, align 1
  %t24 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t24, ptr %code, align 4
  %t25 = load ptr, ptr %s, align 8
  %t26 = call double @"System._ValFloat"(ptr %t25, ptr %$tmp3.addr)
  store double %t26, ptr %$tmp4.addr, align 8
  %t27 = load double, ptr %$tmp4.addr, align 8
  store double %t27, ptr %d.addr, align 8
  %t28 = load i32, ptr %$tmp3.addr, align 4
  store i32 %t28, ptr %code, align 4
  br label %exit
exit:
  %t29 = load i8, ptr %Result.addr, align 1
  ret i8 %t29
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
