; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.TPoint" = type { i32, i32 }
%"M.TFour" = type <{ i8, [3 x i8], i64 }>
%"M.Outer.$frame" = type <{ ptr, %"M.TPoint", i8, [3 x i8], %"M.TFour" }>
%"M.TRect" = type { %"M.TPoint", %"M.TPoint", i16 }
%"M.TPacked" = type <{ i8, i32 }>
%"M.TLine" = type { [2 x %"M.TPoint"], %"M.TFour" }

@"M.G" = global %"M.TRect" zeroinitializer, align 4

declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)

define void @"M.Move"(ptr %p, i32 %dx) #0 {
entry:
  %dx.addr = alloca i32, align 4
  store i32 %dx, ptr %dx.addr, align 4
  %t1 = getelementptr inbounds %"M.TPoint", ptr %p, i32 0, i32 0
  %t2 = load i32, ptr %t1, align 4
  %t3 = load i32, ptr %dx.addr, align 4
  %t4 = add i32 %t2, %t3
  %t5 = getelementptr inbounds %"M.TPoint", ptr %p, i32 0, i32 0
  store i32 %t4, ptr %t5, align 4
  br label %exit
exit:
  ret void
}

define void @"M.Copy"(ptr %r, ptr %q, ptr %f) #0 {
entry:
  %local.addr = alloca %"M.TRect", align 4
  %pt.addr = alloca %"M.TPoint", align 4
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %local.addr, ptr align 4 %r, i64 20, i1 false)
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %r, ptr align 4 @"M.G", i64 20, i1 false)
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 @"M.G", ptr align 4 %local.addr, i64 20, i1 false)
  %t1 = getelementptr inbounds %"M.TRect", ptr %local.addr, i32 0, i32 1
  %t2 = load i64, ptr %t1, align 4
  store i64 %t2, ptr %pt.addr, align 4
  %t3 = getelementptr inbounds %"M.TRect", ptr @"M.G", i32 0, i32 0
  %t4 = load i64, ptr %pt.addr, align 4
  store i64 %t4, ptr %t3, align 4
  %t5 = getelementptr inbounds %"M.TPacked", ptr %q, i32 0, i32 0
  %t6 = load i8, ptr %t5, align 1
  %t7 = zext i8 %t6 to i32
  %t8 = getelementptr inbounds %"M.TPacked", ptr %q, i32 0, i32 1
  store i32 %t7, ptr %t8, align 1
  %t9 = getelementptr inbounds %"M.TFour", ptr %f, i32 0, i32 2
  store i64 7, ptr %t9, align 4
  br label %exit
exit:
  ret void
}

define void @"M.Line"(ptr %l, i32 %i) #0 {
entry:
  %i.addr = alloca i32, align 4
  store i32 %i, ptr %i.addr, align 4
  %t1 = getelementptr inbounds %"M.TLine", ptr %l, i32 0, i32 0
  %t2 = getelementptr inbounds [2 x %"M.TPoint"], ptr %t1, i64 0, i64 0
  %t3 = getelementptr inbounds %"M.TPoint", ptr %t2, i32 0, i32 0
  %t4 = load i32, ptr %t3, align 4
  %t5 = getelementptr inbounds %"M.TLine", ptr %l, i32 0, i32 0
  %t6 = load i32, ptr %i.addr, align 4
  %t7 = sext i32 %t6 to i64
  %t8 = getelementptr inbounds [2 x %"M.TPoint"], ptr %t5, i64 0, i64 %t7
  %t9 = getelementptr inbounds %"M.TPoint", ptr %t8, i32 0, i32 1
  store i32 %t4, ptr %t9, align 4
  %t10 = getelementptr inbounds %"M.TLine", ptr %l, i32 0, i32 0
  %t11 = load i32, ptr %i.addr, align 4
  %t12 = sext i32 %t11 to i64
  %t13 = getelementptr inbounds [2 x %"M.TPoint"], ptr %t10, i64 0, i64 %t12
  %t14 = getelementptr inbounds %"M.TLine", ptr %l, i32 0, i32 0
  %t15 = getelementptr inbounds [2 x %"M.TPoint"], ptr %t14, i64 0, i64 1
  %t16 = load i64, ptr %t13, align 4
  store i64 %t16, ptr %t15, align 4
  %t17 = getelementptr inbounds %"M.TLine", ptr %l, i32 0, i32 1
  %t18 = getelementptr inbounds %"M.TFour", ptr %t17, i32 0, i32 0
  store i8 1, ptr %t18, align 1
  br label %exit
exit:
  ret void
}

define void @"M.Outer"() #0 {
entry:
  %$frame = alloca %"M.Outer.$frame", align 8
  store ptr null, ptr %$frame, align 8
  %r.addr = getelementptr inbounds %"M.Outer.$frame", ptr %$frame, i32 0, i32 1
  %b.addr = getelementptr inbounds %"M.Outer.$frame", ptr %$frame, i32 0, i32 2
  %f.addr = getelementptr inbounds %"M.Outer.$frame", ptr %$frame, i32 0, i32 4
  %t1 = getelementptr inbounds %"M.TPoint", ptr %r.addr, i32 0, i32 0
  store i32 1, ptr %t1, align 4
  store i8 3, ptr %b.addr, align 1
  call void @"M.Outer.Inner"(ptr %$frame)
  br label %exit
exit:
  ret void
}

define internal void @"M.Outer.Inner"(ptr %$link) #0 {
entry:
  %t1 = getelementptr inbounds %"M.Outer.$frame", ptr %$link, i32 0, i32 1
  %t2 = getelementptr inbounds %"M.TPoint", ptr %t1, i32 0, i32 1
  store i32 2, ptr %t2, align 4
  %t3 = getelementptr inbounds %"M.Outer.$frame", ptr %$link, i32 0, i32 2
  %t4 = load i8, ptr %t3, align 1
  %t5 = zext i8 %t4 to i64
  %t6 = getelementptr inbounds %"M.Outer.$frame", ptr %$link, i32 0, i32 4
  %t7 = getelementptr inbounds %"M.TFour", ptr %t6, i32 0, i32 2
  store i64 %t5, ptr %t7, align 4
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
