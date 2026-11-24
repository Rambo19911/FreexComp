; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.TSize8" = type { i32, i32 }
%"M.TRect16" = type { i32, i32, i32, i32 }

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 84, i16 70, i16 111, i16 111, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 71, i16 101, i16 116, i16 83, i16 105, i16 122, i16 101, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 71, i16 101, i16 116, i16 82, i16 101, i16 99, i16 116, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 83, i16 99, i16 97, i16 108, i16 101, i16 100, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 67, i16 111, i16 117, i16 110, i16 116, i16 0] }, align 16
@.str.5 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@"M.TFoo.$intftable" = private unnamed_addr constant { i64, [1 x { [16 x i8], ptr, i64 }] } { i64 1, [1 x { [16 x i8], ptr, i64 }] [{ [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.TFoo.$imt.M.IBar", i64 8 }] }, align 8
@"M.TFoo.$rtti.methods" = private unnamed_addr constant [4 x { ptr, ptr, i8, i8, [6 x i8] }] [{ ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.1, i32 0, i32 5), ptr @"M.TFoo.GetSize", i8 2, i8 1, [6 x i8] zeroinitializer }, { ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.2, i32 0, i32 5), ptr @"M.TFoo.GetRect", i8 2, i8 1, [6 x i8] zeroinitializer }, { ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.3, i32 0, i32 5), ptr @"M.TFoo.Scaled", i8 2, i8 1, [6 x i8] zeroinitializer }, { ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.4, i32 0, i32 5), ptr @"M.TFoo.Count", i8 2, i8 1, [6 x i8] zeroinitializer }], align 8
@"M.TFoo.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TFoo.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.5, i32 0, i32 5), i64 0, ptr null, i64 4, ptr @"M.TFoo.$rtti.methods", i64 0, ptr null, i64 0, ptr null }, align 8
@"M.TFoo.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [1 x ptr] } { ptr null, i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TFoo.$rtti", ptr @"M.TFoo.$intftable", ptr null, ptr null, ptr null, [1 x ptr] [ptr @"M.TFoo.GetRect"] }, align 8
@"M.TFoo.$imt.M.IBar" = constant [4 x ptr] [ptr @"M.TFoo.$thunk.M.IBar.GetSize", ptr @"M.TFoo.$thunk.M.IBar.GetRect", ptr @"M.TFoo.$thunk.M.IBar.Scaled", ptr @"M.TFoo.$thunk.M.IBar.Count"], align 8

declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)

define i64 @"M.TFoo.GetSize"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %Result.addr = alloca %"M.TSize8", align 4
  store ptr %Self, ptr %Self.addr, align 8
  %t1 = getelementptr inbounds %"M.TSize8", ptr %Result.addr, i32 0, i32 0
  store i32 1, ptr %t1, align 4
  %t2 = getelementptr inbounds %"M.TSize8", ptr %Result.addr, i32 0, i32 1
  store i32 2, ptr %t2, align 4
  br label %exit
exit:
  %t3 = load i64, ptr %Result.addr, align 4
  ret i64 %t3
}

define void @"M.TFoo.GetRect"(ptr %Self, ptr %Result) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
  %t1 = getelementptr inbounds %"M.TRect16", ptr %Result, i32 0, i32 0
  store i32 1, ptr %t1, align 4
  %t2 = getelementptr inbounds %"M.TRect16", ptr %Result, i32 0, i32 3
  store i32 4, ptr %t2, align 4
  br label %exit
exit:
  ret void
}

define i64 @"M.TFoo.Scaled"(ptr %Self, i32 %Factor) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %Factor.addr = alloca i32, align 4
  %Result.addr = alloca %"M.TSize8", align 4
  store ptr %Self, ptr %Self.addr, align 8
  store i32 %Factor, ptr %Factor.addr, align 4
  %t1 = load i32, ptr %Factor.addr, align 4
  %t2 = getelementptr inbounds %"M.TSize8", ptr %Result.addr, i32 0, i32 0
  store i32 %t1, ptr %t2, align 4
  %t3 = load i32, ptr %Factor.addr, align 4
  %t4 = getelementptr inbounds %"M.TSize8", ptr %Result.addr, i32 0, i32 1
  store i32 %t3, ptr %t4, align 4
  br label %exit
exit:
  %t5 = load i64, ptr %Result.addr, align 4
  ret i64 %t5
}

define i32 @"M.TFoo.Count"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  store ptr %Self, ptr %Self.addr, align 8
  store i32 3, ptr %Result.addr, align 4
  br label %exit
exit:
  %t1 = load i32, ptr %Result.addr, align 4
  ret i32 %t1
}

define i32 @"M.Run"(ptr %B, ptr %F) #0 {
entry:
  %B.addr = alloca ptr, align 8
  %F.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  %S.addr = alloca %"M.TSize8", align 4
  %R.addr = alloca %"M.TRect16", align 4
  %$tmp1.addr = alloca %"M.TSize8", align 4
  %$tmp2.addr = alloca %"M.TRect16", align 4
  %$tmp3.addr = alloca %"M.TSize8", align 4
  %$tmp4.addr = alloca %"M.TSize8", align 4
  %$tmp5.addr = alloca %"M.TSize8", align 4
  store ptr %B, ptr %B.addr, align 8
  store ptr %F, ptr %F.addr, align 8
  %t1 = load ptr, ptr %B.addr, align 8
  %t2 = load ptr, ptr %t1, align 8
  %t3 = getelementptr inbounds i8, ptr %t2, i64 0
  %t4 = load ptr, ptr %t3, align 8
  call ptr %t4(ptr %t1, ptr %$tmp1.addr)
  %t5 = load i64, ptr %$tmp1.addr, align 4
  store i64 %t5, ptr %S.addr, align 4
  %t6 = load ptr, ptr %B.addr, align 8
  %t7 = load ptr, ptr %t6, align 8
  %t8 = getelementptr inbounds i8, ptr %t7, i64 8
  %t9 = load ptr, ptr %t8, align 8
  call ptr %t9(ptr %t6, ptr %$tmp2.addr)
  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %R.addr, ptr align 4 %$tmp2.addr, i64 16, i1 false)
  %t10 = load ptr, ptr %B.addr, align 8
  %t11 = load ptr, ptr %t10, align 8
  %t12 = getelementptr inbounds i8, ptr %t11, i64 16
  %t13 = load ptr, ptr %t12, align 8
  call ptr %t13(ptr %t10, ptr %$tmp3.addr, i32 2)
  %t14 = load i64, ptr %$tmp3.addr, align 4
  store i64 %t14, ptr %S.addr, align 4
  %t15 = load ptr, ptr %B.addr, align 8
  %t16 = load ptr, ptr %t15, align 8
  %t17 = getelementptr inbounds i8, ptr %t16, i64 0
  %t18 = load ptr, ptr %t17, align 8
  call ptr %t18(ptr %t15, ptr %$tmp4.addr)
  %t19 = load ptr, ptr %F.addr, align 8
  %t20 = call i64 @"M.TFoo.GetSize"(ptr %t19)
  store i64 %t20, ptr %$tmp5.addr, align 4
  %t21 = load i64, ptr %$tmp5.addr, align 4
  store i64 %t21, ptr %S.addr, align 4
  %t22 = getelementptr inbounds %"M.TSize8", ptr %S.addr, i32 0, i32 0
  %t23 = load i32, ptr %t22, align 4
  %t24 = getelementptr inbounds %"M.TRect16", ptr %R.addr, i32 0, i32 3
  %t25 = load i32, ptr %t24, align 4
  %t26 = add i32 %t23, %t25
  %t27 = load ptr, ptr %B.addr, align 8
  %t28 = load ptr, ptr %t27, align 8
  %t29 = getelementptr inbounds i8, ptr %t28, i64 24
  %t30 = load ptr, ptr %t29, align 8
  %t31 = call i32 %t30(ptr %t27)
  %t32 = add i32 %t26, %t31
  store i32 %t32, ptr %Result.addr, align 4
  br label %exit
exit:
  %t33 = load i32, ptr %Result.addr, align 4
  ret i32 %t33
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

define internal ptr @"M.TFoo.$thunk.M.IBar.GetSize"(ptr %iref, ptr %result) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -8
  %v = call i64 @"M.TFoo.GetSize"(ptr %self)
  store i64 %v, ptr %result, align 1
  ret ptr %result
}

define internal ptr @"M.TFoo.$thunk.M.IBar.GetRect"(ptr %iref, ptr %result) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -8
  %vmt = load ptr, ptr %self, align 8
  %slot = getelementptr inbounds i8, ptr %vmt, i64 64
  %fn = load ptr, ptr %slot, align 8
  call void %fn(ptr %self, ptr %result)
  ret ptr %result
}

define internal ptr @"M.TFoo.$thunk.M.IBar.Scaled"(ptr %iref, ptr %result, i32 %p1) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -8
  %v = call i64 @"M.TFoo.Scaled"(ptr %self, i32 %p1)
  store i64 %v, ptr %result, align 1
  ret ptr %result
}

define internal i32 @"M.TFoo.$thunk.M.IBar.Count"(ptr %iref) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -8
  %r = musttail call i32 @"M.TFoo.Count"(ptr %self)
  ret i32 %r
}

attributes #0 = { uwtable "frame-pointer"="all" }
