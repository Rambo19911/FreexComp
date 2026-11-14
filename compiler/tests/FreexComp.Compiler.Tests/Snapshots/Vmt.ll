; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 84, i16 65, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 84, i16 66, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 84, i16 67, i16 0] }, align 16
@"M.TA.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [2 x ptr] } { ptr null, i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), ptr null, ptr null, ptr null, ptr null, ptr null, [2 x ptr] [ptr @"M.TA.P", ptr @"System._AbstractError"] }, align 8
@"M.TB.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [3 x ptr] } { ptr @"M.TA.$vmt", i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.1, i32 0, i32 5), ptr null, ptr null, ptr null, ptr null, ptr null, [3 x ptr] [ptr @"M.TB.P", ptr @"M.TB.Q", ptr @"M.TB.R"] }, align 8
@"M.TC.$vmt" = internal constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [3 x ptr] } { ptr @"M.TB.$vmt", i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.2, i32 0, i32 5), ptr null, ptr null, ptr null, ptr null, ptr null, [3 x ptr] [ptr @"M.TB.P", ptr @"M.TB.Q", ptr @"M.TC.R"] }, align 8

declare void @"System._AbstractError"() #1

define ptr @"M.TA.Create"(ptr %Self, i8 %Alloc) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %Alloc.addr = alloca i8, align 1
  store ptr %Self, ptr %Self.addr, align 8
  store i8 %Alloc, ptr %Alloc.addr, align 1
  br label %body.body.1
body.body.1:
  %t1 = load ptr, ptr %Self.addr, align 8
  %t2 = getelementptr inbounds i8, ptr %t1, i64 8
  store i32 1, ptr %t2, align 4
  br label %body.end.1
body.end.1:
  br label %exit
exit:
  %t3 = load ptr, ptr %Self.addr, align 8
  ret ptr %t3
}

define void @"M.TA.P"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
  br label %exit
exit:
  ret void
}

define void @"M.TA.S"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
  %t1 = load ptr, ptr %Self.addr, align 8
  %t2 = load ptr, ptr %t1, align 8
  %t3 = getelementptr inbounds i8, ptr %t2, i64 64
  %t4 = load ptr, ptr %t3, align 8
  call void %t4(ptr %t1)
  br label %exit
exit:
  ret void
}

define void @"M.TB.P"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
  %t1 = load ptr, ptr %Self.addr, align 8
  call void @"M.TA.P"(ptr %t1)
  br label %exit
exit:
  ret void
}

define i32 @"M.TB.Q"(ptr %Self, i32 %X) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %X.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store ptr %Self, ptr %Self.addr, align 8
  store i32 %X, ptr %X.addr, align 4
  %t1 = load i32, ptr %X.addr, align 4
  %t2 = load ptr, ptr %Self.addr, align 8
  %t3 = getelementptr inbounds i8, ptr %t2, i64 8
  %t4 = load i32, ptr %t3, align 4
  %t5 = add i32 %t1, %t4
  store i32 %t5, ptr %Result.addr, align 4
  br label %exit
exit:
  %t6 = load i32, ptr %Result.addr, align 4
  ret i32 %t6
}

define void @"M.TB.R"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
  br label %exit
exit:
  ret void
}

define internal void @"M.TC.R"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
  br label %exit
exit:
  ret void
}

define i32 @"M.Run"(ptr %A) #0 {
entry:
  %A.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  %B.addr = alloca ptr, align 8
  store ptr %A, ptr %A.addr, align 8
  %t1 = load ptr, ptr %A.addr, align 8
  %t2 = load ptr, ptr %t1, align 8
  %t3 = getelementptr inbounds i8, ptr %t2, i64 64
  %t4 = load ptr, ptr %t3, align 8
  call void %t4(ptr %t1)
  %t5 = load ptr, ptr %A.addr, align 8
  call void @"M.TA.S"(ptr %t5)
  %t6 = call ptr @"M.TA.Create"(ptr @"M.TC.$vmt", i8 1)
  store ptr %t6, ptr %B.addr, align 8
  %t7 = load ptr, ptr %B.addr, align 8
  %t8 = load ptr, ptr %t7, align 8
  %t9 = getelementptr inbounds i8, ptr %t8, i64 80
  %t10 = load ptr, ptr %t9, align 8
  call void %t10(ptr %t7)
  %t11 = load ptr, ptr %A.addr, align 8
  %t12 = load ptr, ptr %t11, align 8
  %t13 = getelementptr inbounds i8, ptr %t12, i64 72
  %t14 = load ptr, ptr %t13, align 8
  %t15 = call i32 %t14(ptr %t11, i32 2)
  %t16 = load ptr, ptr %B.addr, align 8
  %t17 = load ptr, ptr %t16, align 8
  %t18 = getelementptr inbounds i8, ptr %t17, i64 72
  %t19 = load ptr, ptr %t18, align 8
  %t20 = call i32 %t19(ptr %t16, i32 3)
  %t21 = add i32 %t15, %t20
  store i32 %t21, ptr %Result.addr, align 4
  br label %exit
exit:
  %t22 = load i32, ptr %Result.addr, align 4
  ret i32 %t22
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
