; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

define i32 @"M.Count"(i32 %n) #0 {
entry:
  %n.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %n, ptr %n.addr, align 4
  store i32 0, ptr %Result.addr, align 4
  br label %while.cond.1
while.cond.1:
  %t1 = load i32, ptr %n.addr, align 4
  %t2 = icmp sgt i32 %t1, 0
  br i1 %t2, label %while.body.1, label %while.end.1
while.body.1:
  %t3 = load i32, ptr %n.addr, align 4
  %t4 = sub i32 %t3, 1
  store i32 %t4, ptr %n.addr, align 4
  %t5 = load i32, ptr %n.addr, align 4
  %t6 = icmp eq i32 %t5, 5
  br i1 %t6, label %while.cond.1, label %if.end.2
if.end.2:
  %t7 = load i32, ptr %n.addr, align 4
  %t8 = icmp eq i32 %t7, 2
  br i1 %t8, label %while.end.1, label %if.end.3
if.end.3:
  %t9 = load i32, ptr %Result.addr, align 4
  %t10 = add i32 %t9, 1
  store i32 %t10, ptr %Result.addr, align 4
  br label %while.cond.1
while.end.1:
  br label %exit
exit:
  %t11 = load i32, ptr %Result.addr, align 4
  ret i32 %t11
}

define i32 @"M.Digits"(i32 %n) #0 {
entry:
  %n.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %n, ptr %n.addr, align 4
  store i32 0, ptr %Result.addr, align 4
  br label %repeat.body.1
repeat.body.1:
  %t1 = load i32, ptr %Result.addr, align 4
  %t2 = add i32 %t1, 1
  store i32 %t2, ptr %Result.addr, align 4
  %t3 = load i32, ptr %n.addr, align 4
  %t4 = udiv i32 %t3, 10
  store i32 %t4, ptr %n.addr, align 4
  %t5 = load i32, ptr %Result.addr, align 4
  %t6 = icmp sgt i32 %t5, 20
  br i1 %t6, label %repeat.cond.1, label %if.end.2
if.end.2:
  br label %repeat.cond.1
repeat.cond.1:
  %t7 = load i32, ptr %n.addr, align 4
  %t8 = icmp eq i32 %t7, 0
  br i1 %t8, label %repeat.end.1, label %if.end.3
if.end.3:
  br label %repeat.body.1
repeat.end.1:
  br label %exit
exit:
  %t9 = load i32, ptr %Result.addr, align 4
  ret i32 %t9
}

attributes #0 = { uwtable "frame-pointer"="all" }
