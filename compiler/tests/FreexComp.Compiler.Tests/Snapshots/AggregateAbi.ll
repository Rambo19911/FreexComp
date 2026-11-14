; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.Triple.$frame" = type { ptr, ptr, ptr, ptr }
%"M.TSmall" = type { i16, i16 }
%"M.TPoint" = type { i32, i32 }
%"M.TTriple" = type { i32, i32, i32 }
%"M.TPacked" = type <{ i8, %"M.TTriple", %"M.TPoint" }>

@.set.0 = private unnamed_addr constant [32 x i8] [i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 2, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0], align 1
@.set.1 = private unnamed_addr constant [32 x i8] [i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 1, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0, i8 0], align 1
@"M.G" = global %"M.TTriple" zeroinitializer, align 4
@"M.GP" = global %"M.TPacked" zeroinitializer, align 1

declare void @"System._SetUnion"(ptr, ptr, ptr)
declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)

define i32 @"M.Small"(i32 %s) #0 {
entry:
  %s.addr = alloca %"M.TSmall", align 2
  %Result.addr = alloca %"M.TSmall", align 2
  store i32 %s, ptr %s.addr, align 2
  %t1 = load i32, ptr %s.addr, align 2
  store i32 %t1, ptr %Result.addr, align 2
  br label %exit
exit:
  %t2 = load i32, ptr %Result.addr, align 2
  ret i32 %t2
}

define i64 @"M.Point"(i64 %p, i64 %q) #0 {
entry:
  %p.addr = alloca %"M.TPoint", align 4
  %q.addr = alloca %"M.TPoint", align 4
  %Result.addr = alloca %"M.TPoint", align 4
  store i64 %p, ptr %p.addr, align 4
  store i64 %q, ptr %q.addr, align 4
  %t1 = getelementptr inbounds %"M.TPoint", ptr %p.addr, i32 0, i32 0
  %t2 = load i32, ptr %t1, align 4
  %t3 = getelementptr inbounds %"M.TPoint", ptr %q.addr, i32 0, i32 0
  %t4 = load i32, ptr %t3, align 4
  %t5 = add i32 %t2, %t4
  %t6 = getelementptr inbounds %"M.TPoint", ptr %Result.addr, i32 0, i32 0
  store i32 %t5, ptr %t6, align 4
  %t7 = getelementptr inbounds %"M.TPoint", ptr %p.addr, i32 0, i32 1
  %t8 = load i32, ptr %t7, align 4
  %t9 = getelementptr inbounds %"M.TPoint", ptr %Result.addr, i32 0, i32 1
  store i32 %t8, ptr %t9, align 4
  br label %exit
exit:
  %t10 = load i64, ptr %Result.addr, align 4
  ret i64 %t10
}

define void @"M.Triple"(ptr %Result, ptr %t, ptr %c, ptr %v) #0 {
entry:
  %$frame = alloca %"M.Triple.$frame", align 8
  store ptr null, ptr %$frame, align 8
  %Result.addr = getelementptr inbounds %"M.Triple.$frame", ptr %$frame, i32 0, i32 1
  %t.addr = getelementptr inbounds %"M.Triple.$frame", ptr %$frame, i32 0, i32 2
  %c.addr = getelementptr inbounds %"M.Triple.$frame", ptr %$frame, i32 0, i32 3
  store ptr %Result, ptr %Result.addr, align 8
  store ptr %t, ptr %t.addr, align 8
  store ptr %c, ptr %c.addr, align 8
  call void @"M.Triple.Bump"(ptr %$frame)
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %v, ptr align 4 %c, i64 12, i1 false)
  br label %exit
exit:
  ret void
}

define internal void @"M.Triple.Bump"(ptr %$link) #0 {
entry:
  %t1 = getelementptr inbounds %"M.Triple.$frame", ptr %$link, i32 0, i32 3
  %t2 = load ptr, ptr %t1, align 8
  %t3 = getelementptr inbounds %"M.TTriple", ptr %t2, i32 0, i32 1
  %t4 = load i32, ptr %t3, align 4
  %t5 = getelementptr inbounds %"M.Triple.$frame", ptr %$link, i32 0, i32 2
  %t6 = load ptr, ptr %t5, align 8
  %t7 = getelementptr inbounds %"M.TTriple", ptr %t6, i32 0, i32 0
  store i32 %t4, ptr %t7, align 4
  %t8 = getelementptr inbounds %"M.Triple.$frame", ptr %$link, i32 0, i32 2
  %t9 = load ptr, ptr %t8, align 8
  %t10 = getelementptr inbounds %"M.Triple.$frame", ptr %$link, i32 0, i32 1
  %t11 = load ptr, ptr %t10, align 8
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %t11, ptr align 4 %t9, i64 12, i1 false)
  br label %exit
exit:
  ret void
}

define void @"M.Bytes"(ptr %Result, ptr %b) #0 {
entry:
  call void @llvm.memcpy.p0.p0.i64(ptr align 1 %Result, ptr align 1 %b, i64 3, i1 false)
  br label %exit
exit:
  ret void
}

define void @"M.Letters"(ptr %Result, ptr %s) #0 {
entry:
  %$tmp1.addr = alloca [32 x i8], align 1
  call void @"System._SetUnion"(ptr %$tmp1.addr, ptr %s, ptr @.set.0)
  call void @llvm.memcpy.p0.p0.i64(ptr align 1 %Result, ptr align 1 %$tmp1.addr, i64 32, i1 false)
  br label %exit
exit:
  ret void
}

define void @"M.Use"() #0 {
entry:
  %t.addr = alloca %"M.TTriple", align 4
  %p.addr = alloca %"M.TPoint", align 4
  %l.addr = alloca [32 x i8], align 1
  %$tmp1.addr = alloca %"M.TPoint", align 4
  %$tmp2.addr = alloca %"M.TPoint", align 4
  %$tmp3.addr = alloca %"M.TTriple", align 4
  %$tmp4.addr = alloca %"M.TTriple", align 4
  %$tmp5.addr = alloca %"M.TTriple", align 4
  %$tmp6.addr = alloca %"M.TTriple", align 4
  %$tmp7.addr = alloca %"M.TTriple", align 4
  %$tmp8.addr = alloca %"M.TPoint", align 4
  %$tmp9.addr = alloca [32 x i8], align 1
  %$tmp10.addr = alloca %"M.TTriple", align 4
  %$tmp11.addr = alloca %"M.TTriple", align 4
  %t1 = load i64, ptr %p.addr, align 4
  %t2 = load i64, ptr %p.addr, align 4
  %t3 = load i64, ptr %p.addr, align 4
  %t4 = call i64 @"M.Point"(i64 %t2, i64 %t3)
  store i64 %t4, ptr %$tmp1.addr, align 4
  %t5 = load i64, ptr %$tmp1.addr, align 4
  %t6 = call i64 @"M.Point"(i64 %t1, i64 %t5)
  store i64 %t6, ptr %$tmp2.addr, align 4
  %t7 = load i64, ptr %$tmp2.addr, align 4
  store i64 %t7, ptr %p.addr, align 4
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %$tmp3.addr, ptr align 4 @"M.G", i64 12, i1 false)
  call void @"M.Triple"(ptr %$tmp4.addr, ptr %$tmp3.addr, ptr @"M.G", ptr %t.addr)
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %t.addr, ptr align 4 %$tmp4.addr, i64 12, i1 false)
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %$tmp5.addr, ptr align 4 %t.addr, i64 12, i1 false)
  %t8 = getelementptr inbounds %"M.TPacked", ptr @"M.GP", i32 0, i32 1
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %$tmp6.addr, ptr align 1 %t8, i64 12, i1 false)
  call void @"M.Triple"(ptr %$tmp7.addr, ptr %$tmp5.addr, ptr %$tmp6.addr, ptr %t.addr)
  %t9 = getelementptr inbounds %"M.TPacked", ptr @"M.GP", i32 0, i32 2
  %t10 = load i64, ptr %t9, align 1
  %t11 = getelementptr inbounds %"M.TPacked", ptr @"M.GP", i32 0, i32 2
  %t12 = load i64, ptr %t11, align 1
  %t13 = call i64 @"M.Point"(i64 %t10, i64 %t12)
  store i64 %t13, ptr %$tmp8.addr, align 4
  %t14 = load i64, ptr %$tmp8.addr, align 4
  store i64 %t14, ptr %p.addr, align 4
  call void @"M.Letters"(ptr %$tmp9.addr, ptr @.set.1)
  call void @llvm.memcpy.p0.p0.i64(ptr align 1 %l.addr, ptr align 1 %$tmp9.addr, i64 32, i1 false)
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %$tmp10.addr, ptr align 4 %t.addr, i64 12, i1 false)
  call void @"M.Triple"(ptr %$tmp11.addr, ptr %$tmp10.addr, ptr %t.addr, ptr %t.addr)
  %t15 = getelementptr inbounds %"M.TTriple", ptr %$tmp11.addr, i32 0, i32 1
  %t16 = load i32, ptr %t15, align 4
  %t17 = getelementptr inbounds %"M.TTriple", ptr @"M.G", i32 0, i32 0
  store i32 %t16, ptr %t17, align 4
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
