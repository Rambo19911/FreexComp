; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 73, i16 110, i16 116, i16 115, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.1, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
$"M.TInts.$rtti" = comdat any
@"M.TInts.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.0, i32 0, i32 5), i64 4, ptr @"System.Integer.$rtti", ptr null }, comdat, align 8

declare void @"System._DynArrayCat"(ptr, ptr, i64, ptr)
declare void @"System._DynArrayClr"(ptr, ptr)
declare void @"System._DynArraySetLen"(ptr, ptr, i64, ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.Append"(ptr %A, ptr %B, i32 %X) #0 personality ptr @__C_specific_handler {
entry:
  %B.addr = alloca ptr, align 8
  %X.addr = alloca i32, align 4
  %$tmp1.addr = alloca [3 x ptr], align 8
  %$tmp2.addr = alloca ptr, align 8
  %$tmp3.addr = alloca [1 x i64], align 8
  store ptr %B, ptr %B.addr, align 8
  store i32 %X, ptr %X.addr, align 4
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %$tmp2.addr, align 8
  %t1 = load ptr, ptr %A, align 8
  %t2 = getelementptr inbounds [3 x ptr], ptr %$tmp1.addr, i64 0, i64 0
  store ptr %t1, ptr %t2, align 8
  %t3 = load ptr, ptr %B.addr, align 8
  %t4 = getelementptr inbounds [3 x ptr], ptr %$tmp1.addr, i64 0, i64 1
  store ptr %t3, ptr %t4, align 8
  %t5 = getelementptr inbounds [1 x i64], ptr %$tmp3.addr, i64 0, i64 0
  store i64 1, ptr %t5, align 8
  invoke void @"System._DynArraySetLen"(ptr %$tmp2.addr, ptr @"M.TInts.$rtti", i64 1, ptr %$tmp3.addr)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t6 = load i32, ptr %X.addr, align 4
  %t7 = load ptr, ptr %$tmp2.addr, align 8
  %t8 = getelementptr inbounds i32, ptr %t7, i64 0
  store i32 %t6, ptr %t8, align 4
  %t9 = load ptr, ptr %$tmp2.addr, align 8
  %t10 = getelementptr inbounds [3 x ptr], ptr %$tmp1.addr, i64 0, i64 2
  store ptr %t9, ptr %t10, align 8
  invoke void @"System._DynArrayCat"(ptr %A, ptr @"M.TInts.$rtti", i64 3, ptr %$tmp1.addr)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  invoke void @"System._DynArrayClr"(ptr %$tmp2.addr, ptr @"M.TInts.$rtti")
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  call void @"System._DynArrayClr"(ptr %$tmp2.addr, ptr @"M.TInts.$rtti")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._DynArrayClr"(ptr %$tmp2.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define void @"M.Build"(ptr %Result, i32 %N) #0 personality ptr @__C_specific_handler {
entry:
  %N.addr = alloca i32, align 4
  %I.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  %$tmp3.addr = alloca [2 x ptr], align 8
  %$tmp4.addr = alloca ptr, align 8
  %$tmp5.addr = alloca [1 x i64], align 8
  store i32 %N, ptr %N.addr, align 4
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %$tmp4.addr, align 8
  store i32 1, ptr %$tmp1.addr, align 4
  %t1 = load i32, ptr %N.addr, align 4
  store i32 %t1, ptr %$tmp2.addr, align 4
  %t2 = load i32, ptr %$tmp1.addr, align 4
  %t3 = load i32, ptr %$tmp2.addr, align 4
  %t4 = icmp sle i32 %t2, %t3
  br i1 %t4, label %if.then.1, label %if.end.1
if.then.1:
  %t5 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t5, ptr %I.addr, align 4
  br label %for.body.2
for.body.2:
  %t6 = load ptr, ptr %Result, align 8
  %t7 = getelementptr inbounds [2 x ptr], ptr %$tmp3.addr, i64 0, i64 0
  store ptr %t6, ptr %t7, align 8
  %t8 = getelementptr inbounds [1 x i64], ptr %$tmp5.addr, i64 0, i64 0
  store i64 1, ptr %t8, align 8
  invoke void @"System._DynArraySetLen"(ptr %$tmp4.addr, ptr @"M.TInts.$rtti", i64 1, ptr %$tmp5.addr)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t9 = load i32, ptr %I.addr, align 4
  %t10 = load ptr, ptr %$tmp4.addr, align 8
  %t11 = getelementptr inbounds i32, ptr %t10, i64 0
  store i32 %t9, ptr %t11, align 4
  %t12 = load ptr, ptr %$tmp4.addr, align 8
  %t13 = getelementptr inbounds [2 x ptr], ptr %$tmp3.addr, i64 0, i64 1
  store ptr %t12, ptr %t13, align 8
  invoke void @"System._DynArrayCat"(ptr %Result, ptr @"M.TInts.$rtti", i64 2, ptr %$tmp3.addr)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  invoke void @"System._DynArrayClr"(ptr %$tmp4.addr, ptr @"M.TInts.$rtti")
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  br label %for.inc.2
for.inc.2:
  %t14 = load i32, ptr %I.addr, align 4
  %t15 = load i32, ptr %$tmp2.addr, align 4
  %t16 = icmp eq i32 %t14, %t15
  br i1 %t16, label %for.end.2, label %if.end.3
if.end.3:
  %t17 = load i32, ptr %I.addr, align 4
  %t18 = add i32 %t17, 1
  store i32 %t18, ptr %I.addr, align 4
  br label %for.body.2
for.end.2:
  br label %if.end.1
if.end.1:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  call void @"System._DynArrayClr"(ptr %$tmp4.addr, ptr @"M.TInts.$rtti")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._DynArrayClr"(ptr %$tmp4.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define void @"M.Grow"(ptr %G, ptr %B) #0 {
entry:
  %B.addr = alloca ptr, align 8
  %$tmp1.addr = alloca ptr, align 8
  %$tmp2.addr = alloca [2 x ptr], align 8
  store ptr %B, ptr %B.addr, align 8
  %t1 = load ptr, ptr %G, align 8
  %t2 = getelementptr inbounds ptr, ptr %t1, i64 0
  store ptr %t2, ptr %$tmp1.addr, align 8
  %t3 = load ptr, ptr %G, align 8
  %t4 = getelementptr inbounds ptr, ptr %t3, i64 0
  %t5 = load ptr, ptr %t4, align 8
  %t6 = getelementptr inbounds [2 x ptr], ptr %$tmp2.addr, i64 0, i64 0
  store ptr %t5, ptr %t6, align 8
  %t7 = load ptr, ptr %B.addr, align 8
  %t8 = getelementptr inbounds [2 x ptr], ptr %$tmp2.addr, i64 0, i64 1
  store ptr %t7, ptr %t8, align 8
  %t9 = load ptr, ptr %$tmp1.addr, align 8
  call void @"System._DynArrayCat"(ptr %t9, ptr @"M.TInts.$rtti", i64 2, ptr %$tmp2.addr)
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

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
