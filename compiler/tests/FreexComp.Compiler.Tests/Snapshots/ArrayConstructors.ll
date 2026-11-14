; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 73, i16 110, i16 116, i16 115, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 84, i16 78, i16 97, i16 109, i16 101, i16 115, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 115, i16 116, i16 114, i16 105, i16 110, i16 103, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 120, i16 0] }, align 16
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.1, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
$"M.TInts.$rtti" = comdat any
@"M.TInts.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.0, i32 0, i32 5), i64 4, ptr @"System.Integer.$rtti", ptr null }, comdat, align 8
$"System.string.$rtti" = comdat any
@"System.string.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i16, [6 x i8] } { i8 9, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.3, i32 0, i32 5), i16 1200, [6 x i8] zeroinitializer }, comdat, align 8
$"M.TNames.$rtti" = comdat any
@"M.TNames.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.2, i32 0, i32 5), i64 8, ptr @"System.string.$rtti", ptr @"System.string.$rtti" }, comdat, align 8

declare void @"System._DynArrayAsg"(ptr, ptr, ptr)
declare void @"System._DynArrayClr"(ptr, ptr)
declare void @"System._DynArraySetLen"(ptr, ptr, i64, ptr)
declare void @"System._UStrAsg"(ptr, ptr)
declare i32 @__C_specific_handler(...)

define internal i32 @"M.Count"(ptr %a) #0 {
entry:
  %a.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  store ptr %a, ptr %a.addr, align 8
  %t2 = load ptr, ptr %a.addr, align 8
  %t3 = icmp eq ptr %t2, null
  br i1 %t3, label %len.end.1, label %len.load.1
len.load.1:
  %t4 = getelementptr inbounds i8, ptr %t2, i64 -8
  %t5 = load i64, ptr %t4, align 8
  br label %len.end.1
len.end.1:
  %t6 = phi i64 [ 0, %entry ], [ %t5, %len.load.1 ]
  %t1 = trunc i64 %t6 to i32
  store i32 %t1, ptr %Result.addr, align 4
  br label %exit
exit:
  %t7 = load i32, ptr %Result.addr, align 4
  ret i32 %t7
}

define void @"M.Build"(ptr %Result, i32 %n, ptr %p) #0 personality ptr @__C_specific_handler {
entry:
  %n.addr = alloca i32, align 4
  %p.addr = alloca ptr, align 8
  %names.addr = alloca ptr, align 8
  %q.addr = alloca ptr, align 8
  %k.addr = alloca i64, align 8
  %$tmp1.addr = alloca ptr, align 8
  %$tmp2.addr = alloca [1 x i64], align 8
  %$tmp3.addr = alloca ptr, align 8
  %$tmp4.addr = alloca [1 x i64], align 8
  %$tmp5.addr = alloca ptr, align 8
  %$tmp6.addr = alloca ptr, align 8
  %$tmp7.addr = alloca [1 x i64], align 8
  store i32 %n, ptr %n.addr, align 4
  store ptr %p, ptr %p.addr, align 8
  store ptr null, ptr %names.addr, align 8
  store ptr null, ptr %$tmp1.addr, align 8
  store ptr null, ptr %$tmp3.addr, align 8
  store ptr null, ptr %$tmp5.addr, align 8
  store ptr null, ptr %$tmp6.addr, align 8
  %t1 = getelementptr inbounds [1 x i64], ptr %$tmp2.addr, i64 0, i64 0
  store i64 2, ptr %t1, align 8
  invoke void @"System._DynArraySetLen"(ptr %$tmp1.addr, ptr @"M.TInts.$rtti", i64 1, ptr %$tmp2.addr)
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  %t2 = load i32, ptr %n.addr, align 4
  %t3 = load ptr, ptr %$tmp1.addr, align 8
  %t4 = getelementptr inbounds i32, ptr %t3, i64 0
  store i32 %t2, ptr %t4, align 4
  %t5 = load i32, ptr %n.addr, align 4
  %t6 = add i32 %t5, 1
  %t7 = load ptr, ptr %$tmp1.addr, align 8
  %t8 = getelementptr inbounds i32, ptr %t7, i64 1
  store i32 %t6, ptr %t8, align 4
  %t9 = load ptr, ptr %$tmp1.addr, align 8
  invoke void @"System._DynArrayAsg"(ptr %Result, ptr %t9, ptr @"M.TInts.$rtti")
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t10 = getelementptr inbounds [1 x i64], ptr %$tmp4.addr, i64 0, i64 0
  store i64 2, ptr %t10, align 8
  invoke void @"System._DynArraySetLen"(ptr %$tmp3.addr, ptr @"M.TNames.$rtti", i64 1, ptr %$tmp4.addr)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t11 = load ptr, ptr %$tmp3.addr, align 8
  %t12 = getelementptr inbounds ptr, ptr %t11, i64 0
  %t13 = load ptr, ptr %p.addr, align 8
  %t14 = load ptr, ptr %t13, align 8
  invoke void @"System._UStrAsg"(ptr %t12, ptr %t14)
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  %t15 = load ptr, ptr %$tmp3.addr, align 8
  %t16 = getelementptr inbounds ptr, ptr %t15, i64 1
  invoke void @"System._UStrAsg"(ptr %t16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.4, i32 0, i32 5))
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  %t17 = load ptr, ptr %$tmp3.addr, align 8
  invoke void @"System._DynArrayAsg"(ptr %names.addr, ptr %t17, ptr @"M.TNames.$rtti")
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  %t18 = getelementptr inbounds [1 x i64], ptr %$tmp7.addr, i64 0, i64 0
  store i64 1, ptr %t18, align 8
  invoke void @"System._DynArraySetLen"(ptr %$tmp6.addr, ptr @"M.TInts.$rtti", i64 1, ptr %$tmp7.addr)
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  %t19 = load ptr, ptr %$tmp6.addr, align 8
  %t20 = getelementptr inbounds i32, ptr %t19, i64 0
  store i32 7, ptr %t20, align 4
  %t21 = load ptr, ptr %$tmp6.addr, align 8
  invoke void @"System._DynArrayAsg"(ptr %$tmp5.addr, ptr %t21, ptr @"M.TInts.$rtti")
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  %t22 = load ptr, ptr %$tmp5.addr, align 8
  %t23 = invoke i32 @"M.Count"(ptr %t22)
          to label %cont.9 unwind label %cleanup.pad
cont.9:
  store i32 %t23, ptr %n.addr, align 4
  %t24 = load ptr, ptr %Result, align 8
  %t25 = icmp ne ptr %t24, null
  br i1 %t25, label %if.then.1, label %if.end.1
if.then.1:
  %t26 = load ptr, ptr %Result, align 8
  store ptr %t26, ptr %q.addr, align 8
  br label %if.end.1
if.end.1:
  %t27 = load ptr, ptr %Result, align 8
  %t28 = ptrtoint ptr %t27 to i64
  store i64 %t28, ptr %k.addr, align 8
  %t29 = load ptr, ptr %p.addr, align 8
  %t30 = load ptr, ptr %names.addr, align 8
  %t31 = getelementptr inbounds ptr, ptr %t30, i64 0
  %t32 = load ptr, ptr %t31, align 8
  invoke void @"System._UStrAsg"(ptr %t29, ptr %t32)
          to label %cont.10 unwind label %cleanup.pad
cont.10:
  invoke void @"System._DynArrayClr"(ptr %Result, ptr @"M.TInts.$rtti")
          to label %cont.11 unwind label %cleanup.pad
cont.11:
  br label %cleanup
cleanup:
  call void @"System._DynArrayClr"(ptr %names.addr, ptr @"M.TNames.$rtti")
  call void @"System._DynArrayClr"(ptr %$tmp1.addr, ptr @"M.TInts.$rtti")
  call void @"System._DynArrayClr"(ptr %$tmp3.addr, ptr @"M.TNames.$rtti")
  call void @"System._DynArrayClr"(ptr %$tmp5.addr, ptr @"M.TInts.$rtti")
  call void @"System._DynArrayClr"(ptr %$tmp6.addr, ptr @"M.TInts.$rtti")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._DynArrayClr"(ptr %names.addr, ptr @"M.TNames.$rtti") [ "funclet"(token %cl) ]
  call void @"System._DynArrayClr"(ptr %$tmp1.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
  call void @"System._DynArrayClr"(ptr %$tmp3.addr, ptr @"M.TNames.$rtti") [ "funclet"(token %cl) ]
  call void @"System._DynArrayClr"(ptr %$tmp5.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
  call void @"System._DynArrayClr"(ptr %$tmp6.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
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
