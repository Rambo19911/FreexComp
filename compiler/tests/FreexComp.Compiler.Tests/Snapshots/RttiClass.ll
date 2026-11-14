; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 84, i16 65, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 88, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 89, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [4 x i16] } { i32 -1, i32 3, i16 1200, i16 2, i32 0, [4 x i16] [i16 82, i16 117, i16 110, i16 0] }, align 16
@.str.5 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 70, i16 88, i16 0] }, align 16
@.str.6 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@.str.7 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 84, i16 66, i16 0] }, align 16
@"M.TA.$rtti.props" = private unnamed_addr constant [2 x { ptr, ptr, i8, i8, i8, i8, [4 x i8], i64, i64, i64, i32, i32, i64 }] [{ ptr, ptr, i8, i8, i8, i8, [4 x i8], i64, i64, i64, i32, i32, i64 } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.1, i32 0, i32 5), ptr @"System.Integer.$rtti", i8 1, i8 1, i8 0, i8 3, [4 x i8] zeroinitializer, i64 8, i64 8, i64 1, i32 -2147483648, i32 0, i64 -9223372036854775808 }, { ptr, ptr, i8, i8, i8, i8, [4 x i8], i64, i64, i64, i32, i32, i64 } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.3, i32 0, i32 5), ptr @"System.Integer.$rtti", i8 2, i8 3, i8 0, i8 3, [4 x i8] zeroinitializer, i64 ptrtoint (ptr @"M.TA.GetY" to i64), i64 64, i64 1, i32 -2147483648, i32 0, i64 -9223372036854775808 }], align 8
@"M.TA.$rtti.methods" = private unnamed_addr constant [1 x { ptr, ptr, i8, i8, [6 x i8] }] [{ ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.4, i32 0, i32 5), ptr @"M.TA.Run", i8 3, i8 0, [6 x i8] zeroinitializer }], align 8
@"M.TA.$rtti.fields" = private unnamed_addr constant [1 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.5, i32 0, i32 5), i64 8, ptr @"System.Integer.$rtti", i8 0, [7 x i8] zeroinitializer }], align 8
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.2, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
@"M.TA.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TA.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.6, i32 0, i32 5), i64 2, ptr @"M.TA.$rtti.props", i64 1, ptr @"M.TA.$rtti.methods", i64 1, ptr @"M.TA.$rtti.fields", i64 0, ptr null }, align 8
@"M.TB.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.7, i32 0, i32 5), ptr @"M.TB.$vmt", ptr @"M.TA.$rtti", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.6, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 0, ptr null, i64 0, ptr null }, align 8
@"M.TA.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [1 x ptr] } { ptr null, i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TA.$rtti", ptr null, ptr null, ptr null, ptr null, [1 x ptr] [ptr @"M.TA.SetY"] }, align 8
@"M.TB.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [1 x ptr] } { ptr @"M.TA.$vmt", i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.7, i32 0, i32 5), ptr @"M.TB.$rtti", ptr null, ptr null, ptr null, ptr null, [1 x ptr] [ptr @"M.TA.SetY"] }, align 8

define i32 @"M.TA.GetY"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  store ptr %Self, ptr %Self.addr, align 8
  %t1 = load ptr, ptr %Self.addr, align 8
  %t2 = getelementptr inbounds i8, ptr %t1, i64 8
  %t3 = load i32, ptr %t2, align 4
  store i32 %t3, ptr %Result.addr, align 4
  br label %exit
exit:
  %t4 = load i32, ptr %Result.addr, align 4
  ret i32 %t4
}

define void @"M.TA.SetY"(ptr %Self, i32 %V) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %V.addr = alloca i32, align 4
  store ptr %Self, ptr %Self.addr, align 8
  store i32 %V, ptr %V.addr, align 4
  %t1 = load i32, ptr %V.addr, align 4
  %t2 = load ptr, ptr %Self.addr, align 8
  %t3 = getelementptr inbounds i8, ptr %t2, i64 8
  store i32 %t1, ptr %t3, align 4
  br label %exit
exit:
  ret void
}

define void @"M.TA.Run"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
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
