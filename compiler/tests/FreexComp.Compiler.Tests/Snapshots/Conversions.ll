; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@"M.Total" = global i64 zeroinitializer, align 8
@"M.Flag" = global i8 1, align 1
@"M.Count" = internal global i32 42, align 4

define void @"M.Mix"(i8 %b, ptr %w, i16 %c, float %s) #0 {
entry:
  %b.addr = alloca i8, align 1
  %c.addr = alloca i16, align 2
  %s.addr = alloca float, align 4
  %i.addr = alloca i32, align 4
  %d.addr = alloca double, align 8
  %ok.addr = alloca i8, align 1
  %lb.addr = alloca i32, align 4
  store i8 %b, ptr %b.addr, align 1
  store i16 %c, ptr %c.addr, align 2
  store float %s, ptr %s.addr, align 4
  %t1 = load i8, ptr %b.addr, align 1
  %t2 = zext i8 %t1 to i32
  %t3 = load i16, ptr %w, align 2
  %t4 = zext i16 %t3 to i32
  %t5 = add i32 %t2, %t4
  store i32 %t5, ptr %i.addr, align 4
  %t6 = load i32, ptr %i.addr, align 4
  %t7 = trunc i32 %t6 to i16
  store i16 %t7, ptr %w, align 2
  %t8 = load i32, ptr %i.addr, align 4
  %t9 = load i32, ptr @"M.Count", align 4
  %t10 = mul i32 %t8, %t9
  %t11 = sext i32 %t10 to i64
  store i64 %t11, ptr @"M.Total", align 8
  %t12 = load i16, ptr %c.addr, align 2
  %t13 = zext i16 %t12 to i32
  %t14 = shl i32 %t13, 3
  store i32 %t14, ptr %i.addr, align 4
  %t15 = load i32, ptr %i.addr, align 4
  %t16 = load i8, ptr %b.addr, align 1
  %t17 = zext i8 %t16 to i32
  %t18 = and i32 %t17, 31
  %t19 = lshr i32 %t15, %t18
  %t20 = sub i32 0, %t19
  %t21 = load i32, ptr %i.addr, align 4
  %t22 = xor i32 %t21, -1
  %t23 = xor i32 %t20, %t22
  store i32 %t23, ptr %i.addr, align 4
  %t24 = load float, ptr %s.addr, align 4
  %t25 = fpext float %t24 to double
  %t26 = load i32, ptr %i.addr, align 4
  %t27 = sitofp i32 %t26 to double
  %t28 = fadd double %t25, %t27
  store double %t28, ptr %d.addr, align 8
  %t29 = load double, ptr %d.addr, align 8
  %t30 = fptrunc double %t29 to float
  store float %t30, ptr %s.addr, align 4
  %t31 = load i32, ptr %i.addr, align 4
  %t32 = icmp slt i32 %t31, 0
  %t33 = load i8, ptr @"M.Flag", align 1
  %t34 = icmp ne i8 %t33, 0
  %t35 = icmp eq i1 %t32, %t34
  %t36 = zext i1 %t35 to i8
  store i8 %t36, ptr %ok.addr, align 1
  %t37 = load i8, ptr %ok.addr, align 1
  %t38 = icmp ne i8 %t37, 0
  %t39 = sext i1 %t38 to i32
  store i32 %t39, ptr %lb.addr, align 4
  %t40 = load i32, ptr %lb.addr, align 4
  %t41 = icmp ne i32 %t40, 0
  br i1 %t41, label %and.rhs.1, label %and.end.1
and.rhs.1:
  %t42 = load i8, ptr %ok.addr, align 1
  %t43 = icmp ne i8 %t42, 0
  %t44 = xor i1 %t43, true
  br label %and.end.1
and.end.1:
  %t45 = phi i1 [ false, %entry ], [ %t44, %and.rhs.1 ]
  %t46 = zext i1 %t45 to i8
  store i8 %t46, ptr @"M.Flag", align 1
  %t47 = load i8, ptr @"M.Flag", align 1
  %t48 = icmp ne i8 %t47, 0
  %t49 = zext i1 %t48 to i32
  %t50 = load i32, ptr %i.addr, align 4
  %t51 = icmp slt i32 %t50, 0
  %t52 = sub i32 0, %t50
  %t53 = select i1 %t51, i32 %t52, i32 %t50
  %t54 = add i32 %t49, %t53
  store i32 %t54, ptr @"M.Count", align 4
  br label %exit
exit:
  ret void
}

define i8 @"M.Less"(i64 %i, i64 %u) #0 {
entry:
  %i.addr = alloca i64, align 8
  %u.addr = alloca i64, align 8
  %Result.addr = alloca i8, align 1
  store i64 %i, ptr %i.addr, align 8
  store i64 %u, ptr %u.addr, align 8
  %t1 = load i64, ptr %i.addr, align 8
  %t2 = load i64, ptr %u.addr, align 8
  %t3 = icmp ult i64 %t1, %t2
  %t4 = icmp slt i64 %t1, 0
  %t5 = or i1 %t4, %t3
  br i1 %t5, label %and.rhs.1, label %and.end.1
and.rhs.1:
  %t6 = load i64, ptr %u.addr, align 8
  %t7 = load i64, ptr %i.addr, align 8
  %t8 = icmp ule i64 %t7, %t6
  %t9 = icmp slt i64 %t7, 0
  %t10 = or i1 %t9, %t8
  br label %and.end.1
and.end.1:
  %t11 = phi i1 [ false, %entry ], [ %t10, %and.rhs.1 ]
  br i1 %t11, label %or.end.2, label %or.rhs.2
or.rhs.2:
  %t12 = load i64, ptr %i.addr, align 8
  %t13 = load i64, ptr %u.addr, align 8
  %t14 = icmp eq i64 %t12, %t13
  %t15 = icmp sge i64 %t12, 0
  %t16 = and i1 %t15, %t14
  br label %or.end.2
or.end.2:
  %t17 = phi i1 [ true, %and.end.1 ], [ %t16, %or.rhs.2 ]
  %t18 = zext i1 %t17 to i8
  store i8 %t18, ptr %Result.addr, align 1
  br label %exit
exit:
  %t19 = load i8, ptr %Result.addr, align 1
  ret i8 %t19
}

attributes #0 = { uwtable "frame-pointer"="all" }
