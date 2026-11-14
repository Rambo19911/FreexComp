; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@"System.Output" = external global [624 x i8], align 8

declare double @"System._Frac"(double)
declare void @"System._IOCheck"()
declare double @"System._Int"(double)
declare i64 @"System._Round"(double)
declare i64 @"System._Trunc"(double)
declare void @"System._WriteEnd"(ptr)
declare void @"System._WriteFloat"(ptr, double, i32, i32)
declare void @"System._WriteLn"(ptr)
declare double @llvm.sqrt.f64(double)

define void @"M.Show"(double %d, float %s, i32 %w) #0 {
entry:
  %d.addr = alloca double, align 8
  %s.addr = alloca float, align 4
  %w.addr = alloca i32, align 4
  store double %d, ptr %d.addr, align 8
  store float %s, ptr %s.addr, align 4
  store i32 %w, ptr %w.addr, align 4
  %t1 = load double, ptr %d.addr, align 8
  call void @"System._WriteFloat"(ptr @"System.Output", double %t1, i32 23, i32 -1)
  call void @"System._WriteEnd"(ptr @"System.Output")
  call void @"System._IOCheck"()
  %t2 = load float, ptr %s.addr, align 4
  %t3 = fpext float %t2 to double
  %t4 = load i32, ptr %w.addr, align 4
  call void @"System._WriteFloat"(ptr @"System.Output", double %t3, i32 %t4, i32 -1)
  call void @"System._WriteEnd"(ptr @"System.Output")
  call void @"System._IOCheck"()
  %t5 = load double, ptr %d.addr, align 8
  %t6 = load i32, ptr %w.addr, align 4
  call void @"System._WriteFloat"(ptr @"System.Output", double %t5, i32 %t6, i32 2)
  %t7 = load float, ptr %s.addr, align 4
  %t8 = fpext float %t7 to double
  %t9 = call double @llvm.sqrt.f64(double %t8)
  call void @"System._WriteFloat"(ptr @"System.Output", double %t9, i32 23, i32 -1)
  call void @"System._WriteLn"(ptr @"System.Output")
  call void @"System._WriteEnd"(ptr @"System.Output")
  call void @"System._IOCheck"()
  br label %exit
exit:
  ret void
}

define i64 @"M.Parts"(double %d, float %s, i32 %i) #0 {
entry:
  %d.addr = alloca double, align 8
  %s.addr = alloca float, align 4
  %i.addr = alloca i32, align 4
  %Result.addr = alloca i64, align 8
  %x.addr = alloca double, align 8
  store double %d, ptr %d.addr, align 8
  store float %s, ptr %s.addr, align 4
  store i32 %i, ptr %i.addr, align 4
  %t1 = load double, ptr %d.addr, align 8
  %t2 = call double @"System._Int"(double %t1)
  %t3 = load float, ptr %s.addr, align 4
  %t4 = fpext float %t3 to double
  %t5 = call double @"System._Frac"(double %t4)
  %t6 = fadd double %t2, %t5
  %t7 = load double, ptr %d.addr, align 8
  %t8 = call double @llvm.sqrt.f64(double %t7)
  %t9 = fadd double %t6, %t8
  %t10 = load i32, ptr %i.addr, align 4
  %t11 = sitofp i32 %t10 to double
  %t12 = call double @llvm.sqrt.f64(double %t11)
  %t13 = fadd double %t9, %t12
  store double %t13, ptr %x.addr, align 8
  %t14 = load double, ptr %x.addr, align 8
  %t15 = call i64 @"System._Trunc"(double %t14)
  %t16 = load float, ptr %s.addr, align 4
  %t17 = fpext float %t16 to double
  %t18 = call i64 @"System._Round"(double %t17)
  %t19 = add i64 %t15, %t18
  %t20 = add i64 %t19, 2
  %t21 = add i64 %t20, 2
  store i64 %t21, ptr %Result.addr, align 8
  br label %exit
exit:
  %t22 = load i64, ptr %Result.addr, align 8
  ret i64 %t22
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
