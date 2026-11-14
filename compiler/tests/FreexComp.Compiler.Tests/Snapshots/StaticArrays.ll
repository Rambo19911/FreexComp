; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@"M.G" = global [3 x [10 x i8]] zeroinitializer, align 1

declare void @"System._RangeError"() #1
declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)
declare void @llvm.memmove.p0.p0.i64(ptr, ptr, i64, i1)

define void @"M.Fill"(ptr %v, i32 %x) #0 {
entry:
  %x.addr = alloca i32, align 4
  %i.addr = alloca i32, align 4
  %local.addr = alloca [4 x i32], align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  store i32 %x, ptr %x.addr, align 4
  store i32 1, ptr %$tmp1.addr, align 4
  store i32 4, ptr %$tmp2.addr, align 4
  %t1 = load i32, ptr %$tmp1.addr, align 4
  %t2 = load i32, ptr %$tmp2.addr, align 4
  %t3 = icmp sle i32 %t1, %t2
  br i1 %t3, label %if.then.1, label %if.end.1
if.then.1:
  %t4 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t4, ptr %i.addr, align 4
  br label %for.body.2
for.body.2:
  %t5 = load i32, ptr %x.addr, align 4
  %t6 = load i32, ptr %i.addr, align 4
  %t7 = add i32 %t5, %t6
  %t8 = load i32, ptr %i.addr, align 4
  %t9 = sext i32 %t8 to i64
  %t10 = sub i64 %t9, 1
  %t11 = getelementptr inbounds [4 x i32], ptr %local.addr, i64 0, i64 %t10
  store i32 %t7, ptr %t11, align 4
  br label %for.inc.2
for.inc.2:
  %t12 = load i32, ptr %i.addr, align 4
  %t13 = load i32, ptr %$tmp2.addr, align 4
  %t14 = icmp eq i32 %t12, %t13
  br i1 %t14, label %for.end.2, label %if.end.3
if.end.3:
  %t15 = load i32, ptr %i.addr, align 4
  %t16 = add i32 %t15, 1
  store i32 %t16, ptr %i.addr, align 4
  br label %for.body.2
for.end.2:
  br label %if.end.1
if.end.1:
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %v, ptr align 4 %local.addr, i64 16, i1 false)
  %t17 = getelementptr inbounds [4 x i32], ptr %v, i64 0, i64 2
  %t18 = load i32, ptr %t17, align 4
  %t19 = getelementptr inbounds [4 x i32], ptr %v, i64 0, i64 1
  store i32 %t18, ptr %t19, align 4
  br label %exit
exit:
  ret void
}

define i32 @"M.Sum"(ptr %v) #0 {
entry:
  %Result.addr = alloca i32, align 4
  %i.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  store i32 0, ptr %Result.addr, align 4
  store i32 1, ptr %$tmp1.addr, align 4
  store i32 4, ptr %$tmp2.addr, align 4
  %t1 = load i32, ptr %$tmp1.addr, align 4
  %t2 = load i32, ptr %$tmp2.addr, align 4
  %t3 = icmp sle i32 %t1, %t2
  br i1 %t3, label %if.then.1, label %if.end.1
if.then.1:
  %t4 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t4, ptr %i.addr, align 4
  br label %for.body.2
for.body.2:
  %t5 = load i32, ptr %Result.addr, align 4
  %t6 = load i32, ptr %i.addr, align 4
  %t7 = sext i32 %t6 to i64
  %t8 = sub i64 %t7, 1
  %t9 = getelementptr inbounds [4 x i32], ptr %v, i64 0, i64 %t8
  %t10 = load i32, ptr %t9, align 4
  %t11 = add i32 %t5, %t10
  store i32 %t11, ptr %Result.addr, align 4
  br label %for.inc.2
for.inc.2:
  %t12 = load i32, ptr %i.addr, align 4
  %t13 = load i32, ptr %$tmp2.addr, align 4
  %t14 = icmp eq i32 %t12, %t13
  br i1 %t14, label %for.end.2, label %if.end.3
if.end.3:
  %t15 = load i32, ptr %i.addr, align 4
  %t16 = add i32 %t15, 1
  store i32 %t16, ptr %i.addr, align 4
  br label %for.body.2
for.end.2:
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  %t17 = load i32, ptr %Result.addr, align 4
  ret i32 %t17
}

define void @"M.Copy2"(ptr %a, ptr %b, ptr %p, ptr %q) #0 {
entry:
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %a, ptr align 4 %b, i64 16, i1 false)
  %t1 = load i32, ptr %q, align 2
  store i32 %t1, ptr %p, align 2
  br label %exit
exit:
  ret void
}

define void @"M.Grid"(i32 %i, i32 %j, i8 %c, i16 %ch, ptr %f, ptr %n) #0 {
entry:
  %i.addr = alloca i32, align 4
  %j.addr = alloca i32, align 4
  %c.addr = alloca i8, align 1
  %ch.addr = alloca i16, align 2
  store i32 %i, ptr %i.addr, align 4
  store i32 %j, ptr %j.addr, align 4
  store i8 %c, ptr %c.addr, align 1
  store i16 %ch, ptr %ch.addr, align 2
  %t1 = getelementptr inbounds [3 x [10 x i8]], ptr @"M.G", i64 0, i64 2
  %t2 = getelementptr inbounds [10 x i8], ptr %t1, i64 0, i64 9
  %t3 = load i8, ptr %t2, align 1
  %t4 = load i32, ptr %i.addr, align 4
  %t5 = sext i32 %t4 to i64
  %t6 = getelementptr inbounds [3 x [10 x i8]], ptr @"M.G", i64 0, i64 %t5
  %t7 = load i32, ptr %j.addr, align 4
  %t8 = sext i32 %t7 to i64
  %t9 = getelementptr inbounds [10 x i8], ptr %t6, i64 0, i64 %t8
  store i8 %t3, ptr %t9, align 1
  %t10 = load i32, ptr %j.addr, align 4
  %t11 = sext i32 %t10 to i64
  %t12 = getelementptr inbounds [3 x [10 x i8]], ptr @"M.G", i64 0, i64 %t11
  %t13 = load i32, ptr %i.addr, align 4
  %t14 = sext i32 %t13 to i64
  %t15 = getelementptr inbounds [3 x [10 x i8]], ptr @"M.G", i64 0, i64 %t14
  call void @llvm.memmove.p0.p0.i64(ptr align 1 %t15, ptr align 1 %t12, i64 10, i1 false)
  %t16 = getelementptr inbounds [3 x i8], ptr %f, i64 0, i64 2
  %t17 = load i8, ptr %t16, align 1
  %t18 = icmp ne i8 %t17, 0
  %t19 = xor i1 %t18, true
  %t20 = load i8, ptr %c.addr, align 1
  %t21 = zext i8 %t20 to i64
  %t22 = getelementptr inbounds [3 x i8], ptr %f, i64 0, i64 %t21
  %t23 = zext i1 %t19 to i8
  store i8 %t23, ptr %t22, align 1
  %t24 = load i16, ptr %ch.addr, align 2
  %t25 = zext i16 %t24 to i64
  %t26 = getelementptr inbounds [65536 x i16], ptr %n, i64 0, i64 %t25
  %t27 = load i16, ptr %t26, align 2
  %t28 = zext i16 %t27 to i32
  %t29 = add i32 %t28, 1
  %t30 = trunc i32 %t29 to i16
  %t31 = load i16, ptr %ch.addr, align 2
  %t32 = zext i16 %t31 to i64
  %t33 = getelementptr inbounds [65536 x i16], ptr %n, i64 0, i64 %t32
  store i16 %t30, ptr %t33, align 2
  %t34 = load i32, ptr %i.addr, align 4
  %t35 = icmp ule i32 %t34, 2
  br i1 %t35, label %range.ok.1, label %range.error.1
range.error.1:
  call void @"System._RangeError"() #1
  unreachable
range.ok.1:
  %t36 = sext i32 %t34 to i64
  %t37 = getelementptr inbounds [3 x [10 x i8]], ptr @"M.G", i64 0, i64 %t36
  %t38 = load i32, ptr %j.addr, align 4
  %t39 = icmp ule i32 %t38, 9
  br i1 %t39, label %range.ok.2, label %range.error.2
range.error.2:
  call void @"System._RangeError"() #1
  unreachable
range.ok.2:
  %t40 = sext i32 %t38 to i64
  %t41 = getelementptr inbounds [10 x i8], ptr %t37, i64 0, i64 %t40
  store i8 1, ptr %t41, align 1
  %t42 = load i8, ptr %c.addr, align 1
  %t43 = zext i8 %t42 to i64
  %t44 = getelementptr inbounds [3 x i8], ptr %f, i64 0, i64 %t43
  store i8 1, ptr %t44, align 1
  %t45 = load i16, ptr %ch.addr, align 2
  %t46 = zext i16 %t45 to i64
  %t47 = getelementptr inbounds [65536 x i16], ptr %n, i64 0, i64 %t46
  store i16 0, ptr %t47, align 2
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
