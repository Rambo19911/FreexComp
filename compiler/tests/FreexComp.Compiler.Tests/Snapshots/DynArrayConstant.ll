; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.TPair" = type { i32, ptr }

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 73, i16 110, i16 116, i16 115, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 97, i16 98, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 120, i16 0] }, align 16
@.dynarr.0 = private unnamed_addr constant <{ i32, i32, i64, [4 x i32] }> <{ i32 -1, i32 0, i64 4, [4 x i32] [i32 2, i32 3, i32 5, i32 7] }>, align 16
@.dynarr.1 = private unnamed_addr constant <{ i32, i32, i64, [1 x i32] }> <{ i32 -1, i32 0, i64 1, [1 x i32] [i32 1] }>, align 16
@.dynarr.2 = private unnamed_addr constant <{ i32, i32, i64, [2 x i32] }> <{ i32 -1, i32 0, i64 2, [2 x i32] [i32 2, i32 3] }>, align 16
@.dynarr.3 = private unnamed_addr constant <{ i32, i32, i64, [3 x ptr] }> <{ i32 -1, i32 0, i64 3, [3 x ptr] [ptr getelementptr inbounds (<{ i32, i32, i64, [1 x i32] }>, ptr @.dynarr.1, i32 0, i32 3), ptr getelementptr inbounds (<{ i32, i32, i64, [2 x i32] }>, ptr @.dynarr.2, i32 0, i32 3), ptr null] }>, align 16
@.dynarr.4 = private unnamed_addr constant <{ i32, i32, i64, [2 x ptr] }> <{ i32 -1, i32 0, i64 2, [2 x ptr] [ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.2, i32 0, i32 5), ptr null] }>, align 16
@.dynarr.5 = private unnamed_addr constant <{ i32, i32, i64, [2 x %"M.TPair"] }> <{ i32 -1, i32 0, i64 2, [2 x %"M.TPair"] [%"M.TPair" { i32 1, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.3, i32 0, i32 5) }, %"M.TPair" { i32 2, ptr zeroinitializer }] }>, align 16
@.dynarr.6 = private unnamed_addr constant <{ i32, i32, i64, [1 x double] }> <{ i32 -1, i32 0, i64 1, [1 x double] [double 0x3FF8000000000000] }>, align 16
@.dynarr.7 = private unnamed_addr constant <{ i32, i32, i64, [2 x i32] }> <{ i32 -1, i32 0, i64 2, [2 x i32] [i32 10, i32 20] }>, align 16
@.dynarr.8 = private unnamed_addr constant <{ i32, i32, i64, [1 x i32] }> <{ i32 -1, i32 0, i64 1, [1 x i32] [i32 4] }>, align 16
@.dynarr.9 = private unnamed_addr constant <{ i32, i32, i64, [2 x i32] }> <{ i32 -1, i32 0, i64 2, [2 x i32] [i32 5, i32 6] }>, align 16
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.1, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
$"M.TInts.$rtti" = comdat any
@"M.TInts.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.0, i32 0, i32 5), i64 4, ptr @"System.Integer.$rtti", ptr null }, comdat, align 8
@"M.Primes" = constant ptr getelementptr inbounds (<{ i32, i32, i64, [4 x i32] }>, ptr @.dynarr.0, i32 0, i32 3), align 8
@"M.None" = constant ptr null, align 8
@"M.Grid" = constant ptr getelementptr inbounds (<{ i32, i32, i64, [3 x ptr] }>, ptr @.dynarr.3, i32 0, i32 3), align 8
@"M.Names" = internal constant ptr getelementptr inbounds (<{ i32, i32, i64, [2 x ptr] }>, ptr @.dynarr.4, i32 0, i32 3), align 8
@"M.Pairs" = internal constant ptr getelementptr inbounds (<{ i32, i32, i64, [2 x %"M.TPair"] }>, ptr @.dynarr.5, i32 0, i32 3), align 8
@"M.Reals" = internal constant ptr getelementptr inbounds (<{ i32, i32, i64, [1 x double] }>, ptr @.dynarr.6, i32 0, i32 3), align 8
@"M.Second.Local" = internal constant ptr getelementptr inbounds (<{ i32, i32, i64, [2 x i32] }>, ptr @.dynarr.7, i32 0, i32 3), align 8
@"M.Second.C" = internal constant ptr getelementptr inbounds (<{ i32, i32, i64, [1 x i32] }>, ptr @.dynarr.8, i32 0, i32 3), align 8
@"M.Second.C.1" = internal constant ptr getelementptr inbounds (<{ i32, i32, i64, [2 x i32] }>, ptr @.dynarr.9, i32 0, i32 3), align 8

declare void @"System._DynArrayAsg"(ptr, ptr, ptr)
declare void @"System._DynArrayClr"(ptr, ptr)
declare i64 @"System._Trunc"(double)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define i32 @"M.Second"() #0 personality ptr @__C_specific_handler {
entry:
  %Result.addr = alloca i32, align 4
  %B.addr = alloca ptr, align 8
  %X.addr = alloca i32, align 4
  %$in1.addr = alloca ptr, align 8
  %$i2.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %B.addr, align 8
  store ptr null, ptr %$in1.addr, align 8
  %t1 = load ptr, ptr @"M.Primes", align 8
  invoke void @"System._DynArrayAsg"(ptr %B.addr, ptr %t1, ptr @"M.TInts.$rtti")
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t2 = load ptr, ptr @"M.Primes", align 8
  %t3 = getelementptr inbounds i32, ptr %t2, i64 1
  %t4 = load i32, ptr %t3, align 4
  %t6 = load ptr, ptr @"M.Grid", align 8
  %t7 = getelementptr inbounds ptr, ptr %t6, i64 1
  %t8 = load ptr, ptr %t7, align 8
  %t9 = icmp eq ptr %t8, null
  br i1 %t9, label %len.end.1, label %len.load.1
len.load.1:
  %t10 = getelementptr inbounds i8, ptr %t8, i64 -8
  %t11 = load i64, ptr %t10, align 8
  br label %len.end.1
len.end.1:
  %t12 = phi i64 [ 0, %cont.2 ], [ %t11, %len.load.1 ]
  %t5 = trunc i64 %t12 to i32
  %t13 = add i32 %t4, %t5
  %t14 = load ptr, ptr @"M.Second.Local", align 8
  %t15 = getelementptr inbounds i32, ptr %t14, i64 1
  %t16 = load i32, ptr %t15, align 4
  %t17 = add i32 %t13, %t16
  %t18 = load ptr, ptr @"M.Names", align 8
  %t19 = getelementptr inbounds ptr, ptr %t18, i64 0
  %t20 = load ptr, ptr %t19, align 8
  %t21 = icmp eq ptr %t20, null
  br i1 %t21, label %len.end.2, label %len.load.2
len.load.2:
  %t22 = getelementptr inbounds i8, ptr %t20, i64 -12
  %t23 = load i32, ptr %t22, align 4
  br label %len.end.2
len.end.2:
  %t24 = phi i32 [ 0, %len.end.1 ], [ %t23, %len.load.2 ]
  %t25 = add i32 %t17, %t24
  %t26 = load ptr, ptr @"M.Pairs", align 8
  %t27 = getelementptr inbounds %"M.TPair", ptr %t26, i64 0
  %t28 = getelementptr inbounds %"M.TPair", ptr %t27, i32 0, i32 0
  %t29 = load i32, ptr %t28, align 4
  %t30 = add i32 %t25, %t29
  %t31 = sext i32 %t30 to i64
  %t32 = load ptr, ptr @"M.Reals", align 8
  %t33 = getelementptr inbounds double, ptr %t32, i64 0
  %t34 = load double, ptr %t33, align 8
  %t35 = invoke i64 @"System._Trunc"(double %t34)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t36 = add i64 %t31, %t35
  %t37 = trunc i64 %t36 to i32
  store i32 %t37, ptr %Result.addr, align 4
  %t38 = load ptr, ptr @"M.Primes", align 8
  invoke void @"System._DynArrayAsg"(ptr %$in1.addr, ptr %t38, ptr @"M.TInts.$rtti")
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  store i32 0, ptr %$tmp1.addr, align 4
  %t40 = load ptr, ptr %$in1.addr, align 8
  %t41 = icmp eq ptr %t40, null
  br i1 %t41, label %len.end.3, label %len.load.3
len.load.3:
  %t42 = getelementptr inbounds i8, ptr %t40, i64 -8
  %t43 = load i64, ptr %t42, align 8
  br label %len.end.3
len.end.3:
  %t44 = phi i64 [ 0, %cont.4 ], [ %t43, %len.load.3 ]
  %t39 = trunc i64 %t44 to i32
  %t45 = sub i32 %t39, 1
  store i32 %t45, ptr %$tmp2.addr, align 4
  %t46 = load i32, ptr %$tmp1.addr, align 4
  %t47 = load i32, ptr %$tmp2.addr, align 4
  %t48 = icmp sle i32 %t46, %t47
  br i1 %t48, label %if.then.4, label %if.end.4
if.then.4:
  %t49 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t49, ptr %$i2.addr, align 4
  br label %for.body.5
for.body.5:
  %t50 = load ptr, ptr %$in1.addr, align 8
  %t51 = load i32, ptr %$i2.addr, align 4
  %t52 = sext i32 %t51 to i64
  %t53 = getelementptr inbounds i32, ptr %t50, i64 %t52
  %t54 = load i32, ptr %t53, align 4
  store i32 %t54, ptr %X.addr, align 4
  %t55 = load i32, ptr %Result.addr, align 4
  %t56 = load i32, ptr %X.addr, align 4
  %t57 = add i32 %t55, %t56
  store i32 %t57, ptr %Result.addr, align 4
  br label %for.inc.5
for.inc.5:
  %t58 = load i32, ptr %$i2.addr, align 4
  %t59 = load i32, ptr %$tmp2.addr, align 4
  %t60 = icmp eq i32 %t58, %t59
  br i1 %t60, label %for.end.5, label %if.end.6
if.end.6:
  %t61 = load i32, ptr %$i2.addr, align 4
  %t62 = add i32 %t61, 1
  store i32 %t62, ptr %$i2.addr, align 4
  br label %for.body.5
for.end.5:
  br label %if.end.4
if.end.4:
  %t63 = load i32, ptr %Result.addr, align 4
  %t64 = load ptr, ptr @"M.Second.C", align 8
  %t65 = getelementptr inbounds i32, ptr %t64, i64 0
  %t66 = load i32, ptr %t65, align 4
  %t67 = add i32 %t63, %t66
  store i32 %t67, ptr %Result.addr, align 4
  %t68 = load i32, ptr %Result.addr, align 4
  %t69 = load ptr, ptr @"M.Second.C.1", align 8
  %t70 = getelementptr inbounds i32, ptr %t69, i64 1
  %t71 = load i32, ptr %t70, align 4
  %t72 = add i32 %t68, %t71
  store i32 %t72, ptr %Result.addr, align 4
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  call void @"System._DynArrayClr"(ptr %B.addr, ptr @"M.TInts.$rtti")
  call void @"System._DynArrayClr"(ptr %$in1.addr, ptr @"M.TInts.$rtti")
  br label %exit
exit:
  %t73 = load i32, ptr %Result.addr, align 4
  ret i32 %t73
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._DynArrayClr"(ptr %B.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
  call void @"System._DynArrayClr"(ptr %$in1.addr, ptr @"M.TInts.$rtti") [ "funclet"(token %cl) ]
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
