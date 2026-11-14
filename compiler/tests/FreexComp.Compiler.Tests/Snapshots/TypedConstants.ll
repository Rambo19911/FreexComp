; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.TPoint" = type { i32, i32 }
%"M.TFour" = type <{ i8, [3 x i8], i64 }>

@"M.Table" = constant [3 x i32] [i32 1, i32 2, i32 3], align 4
@"M.Origin" = constant %"M.TPoint" { i32 5, i32 -6 }, align 4
@"M.Grid" = global [2 x [2 x i8]] [[2 x i8] [i8 1, i8 2], [2 x i8] [i8 3, i8 4]], align 1
@"M.Four" = internal constant %"M.TFour" <{ i8 1, [3 x i8] zeroinitializer, i64 2 }>, align 4
@"M.Half" = internal constant %"M.TPoint" { i32 9, i32 zeroinitializer }, align 4
@"M.Flags" = internal constant [2 x i8] [i8 0, i8 1], align 1
@"M.Small" = internal constant i8 -127, align 1
@"M.Start" = internal global %"M.TPoint" { i32 1, i32 2 }, align 4
@"M.Pick.Local" = internal constant [2 x i16] [i16 10, i16 20], align 2

define i32 @"M.Pick"(i32 %i) #0 {
entry:
  %i.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %i, ptr %i.addr, align 4
  %t1 = load i32, ptr %i.addr, align 4
  %t2 = sext i32 %t1 to i64
  %t3 = getelementptr inbounds [3 x i32], ptr @"M.Table", i64 0, i64 %t2
  %t4 = load i32, ptr %t3, align 4
  %t5 = load i32, ptr %i.addr, align 4
  %t6 = sext i32 %t5 to i64
  %t7 = getelementptr inbounds [2 x i16], ptr @"M.Pick.Local", i64 0, i64 %t6
  %t8 = load i16, ptr %t7, align 2
  %t9 = zext i16 %t8 to i32
  %t10 = add i32 %t4, %t9
  %t11 = getelementptr inbounds %"M.TPoint", ptr @"M.Origin", i32 0, i32 1
  %t12 = load i32, ptr %t11, align 4
  %t13 = add i32 %t10, %t12
  %t14 = sext i32 %t13 to i64
  %t15 = getelementptr inbounds %"M.TFour", ptr @"M.Four", i32 0, i32 2
  %t16 = load i64, ptr %t15, align 4
  %t17 = add i64 %t14, %t16
  %t18 = getelementptr inbounds %"M.TPoint", ptr @"M.Half", i32 0, i32 0
  %t19 = load i32, ptr %t18, align 4
  %t20 = sext i32 %t19 to i64
  %t21 = add i64 %t17, %t20
  %t22 = trunc i64 %t21 to i32
  store i32 %t22, ptr %Result.addr, align 4
  %t23 = load i64, ptr @"M.Origin", align 4
  store i64 %t23, ptr @"M.Start", align 4
  br label %exit
exit:
  %t24 = load i32, ptr %Result.addr, align 4
  ret i32 %t24
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
