; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

define i8 @"M.Both"(i8 %a, i8 %b) #0 {
entry:
  %a.addr = alloca i8, align 1
  %b.addr = alloca i8, align 1
  %Result.addr = alloca i8, align 1
  store i8 %a, ptr %a.addr, align 1
  store i8 %b, ptr %b.addr, align 1
  %t1 = load i8, ptr %a.addr, align 1
  %t2 = icmp ne i8 %t1, 0
  br i1 %t2, label %and.rhs.1, label %and.end.1
and.rhs.1:
  %t3 = load i8, ptr %b.addr, align 1
  %t4 = icmp ne i8 %t3, 0
  br label %and.end.1
and.end.1:
  %t5 = phi i1 [ false, %entry ], [ %t4, %and.rhs.1 ]
  %t6 = zext i1 %t5 to i8
  store i8 %t6, ptr %Result.addr, align 1
  br label %exit
exit:
  %t7 = load i8, ptr %Result.addr, align 1
  ret i8 %t7
}

define i8 @"M.Either"(i8 %a, i32 %x) #0 {
entry:
  %a.addr = alloca i8, align 1
  %x.addr = alloca i32, align 4
  %Result.addr = alloca i8, align 1
  store i8 %a, ptr %a.addr, align 1
  store i32 %x, ptr %x.addr, align 4
  %t1 = load i8, ptr %a.addr, align 1
  %t2 = icmp ne i8 %t1, 0
  br i1 %t2, label %or.end.1, label %or.rhs.1
or.rhs.1:
  %t3 = load i32, ptr %x.addr, align 4
  %t4 = icmp sgt i32 %t3, 0
  br label %or.end.1
or.end.1:
  %t5 = phi i1 [ true, %entry ], [ %t4, %or.rhs.1 ]
  %t6 = zext i1 %t5 to i8
  store i8 %t6, ptr %Result.addr, align 1
  br label %exit
exit:
  %t7 = load i8, ptr %Result.addr, align 1
  ret i8 %t7
}

define internal i8 @"M.Mixed"(i8 %a, i8 %b, i8 %c) #0 {
entry:
  %a.addr = alloca i8, align 1
  %b.addr = alloca i8, align 1
  %c.addr = alloca i8, align 1
  %Result.addr = alloca i8, align 1
  store i8 %a, ptr %a.addr, align 1
  store i8 %b, ptr %b.addr, align 1
  store i8 %c, ptr %c.addr, align 1
  %t1 = load i8, ptr %a.addr, align 1
  %t2 = icmp ne i8 %t1, 0
  br i1 %t2, label %and.rhs.1, label %and.end.1
and.rhs.1:
  %t3 = load i8, ptr %b.addr, align 1
  %t4 = icmp ne i8 %t3, 0
  br label %and.end.1
and.end.1:
  %t5 = phi i1 [ false, %entry ], [ %t4, %and.rhs.1 ]
  br i1 %t5, label %or.end.2, label %or.rhs.2
or.rhs.2:
  %t6 = load i8, ptr %c.addr, align 1
  %t7 = icmp ne i8 %t6, 0
  %t8 = xor i1 %t7, true
  br label %or.end.2
or.end.2:
  %t9 = phi i1 [ true, %and.end.1 ], [ %t8, %or.rhs.2 ]
  %t10 = zext i1 %t9 to i8
  store i8 %t10, ptr %Result.addr, align 1
  br label %exit
exit:
  %t11 = load i8, ptr %Result.addr, align 1
  ret i8 %t11
}

define internal i8 @"M.Complete"(i8 %a, i8 %b) #0 {
entry:
  %a.addr = alloca i8, align 1
  %b.addr = alloca i8, align 1
  %Result.addr = alloca i8, align 1
  store i8 %a, ptr %a.addr, align 1
  store i8 %b, ptr %b.addr, align 1
  %t1 = load i8, ptr %a.addr, align 1
  %t2 = icmp ne i8 %t1, 0
  %t3 = load i8, ptr %b.addr, align 1
  %t4 = icmp ne i8 %t3, 0
  %t5 = and i1 %t2, %t4
  %t6 = load i8, ptr %a.addr, align 1
  %t7 = icmp ne i8 %t6, 0
  %t8 = load i8, ptr %b.addr, align 1
  %t9 = icmp ne i8 %t8, 0
  %t10 = xor i1 %t7, %t9
  %t11 = or i1 %t5, %t10
  %t12 = zext i1 %t11 to i8
  store i8 %t12, ptr %Result.addr, align 1
  br label %exit
exit:
  %t13 = load i8, ptr %Result.addr, align 1
  ret i8 %t13
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
