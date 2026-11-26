; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

$"M.TList<Integer>.TEnumerator.MoveNext" = comdat any
$"M.TOuter.TCell<Byte>.Get" = comdat any
@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 84, i16 79, i16 117, i16 116, i16 101, i16 114, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 84, i16 73, i16 110, i16 110, i16 101, i16 114, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [4 x i16] } { i32 -1, i32 3, i16 1200, i16 2, i32 0, [4 x i16] [i16 71, i16 101, i16 116, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [15 x i16] } { i32 -1, i32 14, i16 1200, i16 2, i32 0, [15 x i16] [i16 84, i16 76, i16 105, i16 115, i16 116, i16 60, i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 62, i16 0] }, align 16
@.str.5 = private unnamed_addr constant { i32, i32, i16, i16, i32, [12 x i16] } { i32 -1, i32 11, i16 1200, i16 2, i32 0, [12 x i16] [i16 84, i16 69, i16 110, i16 117, i16 109, i16 101, i16 114, i16 97, i16 116, i16 111, i16 114, i16 0] }, align 16
@.str.6 = private unnamed_addr constant { i32, i32, i16, i16, i32, [9 x i16] } { i32 -1, i32 8, i16 1200, i16 2, i32 0, [9 x i16] [i16 77, i16 111, i16 118, i16 101, i16 78, i16 101, i16 120, i16 116, i16 0] }, align 16
@.str.7 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 70, i16 73, i16 110, i16 100, i16 101, i16 120, i16 0] }, align 16
@.str.8 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
@.str.9 = private unnamed_addr constant { i32, i32, i16, i16, i32, [12 x i16] } { i32 -1, i32 11, i16 1200, i16 2, i32 0, [12 x i16] [i16 84, i16 67, i16 101, i16 108, i16 108, i16 60, i16 66, i16 121, i16 116, i16 101, i16 62, i16 0] }, align 16
@.str.10 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 73, i16 116, i16 101, i16 109, i16 0] }, align 16
@.str.11 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 66, i16 121, i16 116, i16 101, i16 0] }, align 16
@"M.TOuter.TInner.$rtti.methods" = private unnamed_addr constant [1 x { ptr, ptr, i8, i8, [6 x i8] }] [{ ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.3, i32 0, i32 5), ptr @"M.TOuter.TInner.Get", i8 2, i8 1, [6 x i8] zeroinitializer }], align 8
@"M.TList<Integer>.TEnumerator.$rtti.methods" = private unnamed_addr constant [1 x { ptr, ptr, i8, i8, [6 x i8] }] [{ ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [9 x i16] }, ptr @.str.6, i32 0, i32 5), ptr @"M.TList<Integer>.TEnumerator.MoveNext", i8 2, i8 1, [6 x i8] zeroinitializer }], align 8
@"M.TList<Integer>.TEnumerator.$rtti.fields" = private unnamed_addr constant [1 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.7, i32 0, i32 5), i64 8, ptr @"System.Integer.$rtti", i8 2, [7 x i8] zeroinitializer }], align 8
@"M.TOuter.TCell<Byte>.$rtti.methods" = private unnamed_addr constant [1 x { ptr, ptr, i8, i8, [6 x i8] }] [{ ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.3, i32 0, i32 5), ptr @"M.TOuter.TCell<Byte>.Get", i8 2, i8 1, [6 x i8] zeroinitializer }], align 8
@"M.TOuter.TCell<Byte>.$rtti.fields" = private unnamed_addr constant [1 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.10, i32 0, i32 5), i64 8, ptr @"System.Byte.$rtti", i8 2, [7 x i8] zeroinitializer }], align 8
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.8, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
$"System.Byte.$rtti" = comdat any
@"System.Byte.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.11, i32 0, i32 5), i8 1, [7 x i8] zeroinitializer, i64 0, i64 255 }, comdat, align 8
@"M.TOuter.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TOuter.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.1, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 0, ptr null, i64 0, ptr null }, align 8
@"M.TOuter.TInner.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.2, i32 0, i32 5), ptr @"M.TOuter.TInner.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.1, i32 0, i32 5), i64 0, ptr null, i64 1, ptr @"M.TOuter.TInner.$rtti.methods", i64 0, ptr null, i64 0, ptr null }, align 8
$"M.TList<Integer>.$rtti" = comdat any
@"M.TList<Integer>.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [15 x i16] }, ptr @.str.4, i32 0, i32 5), ptr @"M.TList<Integer>.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.1, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 0, ptr null, i64 0, ptr null }, comdat, align 8
$"M.TList<Integer>.TEnumerator.$rtti" = comdat any
@"M.TList<Integer>.TEnumerator.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [12 x i16] }, ptr @.str.5, i32 0, i32 5), ptr @"M.TList<Integer>.TEnumerator.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.1, i32 0, i32 5), i64 0, ptr null, i64 1, ptr @"M.TList<Integer>.TEnumerator.$rtti.methods", i64 1, ptr @"M.TList<Integer>.TEnumerator.$rtti.fields", i64 0, ptr null }, comdat, align 8
$"M.TOuter.TCell<Byte>.$rtti" = comdat any
@"M.TOuter.TCell<Byte>.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [12 x i16] }, ptr @.str.9, i32 0, i32 5), ptr @"M.TOuter.TCell<Byte>.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.1, i32 0, i32 5), i64 0, ptr null, i64 1, ptr @"M.TOuter.TCell<Byte>.$rtti.methods", i64 1, ptr @"M.TOuter.TCell<Byte>.$rtti.fields", i64 0, ptr null }, comdat, align 8
@"M.TOuter.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 8, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TOuter.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.TOuter.TInner.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 8, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.2, i32 0, i32 5), ptr @"M.TOuter.TInner.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
$"M.TList<Integer>.$vmt" = comdat any
@"M.TList<Integer>.$vmt" = linkonce_odr constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 8, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [15 x i16] }, ptr @.str.4, i32 0, i32 5), ptr @"M.TList<Integer>.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, comdat, align 8
$"M.TList<Integer>.TEnumerator.$vmt" = comdat any
@"M.TList<Integer>.TEnumerator.$vmt" = linkonce_odr constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [12 x i16] }, ptr @.str.5, i32 0, i32 5), ptr @"M.TList<Integer>.TEnumerator.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, comdat, align 8
$"M.TOuter.TCell<Byte>.$vmt" = comdat any
@"M.TOuter.TCell<Byte>.$vmt" = linkonce_odr constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [12 x i16] }, ptr @.str.9, i32 0, i32 5), ptr @"M.TOuter.TCell<Byte>.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, comdat, align 8

define i32 @"M.TOuter.TInner.Get"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  store ptr %Self, ptr %Self.addr, align 8
  store i32 1, ptr %Result.addr, align 4
  br label %exit
exit:
  %t1 = load i32, ptr %Result.addr, align 4
  ret i32 %t1
}

define i32 @"M.Count"() #0 {
entry:
  %Result.addr = alloca i32, align 4
  %E.addr = alloca ptr, align 8
  %C.addr = alloca ptr, align 8
  store ptr null, ptr %C.addr, align 8
  %t1 = load ptr, ptr %C.addr, align 8
  %t2 = call i8 @"M.TOuter.TCell<Byte>.Get"(ptr %t1)
  %t3 = zext i8 %t2 to i32
  store i32 %t3, ptr %Result.addr, align 4
  store ptr null, ptr %E.addr, align 8
  br label %while.cond.1
while.cond.1:
  %t4 = load ptr, ptr %E.addr, align 8
  %t5 = call i8 @"M.TList<Integer>.TEnumerator.MoveNext"(ptr %t4)
  %t6 = icmp ne i8 %t5, 0
  br i1 %t6, label %while.body.1, label %while.end.1
while.body.1:
  %t7 = load i32, ptr %Result.addr, align 4
  %t8 = add i32 %t7, 1
  store i32 %t8, ptr %Result.addr, align 4
  br label %while.cond.1
while.end.1:
  br label %exit
exit:
  %t9 = load i32, ptr %Result.addr, align 4
  ret i32 %t9
}

define linkonce_odr i8 @"M.TList<Integer>.TEnumerator.MoveNext"(ptr %Self) #0 comdat {
entry:
  %Self.addr = alloca ptr, align 8
  %Result.addr = alloca i8, align 1
  store ptr %Self, ptr %Self.addr, align 8
  %t1 = load ptr, ptr %Self.addr, align 8
  %t2 = getelementptr inbounds i8, ptr %t1, i64 8
  %t3 = load i32, ptr %t2, align 4
  %t4 = add i32 %t3, 1
  %t5 = load ptr, ptr %Self.addr, align 8
  %t6 = getelementptr inbounds i8, ptr %t5, i64 8
  store i32 %t4, ptr %t6, align 4
  %t7 = load ptr, ptr %Self.addr, align 8
  %t8 = getelementptr inbounds i8, ptr %t7, i64 8
  %t9 = load i32, ptr %t8, align 4
  %t10 = icmp slt i32 %t9, 2
  %t11 = zext i1 %t10 to i8
  store i8 %t11, ptr %Result.addr, align 1
  br label %exit
exit:
  %t12 = load i8, ptr %Result.addr, align 1
  ret i8 %t12
}

define linkonce_odr i8 @"M.TOuter.TCell<Byte>.Get"(ptr %Self) #0 comdat {
entry:
  %Self.addr = alloca ptr, align 8
  %Result.addr = alloca i8, align 1
  store ptr %Self, ptr %Self.addr, align 8
  %t1 = load ptr, ptr %Self.addr, align 8
  %t2 = getelementptr inbounds i8, ptr %t1, i64 8
  %t3 = load i8, ptr %t2, align 1
  store i8 %t3, ptr %Result.addr, align 1
  br label %exit
exit:
  %t4 = load i8, ptr %Result.addr, align 1
  ret i8 %t4
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
