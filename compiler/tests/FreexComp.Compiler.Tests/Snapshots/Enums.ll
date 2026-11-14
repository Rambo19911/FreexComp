; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@"M.Color" = global i8 2, align 1

declare void @"System._RangeError"() #1

define i8 @"M.Next"(i8 %C) #0 {
entry:
  %C.addr = alloca i8, align 1
  %Result.addr = alloca i8, align 1
  store i8 %C, ptr %C.addr, align 1
  %t1 = load i8, ptr %C.addr, align 1
  %t2 = zext i8 %t1 to i32
  switch i32 %t2, label %case.end.1 [
    i32 0, label %case.arm.1.1
    i32 1, label %case.arm.1.2
    i32 2, label %case.arm.1.2
  ]
case.arm.1.1:
  store i8 1, ptr %Result.addr, align 1
  br label %case.end.1
case.arm.1.2:
  store i8 1, ptr %Result.addr, align 1
  br label %case.end.1
case.end.1:
  %t3 = load i8, ptr %C.addr, align 1
  %t4 = icmp ult i8 %t3, 2
  br i1 %t4, label %if.then.2, label %if.end.2
if.then.2:
  %t5 = load i8, ptr %C.addr, align 1
  %t6 = zext i8 %t5 to i32
  %t7 = add i32 %t6, 1
  %t8 = trunc i32 %t7 to i8
  store i8 %t8, ptr %Result.addr, align 1
  br label %if.end.2
if.end.2:
  br label %exit
exit:
  %t9 = load i8, ptr %Result.addr, align 1
  ret i8 %t9
}

define i8 @"M.Bright"(i8 %C) #0 {
entry:
  %C.addr = alloca i8, align 1
  %Result.addr = alloca i8, align 1
  store i8 %C, ptr %C.addr, align 1
  %t1 = load i8, ptr %C.addr, align 1
  %t2 = sub i8 %t1, 1
  %t3 = icmp ule i8 %t2, 1
  br i1 %t3, label %range.ok.1, label %range.error.1
range.error.1:
  call void @"System._RangeError"() #1
  unreachable
range.ok.1:
  store i8 %t1, ptr %Result.addr, align 1
  %t4 = load i8, ptr %Result.addr, align 1
  %t5 = zext i8 %t4 to i32
  %t6 = add i32 %t5, 1
  %t7 = sub i32 %t6, 1
  %t8 = icmp ule i32 %t7, 1
  br i1 %t8, label %range.ok.2, label %range.error.2
range.error.2:
  call void @"System._RangeError"() #1
  unreachable
range.ok.2:
  %t9 = trunc i32 %t6 to i8
  store i8 %t9, ptr %Result.addr, align 1
  br label %exit
exit:
  %t10 = load i8, ptr %Result.addr, align 1
  ret i8 %t10
}

define i8 @"M.Percent"(i32 %I) #0 {
entry:
  %I.addr = alloca i32, align 4
  %Result.addr = alloca i8, align 1
  store i32 %I, ptr %I.addr, align 4
  %t1 = load i32, ptr %I.addr, align 4
  %t2 = icmp ule i32 %t1, 100
  br i1 %t2, label %range.ok.1, label %range.error.1
range.error.1:
  call void @"System._RangeError"() #1
  unreachable
range.ok.1:
  %t3 = trunc i32 %t1 to i8
  store i8 %t3, ptr %Result.addr, align 1
  br label %exit
exit:
  %t4 = load i8, ptr %Result.addr, align 1
  ret i8 %t4
}

define i32 @"M.Count"(i16 %W) #0 {
entry:
  %W.addr = alloca i16, align 2
  %Result.addr = alloca i32, align 4
  %X.addr = alloca i16, align 2
  %$tmp1.addr = alloca i16, align 2
  %$tmp2.addr = alloca i16, align 2
  store i16 %W, ptr %W.addr, align 2
  store i32 0, ptr %Result.addr, align 4
  store i16 0, ptr %$tmp1.addr, align 2
  %t1 = load i16, ptr %W.addr, align 2
  store i16 %t1, ptr %$tmp2.addr, align 2
  %t2 = load i16, ptr %$tmp1.addr, align 2
  %t3 = load i16, ptr %$tmp2.addr, align 2
  %t4 = icmp ule i16 %t2, %t3
  br i1 %t4, label %if.then.1, label %if.end.1
if.then.1:
  %t5 = load i16, ptr %$tmp1.addr, align 2
  store i16 %t5, ptr %X.addr, align 2
  br label %for.body.2
for.body.2:
  %t6 = load i32, ptr %Result.addr, align 4
  %t7 = load i16, ptr %X.addr, align 2
  %t8 = zext i16 %t7 to i32
  %t9 = add i32 %t6, %t8
  store i32 %t9, ptr %Result.addr, align 4
  br label %for.inc.2
for.inc.2:
  %t10 = load i16, ptr %X.addr, align 2
  %t11 = load i16, ptr %$tmp2.addr, align 2
  %t12 = icmp eq i16 %t10, %t11
  br i1 %t12, label %for.end.2, label %if.end.3
if.end.3:
  %t13 = load i16, ptr %X.addr, align 2
  %t14 = zext i16 %t13 to i32
  %t15 = add i32 %t14, 1
  %t16 = trunc i32 %t15 to i16
  store i16 %t16, ptr %X.addr, align 2
  br label %for.body.2
for.end.2:
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  %t17 = load i32, ptr %Result.addr, align 4
  ret i32 %t17
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
