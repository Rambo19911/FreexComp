; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [4 x i16] } { i32 -1, i32 3, i16 1200, i16 2, i32 0, [4 x i16] [i16 82, i16 101, i16 100, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 71, i16 114, i16 101, i16 101, i16 110, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 84, i16 67, i16 111, i16 108, i16 111, i16 114, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 87, i16 97, i16 114, i16 109, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 84, i16 83, i16 101, i16 116, i16 0] }, align 16
@.str.5 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
@"M.TColor.$names" = private unnamed_addr constant [2 x ptr] [ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.0, i32 0, i32 5), ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.1, i32 0, i32 5)], align 8
@"M.TWarm.$names" = private unnamed_addr constant [1 x ptr] [ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.1, i32 0, i32 5)], align 8
$"M.TColor.$rtti" = comdat any
@"M.TColor.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64, ptr, ptr } { i8 3, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.2, i32 0, i32 5), i8 1, [7 x i8] zeroinitializer, i64 0, i64 1, ptr @"M.TColor.$rtti", ptr @"M.TColor.$names" }, comdat, align 8
$"M.TWarm.$rtti" = comdat any
@"M.TWarm.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64, ptr, ptr } { i8 3, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.3, i32 0, i32 5), i8 1, [7 x i8] zeroinitializer, i64 1, i64 1, ptr @"M.TColor.$rtti", ptr @"M.TWarm.$names" }, comdat, align 8
$"M.TSet.$rtti" = comdat any
@"M.TSet.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i32, [4 x i8], ptr } { i8 5, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.4, i32 0, i32 5), i32 1, [4 x i8] zeroinitializer, ptr @"M.TColor.$rtti" }, comdat, align 8
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.5, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8

define ptr @"M.Info"() #0 {
entry:
  %Result.addr = alloca ptr, align 8
  store ptr @"M.TColor.$rtti", ptr %Result.addr, align 8
  store ptr @"M.TWarm.$rtti", ptr %Result.addr, align 8
  store ptr @"M.TSet.$rtti", ptr %Result.addr, align 8
  store ptr @"System.Integer.$rtti", ptr %Result.addr, align 8
  br label %exit
exit:
  %t1 = load ptr, ptr %Result.addr, align 8
  ret ptr %t1
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
