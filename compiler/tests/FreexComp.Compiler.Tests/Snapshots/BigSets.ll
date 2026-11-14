; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.set.0 = private unnamed_addr constant [32 x i8] [i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 -2, i8 -1, i8 -1, i8 7, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0], align 1
@.set.1 = private unnamed_addr constant [32 x i8] zeroinitializer, align 1
@.set.2 = private unnamed_addr constant [32 x i8] [i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 1, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0], align 1
@"M.Vowels" = global [32 x i8] [i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 34, i8 -126, i8 32, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0], align 1

declare void @"System._RangeError"() #1
declare void @"System._SetDiff"(ptr, ptr, ptr)
declare i8 @"System._SetEq"(ptr, ptr)
declare void @"System._SetInter"(ptr, ptr, ptr)
declare i8 @"System._SetLe"(ptr, ptr)
declare void @"System._SetRange"(ptr, i32, i32)
declare void @"System._SetUnion"(ptr, ptr, ptr)
declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)
declare void @llvm.memset.p0.i64(ptr, i8, i64, i1)

define void @"M.Ops"(ptr %r, ptr %a, ptr %b) #0 {
entry:
  %$tmp1.addr = alloca [32 x i8], align 1
  %$tmp2.addr = alloca [32 x i8], align 1
  %$tmp3.addr = alloca [32 x i8], align 1
  call void @"System._SetUnion"(ptr %$tmp1.addr, ptr %a, ptr %b)
  call void @"System._SetInter"(ptr %$tmp2.addr, ptr %$tmp1.addr, ptr @.set.0)
  call void @"System._SetDiff"(ptr %$tmp3.addr, ptr %$tmp2.addr, ptr %b)
  call void @llvm.memcpy.p0.p0.i64(ptr align 1 %r, ptr align 1 %$tmp3.addr, i64 32, i1 false)
  call void @llvm.memcpy.p0.p0.i64(ptr align 1 %r, ptr align 1 @"M.Vowels", i64 32, i1 false)
  br label %exit
exit:
  ret void
}

define i8 @"M.Compare"(ptr %a, ptr %b) #0 {
entry:
  %Result.addr = alloca i8, align 1
  %t1 = call i8 @"System._SetEq"(ptr %a, ptr %b)
  %t2 = icmp ne i8 %t1, 0
  br i1 %t2, label %or.end.1, label %or.rhs.1
or.rhs.1:
  %t3 = call i8 @"System._SetEq"(ptr %a, ptr @.set.1)
  %t4 = icmp ne i8 %t3, 0
  %t5 = xor i1 %t4, true
  br i1 %t5, label %and.rhs.2, label %and.end.2
and.rhs.2:
  %t6 = call i8 @"System._SetLe"(ptr %a, ptr %b)
  %t7 = icmp ne i8 %t6, 0
  br label %and.end.2
and.end.2:
  %t8 = phi i1 [ false, %or.rhs.1 ], [ %t7, %and.rhs.2 ]
  br i1 %t8, label %and.rhs.3, label %and.end.3
and.rhs.3:
  %t9 = call i8 @"System._SetLe"(ptr @.set.2, ptr %b)
  %t10 = icmp ne i8 %t9, 0
  br label %and.end.3
and.end.3:
  %t11 = phi i1 [ false, %and.end.2 ], [ %t10, %and.rhs.3 ]
  br label %or.end.1
or.end.1:
  %t12 = phi i1 [ true, %entry ], [ %t11, %and.end.3 ]
  %t13 = zext i1 %t12 to i8
  store i8 %t13, ptr %Result.addr, align 1
  br label %exit
exit:
  %t14 = load i8, ptr %Result.addr, align 1
  ret i8 %t14
}

define i8 @"M.Has"(i16 %c, ptr %s, i32 %i, ptr %w) #0 {
entry:
  %c.addr = alloca i16, align 2
  %i.addr = alloca i32, align 4
  %Result.addr = alloca i8, align 1
  store i16 %c, ptr %c.addr, align 2
  store i32 %i, ptr %i.addr, align 4
  %t1 = load i16, ptr %c.addr, align 2
  %t2 = zext i16 %t1 to i32
  %t3 = icmp ult i32 %t2, 256
  br i1 %t3, label %and.rhs.1, label %and.end.1
and.rhs.1:
  %t4 = lshr i32 %t2, 3
  %t5 = zext i32 %t4 to i64
  %t6 = getelementptr inbounds i8, ptr %s, i64 %t5
  %t7 = and i32 %t2, 7
  %t8 = trunc i32 %t7 to i8
  %t9 = load i8, ptr %t6, align 1
  %t10 = lshr i8 %t9, %t8
  %t11 = and i8 %t10, 1
  %t12 = icmp ne i8 %t11, 0
  br label %and.end.1
and.end.1:
  %t13 = phi i1 [ false, %entry ], [ %t12, %and.rhs.1 ]
  br i1 %t13, label %and.rhs.2, label %and.end.2
and.rhs.2:
  %t14 = load i32, ptr %i.addr, align 4
  %t15 = icmp ult i32 %t14, 256
  br i1 %t15, label %and.rhs.3, label %and.end.3
and.rhs.3:
  %t16 = lshr i32 %t14, 3
  %t17 = zext i32 %t16 to i64
  %t18 = getelementptr inbounds i8, ptr %w, i64 %t17
  %t19 = and i32 %t14, 7
  %t20 = trunc i32 %t19 to i8
  %t21 = load i8, ptr %t18, align 1
  %t22 = lshr i8 %t21, %t20
  %t23 = and i8 %t22, 1
  %t24 = icmp ne i8 %t23, 0
  br label %and.end.3
and.end.3:
  %t25 = phi i1 [ false, %and.rhs.2 ], [ %t24, %and.rhs.3 ]
  br label %and.end.2
and.end.2:
  %t26 = phi i1 [ false, %and.end.1 ], [ %t25, %and.end.3 ]
  br i1 %t26, label %and.rhs.4, label %and.end.4
and.rhs.4:
  %t27 = getelementptr inbounds i8, ptr %s, i64 14
  %t28 = load i8, ptr %t27, align 1
  %t29 = lshr i8 %t28, 1
  %t30 = and i8 %t29, 1
  %t31 = icmp ne i8 %t30, 0
  br label %and.end.4
and.end.4:
  %t32 = phi i1 [ false, %and.end.2 ], [ %t31, %and.rhs.4 ]
  br i1 %t32, label %and.rhs.5, label %and.end.5
and.rhs.5:
  br label %and.end.5
and.end.5:
  %t33 = phi i1 [ false, %and.end.4 ], [ false, %and.rhs.5 ]
  %t34 = zext i1 %t33 to i8
  store i8 %t34, ptr %Result.addr, align 1
  br label %exit
exit:
  %t35 = load i8, ptr %Result.addr, align 1
  ret i8 %t35
}

define void @"M.Change"(ptr %s, i16 %c, i16 %lo, i16 %hi) #0 {
entry:
  %c.addr = alloca i16, align 2
  %lo.addr = alloca i16, align 2
  %hi.addr = alloca i16, align 2
  %$tmp1.addr = alloca [32 x i8], align 1
  %$tmp2.addr = alloca [32 x i8], align 1
  store i16 %c, ptr %c.addr, align 2
  store i16 %lo, ptr %lo.addr, align 2
  store i16 %hi, ptr %hi.addr, align 2
  call void @llvm.memset.p0.i64(ptr align 1 %$tmp1.addr, i8 0, i64 32, i1 false)
  %t1 = load i16, ptr %c.addr, align 2
  %t2 = zext i16 %t1 to i32
  %t3 = icmp ult i32 %t2, 256
  br i1 %t3, label %if.then.1, label %if.end.1
if.then.1:
  %t4 = lshr i32 %t2, 3
  %t5 = zext i32 %t4 to i64
  %t6 = getelementptr inbounds i8, ptr %$tmp1.addr, i64 %t5
  %t7 = and i32 %t2, 7
  %t8 = trunc i32 %t7 to i8
  %t9 = load i8, ptr %t6, align 1
  %t10 = shl i8 1, %t8
  %t11 = or i8 %t9, %t10
  store i8 %t11, ptr %t6, align 1
  br label %if.end.1
if.end.1:
  %t12 = load i16, ptr %lo.addr, align 2
  %t13 = zext i16 %t12 to i32
  %t14 = sext i32 %t13 to i64
  %t15 = icmp slt i64 %t14, 0
  %t17 = icmp sgt i64 %t14, 256
  %t18 = select i1 %t17, i64 256, i64 %t14
  %t16 = select i1 %t15, i64 0, i64 %t18
  %t19 = trunc i64 %t16 to i32
  %t20 = load i16, ptr %hi.addr, align 2
  %t21 = zext i16 %t20 to i32
  %t22 = sext i32 %t21 to i64
  %t23 = icmp slt i64 %t22, -1
  %t25 = icmp sgt i64 %t22, 255
  %t26 = select i1 %t25, i64 255, i64 %t22
  %t24 = select i1 %t23, i64 -1, i64 %t26
  %t27 = trunc i64 %t24 to i32
  call void @"System._SetRange"(ptr %$tmp1.addr, i32 %t19, i32 %t27)
  %t28 = getelementptr inbounds i8, ptr %$tmp1.addr, i64 15
  %t29 = load i8, ptr %t28, align 1
  %t30 = or i8 %t29, 4
  store i8 %t30, ptr %t28, align 1
  call void @llvm.memcpy.p0.p0.i64(ptr align 1 %s, ptr align 1 %$tmp1.addr, i64 32, i1 false)
  %t31 = load i16, ptr %c.addr, align 2
  %t32 = zext i16 %t31 to i32
  %t33 = icmp ult i32 %t32, 256
  br i1 %t33, label %if.then.2, label %if.end.2
if.then.2:
  %t34 = lshr i32 %t32, 3
  %t35 = zext i32 %t34 to i64
  %t36 = getelementptr inbounds i8, ptr %s, i64 %t35
  %t37 = and i32 %t32, 7
  %t38 = trunc i32 %t37 to i8
  %t39 = load i8, ptr %t36, align 1
  %t40 = shl i8 1, %t38
  %t41 = or i8 %t39, %t40
  store i8 %t41, ptr %t36, align 1
  br label %if.end.2
if.end.2:
  %t42 = getelementptr inbounds i8, ptr %s, i64 12
  %t43 = load i8, ptr %t42, align 1
  %t44 = and i8 %t43, -3
  store i8 %t44, ptr %t42, align 1
  call void @llvm.memset.p0.i64(ptr align 1 %$tmp2.addr, i8 0, i64 32, i1 false)
  %t45 = load i16, ptr %c.addr, align 2
  %t46 = zext i16 %t45 to i32
  %t47 = icmp ule i32 %t46, 255
  br i1 %t47, label %range.ok.3, label %range.error.3
range.error.3:
  call void @"System._RangeError"() #1
  unreachable
range.ok.3:
  %t48 = lshr i32 %t46, 3
  %t49 = zext i32 %t48 to i64
  %t50 = getelementptr inbounds i8, ptr %$tmp2.addr, i64 %t49
  %t51 = and i32 %t46, 7
  %t52 = trunc i32 %t51 to i8
  %t53 = load i8, ptr %t50, align 1
  %t54 = shl i8 1, %t52
  %t55 = or i8 %t53, %t54
  store i8 %t55, ptr %t50, align 1
  %t56 = load i16, ptr %lo.addr, align 2
  %t57 = zext i16 %t56 to i32
  %t58 = icmp ule i32 %t57, 255
  br i1 %t58, label %range.ok.4, label %range.error.4
range.error.4:
  call void @"System._RangeError"() #1
  unreachable
range.ok.4:
  %t59 = load i16, ptr %hi.addr, align 2
  %t60 = zext i16 %t59 to i32
  %t61 = icmp ule i32 %t60, 255
  br i1 %t61, label %range.ok.5, label %range.error.5
range.error.5:
  call void @"System._RangeError"() #1
  unreachable
range.ok.5:
  call void @"System._SetRange"(ptr %$tmp2.addr, i32 %t57, i32 %t60)
  call void @llvm.memcpy.p0.p0.i64(ptr align 1 %s, ptr align 1 %$tmp2.addr, i64 32, i1 false)
  %t62 = load i16, ptr %c.addr, align 2
  %t63 = zext i16 %t62 to i32
  %t64 = icmp ule i32 %t63, 255
  br i1 %t64, label %range.ok.6, label %range.error.6
range.error.6:
  call void @"System._RangeError"() #1
  unreachable
range.ok.6:
  %t65 = lshr i32 %t63, 3
  %t66 = zext i32 %t65 to i64
  %t67 = getelementptr inbounds i8, ptr %s, i64 %t66
  %t68 = and i32 %t63, 7
  %t69 = trunc i32 %t68 to i8
  %t70 = load i8, ptr %t67, align 1
  %t71 = shl i8 1, %t69
  %t72 = or i8 %t70, %t71
  store i8 %t72, ptr %t67, align 1
  br label %exit
exit:
  ret void
}

define void @"M.Mixed"(ptr %b, ptr %d) #0 {
entry:
  %$tmp1.addr = alloca [32 x i8], align 1
  %$tmp2.addr = alloca [32 x i8], align 1
  %$tmp3.addr = alloca [32 x i8], align 1
  call void @llvm.memset.p0.i64(ptr align 1 %$tmp1.addr, i8 0, i64 32, i1 false)
  %t1 = load i16, ptr %d, align 2
  store i16 %t1, ptr %$tmp1.addr, align 1
  call void @llvm.memcpy.p0.p0.i64(ptr align 1 %b, ptr align 1 %$tmp1.addr, i64 32, i1 false)
  %t2 = load i16, ptr %b, align 1
  store i16 %t2, ptr %d, align 2
  call void @llvm.memset.p0.i64(ptr align 1 %$tmp2.addr, i8 0, i64 32, i1 false)
  %t3 = load i16, ptr %d, align 2
  store i16 %t3, ptr %$tmp2.addr, align 1
  call void @"System._SetUnion"(ptr %$tmp3.addr, ptr %b, ptr %$tmp2.addr)
  call void @llvm.memcpy.p0.p0.i64(ptr align 1 %b, ptr align 1 %$tmp3.addr, i64 32, i1 false)
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
attributes #1 = { noreturn uwtable }
