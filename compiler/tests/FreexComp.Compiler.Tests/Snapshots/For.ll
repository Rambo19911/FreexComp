; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

define i32 @"M.Sum"(i32 %n) #0 {
entry:
  %n.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  %i.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  store i32 %n, ptr %n.addr, align 4
  store i32 0, ptr %Result.addr, align 4
  store i32 1, ptr %$tmp1.addr, align 4
  %t1 = load i32, ptr %n.addr, align 4
  store i32 %t1, ptr %$tmp2.addr, align 4
  %t2 = load i32, ptr %$tmp1.addr, align 4
  %t3 = load i32, ptr %$tmp2.addr, align 4
  %t4 = icmp sle i32 %t2, %t3
  br i1 %t4, label %if.then.1, label %if.end.1
if.then.1:
  %t5 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t5, ptr %i.addr, align 4
  br label %for.body.2
for.body.2:
  %t6 = load i32, ptr %i.addr, align 4
  %t7 = icmp eq i32 %t6, 3
  br i1 %t7, label %for.inc.2, label %if.end.3
if.end.3:
  %t8 = load i32, ptr %i.addr, align 4
  %t9 = icmp sgt i32 %t8, 10
  br i1 %t9, label %for.end.2, label %if.end.4
if.end.4:
  %t10 = load i32, ptr %Result.addr, align 4
  %t11 = load i32, ptr %i.addr, align 4
  %t12 = add i32 %t10, %t11
  store i32 %t12, ptr %Result.addr, align 4
  br label %for.inc.2
for.inc.2:
  %t13 = load i32, ptr %i.addr, align 4
  %t14 = load i32, ptr %$tmp2.addr, align 4
  %t15 = icmp eq i32 %t13, %t14
  br i1 %t15, label %for.end.2, label %if.end.5
if.end.5:
  %t16 = load i32, ptr %i.addr, align 4
  %t17 = add i32 %t16, 1
  store i32 %t17, ptr %i.addr, align 4
  br label %for.body.2
for.end.2:
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  %t18 = load i32, ptr %Result.addr, align 4
  ret i32 %t18
}

define void @"M.Down"(ptr %b) #0 {
entry:
  %c.addr = alloca i8, align 1
  %$tmp1.addr = alloca i8, align 1
  %$tmp2.addr = alloca i8, align 1
  store i8 -1, ptr %$tmp1.addr, align 1
  %t1 = load i8, ptr %b, align 1
  store i8 %t1, ptr %$tmp2.addr, align 1
  %t2 = load i8, ptr %$tmp1.addr, align 1
  %t3 = load i8, ptr %$tmp2.addr, align 1
  %t4 = icmp uge i8 %t2, %t3
  br i1 %t4, label %if.then.1, label %if.end.1
if.then.1:
  %t5 = load i8, ptr %$tmp1.addr, align 1
  store i8 %t5, ptr %c.addr, align 1
  br label %for.body.2
for.body.2:
  %t6 = load i8, ptr %c.addr, align 1
  store i8 %t6, ptr %b, align 1
  br label %for.inc.2
for.inc.2:
  %t7 = load i8, ptr %c.addr, align 1
  %t8 = load i8, ptr %$tmp2.addr, align 1
  %t9 = icmp eq i8 %t7, %t8
  br i1 %t9, label %for.end.2, label %if.end.3
if.end.3:
  %t10 = load i8, ptr %c.addr, align 1
  %t11 = zext i8 %t10 to i32
  %t12 = sub i32 %t11, 1
  %t13 = trunc i32 %t12 to i8
  store i8 %t13, ptr %c.addr, align 1
  br label %for.body.2
for.end.2:
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  ret void
}

attributes #0 = { uwtable "frame-pointer"="all" }
