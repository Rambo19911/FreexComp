; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.TNode" = type { i32, ptr }
%"M.TPacked" = type <{ i8, i32 }>

@"M.G" = global i32 zeroinitializer, align 4
@"M.E" = global [1 x i8] zeroinitializer, align 1
@"M.K" = global %"M.TPacked" zeroinitializer, align 1
@"M.CG" = constant ptr @"M.G", align 8
@"M.Table" = constant [2 x ptr] [ptr @"M.G", ptr null], align 8

declare ptr @"System._AllocZero"(i64)
declare void @"System._FreeMem"(ptr)
declare ptr @"System._GetMem"(i64)
declare void @llvm.memmove.p0.p0.i64(ptr, ptr, i64, i1)
declare void @llvm.memset.p0.i64(ptr, i8, i64, i1)

define void @"M.Bump"(ptr %V) #0 {
entry:
  %t1 = load i32, ptr %V, align 4
  %t2 = add i32 %t1, 1
  store i32 %t2, ptr %V, align 4
  br label %exit
exit:
  ret void
}

define void @"M.Use"() #0 {
entry:
  %p.addr = alloca ptr, align 8
  %q.addr = alloca ptr, align 8
  %n.addr = alloca ptr, align 8
  %c.addr = alloca ptr, align 8
  %d.addr = alloca i64, align 8
  %u.addr = alloca ptr, align 8
  %e2.addr = alloca [1 x i8], align 1
  %$tmp1.addr = alloca i32, align 4
  store ptr @"M.G", ptr %p.addr, align 8
  %t1 = load ptr, ptr %p.addr, align 8
  %t2 = load i32, ptr %t1, align 4
  %t3 = add i32 %t2, 1
  %t4 = load ptr, ptr %p.addr, align 8
  store i32 %t3, ptr %t4, align 4
  %t5 = call ptr @"System._AllocZero"(i64 16)
  store ptr %t5, ptr %n.addr, align 8
  %t6 = load ptr, ptr %n.addr, align 8
  %t7 = getelementptr inbounds %"M.TNode", ptr %t6, i32 0, i32 0
  store i32 1, ptr %t7, align 4
  %t8 = load ptr, ptr %n.addr, align 8
  %t9 = getelementptr inbounds %"M.TNode", ptr %t8, i32 0, i32 1
  store ptr null, ptr %t9, align 8
  %t10 = load ptr, ptr %n.addr, align 8
  call void @"System._FreeMem"(ptr %t10)
  %t11 = call ptr @"System._GetMem"(i64 16)
  store ptr %t11, ptr %u.addr, align 8
  %t12 = load ptr, ptr %u.addr, align 8
  call void @llvm.memset.p0.i64(ptr align 1 %t12, i8 0, i64 16, i1 false)
  %t13 = load ptr, ptr %u.addr, align 8
  call void @llvm.memmove.p0.p0.i64(ptr align 1 %t13, ptr align 4 @"M.G", i64 4, i1 false)
  %t14 = load ptr, ptr %u.addr, align 8
  call void @"System._FreeMem"(ptr %t14)
  %t15 = load ptr, ptr %c.addr, align 8
  %t16 = getelementptr inbounds i16, ptr %t15, i64 2
  store ptr %t16, ptr %c.addr, align 8
  %t17 = load ptr, ptr %c.addr, align 8
  %t18 = ptrtoint ptr %t17 to i64
  %t19 = load ptr, ptr %c.addr, align 8
  %t20 = ptrtoint ptr %t19 to i64
  %t21 = sub i64 %t18, %t20
  %t22 = sdiv i64 %t21, 2
  store i64 %t22, ptr %d.addr, align 8
  %t23 = load ptr, ptr %p.addr, align 8
  %t24 = getelementptr inbounds i32, ptr %t23, i64 -1
  store ptr %t24, ptr %q.addr, align 8
  %t25 = load ptr, ptr %q.addr, align 8
  %t26 = ptrtoint ptr %t25 to i64
  %t27 = load ptr, ptr %p.addr, align 8
  %t28 = ptrtoint ptr %t27 to i64
  %t29 = sub i64 %t26, %t28
  %t30 = sdiv i64 %t29, 4
  store i64 %t30, ptr %d.addr, align 8
  %t31 = load ptr, ptr %p.addr, align 8
  %t32 = getelementptr inbounds i32, ptr %t31, i64 3
  store ptr %t32, ptr %p.addr, align 8
  %t33 = load ptr, ptr %p.addr, align 8
  %t34 = icmp ne ptr %t33, null
  br i1 %t34, label %and.rhs.1, label %and.end.1
and.rhs.1:
  %t35 = load ptr, ptr %p.addr, align 8
  %t36 = load ptr, ptr @"M.CG", align 8
  %t37 = icmp ne ptr %t35, %t36
  br label %and.end.1
and.end.1:
  %t38 = phi i1 [ false, %entry ], [ %t37, %and.rhs.1 ]
  br i1 %t38, label %if.then.2, label %if.end.2
if.then.2:
  store ptr @"M.Bump", ptr %u.addr, align 8
  br label %if.end.2
if.end.2:
  %t39 = getelementptr inbounds %"M.TPacked", ptr @"M.K", i32 0, i32 1
  %t40 = load i32, ptr %t39, align 1
  store i32 %t40, ptr %$tmp1.addr, align 4
  call void @"M.Bump"(ptr %$tmp1.addr)
  %t41 = load i32, ptr %$tmp1.addr, align 4
  %t42 = getelementptr inbounds %"M.TPacked", ptr @"M.K", i32 0, i32 1
  store i32 %t41, ptr %t42, align 1
  store ptr @"M.E", ptr %u.addr, align 8
  store ptr %e2.addr, ptr %u.addr, align 8
  br label %exit
exit:
  ret void
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
