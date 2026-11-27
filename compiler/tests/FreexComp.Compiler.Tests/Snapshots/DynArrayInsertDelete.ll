; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 73, i16 110, i16 116, i16 115, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 33, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 83, i16 116, i16 114, i16 115, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 115, i16 116, i16 114, i16 105, i16 110, i16 103, i16 0] }, align 16
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.1, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
$"M.TInts.$rtti" = comdat any
@"M.TInts.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.0, i32 0, i32 5), i64 4, ptr @"System.Integer.$rtti", ptr null }, comdat, align 8
$"System.string.$rtti" = comdat any
@"System.string.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i16, [6 x i8] } { i8 9, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.4, i32 0, i32 5), i16 1200, [6 x i8] zeroinitializer }, comdat, align 8
$"M.TStrs.$rtti" = comdat any
@"M.TStrs.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.3, i32 0, i32 5), i64 8, ptr @"System.string.$rtti", ptr @"System.string.$rtti" }, comdat, align 8

declare void @"System._DynArrayAsg"(ptr, ptr, ptr)
declare void @"System._DynArrayCat"(ptr, ptr, i64, ptr)
declare void @"System._DynArrayClr"(ptr, ptr)
declare void @"System._DynArrayDelete"(ptr, ptr, i64, i64)
declare void @"System._DynArrayInsert"(ptr, ptr, i64, ptr, i64)
declare void @"System._DynArraySetLen"(ptr, ptr, i64, ptr)
declare void @"System._UStrCat"(ptr, ptr, ptr)
declare void @"System._UStrClr"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.Edit"(ptr %A, ptr %B, i32 %I, i16 %S) #0 personality ptr @__C_specific_handler {
entry:
  %B.addr = alloca ptr, align 8
  %I.addr = alloca i32, align 4
  %S.addr = alloca i16, align 2
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca ptr, align 8
  %$tmp3.addr = alloca ptr, align 8
  %$tmp4.addr = alloca [1 x i64], align 8
  store ptr %B, ptr %B.addr, align 8
  store i32 %I, ptr %I.addr, align 4
  store i16 %S, ptr %S.addr, align 2
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %$tmp2.addr, align 8
  store ptr null, ptr %$tmp3.addr, align 8
  %t1 = load i32, ptr %I.addr, align 4
  store i32 %t1, ptr %$tmp1.addr, align 4
  %t2 = load i16, ptr %S.addr, align 2
  %t3 = sext i16 %t2 to i64
  invoke void @"System._DynArrayInsert"(ptr %A, ptr @"M.TInts.$rtti", i64 %t3, ptr %$tmp1.addr, i64 1)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t4 = load ptr, ptr %B.addr, align 8
  invoke void @"System._DynArrayAsg"(ptr %$tmp2.addr, ptr %t4, ptr @"M.TInts.$rtti")
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t5 = load ptr, ptr %$tmp2.addr, align 8
  %t7 = load ptr, ptr %$tmp2.addr, align 8
  %t8 = icmp eq ptr %t7, null
  br i1 %t8, label %len.end.1, label %len.load.1
len.load.1:
  %t9 = getelementptr inbounds i8, ptr %t7, i64 -8
  %t10 = load i64, ptr %t9, align 8
  br label %len.end.1
len.end.1:
  %t11 = phi i64 [ 0, %cont.3 ], [ %t10, %len.load.1 ]
  %t6 = trunc i64 %t11 to i32
  %t12 = sext i32 %t6 to i64
  invoke void @"System._DynArrayInsert"(ptr %A, ptr @"M.TInts.$rtti", i64 0, ptr %t5, i64 %t12)
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  invoke void @"System._DynArrayClr"(ptr %$tmp2.addr, ptr @"M.TInts.$rtti")
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  %t13 = getelementptr inbounds [1 x i64], ptr %$tmp4.addr, i64 0, i64 0
  store i64 2, ptr %t13, align 8
  invoke void @"System._DynArraySetLen"(ptr %$tmp3.addr, ptr @"M.TInts.$rtti", i64 1, ptr %$tmp4.addr)
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  %t14 = load i32, ptr %I.addr, align 4
  %t15 = load ptr, ptr %$tmp3.addr, align 8
  %t16 = getelementptr inbounds i32, ptr %t15, i64 0
  store i32 %t14, ptr %t16, align 4
  %t17 = load ptr, ptr %$tmp3.addr, align 8
  %t18 = getelementptr inbounds i32, ptr %t17, i64 1
  store i32 2, ptr %t18, align 4
  %t20 = load ptr, ptr %A, align 8
  %t21 = icmp eq ptr %t20, null
  br i1 %t21, label %len.end.2, label %len.load.2
len.load.2:
  %t22 = getelementptr inbounds i8, ptr %t20, i64 -8
  %t23 = load i64, ptr %t22, align 8
  br label %len.end.2
len.end.2:
  %t24 = phi i64 [ 0, %cont.6 ], [ %t23, %len.load.2 ]
  %t19 = trunc i64 %t24 to i32
  %t25 = sext i32 %t19 to i64
  %t26 = load ptr, ptr %$tmp3.addr, align 8
  %t28 = load ptr, ptr %$tmp3.addr, align 8
  %t29 = icmp eq ptr %t28, null
  br i1 %t29, label %len.end.3, label %len.load.3
len.load.3:
  %t30 = getelementptr inbounds i8, ptr %t28, i64 -8
  %t31 = load i64, ptr %t30, align 8
  br label %len.end.3
len.end.3:
  %t32 = phi i64 [ 0, %len.end.2 ], [ %t31, %len.load.3 ]
  %t27 = trunc i64 %t32 to i32
  %t33 = sext i32 %t27 to i64
  invoke void @"System._DynArrayInsert"(ptr %A, ptr @"M.TInts.$rtti", i64 %t25, ptr %t26, i64 %t33)
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  invoke void @"System._DynArrayClr"(ptr %$tmp3.addr, ptr @"M.TInts.$rtti")
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  invoke void @"System._DynArrayInsert"(ptr %A, ptr @"M.TInts.$rtti", i64 1, ptr null, i64 0)
          to label %cont.9 unwind label %cleanup.pad
cont.9:
  %t34 = load i32, ptr %I.addr, align 4
  %t35 = sext i32 %t34 to i64
  %t36 = load i16, ptr %S.addr, align 2
  %t37 = sext i16 %t36 to i64
  invoke void @"System._DynArrayDelete"(ptr %A, ptr @"M.TInts.$rtti", i64 %t35, i64 %t37)
          to label %cont.10 unwind label %cleanup.pad
cont.10:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.11 unwind label %cleanup.pad
cont.11:
  call void @"System._DynArrayClr"(ptr %$tmp2.addr, ptr @"M.TInts.$rtti")
  call void @"System._DynArrayClr"(ptr %$tmp3.addr, ptr @"M.TInts.$rtti")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._DynArrayClr"(ptr %$tmp2.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
  call void @"System._DynArrayClr"(ptr %$tmp3.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define void @"M.EditStrs"(ptr %A, ptr %W) #0 personality ptr @__C_specific_handler {
entry:
  %W.addr = alloca ptr, align 8
  %$tmp1.addr = alloca ptr, align 8
  %$tmp2.addr = alloca ptr, align 8
  store ptr %W, ptr %W.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %$tmp1.addr, align 8
  store ptr null, ptr %$tmp2.addr, align 8
  %t1 = load ptr, ptr %W.addr, align 8
  invoke void @"System._UStrCat"(ptr %$tmp1.addr, ptr %t1, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.2, i32 0, i32 5))
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  invoke void @"System._DynArrayInsert"(ptr %A, ptr @"M.TStrs.$rtti", i64 0, ptr %$tmp1.addr, i64 1)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  invoke void @"System._UStrClr"(ptr %$tmp1.addr)
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  %t2 = load ptr, ptr %A, align 8
  invoke void @"System._DynArrayAsg"(ptr %$tmp2.addr, ptr %t2, ptr @"M.TStrs.$rtti")
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  %t3 = load ptr, ptr %$tmp2.addr, align 8
  %t5 = load ptr, ptr %$tmp2.addr, align 8
  %t6 = icmp eq ptr %t5, null
  br i1 %t6, label %len.end.1, label %len.load.1
len.load.1:
  %t7 = getelementptr inbounds i8, ptr %t5, i64 -8
  %t8 = load i64, ptr %t7, align 8
  br label %len.end.1
len.end.1:
  %t9 = phi i64 [ 0, %cont.5 ], [ %t8, %len.load.1 ]
  %t4 = trunc i64 %t9 to i32
  %t10 = sext i32 %t4 to i64
  invoke void @"System._DynArrayInsert"(ptr %A, ptr @"M.TStrs.$rtti", i64 1, ptr %t3, i64 %t10)
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  invoke void @"System._DynArrayClr"(ptr %$tmp2.addr, ptr @"M.TStrs.$rtti")
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  invoke void @"System._DynArrayDelete"(ptr %A, ptr @"M.TStrs.$rtti", i64 0, i64 1)
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.9 unwind label %cleanup.pad
cont.9:
  call void @"System._UStrClr"(ptr %$tmp1.addr)
  call void @"System._DynArrayClr"(ptr %$tmp2.addr, ptr @"M.TStrs.$rtti")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %$tmp1.addr) [ "funclet"(token %cl) ]
  call void @"System._DynArrayClr"(ptr %$tmp2.addr, ptr @"M.TStrs.$rtti") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define void @"M.Copied"(ptr %Result, ptr %A) #0 {
entry:
  %A.addr = alloca ptr, align 8
  %$tmp1.addr = alloca [1 x ptr], align 8
  store ptr %A, ptr %A.addr, align 8
  %t1 = load ptr, ptr %A.addr, align 8
  %t2 = getelementptr inbounds [1 x ptr], ptr %$tmp1.addr, i64 0, i64 0
  store ptr %t1, ptr %t2, align 8
  call void @"System._DynArrayCat"(ptr %Result, ptr @"M.TInts.$rtti", i64 1, ptr %$tmp1.addr)
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
