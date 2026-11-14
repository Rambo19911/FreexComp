; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@"M.g" = internal global i32 zeroinitializer, align 4

define i64 @"M.Fact"(i32 %n) #0 {
entry:
  %n.addr = alloca i32, align 4
  %Result.addr = alloca i64, align 8
  store i32 %n, ptr %n.addr, align 4
  %t1 = load i32, ptr %n.addr, align 4
  %t2 = icmp sle i32 %t1, 1
  br i1 %t2, label %if.then.1, label %if.else.1
if.then.1:
  store i64 1, ptr %Result.addr, align 8
  br label %if.end.1
if.else.1:
  %t3 = load i32, ptr %n.addr, align 4
  %t4 = sext i32 %t3 to i64
  %t5 = load i32, ptr %n.addr, align 4
  %t6 = sub i32 %t5, 1
  %t7 = call i64 @"M.Fact"(i32 %t6)
  %t8 = mul i64 %t4, %t7
  store i64 %t8, ptr %Result.addr, align 8
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  %t9 = load i64, ptr %Result.addr, align 8
  ret i64 %t9
}

define void @"M.Swap2"(ptr %a, ptr %b) #0 {
entry:
  %t.addr = alloca i32, align 4
  %t1 = load i32, ptr %a, align 4
  store i32 %t1, ptr %t.addr, align 4
  %t2 = load i32, ptr %b, align 4
  store i32 %t2, ptr %a, align 4
  %t3 = load i32, ptr %t.addr, align 4
  store i32 %t3, ptr %b, align 4
  br label %exit
exit:
  ret void
}

define void @"M.Init"(ptr %x, i8 %flag) #0 {
entry:
  %flag.addr = alloca i8, align 1
  store i8 %flag, ptr %flag.addr, align 1
  %t1 = load i8, ptr %flag.addr, align 1
  %t2 = icmp ne i8 %t1, 0
  br i1 %t2, label %if.then.1, label %if.else.1
if.then.1:
  store i32 1, ptr %x, align 4
  br label %if.end.1
if.else.1:
  store i32 0, ptr %x, align 4
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  ret void
}

define i8 @"M.IsPos"(i32 %x) #0 {
entry:
  %x.addr = alloca i32, align 4
  %Result.addr = alloca i8, align 1
  store i32 %x, ptr %x.addr, align 4
  %t1 = load i32, ptr %x.addr, align 4
  %t2 = icmp sgt i32 %t1, 0
  %t3 = zext i1 %t2 to i8
  store i8 %t3, ptr %Result.addr, align 1
  br label %exit
exit:
  %t4 = load i8, ptr %Result.addr, align 1
  ret i8 %t4
}

define void @"M.Use"() #0 {
entry:
  %a.addr = alloca i32, align 4
  %b.addr = alloca i32, align 4
  %t1 = load i32, ptr @"M.g", align 4
  %t2 = call i8 @"M.IsPos"(i32 %t1)
  %t3 = icmp ne i8 %t2, 0
  %t4 = zext i1 %t3 to i8
  call void @"M.Init"(ptr %a.addr, i8 %t4)
  store i32 2, ptr %b.addr, align 4
  call void @"M.Swap2"(ptr %a.addr, ptr %b.addr)
  call void @"M.Swap2"(ptr @"M.g", ptr %a.addr)
  call i64 @"M.Fact"(i32 3)
  %t5 = load i32, ptr %a.addr, align 4
  %t6 = call i8 @"M.IsPos"(i32 %t5)
  %t7 = icmp ne i8 %t6, 0
  br i1 %t7, label %if.then.1, label %if.end.1
if.then.1:
  %t8 = load i32, ptr %b.addr, align 4
  %t9 = call i64 @"M.Fact"(i32 %t8)
  %t10 = trunc i64 %t9 to i32
  store i32 %t10, ptr @"M.g", align 4
  br label %if.end.1
if.end.1:
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
