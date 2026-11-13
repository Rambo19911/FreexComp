; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

declare void @"System._DivZeroError"() #1

define i32 @"M.Quot"(i32 %a, i32 %b) #0 {
entry:
  %a.addr = alloca i32, align 4
  %b.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %a, ptr %a.addr, align 4
  store i32 %b, ptr %b.addr, align 4
  %t1 = load i32, ptr %a.addr, align 4
  %t2 = load i32, ptr %b.addr, align 4
  %t3 = icmp eq i32 %t2, 0
  br i1 %t3, label %div.zero.1, label %div.ok.1
div.zero.1:
  call void @"System._DivZeroError"() #1
  unreachable
div.ok.1:
  %t4 = icmp eq i32 %t2, -1
  br i1 %t4, label %div.neg.1, label %div.do.1
div.neg.1:
  %t5 = sub i32 0, %t1
  br label %div.end.1
div.do.1:
  %t6 = sdiv i32 %t1, %t2
  br label %div.end.1
div.end.1:
  %t7 = phi i32 [ %t5, %div.neg.1 ], [ %t6, %div.do.1 ]
  store i32 %t7, ptr %Result.addr, align 4
  br label %exit
exit:
  %t8 = load i32, ptr %Result.addr, align 4
  ret i32 %t8
}

define i64 @"M.Rem"(i64 %a, i64 %b) #0 {
entry:
  %a.addr = alloca i64, align 8
  %b.addr = alloca i64, align 8
  %Result.addr = alloca i64, align 8
  store i64 %a, ptr %a.addr, align 8
  store i64 %b, ptr %b.addr, align 8
  %t1 = load i64, ptr %a.addr, align 8
  %t2 = load i64, ptr %b.addr, align 8
  %t3 = icmp eq i64 %t2, 0
  br i1 %t3, label %div.zero.1, label %div.ok.1
div.zero.1:
  call void @"System._DivZeroError"() #1
  unreachable
div.ok.1:
  %t4 = icmp eq i64 %t2, -1
  br i1 %t4, label %div.neg.1, label %div.do.1
div.neg.1:
  br label %div.end.1
div.do.1:
  %t5 = srem i64 %t1, %t2
  br label %div.end.1
div.end.1:
  %t6 = phi i64 [ 0, %div.neg.1 ], [ %t5, %div.do.1 ]
  store i64 %t6, ptr %Result.addr, align 8
  br label %exit
exit:
  %t7 = load i64, ptr %Result.addr, align 8
  ret i64 %t7
}

define i32 @"M.UQuot"(i32 %a, i32 %b) #0 {
entry:
  %a.addr = alloca i32, align 4
  %b.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %a, ptr %a.addr, align 4
  store i32 %b, ptr %b.addr, align 4
  %t1 = load i32, ptr %a.addr, align 4
  %t2 = load i32, ptr %b.addr, align 4
  %t3 = icmp eq i32 %t2, 0
  br i1 %t3, label %div.zero.1, label %div.ok.1
div.zero.1:
  call void @"System._DivZeroError"() #1
  unreachable
div.ok.1:
  %t4 = udiv i32 %t1, %t2
  store i32 %t4, ptr %Result.addr, align 4
  br label %exit
exit:
  %t5 = load i32, ptr %Result.addr, align 4
  ret i32 %t5
}

define i32 @"M.ByConstants"(i32 %a) #0 {
entry:
  %a.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %a, ptr %a.addr, align 4
  %t1 = load i32, ptr %a.addr, align 4
  %t2 = sdiv i32 %t1, 7
  %t3 = load i32, ptr %a.addr, align 4
  %t4 = srem i32 %t3, 3
  %t5 = add i32 %t2, %t4
  %t6 = load i32, ptr %a.addr, align 4
  %t7 = sub i32 0, %t6
  %t8 = add i32 %t5, %t7
  %t9 = load i32, ptr %a.addr, align 4
  %t10 = add i32 %t8, 0
  store i32 %t10, ptr %Result.addr, align 4
  br label %exit
exit:
  %t11 = load i32, ptr %Result.addr, align 4
  ret i32 %t11
}

attributes #0 = { uwtable "frame-pointer"="all" }
attributes #1 = { noreturn uwtable }
