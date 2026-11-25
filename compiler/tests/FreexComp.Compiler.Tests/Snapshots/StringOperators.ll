; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

declare void @"System._RangeError"() #1
declare void @"System._UStrCat"(ptr, ptr, ptr)
declare void @"System._UStrCatN"(ptr, ptr, i32)
declare void @"System._UStrClr"(ptr)
declare i32 @"System._UStrCmp"(ptr, ptr)
declare i8 @"System._UStrEq"(ptr, ptr)
declare void @"System._UStrFromWChar"(ptr, i16)
declare void @"System._UStrUnique"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.Join"(ptr %Result, ptr %a, ptr %b, ptr %c, i16 %ch) #0 personality ptr @__C_specific_handler {
entry:
  %a.addr = alloca ptr, align 8
  %b.addr = alloca ptr, align 8
  %c.addr = alloca ptr, align 8
  %ch.addr = alloca i16, align 2
  %$tmp1.addr = alloca [3 x ptr], align 8
  %$tmp2.addr = alloca ptr, align 8
  %$tmp3.addr = alloca ptr, align 8
  %$tmp4.addr = alloca ptr, align 8
  store ptr %a, ptr %a.addr, align 8
  store ptr %b, ptr %b.addr, align 8
  store ptr %c, ptr %c.addr, align 8
  store i16 %ch, ptr %ch.addr, align 2
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %$tmp2.addr, align 8
  store ptr null, ptr %$tmp3.addr, align 8
  store ptr null, ptr %$tmp4.addr, align 8
  %t1 = load ptr, ptr %a.addr, align 8
  %t2 = load ptr, ptr %b.addr, align 8
  invoke void @"System._UStrCat"(ptr %Result, ptr %t1, ptr %t2)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t3 = load ptr, ptr %a.addr, align 8
  %t4 = getelementptr inbounds [3 x ptr], ptr %$tmp1.addr, i64 0, i64 0
  store ptr %t3, ptr %t4, align 8
  %t5 = load ptr, ptr %b.addr, align 8
  %t6 = getelementptr inbounds [3 x ptr], ptr %$tmp1.addr, i64 0, i64 1
  store ptr %t5, ptr %t6, align 8
  %t7 = load ptr, ptr %c.addr, align 8
  %t8 = getelementptr inbounds [3 x ptr], ptr %$tmp1.addr, i64 0, i64 2
  store ptr %t7, ptr %t8, align 8
  invoke void @"System._UStrCatN"(ptr %Result, ptr %$tmp1.addr, i32 3)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t9 = load ptr, ptr %Result, align 8
  %t10 = load i16, ptr %ch.addr, align 2
  invoke void @"System._UStrFromWChar"(ptr %$tmp2.addr, i16 %t10)
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  %t11 = load ptr, ptr %$tmp2.addr, align 8
  invoke void @"System._UStrCat"(ptr %Result, ptr %t9, ptr %t11)
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  %t12 = load i16, ptr %ch.addr, align 2
  invoke void @"System._UStrFromWChar"(ptr %$tmp3.addr, i16 %t12)
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  %t13 = load ptr, ptr %$tmp3.addr, align 8
  %t14 = load i16, ptr %ch.addr, align 2
  invoke void @"System._UStrFromWChar"(ptr %$tmp4.addr, i16 %t14)
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  %t15 = load ptr, ptr %$tmp4.addr, align 8
  invoke void @"System._UStrCat"(ptr %Result, ptr %t13, ptr %t15)
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.9 unwind label %cleanup.pad
cont.9:
  call void @"System._UStrClr"(ptr %$tmp2.addr)
  call void @"System._UStrClr"(ptr %$tmp3.addr)
  call void @"System._UStrClr"(ptr %$tmp4.addr)
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %$tmp2.addr) [ "funclet"(token %cl) ]
  call void @"System._UStrClr"(ptr %$tmp3.addr) [ "funclet"(token %cl) ]
  call void @"System._UStrClr"(ptr %$tmp4.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define i32 @"M.Order"(ptr %a, ptr %b) #0 personality ptr @__C_specific_handler {
entry:
  %a.addr = alloca ptr, align 8
  %b.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  %$tmp1.addr = alloca ptr, align 8
  store ptr %a, ptr %a.addr, align 8
  store ptr %b, ptr %b.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %$tmp1.addr, align 8
  store i32 0, ptr %Result.addr, align 4
  %t1 = load ptr, ptr %a.addr, align 8
  %t2 = load ptr, ptr %b.addr, align 8
  %t3 = call i8 @"System._UStrEq"(ptr %t1, ptr %t2)
  %t4 = icmp ne i8 %t3, 0
  br i1 %t4, label %if.then.1, label %if.end.1
if.then.1:
  store i32 1, ptr %Result.addr, align 4
  br label %if.end.1
if.end.1:
  %t5 = load ptr, ptr %a.addr, align 8
  %t6 = load ptr, ptr %b.addr, align 8
  %t7 = call i8 @"System._UStrEq"(ptr %t5, ptr %t6)
  %t8 = icmp ne i8 %t7, 0
  %t9 = xor i1 %t8, true
  br i1 %t9, label %if.then.2, label %if.end.2
if.then.2:
  %t10 = load i32, ptr %Result.addr, align 4
  %t11 = add i32 %t10, 2
  store i32 %t11, ptr %Result.addr, align 4
  br label %if.end.2
if.end.2:
  %t12 = load ptr, ptr %a.addr, align 8
  %t13 = load ptr, ptr %b.addr, align 8
  %t14 = call i32 @"System._UStrCmp"(ptr %t12, ptr %t13)
  %t15 = icmp slt i32 %t14, 0
  br i1 %t15, label %if.then.3, label %if.end.3
if.then.3:
  %t16 = load i32, ptr %Result.addr, align 4
  %t17 = add i32 %t16, 4
  store i32 %t17, ptr %Result.addr, align 4
  br label %if.end.3
if.end.3:
  %t18 = load ptr, ptr %a.addr, align 8
  %t19 = load ptr, ptr %b.addr, align 8
  %t20 = call i32 @"System._UStrCmp"(ptr %t18, ptr %t19)
  %t21 = icmp sge i32 %t20, 0
  br i1 %t21, label %if.then.4, label %if.end.4
if.then.4:
  %t22 = load i32, ptr %Result.addr, align 4
  %t23 = add i32 %t22, 8
  store i32 %t23, ptr %Result.addr, align 4
  br label %if.end.4
if.end.4:
  %t24 = load ptr, ptr %a.addr, align 8
  %t25 = icmp eq ptr %t24, null
  br i1 %t25, label %if.then.5, label %if.end.5
if.then.5:
  %t26 = load i32, ptr %Result.addr, align 4
  %t27 = add i32 %t26, 16
  store i32 %t27, ptr %Result.addr, align 4
  br label %if.end.5
if.end.5:
  %t28 = load ptr, ptr %a.addr, align 8
  %t29 = load ptr, ptr %b.addr, align 8
  invoke void @"System._UStrCat"(ptr %$tmp1.addr, ptr %t28, ptr %t29)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t30 = load ptr, ptr %$tmp1.addr, align 8
  %t31 = icmp ne ptr %t30, null
  br i1 %t31, label %if.then.6, label %if.end.6
if.then.6:
  %t32 = load i32, ptr %Result.addr, align 4
  %t33 = add i32 %t32, 32
  store i32 %t33, ptr %Result.addr, align 4
  br label %if.end.6
if.end.6:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  call void @"System._UStrClr"(ptr %$tmp1.addr)
  br label %exit
exit:
  %t34 = load i32, ptr %Result.addr, align 4
  ret i32 %t34
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %$tmp1.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define void @"M.Poke"(ptr %s, i32 %i, i16 %ch) #0 {
entry:
  %i.addr = alloca i32, align 4
  %ch.addr = alloca i16, align 2
  store i32 %i, ptr %i.addr, align 4
  store i16 %ch, ptr %ch.addr, align 2
  %t1 = load i16, ptr %ch.addr, align 2
  call void @"System._UStrUnique"(ptr %s)
  %t2 = load ptr, ptr %s, align 8
  %t3 = load i32, ptr %i.addr, align 4
  %t4 = sext i32 %t3 to i64
  %t5 = sub i64 %t4, 1
  %t6 = getelementptr inbounds i16, ptr %t2, i64 %t5
  store i16 %t1, ptr %t6, align 2
  %t7 = load ptr, ptr %s, align 8
  %t8 = load i32, ptr %i.addr, align 4
  %t9 = sext i32 %t8 to i64
  %t10 = sub i64 %t9, 1
  %t11 = icmp eq ptr %t7, null
  br i1 %t11, label %len.end.1, label %len.load.1
len.load.1:
  %t12 = getelementptr inbounds i8, ptr %t7, i64 -12
  %t13 = load i32, ptr %t12, align 4
  br label %len.end.1
len.end.1:
  %t14 = phi i32 [ 0, %entry ], [ %t13, %len.load.1 ]
  %t15 = zext i32 %t14 to i64
  %t16 = icmp ult i64 %t10, %t15
  br i1 %t16, label %range.ok.2, label %range.error.2
range.error.2:
  call void @"System._RangeError"() #1
  unreachable
range.ok.2:
  %t17 = getelementptr inbounds i16, ptr %t7, i64 %t10
  %t18 = load i16, ptr %t17, align 2
  call void @"System._UStrUnique"(ptr %s)
  %t19 = load ptr, ptr %s, align 8
  %t20 = load i32, ptr %i.addr, align 4
  %t21 = add i32 %t20, 1
  %t22 = sext i32 %t21 to i64
  %t23 = sub i64 %t22, 1
  %t24 = icmp eq ptr %t19, null
  br i1 %t24, label %len.end.3, label %len.load.3
len.load.3:
  %t25 = getelementptr inbounds i8, ptr %t19, i64 -12
  %t26 = load i32, ptr %t25, align 4
  br label %len.end.3
len.end.3:
  %t27 = phi i32 [ 0, %range.ok.2 ], [ %t26, %len.load.3 ]
  %t28 = zext i32 %t27 to i64
  %t29 = icmp ult i64 %t23, %t28
  br i1 %t29, label %range.ok.4, label %range.error.4
range.error.4:
  call void @"System._RangeError"() #1
  unreachable
range.ok.4:
  %t30 = getelementptr inbounds i16, ptr %t19, i64 %t23
  store i16 %t18, ptr %t30, align 2
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
attributes #1 = { noreturn uwtable }

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
