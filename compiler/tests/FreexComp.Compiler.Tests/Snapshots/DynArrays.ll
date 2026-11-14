; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.TPoint" = type { i32, i32 }

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 73, i16 110, i16 116, i16 115, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 71, i16 114, i16 105, i16 100, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 115, i16 116, i16 114, i16 105, i16 110, i16 103, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 84, i16 78, i16 97, i16 109, i16 101, i16 115, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 84, i16 80, i16 111, i16 105, i16 110, i16 116, i16 115, i16 0] }, align 16
@.str.5 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 84, i16 80, i16 111, i16 105, i16 110, i16 116, i16 0] }, align 16
@.str.6 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 84, i16 80, i16 97, i16 105, i16 114, i16 115, i16 0] }, align 16
@.str.7 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 84, i16 65, i16 110, i16 115, i16 105, i16 115, i16 0] }, align 16
@.str.8 = private unnamed_addr constant { i32, i32, i16, i16, i32, [11 x i16] } { i32 -1, i32 10, i16 1200, i16 2, i32 0, [11 x i16] [i16 65, i16 110, i16 115, i16 105, i16 83, i16 116, i16 114, i16 105, i16 110, i16 103, i16 0] }, align 16
$"M.TInts.$rtti" = comdat any
@"M.TInts.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.0, i32 0, i32 5), i64 4, ptr null, ptr null }, comdat, align 8
$"M.TGrid.$rtti" = comdat any
@"M.TGrid.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.1, i32 0, i32 5), i64 8, ptr @"M.TInts.$rtti", ptr @"M.TInts.$rtti" }, comdat, align 8
$"System.string.$rtti" = comdat any
@"System.string.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i16, [6 x i8] } { i8 9, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.2, i32 0, i32 5), i16 1200, [6 x i8] zeroinitializer }, comdat, align 8
$"M.$rtti.array of string" = comdat any
@"M.$rtti.array of string" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr null, i64 8, ptr @"System.string.$rtti", ptr @"System.string.$rtti" }, comdat, align 8
$"M.TNames.$rtti" = comdat any
@"M.TNames.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.3, i32 0, i32 5), i64 8, ptr @"System.string.$rtti", ptr @"System.string.$rtti" }, comdat, align 8
$"M.TPoint.$rtti" = comdat any
@"M.TPoint.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, i64, ptr } { i8 12, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.5, i32 0, i32 5), i64 8, i64 0, ptr null }, comdat, align 8
$"M.TPoints.$rtti" = comdat any
@"M.TPoints.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.4, i32 0, i32 5), i64 8, ptr @"M.TPoint.$rtti", ptr null }, comdat, align 8
$"M.$rtti.array[0..1] of Byte" = comdat any
@"M.$rtti.array[0..1] of Byte" = linkonce_odr constant { i8, [7 x i8], ptr, i64, i64, ptr } { i8 13, [7 x i8] zeroinitializer, ptr null, i64 2, i64 2, ptr null }, comdat, align 8
$"M.TPairs.$rtti" = comdat any
@"M.TPairs.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.6, i32 0, i32 5), i64 2, ptr @"M.$rtti.array[0..1] of Byte", ptr null }, comdat, align 8
$"System.AnsiString.$rtti" = comdat any
@"System.AnsiString.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i16, [6 x i8] } { i8 10, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [11 x i16] }, ptr @.str.8, i32 0, i32 5), i16 -535, [6 x i8] zeroinitializer }, comdat, align 8
$"M.TAnsis.$rtti" = comdat any
@"M.TAnsis.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.7, i32 0, i32 5), i64 8, ptr @"System.AnsiString.$rtti", ptr @"System.AnsiString.$rtti" }, comdat, align 8

declare void @"System._DynArrayAsg"(ptr, ptr, ptr)
declare void @"System._DynArrayClr"(ptr, ptr)
declare void @"System._DynArrayCopy"(ptr, ptr, ptr, i64, i64)
declare void @"System._DynArraySetLen"(ptr, ptr, i64, ptr)
declare void @"System._RangeError"() #1
declare i32 @__C_specific_handler(...)

define void @"M.Build"(ptr %Result, i32 %n, ptr %names) #0 personality ptr @__C_specific_handler {
entry:
  %n.addr = alloca i32, align 4
  %names.addr = alloca ptr, align 8
  %g.addr = alloca ptr, align 8
  %rest.addr = alloca ptr, align 8
  %$tmp1.addr = alloca [1 x i64], align 8
  %$tmp2.addr = alloca [2 x i64], align 8
  %$tmp3.addr = alloca ptr, align 8
  store i32 %n, ptr %n.addr, align 4
  store ptr %names, ptr %names.addr, align 8
  store ptr null, ptr %g.addr, align 8
  store ptr null, ptr %rest.addr, align 8
  store ptr null, ptr %$tmp3.addr, align 8
  %t1 = load i32, ptr %n.addr, align 4
  %t2 = sext i32 %t1 to i64
  %t3 = getelementptr inbounds [1 x i64], ptr %$tmp1.addr, i64 0, i64 0
  store i64 %t2, ptr %t3, align 8
  invoke void @"System._DynArraySetLen"(ptr %Result, ptr @"M.TInts.$rtti", i64 1, ptr %$tmp1.addr)
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  %t4 = load i32, ptr %n.addr, align 4
  %t5 = sext i32 %t4 to i64
  %t6 = getelementptr inbounds [2 x i64], ptr %$tmp2.addr, i64 0, i64 0
  store i64 %t5, ptr %t6, align 8
  %t7 = getelementptr inbounds [2 x i64], ptr %$tmp2.addr, i64 0, i64 1
  store i64 2, ptr %t7, align 8
  invoke void @"System._DynArraySetLen"(ptr %g.addr, ptr @"M.TGrid.$rtti", i64 2, ptr %$tmp2.addr)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t8 = load ptr, ptr %g.addr, align 8
  %t9 = getelementptr inbounds ptr, ptr %t8, i64 0
  %t10 = load ptr, ptr %Result, align 8
  invoke void @"System._DynArrayAsg"(ptr %t9, ptr %t10, ptr @"M.TInts.$rtti")
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t11 = load ptr, ptr %names.addr, align 8
  %t12 = icmp eq ptr %t11, null
  br i1 %t12, label %len.end.1, label %len.load.1
len.load.1:
  %t13 = getelementptr inbounds i8, ptr %t11, i64 -8
  %t14 = load i64, ptr %t13, align 8
  br label %len.end.1
len.end.1:
  %t15 = phi i64 [ 0, %cont.3 ], [ %t14, %len.load.1 ]
  %t16 = icmp ult i64 0, %t15
  br i1 %t16, label %range.ok.2, label %range.error.2
range.error.2:
  invoke void @"System._RangeError"() #1
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  unreachable
range.ok.2:
  %t17 = getelementptr inbounds ptr, ptr %t11, i64 0
  %t18 = load ptr, ptr %t17, align 8
  %t19 = icmp eq ptr %t18, null
  br i1 %t19, label %len.end.3, label %len.load.3
len.load.3:
  %t20 = getelementptr inbounds i8, ptr %t18, i64 -12
  %t21 = load i32, ptr %t20, align 4
  br label %len.end.3
len.end.3:
  %t22 = phi i32 [ 0, %range.ok.2 ], [ %t21, %len.load.3 ]
  %t23 = load ptr, ptr %Result, align 8
  %t24 = load i32, ptr %n.addr, align 4
  %t25 = sub i32 %t24, 1
  %t26 = sext i32 %t25 to i64
  %t27 = icmp eq ptr %t23, null
  br i1 %t27, label %len.end.4, label %len.load.4
len.load.4:
  %t28 = getelementptr inbounds i8, ptr %t23, i64 -8
  %t29 = load i64, ptr %t28, align 8
  br label %len.end.4
len.end.4:
  %t30 = phi i64 [ 0, %len.end.3 ], [ %t29, %len.load.4 ]
  %t31 = icmp ult i64 %t26, %t30
  br i1 %t31, label %range.ok.5, label %range.error.5
range.error.5:
  invoke void @"System._RangeError"() #1
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  unreachable
range.ok.5:
  %t32 = getelementptr inbounds i32, ptr %t23, i64 %t26
  store i32 %t22, ptr %t32, align 4
  invoke void @"System._DynArrayClr"(ptr %rest.addr, ptr @"M.$rtti.array of string")
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  %t33 = load ptr, ptr %names.addr, align 8
  invoke void @"System._DynArrayCopy"(ptr %$tmp3.addr, ptr %t33, ptr @"M.TNames.$rtti", i64 1, i64 9223372036854775807)
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  %t34 = load ptr, ptr %$tmp3.addr, align 8
  %t35 = icmp ne ptr %t34, null
  br i1 %t35, label %if.then.6, label %if.end.6
if.then.6:
  invoke void @"System._DynArrayClr"(ptr %g.addr, ptr @"M.TGrid.$rtti")
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  br label %if.end.6
if.end.6:
  br label %cleanup
cleanup:
  call void @"System._DynArrayClr"(ptr %g.addr, ptr @"M.TGrid.$rtti")
  call void @"System._DynArrayClr"(ptr %rest.addr, ptr @"M.$rtti.array of string")
  call void @"System._DynArrayClr"(ptr %$tmp3.addr, ptr @"M.TNames.$rtti")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._DynArrayClr"(ptr %g.addr, ptr @"M.TGrid.$rtti") [ "funclet"(token %cl) ]
  call void @"System._DynArrayClr"(ptr %rest.addr, ptr @"M.$rtti.array of string") [ "funclet"(token %cl) ]
  call void @"System._DynArrayClr"(ptr %$tmp3.addr, ptr @"M.TNames.$rtti") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define void @"M.Kinds"(ptr %p, ptr %t, ptr %a) #0 {
entry:
  %$tmp1.addr = alloca [1 x i64], align 8
  %$tmp2.addr = alloca [1 x i64], align 8
  %$tmp3.addr = alloca [1 x i64], align 8
  %t1 = getelementptr inbounds [1 x i64], ptr %$tmp1.addr, i64 0, i64 0
  store i64 1, ptr %t1, align 8
  call void @"System._DynArraySetLen"(ptr %p, ptr @"M.TPoints.$rtti", i64 1, ptr %$tmp1.addr)
  %t2 = getelementptr inbounds [1 x i64], ptr %$tmp2.addr, i64 0, i64 0
  store i64 1, ptr %t2, align 8
  call void @"System._DynArraySetLen"(ptr %t, ptr @"M.TPairs.$rtti", i64 1, ptr %$tmp2.addr)
  %t3 = getelementptr inbounds [1 x i64], ptr %$tmp3.addr, i64 0, i64 0
  store i64 1, ptr %t3, align 8
  call void @"System._DynArraySetLen"(ptr %a, ptr @"M.TAnsis.$rtti", i64 1, ptr %$tmp3.addr)
  %t4 = load ptr, ptr %t, align 8
  %t5 = getelementptr inbounds [2 x i8], ptr %t4, i64 0
  %t6 = getelementptr inbounds [2 x i8], ptr %t5, i64 0, i64 1
  %t7 = load i8, ptr %t6, align 1
  %t8 = zext i8 %t7 to i32
  %t9 = load ptr, ptr %p, align 8
  %t10 = getelementptr inbounds %"M.TPoint", ptr %t9, i64 0
  %t11 = getelementptr inbounds %"M.TPoint", ptr %t10, i32 0, i32 1
  store i32 %t8, ptr %t11, align 4
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
