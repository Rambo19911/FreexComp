; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.TCell" = type { ptr }

@"M.Table" = internal global [2 x ptr] zeroinitializer, align 8

define i32 @"M.Add"(i32 %a, i32 %b) #0 {
entry:
  %a.addr = alloca i32, align 4
  %b.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %a, ptr %a.addr, align 4
  store i32 %b, ptr %b.addr, align 4
  %t1 = load i32, ptr %a.addr, align 4
  %t2 = load i32, ptr %b.addr, align 4
  %t3 = add i32 %t1, %t2
  store i32 %t3, ptr %Result.addr, align 4
  br label %exit
exit:
  %t4 = load i32, ptr %Result.addr, align 4
  ret i32 %t4
}

define i32 @"M.Apply"(ptr %f, i32 %x) #0 {
entry:
  %f.addr = alloca ptr, align 8
  %x.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store ptr %f, ptr %f.addr, align 8
  store i32 %x, ptr %x.addr, align 4
  %t1 = load ptr, ptr %f.addr, align 8
  %t2 = icmp ne ptr %t1, null
  br i1 %t2, label %if.then.1, label %if.else.1
if.then.1:
  %t3 = load ptr, ptr %f.addr, align 8
  %t4 = load i32, ptr %x.addr, align 4
  %t5 = load i32, ptr %x.addr, align 4
  %t6 = call i32 %t3(i32 %t4, i32 %t5)
  store i32 %t6, ptr %Result.addr, align 4
  br label %if.end.1
if.else.1:
  store i32 0, ptr %Result.addr, align 4
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  %t7 = load i32, ptr %Result.addr, align 4
  ret i32 %t7
}

define ptr @"M.Pick"(i32 %i) #0 {
entry:
  %i.addr = alloca i32, align 4
  %Result.addr = alloca ptr, align 8
  store i32 %i, ptr %i.addr, align 4
  %t1 = load i32, ptr %i.addr, align 4
  %t2 = sext i32 %t1 to i64
  %t3 = getelementptr inbounds [2 x ptr], ptr @"M.Table", i64 0, i64 %t2
  %t4 = load ptr, ptr %t3, align 8
  store ptr %t4, ptr %Result.addr, align 8
  br label %exit
exit:
  %t5 = load ptr, ptr %Result.addr, align 8
  ret ptr %t5
}

define void @"M.Run"(ptr %cell, ptr %n) #0 {
entry:
  %n.addr = alloca ptr, align 8
  %p.addr = alloca ptr, align 8
  store ptr %n, ptr %n.addr, align 8
  store ptr @"M.Add", ptr %p.addr, align 8
  %t1 = getelementptr inbounds %"M.TCell", ptr %cell, i32 0, i32 0
  store ptr @"M.Add", ptr %t1, align 8
  %t2 = load ptr, ptr %p.addr, align 8
  %t3 = getelementptr inbounds [2 x ptr], ptr @"M.Table", i64 0, i64 0
  store ptr %t2, ptr %t3, align 8
  %t4 = getelementptr inbounds [2 x ptr], ptr @"M.Table", i64 0, i64 1
  store ptr null, ptr %t4, align 8
  %t5 = load ptr, ptr %p.addr, align 8
  %t6 = icmp ne ptr %t5, null
  br i1 %t6, label %if.then.1, label %if.end.1
if.then.1:
  %t7 = load ptr, ptr %n.addr, align 8
  %t8 = getelementptr inbounds %"M.TCell", ptr %cell, i32 0, i32 0
  %t9 = load ptr, ptr %t8, align 8
  %t10 = call i32 @"M.Apply"(ptr %t9, i32 2)
  %t11 = getelementptr inbounds [2 x ptr], ptr @"M.Table", i64 0, i64 0
  %t12 = load ptr, ptr %t11, align 8
  %t13 = call i32 %t12(i32 1, i32 2)
  %t14 = add i32 %t10, %t13
  call void %t7(i32 %t14)
  br label %if.end.1
if.end.1:
  %t15 = load ptr, ptr %n.addr, align 8
  %t16 = call ptr @"M.Pick"(i32 0)
  %t17 = call i32 %t16(i32 3, i32 4)
  call void %t15(i32 %t17)
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
