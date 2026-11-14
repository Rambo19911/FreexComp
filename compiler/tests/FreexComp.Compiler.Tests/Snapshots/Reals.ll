; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@"M.G" = global float 0x3FB99999A0000000, align 4

define double @"M.Mix"(double %d, float %s, i32 %i, i32 %c, i64 %u) #0 {
entry:
  %d.addr = alloca double, align 8
  %s.addr = alloca float, align 4
  %i.addr = alloca i32, align 4
  %c.addr = alloca i32, align 4
  %u.addr = alloca i64, align 8
  %Result.addr = alloca double, align 8
  %t.addr = alloca float, align 4
  store double %d, ptr %d.addr, align 8
  store float %s, ptr %s.addr, align 4
  store i32 %i, ptr %i.addr, align 4
  store i32 %c, ptr %c.addr, align 4
  store i64 %u, ptr %u.addr, align 8
  %t1 = load float, ptr %s.addr, align 4
  %t2 = load float, ptr %s.addr, align 4
  %t3 = fmul float %t1, %t2
  %t4 = load float, ptr @"M.G", align 4
  %t5 = fsub float %t3, %t4
  store float %t5, ptr %t.addr, align 4
  %t6 = load double, ptr %d.addr, align 8
  %t7 = load i32, ptr %i.addr, align 4
  %t8 = sitofp i32 %t7 to double
  %t9 = fadd double %t6, %t8
  %t10 = load i32, ptr %c.addr, align 4
  %t11 = uitofp i32 %t10 to double
  %t12 = fmul double %t9, %t11
  %t13 = load i64, ptr %u.addr, align 8
  %t14 = uitofp i64 %t13 to double
  %t15 = fdiv double %t12, %t14
  %t16 = load float, ptr %t.addr, align 4
  %t17 = fpext float %t16 to double
  %t18 = fsub double %t15, %t17
  store double %t18, ptr %Result.addr, align 8
  %t19 = load double, ptr %Result.addr, align 8
  %t20 = fneg double %t19
  %t21 = load i32, ptr %i.addr, align 4
  %t22 = sitofp i32 %t21 to double
  %t23 = fdiv double %t22, 0x4000000000000000
  %t24 = fadd double %t20, %t23
  store double %t24, ptr %Result.addr, align 8
  %t25 = load double, ptr %Result.addr, align 8
  %t26 = fptrunc double %t25 to float
  store float %t26, ptr @"M.G", align 4
  br label %exit
exit:
  %t27 = load double, ptr %Result.addr, align 8
  ret double %t27
}

define float @"M.Scale"(float %s) #0 {
entry:
  %s.addr = alloca float, align 4
  %Result.addr = alloca float, align 4
  store float %s, ptr %s.addr, align 4
  %t1 = load float, ptr %s.addr, align 4
  %t2 = fmul float %t1, 0x4000000000000000
  %t3 = fpext float %t2 to double
  %t4 = fadd double %t3, 0x3FE0000000000000
  %t5 = fptrunc double %t4 to float
  store float %t5, ptr %Result.addr, align 4
  br label %exit
exit:
  %t6 = load float, ptr %Result.addr, align 4
  ret float %t6
}

define i32 @"M.Order"(double %a, double %b) #0 {
entry:
  %a.addr = alloca double, align 8
  %b.addr = alloca double, align 8
  %Result.addr = alloca i32, align 4
  store double %a, ptr %a.addr, align 8
  store double %b, ptr %b.addr, align 8
  store i32 0, ptr %Result.addr, align 4
  %t1 = load double, ptr %a.addr, align 8
  %t2 = load double, ptr %b.addr, align 8
  %t3 = fcmp oeq double %t1, %t2
  br i1 %t3, label %if.then.1, label %if.end.1
if.then.1:
  store i32 1, ptr %Result.addr, align 4
  br label %if.end.1
if.end.1:
  %t4 = load double, ptr %a.addr, align 8
  %t5 = load double, ptr %b.addr, align 8
  %t6 = fcmp une double %t4, %t5
  br i1 %t6, label %if.then.2, label %if.end.2
if.then.2:
  %t7 = load i32, ptr %Result.addr, align 4
  %t8 = add i32 %t7, 2
  store i32 %t8, ptr %Result.addr, align 4
  br label %if.end.2
if.end.2:
  %t9 = load double, ptr %a.addr, align 8
  %t10 = load double, ptr %b.addr, align 8
  %t11 = fcmp olt double %t9, %t10
  br i1 %t11, label %if.then.3, label %if.end.3
if.then.3:
  %t12 = load i32, ptr %Result.addr, align 4
  %t13 = add i32 %t12, 4
  store i32 %t13, ptr %Result.addr, align 4
  br label %if.end.3
if.end.3:
  %t14 = load double, ptr %a.addr, align 8
  %t15 = load double, ptr %b.addr, align 8
  %t16 = fcmp ole double %t14, %t15
  br i1 %t16, label %if.then.4, label %if.end.4
if.then.4:
  %t17 = load i32, ptr %Result.addr, align 4
  %t18 = add i32 %t17, 8
  store i32 %t18, ptr %Result.addr, align 4
  br label %if.end.4
if.end.4:
  %t19 = load double, ptr %a.addr, align 8
  %t20 = load double, ptr %b.addr, align 8
  %t21 = fcmp ogt double %t19, %t20
  br i1 %t21, label %if.then.5, label %if.end.5
if.then.5:
  %t22 = load i32, ptr %Result.addr, align 4
  %t23 = add i32 %t22, 16
  store i32 %t23, ptr %Result.addr, align 4
  br label %if.end.5
if.end.5:
  %t24 = load double, ptr %a.addr, align 8
  %t25 = load double, ptr %b.addr, align 8
  %t26 = fcmp oge double %t24, %t25
  br i1 %t26, label %if.then.6, label %if.end.6
if.then.6:
  %t27 = load i32, ptr %Result.addr, align 4
  %t28 = add i32 %t27, 32
  store i32 %t28, ptr %Result.addr, align 4
  br label %if.end.6
if.end.6:
  br label %exit
exit:
  %t29 = load i32, ptr %Result.addr, align 4
  ret i32 %t29
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
