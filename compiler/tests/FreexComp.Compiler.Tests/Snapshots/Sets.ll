; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@"M.Primary" = global i8 5, align 1

declare void @"System._RangeError"() #1

define i8 @"M.Ops"(i8 %a, i8 %b) #0 {
entry:
  %a.addr = alloca i8, align 1
  %b.addr = alloca i8, align 1
  %Result.addr = alloca i8, align 1
  store i8 %a, ptr %a.addr, align 1
  store i8 %b, ptr %b.addr, align 1
  %t1 = load i8, ptr %a.addr, align 1
  %t2 = load i8, ptr %b.addr, align 1
  %t3 = or i8 %t1, %t2
  %t4 = and i8 %t3, 6
  %t5 = load i8, ptr %b.addr, align 1
  %t6 = xor i8 %t5, -1
  %t7 = and i8 %t4, %t6
  store i8 %t7, ptr %Result.addr, align 1
  br label %exit
exit:
  %t8 = load i8, ptr %Result.addr, align 1
  ret i8 %t8
}

define i8 @"M.Compare"(i8 %a, i8 %b) #0 {
entry:
  %a.addr = alloca i8, align 1
  %b.addr = alloca i8, align 1
  %Result.addr = alloca i8, align 1
  store i8 %a, ptr %a.addr, align 1
  store i8 %b, ptr %b.addr, align 1
  %t1 = load i8, ptr %a.addr, align 1
  %t2 = load i8, ptr %b.addr, align 1
  %t3 = icmp eq i8 %t1, %t2
  br i1 %t3, label %or.end.1, label %or.rhs.1
or.rhs.1:
  %t4 = load i8, ptr %a.addr, align 1
  %t5 = icmp ne i8 %t4, 0
  br i1 %t5, label %and.rhs.2, label %and.end.2
and.rhs.2:
  %t6 = load i8, ptr %a.addr, align 1
  %t7 = load i8, ptr %b.addr, align 1
  %t8 = xor i8 %t7, -1
  %t9 = and i8 %t6, %t8
  %t10 = icmp eq i8 %t9, 0
  br label %and.end.2
and.end.2:
  %t11 = phi i1 [ false, %or.rhs.1 ], [ %t10, %and.rhs.2 ]
  br i1 %t11, label %and.rhs.3, label %and.end.3
and.rhs.3:
  %t12 = load i8, ptr %b.addr, align 1
  %t13 = xor i8 %t12, -1
  %t14 = and i8 %t13, 1
  %t15 = icmp eq i8 %t14, 0
  br label %and.end.3
and.end.3:
  %t16 = phi i1 [ false, %and.end.2 ], [ %t15, %and.rhs.3 ]
  br label %or.end.1
or.end.1:
  %t17 = phi i1 [ true, %entry ], [ %t16, %and.end.3 ]
  %t18 = zext i1 %t17 to i8
  store i8 %t18, ptr %Result.addr, align 1
  br label %exit
exit:
  %t19 = load i8, ptr %Result.addr, align 1
  ret i8 %t19
}

define i8 @"M.Has"(i8 %c, i8 %s, i32 %i, i64 %w) #0 {
entry:
  %c.addr = alloca i8, align 1
  %s.addr = alloca i8, align 1
  %i.addr = alloca i32, align 4
  %w.addr = alloca i64, align 8
  %Result.addr = alloca i8, align 1
  store i8 %c, ptr %c.addr, align 1
  store i8 %s, ptr %s.addr, align 1
  store i32 %i, ptr %i.addr, align 4
  store i64 %w, ptr %w.addr, align 8
  %t1 = load i8, ptr %c.addr, align 1
  %t2 = zext i8 %t1 to i32
  %t3 = icmp ult i32 %t2, 8
  %t4 = load i8, ptr %s.addr, align 1
  %t5 = trunc i32 %t2 to i8
  %t6 = and i8 %t5, 7
  %t7 = lshr i8 %t4, %t6
  %t8 = and i8 %t7, 1
  %t9 = icmp ne i8 %t8, 0
  %t10 = and i1 %t3, %t9
  br i1 %t10, label %and.rhs.1, label %and.end.1
and.rhs.1:
  %t11 = load i32, ptr %i.addr, align 4
  %t12 = icmp ult i32 %t11, 64
  %t13 = load i64, ptr %w.addr, align 8
  %t14 = zext i32 %t11 to i64
  %t15 = and i64 %t14, 63
  %t16 = lshr i64 %t13, %t15
  %t17 = and i64 %t16, 1
  %t18 = icmp ne i64 %t17, 0
  %t19 = and i1 %t12, %t18
  br label %and.end.1
and.end.1:
  %t20 = phi i1 [ false, %entry ], [ %t19, %and.rhs.1 ]
  br i1 %t20, label %and.rhs.2, label %and.end.2
and.rhs.2:
  %t21 = load i8, ptr %s.addr, align 1
  %t22 = and i8 %t21, 4
  %t23 = icmp ne i8 %t22, 0
  br label %and.end.2
and.end.2:
  %t24 = phi i1 [ false, %and.end.1 ], [ %t23, %and.rhs.2 ]
  %t25 = zext i1 %t24 to i8
  store i8 %t25, ptr %Result.addr, align 1
  br label %exit
exit:
  %t26 = load i8, ptr %Result.addr, align 1
  ret i8 %t26
}

define void @"M.Change"(ptr %s, i32 %i) #0 {
entry:
  %i.addr = alloca i32, align 4
  store i32 %i, ptr %i.addr, align 4
  %t1 = load i32, ptr %i.addr, align 4
  %t2 = icmp ult i32 %t1, 8
  %t3 = trunc i32 %t1 to i8
  %t4 = and i8 %t3, 7
  %t5 = shl i8 1, %t4
  %t6 = select i1 %t2, i8 %t5, i8 0
  store i8 %t6, ptr %s, align 1
  %t7 = load i8, ptr %s, align 1
  %t8 = load i32, ptr %i.addr, align 4
  %t9 = icmp ult i32 %t8, 8
  %t10 = trunc i32 %t8 to i8
  %t11 = and i8 %t10, 7
  %t12 = shl i8 1, %t11
  %t13 = select i1 %t9, i8 %t12, i8 0
  %t14 = or i8 %t7, %t13
  store i8 %t14, ptr %s, align 1
  %t15 = load i8, ptr %s, align 1
  %t16 = and i8 %t15, -9
  store i8 %t16, ptr %s, align 1
  %t17 = load i32, ptr %i.addr, align 4
  %t18 = icmp ule i32 %t17, 7
  br i1 %t18, label %range.ok.1, label %range.error.1
range.error.1:
  call void @"System._RangeError"() #1
  unreachable
range.ok.1:
  %t19 = icmp ult i32 %t17, 8
  %t20 = trunc i32 %t17 to i8
  %t21 = and i8 %t20, 7
  %t22 = shl i8 1, %t21
  %t23 = select i1 %t19, i8 %t22, i8 0
  store i8 %t23, ptr %s, align 1
  br label %exit
exit:
  ret void
}

define i64 @"M.Range"(i32 %lo, i32 %hi) #0 {
entry:
  %lo.addr = alloca i32, align 4
  %hi.addr = alloca i32, align 4
  %Result.addr = alloca i64, align 8
  store i32 %lo, ptr %lo.addr, align 4
  store i32 %hi, ptr %hi.addr, align 4
  %t1 = load i32, ptr %lo.addr, align 4
  %t2 = sext i32 %t1 to i64
  %t3 = load i32, ptr %hi.addr, align 4
  %t4 = sext i32 %t3 to i64
  %t5 = icmp slt i64 %t2, 0
  %t6 = select i1 %t5, i64 0, i64 %t2
  %t7 = icmp sgt i64 %t4, 63
  %t8 = select i1 %t7, i64 63, i64 %t4
  %t9 = icmp sle i64 %t6, %t8
  %t10 = sub i64 %t8, %t6
  %t11 = sub i64 63, %t10
  %t12 = and i64 %t11, 63
  %t13 = lshr i64 -1, %t12
  %t14 = and i64 %t6, 63
  %t15 = shl i64 %t13, %t14
  %t16 = select i1 %t9, i64 %t15, i64 0
  store i64 %t16, ptr %Result.addr, align 8
  br label %exit
exit:
  %t17 = load i64, ptr %Result.addr, align 8
  ret i64 %t17
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
