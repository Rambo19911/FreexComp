; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

declare void @"System._OverflowError"() #1
declare void @"System._RangeError"() #1
declare { i32, i1 } @llvm.sadd.with.overflow.i32(i32, i32)
declare { i64, i1 } @llvm.sadd.with.overflow.i64(i64, i64)
declare { i32, i1 } @llvm.smul.with.overflow.i32(i32, i32)
declare { i32, i1 } @llvm.ssub.with.overflow.i32(i32, i32)
declare { i64, i1 } @llvm.ssub.with.overflow.i64(i64, i64)
declare { i32, i1 } @llvm.uadd.with.overflow.i32(i32, i32)
declare { i32, i1 } @llvm.umul.with.overflow.i32(i32, i32)

define i8 @"M.Narrow"(i32 %i) #0 {
entry:
  %i.addr = alloca i32, align 4
  %Result.addr = alloca i8, align 1
  store i32 %i, ptr %i.addr, align 4
  %t1 = load i32, ptr %i.addr, align 4
  %t2 = icmp ule i32 %t1, 255
  br i1 %t2, label %range.ok.1, label %range.error.1
range.error.1:
  call void @"System._RangeError"() #1
  unreachable
range.ok.1:
  %t3 = trunc i32 %t1 to i8
  store i8 %t3, ptr %Result.addr, align 1
  br label %exit
exit:
  %t4 = load i8, ptr %Result.addr, align 1
  ret i8 %t4
}

define i32 @"M.Signed"(i32 %c) #0 {
entry:
  %c.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %c, ptr %c.addr, align 4
  %t1 = load i32, ptr %c.addr, align 4
  %t2 = icmp ule i32 %t1, 2147483647
  br i1 %t2, label %range.ok.1, label %range.error.1
range.error.1:
  call void @"System._RangeError"() #1
  unreachable
range.ok.1:
  store i32 %t1, ptr %Result.addr, align 4
  br label %exit
exit:
  %t3 = load i32, ptr %Result.addr, align 4
  ret i32 %t3
}

define internal i32 @"M.Unchecked"(i32 %i, i16 %w) #0 {
entry:
  %i.addr = alloca i32, align 4
  %w.addr = alloca i16, align 2
  %Result.addr = alloca i32, align 4
  %b.addr = alloca i8, align 1
  store i32 %i, ptr %i.addr, align 4
  store i16 %w, ptr %w.addr, align 2
  %t1 = load i32, ptr %i.addr, align 4
  %t2 = trunc i32 %t1 to i8
  store i8 %t2, ptr %b.addr, align 1
  %t3 = load i16, ptr %w.addr, align 2
  %t4 = zext i16 %t3 to i32
  %t5 = load i8, ptr %b.addr, align 1
  %t6 = zext i8 %t5 to i32
  %t7 = add i32 %t4, %t6
  store i32 %t7, ptr %Result.addr, align 4
  br label %exit
exit:
  %t8 = load i32, ptr %Result.addr, align 4
  ret i32 %t8
}

define internal i16 @"M.Letter"(i32 %i) #0 {
entry:
  %i.addr = alloca i32, align 4
  %Result.addr = alloca i16, align 2
  store i32 %i, ptr %i.addr, align 4
  %t1 = load i32, ptr %i.addr, align 4
  %t2 = icmp ule i32 %t1, 65535
  br i1 %t2, label %range.ok.1, label %range.error.1
range.error.1:
  call void @"System._RangeError"() #1
  unreachable
range.ok.1:
  %t3 = trunc i32 %t1 to i16
  store i16 %t3, ptr %Result.addr, align 2
  br label %exit
exit:
  %t4 = load i16, ptr %Result.addr, align 2
  ret i16 %t4
}

define internal void @"M.Bump"(ptr %b) #0 {
entry:
  %t1 = load i8, ptr %b, align 1
  %t2 = zext i8 %t1 to i32
  %t3 = add i32 %t2, 1
  %t4 = icmp ule i32 %t3, 255
  br i1 %t4, label %range.ok.1, label %range.error.1
range.error.1:
  call void @"System._RangeError"() #1
  unreachable
range.ok.1:
  %t5 = trunc i32 %t3 to i8
  store i8 %t5, ptr %b, align 1
  %t6 = load i8, ptr %b, align 1
  %t7 = zext i8 %t6 to i32
  %t8 = add i32 %t7, 1
  %t9 = icmp ule i32 %t8, 255
  br i1 %t9, label %range.ok.2, label %range.error.2
range.error.2:
  call void @"System._RangeError"() #1
  unreachable
range.ok.2:
  %t10 = trunc i32 %t8 to i8
  store i8 %t10, ptr %b, align 1
  br label %exit
exit:
  ret void
}

define i8 @"M.Both"(i32 %a, i8 %b) #0 {
entry:
  %a.addr = alloca i32, align 4
  %b.addr = alloca i8, align 1
  %Result.addr = alloca i8, align 1
  store i32 %a, ptr %a.addr, align 4
  store i8 %b, ptr %b.addr, align 1
  %t1 = load i32, ptr %a.addr, align 4
  %t2 = load i8, ptr %b.addr, align 1
  %t3 = sext i8 %t2 to i32
  %t4 = call { i32, i1 } @llvm.smul.with.overflow.i32(i32 %t1, i32 %t3)
  %t5 = extractvalue { i32, i1 } %t4, 1
  br i1 %t5, label %ovf.error.1, label %ovf.ok.1
ovf.error.1:
  call void @"System._OverflowError"() #1
  unreachable
ovf.ok.1:
  %t6 = extractvalue { i32, i1 } %t4, 0
  %t7 = sub i32 %t6, -128
  %t8 = icmp ule i32 %t7, 255
  br i1 %t8, label %range.ok.2, label %range.error.2
range.error.2:
  call void @"System._RangeError"() #1
  unreachable
range.ok.2:
  %t9 = trunc i32 %t6 to i8
  store i8 %t9, ptr %Result.addr, align 1
  br label %exit
exit:
  %t10 = load i8, ptr %Result.addr, align 1
  ret i8 %t10
}

define i32 @"M.Sum"(i32 %a, i32 %b) #0 {
entry:
  %a.addr = alloca i32, align 4
  %b.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %a, ptr %a.addr, align 4
  store i32 %b, ptr %b.addr, align 4
  %t1 = load i32, ptr %a.addr, align 4
  %t2 = load i32, ptr %b.addr, align 4
  %t3 = call { i32, i1 } @llvm.sadd.with.overflow.i32(i32 %t1, i32 %t2)
  %t4 = extractvalue { i32, i1 } %t3, 1
  br i1 %t4, label %ovf.error.1, label %ovf.ok.1
ovf.error.1:
  call void @"System._OverflowError"() #1
  unreachable
ovf.ok.1:
  %t5 = extractvalue { i32, i1 } %t3, 0
  %t6 = call { i32, i1 } @llvm.ssub.with.overflow.i32(i32 %t5, i32 1)
  %t7 = extractvalue { i32, i1 } %t6, 1
  br i1 %t7, label %ovf.error.2, label %ovf.ok.2
ovf.error.2:
  call void @"System._OverflowError"() #1
  unreachable
ovf.ok.2:
  %t8 = extractvalue { i32, i1 } %t6, 0
  store i32 %t8, ptr %Result.addr, align 4
  br label %exit
exit:
  %t9 = load i32, ptr %Result.addr, align 4
  ret i32 %t9
}

define i32 @"M.USum"(i32 %a, i32 %b) #0 {
entry:
  %a.addr = alloca i32, align 4
  %b.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %a, ptr %a.addr, align 4
  store i32 %b, ptr %b.addr, align 4
  %t1 = load i32, ptr %a.addr, align 4
  %t2 = load i32, ptr %b.addr, align 4
  %t3 = call { i32, i1 } @llvm.umul.with.overflow.i32(i32 %t1, i32 %t2)
  %t4 = extractvalue { i32, i1 } %t3, 1
  br i1 %t4, label %ovf.error.1, label %ovf.ok.1
ovf.error.1:
  call void @"System._OverflowError"() #1
  unreachable
ovf.ok.1:
  %t5 = extractvalue { i32, i1 } %t3, 0
  %t6 = call { i32, i1 } @llvm.uadd.with.overflow.i32(i32 %t5, i32 1)
  %t7 = extractvalue { i32, i1 } %t6, 1
  br i1 %t7, label %ovf.error.2, label %ovf.ok.2
ovf.error.2:
  call void @"System._OverflowError"() #1
  unreachable
ovf.ok.2:
  %t8 = extractvalue { i32, i1 } %t6, 0
  store i32 %t8, ptr %Result.addr, align 4
  br label %exit
exit:
  %t9 = load i32, ptr %Result.addr, align 4
  ret i32 %t9
}

define i64 @"M.Neg"(i64 %x) #0 {
entry:
  %x.addr = alloca i64, align 8
  %Result.addr = alloca i64, align 8
  store i64 %x, ptr %x.addr, align 8
  %t1 = load i64, ptr %x.addr, align 8
  %t2 = call { i64, i1 } @llvm.ssub.with.overflow.i64(i64 0, i64 %t1)
  %t3 = extractvalue { i64, i1 } %t2, 1
  br i1 %t3, label %ovf.error.1, label %ovf.ok.1
ovf.error.1:
  call void @"System._OverflowError"() #1
  unreachable
ovf.ok.1:
  %t4 = extractvalue { i64, i1 } %t2, 0
  %t5 = load i64, ptr %x.addr, align 8
  %t6 = icmp slt i64 %t5, 0
  %t7 = call { i64, i1 } @llvm.ssub.with.overflow.i64(i64 0, i64 %t5)
  %t8 = extractvalue { i64, i1 } %t7, 1
  br i1 %t8, label %ovf.error.2, label %ovf.ok.2
ovf.error.2:
  call void @"System._OverflowError"() #1
  unreachable
ovf.ok.2:
  %t9 = extractvalue { i64, i1 } %t7, 0
  %t10 = select i1 %t6, i64 %t9, i64 %t5
  %t11 = call { i64, i1 } @llvm.sadd.with.overflow.i64(i64 %t4, i64 %t10)
  %t12 = extractvalue { i64, i1 } %t11, 1
  br i1 %t12, label %ovf.error.3, label %ovf.ok.3
ovf.error.3:
  call void @"System._OverflowError"() #1
  unreachable
ovf.ok.3:
  %t13 = extractvalue { i64, i1 } %t11, 0
  store i64 %t13, ptr %Result.addr, align 8
  br label %exit
exit:
  %t14 = load i64, ptr %Result.addr, align 8
  ret i64 %t14
}

define internal void @"M.Next"(ptr %i) #0 {
entry:
  %t1 = load i32, ptr %i, align 4
  %t2 = call { i32, i1 } @llvm.sadd.with.overflow.i32(i32 %t1, i32 1)
  %t3 = extractvalue { i32, i1 } %t2, 1
  br i1 %t3, label %ovf.error.1, label %ovf.ok.1
ovf.error.1:
  call void @"System._OverflowError"() #1
  unreachable
ovf.ok.1:
  %t4 = extractvalue { i32, i1 } %t2, 0
  store i32 %t4, ptr %i, align 4
  br label %exit
exit:
  ret void
}

define i32 @"M.Total"(i8 %n) #0 {
entry:
  %n.addr = alloca i8, align 1
  %Result.addr = alloca i32, align 4
  %c.addr = alloca i8, align 1
  %$tmp1.addr = alloca i8, align 1
  %$tmp2.addr = alloca i8, align 1
  store i8 %n, ptr %n.addr, align 1
  store i32 0, ptr %Result.addr, align 4
  store i8 1, ptr %$tmp1.addr, align 1
  %t1 = load i8, ptr %n.addr, align 1
  store i8 %t1, ptr %$tmp2.addr, align 1
  %t2 = load i8, ptr %$tmp1.addr, align 1
  %t3 = load i8, ptr %$tmp2.addr, align 1
  %t4 = icmp ule i8 %t2, %t3
  br i1 %t4, label %if.then.1, label %if.end.1
if.then.1:
  %t5 = load i8, ptr %$tmp1.addr, align 1
  store i8 %t5, ptr %c.addr, align 1
  br label %for.body.2
for.body.2:
  %t6 = load i32, ptr %Result.addr, align 4
  %t7 = load i8, ptr %c.addr, align 1
  %t8 = zext i8 %t7 to i32
  %t9 = call { i32, i1 } @llvm.sadd.with.overflow.i32(i32 %t6, i32 %t8)
  %t10 = extractvalue { i32, i1 } %t9, 1
  br i1 %t10, label %ovf.error.3, label %ovf.ok.3
ovf.error.3:
  call void @"System._OverflowError"() #1
  unreachable
ovf.ok.3:
  %t11 = extractvalue { i32, i1 } %t9, 0
  store i32 %t11, ptr %Result.addr, align 4
  br label %for.inc.2
for.inc.2:
  %t12 = load i8, ptr %c.addr, align 1
  %t13 = load i8, ptr %$tmp2.addr, align 1
  %t14 = icmp eq i8 %t12, %t13
  br i1 %t14, label %for.end.2, label %if.end.4
if.end.4:
  %t15 = load i8, ptr %c.addr, align 1
  %t16 = zext i8 %t15 to i32
  %t17 = add i32 %t16, 1
  %t18 = trunc i32 %t17 to i8
  store i8 %t18, ptr %c.addr, align 1
  br label %for.body.2
for.end.2:
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  %t19 = load i32, ptr %Result.addr, align 4
  ret i32 %t19
}

define i32 @"M.Wrap"(i32 %a) #0 {
entry:
  %a.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %a, ptr %a.addr, align 4
  %t1 = load i32, ptr %a.addr, align 4
  %t2 = add i32 %t1, 1
  store i32 %t2, ptr %Result.addr, align 4
  br label %exit
exit:
  %t3 = load i32, ptr %Result.addr, align 4
  ret i32 %t3
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
