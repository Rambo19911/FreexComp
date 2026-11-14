; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.Outer.$frame" = type { ptr, i32 }
%"M.Twice.$frame" = type { ptr, ptr, i32, i32 }
%"M.Twice.Get.$frame" = type { ptr, i32 }

define i32 @"M.Outer"() #0 {
entry:
  %$frame = alloca %"M.Outer.$frame", align 8
  %Result.addr = alloca i32, align 4
  store ptr null, ptr %$frame, align 8
  %total.addr = getelementptr inbounds %"M.Outer.$frame", ptr %$frame, i32 0, i32 1
  store i32 0, ptr %total.addr, align 4
  call void @"M.Outer.Add"(ptr %$frame, i32 2)
  call void @"M.Outer.Add"(ptr %$frame, i32 40)
  %t1 = load i32, ptr %total.addr, align 4
  store i32 %t1, ptr %Result.addr, align 4
  br label %exit
exit:
  %t2 = load i32, ptr %Result.addr, align 4
  ret i32 %t2
}

define internal void @"M.Outer.Add"(ptr %$link, i32 %x) #0 {
entry:
  %x.addr = alloca i32, align 4
  store i32 %x, ptr %x.addr, align 4
  %t1 = getelementptr inbounds %"M.Outer.$frame", ptr %$link, i32 0, i32 1
  %t2 = load i32, ptr %t1, align 4
  %t3 = load i32, ptr %x.addr, align 4
  %t4 = add i32 %t2, %t3
  %t5 = getelementptr inbounds %"M.Outer.$frame", ptr %$link, i32 0, i32 1
  store i32 %t4, ptr %t5, align 4
  br label %exit
exit:
  ret void
}

define void @"M.Twice"(ptr %v, i32 %k) #0 {
entry:
  %$frame = alloca %"M.Twice.$frame", align 8
  store ptr null, ptr %$frame, align 8
  %v.addr = getelementptr inbounds %"M.Twice.$frame", ptr %$frame, i32 0, i32 1
  %k.addr = getelementptr inbounds %"M.Twice.$frame", ptr %$frame, i32 0, i32 2
  %calls.addr = getelementptr inbounds %"M.Twice.$frame", ptr %$frame, i32 0, i32 3
  store ptr %v, ptr %v.addr, align 8
  store i32 %k, ptr %k.addr, align 4
  store i32 0, ptr %calls.addr, align 4
  call void @"M.Twice.Again"(ptr %$frame)
  br label %exit
exit:
  ret void
}

define internal i32 @"M.Twice.Get"(ptr %$link) #0 {
entry:
  %$frame = alloca %"M.Twice.Get.$frame", align 8
  store ptr %$link, ptr %$frame, align 8
  %Result.addr = getelementptr inbounds %"M.Twice.Get.$frame", ptr %$frame, i32 0, i32 1
  call void @"M.Twice.Get.Bump"(ptr %$frame)
  call void @"M.Twice.Get.Bump"(ptr %$frame)
  br label %exit
exit:
  %t1 = load i32, ptr %Result.addr, align 4
  ret i32 %t1
}

define internal void @"M.Twice.Get.Bump"(ptr %$link) #0 {
entry:
  %t1 = load ptr, ptr %$link, align 8
  %t2 = getelementptr inbounds %"M.Twice.$frame", ptr %t1, i32 0, i32 3
  %t3 = load i32, ptr %t2, align 4
  %t4 = add i32 %t3, 1
  %t5 = load ptr, ptr %$link, align 8
  %t6 = getelementptr inbounds %"M.Twice.$frame", ptr %t5, i32 0, i32 3
  store i32 %t4, ptr %t6, align 4
  %t7 = load ptr, ptr %$link, align 8
  %t8 = getelementptr inbounds %"M.Twice.$frame", ptr %t7, i32 0, i32 1
  %t9 = load ptr, ptr %t8, align 8
  %t10 = load i32, ptr %t9, align 4
  %t11 = load ptr, ptr %$link, align 8
  %t12 = getelementptr inbounds %"M.Twice.$frame", ptr %t11, i32 0, i32 2
  %t13 = load i32, ptr %t12, align 4
  %t14 = add i32 %t10, %t13
  %t15 = load ptr, ptr %$link, align 8
  %t16 = getelementptr inbounds %"M.Twice.$frame", ptr %t15, i32 0, i32 1
  %t17 = load ptr, ptr %t16, align 8
  store i32 %t14, ptr %t17, align 4
  %t18 = load ptr, ptr %$link, align 8
  %t19 = getelementptr inbounds %"M.Twice.$frame", ptr %t18, i32 0, i32 3
  %t20 = load i32, ptr %t19, align 4
  %t21 = getelementptr inbounds %"M.Twice.Get.$frame", ptr %$link, i32 0, i32 1
  store i32 %t20, ptr %t21, align 4
  br label %exit
exit:
  ret void
}

define internal void @"M.Twice.Again"(ptr %$link) #0 {
entry:
  %t1 = call i32 @"M.Twice.Get"(ptr %$link)
  %t2 = getelementptr inbounds %"M.Twice.$frame", ptr %$link, i32 0, i32 3
  store i32 %t1, ptr %t2, align 4
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
