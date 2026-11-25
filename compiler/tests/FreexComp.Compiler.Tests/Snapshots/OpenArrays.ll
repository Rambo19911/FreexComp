; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 115, i16 116, i16 114, i16 105, i16 110, i16 103, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 33, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 120, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
$"System.string.$rtti" = comdat any
@"System.string.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i16, [6 x i8] } { i8 9, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.0, i32 0, i32 5), i16 1200, [6 x i8] zeroinitializer }, comdat, align 8
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.3, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
$"M.$rtti.array of Integer" = comdat any
@"M.$rtti.array of Integer" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr null, i64 4, ptr @"System.Integer.$rtti", ptr null }, comdat, align 8
$"M.$rtti.array[0..0] of string" = comdat any
@"M.$rtti.array[0..0] of string" = linkonce_odr constant { i8, [7 x i8], ptr, i64, i64, ptr } { i8 13, [7 x i8] zeroinitializer, ptr null, i64 8, i64 1, ptr @"System.string.$rtti" }, comdat, align 8

declare void @"System._AddRefArray"(ptr, ptr, i64)
declare void @"System._DynArrayClr"(ptr, ptr)
declare void @"System._Finalize"(ptr, ptr)
declare void @"System._FinalizeArray"(ptr, ptr, i64)
declare void @"System._FreeMem"(ptr)
declare ptr @"System._GetMem"(i64)
declare void @"System._RangeError"() #1
declare void @"System._UStrAsg"(ptr, ptr)
declare void @"System._UStrCat"(ptr, ptr, ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.memmove.p0.p0.i64(ptr, ptr, i64, i1)
declare void @llvm.memset.p0.i64(ptr, i8, i64, i1)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define i32 @"M.Sum"(ptr %a, i64 %a$high) #0 {
entry:
  %a.addr = alloca ptr, align 8
  %a$high.addr = alloca i64, align 8
  %Result.addr = alloca i32, align 4
  %i.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  store ptr %a, ptr %a.addr, align 8
  store i64 %a$high, ptr %a$high.addr, align 8
  store i32 0, ptr %Result.addr, align 4
  store i32 0, ptr %$tmp1.addr, align 4
  %t1 = load i64, ptr %a$high.addr, align 8
  %t2 = trunc i64 %t1 to i32
  store i32 %t2, ptr %$tmp2.addr, align 4
  %t3 = load i32, ptr %$tmp1.addr, align 4
  %t4 = load i32, ptr %$tmp2.addr, align 4
  %t5 = icmp sle i32 %t3, %t4
  br i1 %t5, label %if.then.1, label %if.end.1
if.then.1:
  %t6 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t6, ptr %i.addr, align 4
  br label %for.body.2
for.body.2:
  %t7 = load i32, ptr %Result.addr, align 4
  %t8 = load ptr, ptr %a.addr, align 8
  %t9 = load i32, ptr %i.addr, align 4
  %t10 = sext i32 %t9 to i64
  %t11 = getelementptr inbounds i32, ptr %t8, i64 %t10
  %t12 = load i32, ptr %t11, align 4
  %t13 = add i32 %t7, %t12
  store i32 %t13, ptr %Result.addr, align 4
  br label %for.inc.2
for.inc.2:
  %t14 = load i32, ptr %i.addr, align 4
  %t15 = load i32, ptr %$tmp2.addr, align 4
  %t16 = icmp eq i32 %t14, %t15
  br i1 %t16, label %for.end.2, label %if.end.3
if.end.3:
  %t17 = load i32, ptr %i.addr, align 4
  %t18 = add i32 %t17, 1
  store i32 %t18, ptr %i.addr, align 4
  br label %for.body.2
for.end.2:
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  %t19 = load i32, ptr %Result.addr, align 4
  ret i32 %t19
}

define void @"M.Bump"(ptr %s, i64 %s$high) #0 personality ptr @__C_specific_handler {
entry:
  %s.addr = alloca ptr, align 8
  %s$high.addr = alloca i64, align 8
  %$tmp1.addr = alloca i64, align 8
  %$tmp2.addr = alloca ptr, align 8
  %$tmp3.addr = alloca [4096 x i8], align 1
  store ptr %s, ptr %s.addr, align 8
  store i64 %s$high, ptr %s$high.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  %t1 = load i64, ptr %s$high.addr, align 8
  %t2 = add i64 %t1, 1
  %t3 = mul i64 %t2, 8
  store i64 %t3, ptr %$tmp1.addr, align 8
  %t4 = load ptr, ptr %s.addr, align 8
  store ptr %t4, ptr %$tmp2.addr, align 8
  %t5 = load i64, ptr %$tmp1.addr, align 8
  %t6 = icmp sle i64 %t5, 4096
  br i1 %t6, label %if.then.1, label %if.else.1
if.then.1:
  store ptr %$tmp3.addr, ptr %s.addr, align 8
  br label %if.end.1
if.else.1:
  %t7 = load i64, ptr %$tmp1.addr, align 8
  %t8 = invoke ptr @"System._GetMem"(i64 %t7)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  store ptr %t8, ptr %s.addr, align 8
  br label %if.end.1
if.end.1:
  %t9 = load ptr, ptr %$tmp2.addr, align 8
  %t10 = load ptr, ptr %s.addr, align 8
  %t11 = load i64, ptr %$tmp1.addr, align 8
  call void @llvm.memmove.p0.p0.i64(ptr align 1 %t10, ptr align 1 %t9, i64 %t11, i1 false)
  %t12 = load ptr, ptr %s.addr, align 8
  %t13 = load i64, ptr %s$high.addr, align 8
  %t14 = add i64 %t13, 1
  invoke void @"System._AddRefArray"(ptr %t12, ptr @"System.string.$rtti", i64 %t14)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t15 = load ptr, ptr %s.addr, align 8
  %t16 = load i64, ptr %s$high.addr, align 8
  %t17 = add i64 %t16, 1
  %t18 = icmp ult i64 0, %t17
  br i1 %t18, label %range.ok.2, label %range.error.2
range.error.2:
  invoke void @"System._RangeError"() #1
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  unreachable
range.ok.2:
  %t19 = getelementptr inbounds ptr, ptr %t15, i64 0
  %t20 = load ptr, ptr %s.addr, align 8
  %t21 = load i64, ptr %s$high.addr, align 8
  %t22 = add i64 %t21, 1
  %t23 = icmp ult i64 0, %t22
  br i1 %t23, label %range.ok.3, label %range.error.3
range.error.3:
  invoke void @"System._RangeError"() #1
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  unreachable
range.ok.3:
  %t24 = getelementptr inbounds ptr, ptr %t20, i64 0
  %t25 = load ptr, ptr %t24, align 8
  invoke void @"System._UStrCat"(ptr %t19, ptr %t25, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.1, i32 0, i32 5))
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  %t26 = load ptr, ptr %s.addr, align 8
  %t27 = load i64, ptr %s$high.addr, align 8
  %t28 = add i64 %t27, 1
  call void @"System._FinalizeArray"(ptr %t26, ptr @"System.string.$rtti", i64 %t28)
  %t29 = load i64, ptr %$tmp1.addr, align 8
  %t30 = icmp sgt i64 %t29, 4096
  br i1 %t30, label %if.then.4, label %if.end.4
if.then.4:
  %t31 = load ptr, ptr %s.addr, align 8
  call void @"System._FreeMem"(ptr %t31)
  br label %if.end.4
if.end.4:
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  %t32 = load ptr, ptr %s.addr, align 8
  %t33 = load i64, ptr %s$high.addr, align 8
  %t34 = add i64 %t33, 1
  call void @"System._FinalizeArray"(ptr %t32, ptr @"System.string.$rtti", i64 %t34) [ "funclet"(token %cl) ]
  %t35 = load i64, ptr %$tmp1.addr, align 8
  %t36 = icmp sgt i64 %t35, 4096
  br i1 %t36, label %if.then.5, label %if.end.5
if.then.5:
  %t37 = load ptr, ptr %s.addr, align 8
  call void @"System._FreeMem"(ptr %t37) [ "funclet"(token %cl) ]
  br label %if.end.5
if.end.5:
  cleanupret from %cl unwind to caller
}

define void @"M.Caller"() #0 personality ptr @__C_specific_handler {
entry:
  %f.addr = alloca [3 x i32], align 4
  %d.addr = alloca ptr, align 8
  %n.addr = alloca i32, align 4
  %$tmp1.addr = alloca [2 x i32], align 4
  %$tmp2.addr = alloca [1 x ptr], align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %d.addr, align 8
  call void @llvm.memset.p0.i64(ptr align 8 %$tmp2.addr, i8 0, i64 8, i1 false)
  %t1 = invoke i32 @"M.Sum"(ptr %f.addr, i64 2)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t2 = load ptr, ptr %d.addr, align 8
  %t3 = icmp eq ptr %t2, null
  br i1 %t3, label %len.end.1, label %len.load.1
len.load.1:
  %t4 = getelementptr inbounds i8, ptr %t2, i64 -8
  %t5 = load i64, ptr %t4, align 8
  br label %len.end.1
len.end.1:
  %t6 = phi i64 [ 0, %cont.2 ], [ %t5, %len.load.1 ]
  %t7 = sub i64 %t6, 1
  %t8 = invoke i32 @"M.Sum"(ptr %t2, i64 %t7)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t9 = add i32 %t1, %t8
  %t10 = getelementptr inbounds [2 x i32], ptr %$tmp1.addr, i64 0, i64 0
  store i32 1, ptr %t10, align 4
  %t11 = load i32, ptr %n.addr, align 4
  %t12 = getelementptr inbounds [2 x i32], ptr %$tmp1.addr, i64 0, i64 1
  store i32 %t11, ptr %t12, align 4
  %t13 = invoke i32 @"M.Sum"(ptr %$tmp1.addr, i64 1)
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  %t14 = add i32 %t9, %t13
  %t15 = sub i64 2, 1
  %t16 = invoke i32 @"M.Sum"(ptr %f.addr, i64 %t15)
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  %t17 = add i32 %t14, %t16
  %t18 = invoke i32 @"M.Sum"(ptr null, i64 -1)
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  %t19 = add i32 %t17, %t18
  store i32 %t19, ptr %n.addr, align 4
  %t20 = getelementptr inbounds [1 x ptr], ptr %$tmp2.addr, i64 0, i64 0
  invoke void @"System._UStrAsg"(ptr %t20, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.2, i32 0, i32 5))
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  invoke void @"M.Bump"(ptr %$tmp2.addr, i64 0)
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.9 unwind label %cleanup.pad
cont.9:
  call void @"System._DynArrayClr"(ptr %d.addr, ptr @"M.$rtti.array of Integer")
  call void @"System._Finalize"(ptr %$tmp2.addr, ptr @"M.$rtti.array[0..0] of string")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._DynArrayClr"(ptr %d.addr, ptr @"M.$rtti.array of Integer") [ "funclet"(token %cl) ]
  call void @"System._Finalize"(ptr %$tmp2.addr, ptr @"M.$rtti.array[0..0] of string") [ "funclet"(token %cl) ]
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
attributes #1 = { noreturn uwtable }

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
