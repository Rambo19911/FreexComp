; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 73, i16 110, i16 116, i16 115, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.1, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
$"M.TInts.$rtti" = comdat any
@"M.TInts.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.0, i32 0, i32 5), i64 4, ptr @"System.Integer.$rtti", ptr null }, comdat, align 8

declare void @"System._DynArrayAsg"(ptr, ptr, ptr)
declare void @"System._DynArrayCat"(ptr, ptr, i64, ptr)
declare void @"System._DynArrayClr"(ptr, ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define i32 @"M.Total"(ptr %A) #0 personality ptr @__C_specific_handler {
entry:
  %A.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  %X.addr = alloca i32, align 4
  %$in1.addr = alloca ptr, align 8
  %$i2.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  store ptr %A, ptr %A.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %$in1.addr, align 8
  store i32 0, ptr %Result.addr, align 4
  %t1 = load ptr, ptr %A.addr, align 8
  invoke void @"System._DynArrayAsg"(ptr %$in1.addr, ptr %t1, ptr @"M.TInts.$rtti")
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  store i32 0, ptr %$tmp1.addr, align 4
  %t3 = load ptr, ptr %$in1.addr, align 8
  %t4 = icmp eq ptr %t3, null
  br i1 %t4, label %len.end.1, label %len.load.1
len.load.1:
  %t5 = getelementptr inbounds i8, ptr %t3, i64 -8
  %t6 = load i64, ptr %t5, align 8
  br label %len.end.1
len.end.1:
  %t7 = phi i64 [ 0, %cont.2 ], [ %t6, %len.load.1 ]
  %t2 = trunc i64 %t7 to i32
  %t8 = sub i32 %t2, 1
  store i32 %t8, ptr %$tmp2.addr, align 4
  %t9 = load i32, ptr %$tmp1.addr, align 4
  %t10 = load i32, ptr %$tmp2.addr, align 4
  %t11 = icmp sle i32 %t9, %t10
  br i1 %t11, label %if.then.2, label %if.end.2
if.then.2:
  %t12 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t12, ptr %$i2.addr, align 4
  br label %for.body.3
for.body.3:
  %t13 = load ptr, ptr %$in1.addr, align 8
  %t14 = load i32, ptr %$i2.addr, align 4
  %t15 = sext i32 %t14 to i64
  %t16 = getelementptr inbounds i32, ptr %t13, i64 %t15
  %t17 = load i32, ptr %t16, align 4
  store i32 %t17, ptr %X.addr, align 4
  %t18 = load i32, ptr %Result.addr, align 4
  %t19 = load i32, ptr %X.addr, align 4
  %t20 = add i32 %t18, %t19
  store i32 %t20, ptr %Result.addr, align 4
  br label %for.inc.3
for.inc.3:
  %t21 = load i32, ptr %$i2.addr, align 4
  %t22 = load i32, ptr %$tmp2.addr, align 4
  %t23 = icmp eq i32 %t21, %t22
  br i1 %t23, label %for.end.3, label %if.end.4
if.end.4:
  %t24 = load i32, ptr %$i2.addr, align 4
  %t25 = add i32 %t24, 1
  store i32 %t25, ptr %$i2.addr, align 4
  br label %for.body.3
for.end.3:
  br label %if.end.2
if.end.2:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  call void @"System._DynArrayClr"(ptr %$in1.addr, ptr @"M.TInts.$rtti")
  br label %exit
exit:
  %t26 = load i32, ptr %Result.addr, align 4
  ret i32 %t26
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._DynArrayClr"(ptr %$in1.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define i32 @"M.Twice"(ptr %A) #0 personality ptr @__C_specific_handler {
entry:
  %A.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  %$tmp1.addr = alloca ptr, align 8
  %$tmp2.addr = alloca [2 x ptr], align 8
  %$tmp3.addr = alloca ptr, align 8
  %$tmp4.addr = alloca [3 x ptr], align 8
  store ptr %A, ptr %A.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %$tmp1.addr, align 8
  store ptr null, ptr %$tmp3.addr, align 8
  %t1 = load ptr, ptr %A.addr, align 8
  %t2 = getelementptr inbounds [2 x ptr], ptr %$tmp2.addr, i64 0, i64 0
  store ptr %t1, ptr %t2, align 8
  %t3 = load ptr, ptr %A.addr, align 8
  %t4 = getelementptr inbounds [2 x ptr], ptr %$tmp2.addr, i64 0, i64 1
  store ptr %t3, ptr %t4, align 8
  invoke void @"System._DynArrayCat"(ptr %$tmp1.addr, ptr @"M.TInts.$rtti", i64 2, ptr %$tmp2.addr)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t5 = load ptr, ptr %$tmp1.addr, align 8
  %t6 = invoke i32 @"M.Total"(ptr %t5)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t8 = getelementptr inbounds [3 x ptr], ptr %$tmp4.addr, i64 0, i64 0
  store ptr null, ptr %t8, align 8
  %t9 = load ptr, ptr %A.addr, align 8
  %t10 = getelementptr inbounds [3 x ptr], ptr %$tmp4.addr, i64 0, i64 1
  store ptr %t9, ptr %t10, align 8
  %t11 = getelementptr inbounds [3 x ptr], ptr %$tmp4.addr, i64 0, i64 2
  store ptr null, ptr %t11, align 8
  invoke void @"System._DynArrayCat"(ptr %$tmp3.addr, ptr @"M.TInts.$rtti", i64 3, ptr %$tmp4.addr)
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  %t12 = load ptr, ptr %$tmp3.addr, align 8
  %t13 = icmp eq ptr %t12, null
  br i1 %t13, label %len.end.1, label %len.load.1
len.load.1:
  %t14 = getelementptr inbounds i8, ptr %t12, i64 -8
  %t15 = load i64, ptr %t14, align 8
  br label %len.end.1
len.end.1:
  %t16 = phi i64 [ 0, %cont.4 ], [ %t15, %len.load.1 ]
  %t7 = trunc i64 %t16 to i32
  %t17 = add i32 %t6, %t7
  store i32 %t17, ptr %Result.addr, align 4
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  call void @"System._DynArrayClr"(ptr %$tmp1.addr, ptr @"M.TInts.$rtti")
  call void @"System._DynArrayClr"(ptr %$tmp3.addr, ptr @"M.TInts.$rtti")
  br label %exit
exit:
  %t18 = load i32, ptr %Result.addr, align 4
  ret i32 %t18
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._DynArrayClr"(ptr %$tmp1.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
  call void @"System._DynArrayClr"(ptr %$tmp3.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
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
