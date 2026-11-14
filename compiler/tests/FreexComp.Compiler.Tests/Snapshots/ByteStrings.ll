; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i8] } { i32 -1, i32 5, i16 -535, i16 1, i32 0, [6 x i8] [i8 103, i8 114, i8 -61, i8 -68, i8 110, i8 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i8] } { i32 -1, i32 1, i16 -535, i16 1, i32 0, [2 x i8] [i8 120, i8 0] }, align 16
@.str.empty = private unnamed_addr constant [1 x i16] zeroinitializer, align 2
@.sstr.0 = private unnamed_addr constant [8 x i8] [i8 7, i8 97, i8 98, i8 99, i8 100, i8 101, i8 102, i8 103], align 1
@"System.Output" = external global [624 x i8], align 8

declare void @"System._AStrAsg"(ptr, ptr)
declare void @"System._AStrCat"(ptr, ptr, ptr)
declare void @"System._AStrClr"(ptr)
declare i32 @"System._AStrCmp"(ptr, ptr)
declare void @"System._AStrFromPCharLen"(ptr, ptr, i32, i16)
declare void @"System._AStrFromUStr"(ptr, ptr, i16)
declare void @"System._AStrUnique"(ptr)
declare void @"System._IOCheck"()
declare void @"System._SStrAsg"(ptr, i32, ptr)
declare i32 @"System._SStrCmp"(ptr, ptr)
declare void @"System._UStrClr"(ptr)
declare void @"System._UStrFromAStr"(ptr, ptr)
declare void @"System._UStrFromPWChar"(ptr, ptr)
declare void @"System._WriteAStr"(ptr, ptr, i32)
declare void @"System._WriteEnd"(ptr)
declare void @"System._WriteLn"(ptr)
declare void @"System._WritePWChar"(ptr, ptr, i32)
declare void @"System._WriteSStr"(ptr, ptr, i32)
declare void @"System._WriteUStr"(ptr, ptr, i32)

define i8 @"M.Ansi"(ptr %a, ptr %s, i8 %c) #0 {
entry:
  %s.addr = alloca ptr, align 8
  %c.addr = alloca i8, align 1
  %Result.addr = alloca i8, align 1
  %u.addr = alloca ptr, align 8
  %$tmp1.addr = alloca ptr, align 8
  store ptr %s, ptr %s.addr, align 8
  store i8 %c, ptr %c.addr, align 1
  store ptr null, ptr %u.addr, align 8
  store ptr null, ptr %$tmp1.addr, align 8
  call void @"System._AStrAsg"(ptr %a, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i8] }, ptr @.str.0, i32 0, i32 5))
  %t1 = load ptr, ptr %s.addr, align 8
  call void @"System._AStrFromUStr"(ptr %u.addr, ptr %t1, i16 -535)
  %t2 = load ptr, ptr %a, align 8
  %t3 = load ptr, ptr %u.addr, align 8
  call void @"System._AStrCat"(ptr %$tmp1.addr, ptr %t2, ptr %t3)
  %t4 = load ptr, ptr %$tmp1.addr, align 8
  call void @"System._AStrCat"(ptr %$tmp1.addr, ptr %t4, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i8] }, ptr @.str.1, i32 0, i32 5))
  %t5 = load ptr, ptr %$tmp1.addr, align 8
  call void @"System._AStrAsg"(ptr %a, ptr %t5)
  %t6 = load i8, ptr %c.addr, align 1
  call void @"System._AStrUnique"(ptr %a)
  %t7 = load ptr, ptr %a, align 8
  %t8 = getelementptr inbounds i8, ptr %t7, i64 0
  store i8 %t6, ptr %t8, align 1
  %t9 = load ptr, ptr %a, align 8
  %t10 = load ptr, ptr %u.addr, align 8
  %t11 = call i32 @"System._AStrCmp"(ptr %t9, ptr %t10)
  %t12 = icmp slt i32 %t11, 0
  br i1 %t12, label %or.end.1, label %or.rhs.1
or.rhs.1:
  %t13 = load ptr, ptr %a, align 8
  %t14 = icmp eq ptr %t13, null
  br label %or.end.1
or.end.1:
  %t15 = phi i1 [ true, %entry ], [ %t14, %or.rhs.1 ]
  %t16 = zext i1 %t15 to i8
  store i8 %t16, ptr %Result.addr, align 1
  %t17 = load ptr, ptr %a, align 8
  call void @"System._WriteAStr"(ptr @"System.Output", ptr %t17, i32 0)
  %t18 = load ptr, ptr %s.addr, align 8
  call void @"System._WriteUStr"(ptr @"System.Output", ptr %t18, i32 0)
  call void @"System._WriteLn"(ptr @"System.Output")
  call void @"System._WriteEnd"(ptr @"System.Output")
  call void @"System._IOCheck"()
  br label %cleanup
cleanup:
  call void @"System._AStrClr"(ptr %u.addr)
  call void @"System._AStrClr"(ptr %$tmp1.addr)
  br label %exit
exit:
  %t19 = load i8, ptr %Result.addr, align 1
  ret i8 %t19
}

define i32 @"M.Short"(ptr %n, ptr %h, i32 %i) #0 {
entry:
  %i.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %i, ptr %i.addr, align 4
  %t1 = load i64, ptr @.sstr.0, align 1
  store i64 %t1, ptr %n, align 1
  call void @"System._SStrAsg"(ptr %n, i32 7, ptr %h)
  %t2 = load i32, ptr %i.addr, align 4
  %t3 = sext i32 %t2 to i64
  %t4 = getelementptr inbounds [8 x i8], ptr %n, i64 0, i64 %t3
  store i8 113, ptr %t4, align 1
  %t5 = load i8, ptr %n, align 1
  %t6 = zext i8 %t5 to i32
  %t7 = getelementptr inbounds [256 x i8], ptr %h, i64 0, i64 0
  %t8 = load i8, ptr %t7, align 1
  %t9 = zext i8 %t8 to i32
  %t10 = add i32 %t6, %t9
  store i32 %t10, ptr %Result.addr, align 4
  %t11 = call i32 @"System._SStrCmp"(ptr %n, ptr %h)
  %t12 = icmp slt i32 %t11, 0
  br i1 %t12, label %if.then.1, label %if.end.1
if.then.1:
  call void @"System._WriteSStr"(ptr @"System.Output", ptr %n, i32 0)
  call void @"System._WriteLn"(ptr @"System.Output")
  call void @"System._WriteEnd"(ptr @"System.Output")
  call void @"System._IOCheck"()
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  %t13 = load i32, ptr %Result.addr, align 4
  ret i32 %t13
}

define ptr @"M.Data"(ptr %s, ptr %a, ptr %p, ptr %pa) #0 {
entry:
  %s.addr = alloca ptr, align 8
  %a.addr = alloca ptr, align 8
  %p.addr = alloca ptr, align 8
  %pa.addr = alloca ptr, align 8
  %Result.addr = alloca ptr, align 8
  %t.addr = alloca ptr, align 8
  %$tmp1.addr = alloca ptr, align 8
  %$tmp2.addr = alloca ptr, align 8
  %$tmp3.addr = alloca i32, align 4
  %$tmp4.addr = alloca ptr, align 8
  %$tmp5.addr = alloca ptr, align 8
  %$tmp6.addr = alloca i32, align 4
  store ptr %s, ptr %s.addr, align 8
  store ptr %a, ptr %a.addr, align 8
  store ptr %p, ptr %p.addr, align 8
  store ptr %pa, ptr %pa.addr, align 8
  store ptr null, ptr %t.addr, align 8
  store ptr null, ptr %$tmp1.addr, align 8
  store ptr null, ptr %$tmp4.addr, align 8
  %t1 = load ptr, ptr %p.addr, align 8
  call void @"System._UStrFromPWChar"(ptr %t.addr, ptr %t1)
  %t2 = load ptr, ptr %pa.addr, align 8
  store ptr %t2, ptr %$tmp2.addr, align 8
  store i32 0, ptr %$tmp3.addr, align 4
  %t3 = load ptr, ptr %$tmp2.addr, align 8
  %t4 = icmp ne ptr %t3, null
  br i1 %t4, label %if.then.1, label %if.end.1
if.then.1:
  br label %while.cond.2
while.cond.2:
  %t5 = load ptr, ptr %$tmp2.addr, align 8
  %t6 = load i32, ptr %$tmp3.addr, align 4
  %t7 = sext i32 %t6 to i64
  %t8 = getelementptr inbounds i8, ptr %t5, i64 %t7
  %t9 = load i8, ptr %t8, align 1
  %t10 = icmp ne i8 %t9, 0
  br i1 %t10, label %while.body.2, label %while.end.2
while.body.2:
  %t11 = load i32, ptr %$tmp3.addr, align 4
  %t12 = add i32 %t11, 1
  store i32 %t12, ptr %$tmp3.addr, align 4
  br label %while.cond.2
while.end.2:
  br label %if.end.1
if.end.1:
  %t13 = load ptr, ptr %$tmp2.addr, align 8
  %t14 = load i32, ptr %$tmp3.addr, align 4
  call void @"System._AStrFromPCharLen"(ptr %$tmp1.addr, ptr %t13, i32 %t14, i16 -535)
  %t15 = load ptr, ptr %$tmp1.addr, align 8
  call void @"System._UStrFromAStr"(ptr %t.addr, ptr %t15)
  %t16 = load ptr, ptr %p.addr, align 8
  call void @"System._WritePWChar"(ptr @"System.Output", ptr %t16, i32 0)
  %t17 = load ptr, ptr %pa.addr, align 8
  store ptr %t17, ptr %$tmp5.addr, align 8
  store i32 0, ptr %$tmp6.addr, align 4
  %t18 = load ptr, ptr %$tmp5.addr, align 8
  %t19 = icmp ne ptr %t18, null
  br i1 %t19, label %if.then.3, label %if.end.3
if.then.3:
  br label %while.cond.4
while.cond.4:
  %t20 = load ptr, ptr %$tmp5.addr, align 8
  %t21 = load i32, ptr %$tmp6.addr, align 4
  %t22 = sext i32 %t21 to i64
  %t23 = getelementptr inbounds i8, ptr %t20, i64 %t22
  %t24 = load i8, ptr %t23, align 1
  %t25 = icmp ne i8 %t24, 0
  br i1 %t25, label %while.body.4, label %while.end.4
while.body.4:
  %t26 = load i32, ptr %$tmp6.addr, align 4
  %t27 = add i32 %t26, 1
  store i32 %t27, ptr %$tmp6.addr, align 4
  br label %while.cond.4
while.end.4:
  br label %if.end.3
if.end.3:
  %t28 = load ptr, ptr %$tmp5.addr, align 8
  %t29 = load i32, ptr %$tmp6.addr, align 4
  call void @"System._AStrFromPCharLen"(ptr %$tmp4.addr, ptr %t28, i32 %t29, i16 -535)
  %t30 = load ptr, ptr %$tmp4.addr, align 8
  call void @"System._WriteAStr"(ptr @"System.Output", ptr %t30, i32 0)
  call void @"System._WriteLn"(ptr @"System.Output")
  call void @"System._WriteEnd"(ptr @"System.Output")
  call void @"System._IOCheck"()
  %t31 = load ptr, ptr %s.addr, align 8
  %t32 = icmp eq ptr %t31, null
  %t33 = select i1 %t32, ptr @.str.empty, ptr %t31
  store ptr %t33, ptr %Result.addr, align 8
  %t34 = load ptr, ptr %a.addr, align 8
  %t35 = icmp eq ptr %t34, null
  br i1 %t35, label %if.then.5, label %if.end.5
if.then.5:
  store ptr @.str.empty, ptr %Result.addr, align 8
  br label %if.end.5
if.end.5:
  br label %cleanup
cleanup:
  call void @"System._UStrClr"(ptr %t.addr)
  call void @"System._AStrClr"(ptr %$tmp1.addr)
  call void @"System._AStrClr"(ptr %$tmp4.addr)
  br label %exit
exit:
  %t36 = load ptr, ptr %Result.addr, align 8
  ret ptr %t36
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
