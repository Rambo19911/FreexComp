; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@"M.$tls" = global { i64, i64 } { i64 16, i64 -1 }, align 8

declare ptr @"System._TlsBase"()
declare void @"System._TlsRegister"(ptr)

define void @"M.Enter"(ptr %Level) #0 {
entry:
  %t1 = load i32, ptr %Level, align 4
  %t2 = add i32 %t1, 1
  store i32 %t2, ptr %Level, align 4
  br label %exit
exit:
  ret void
}

define i64 @"M.Run"() #0 {
entry:
  %Result.addr = alloca i64, align 8
  %P.addr = alloca ptr, align 8
  %t1 = call ptr @"System._TlsBase"()
  %t2 = load i64, ptr getelementptr inbounds (i8, ptr @"M.$tls", i64 8), align 8
  %t3 = getelementptr inbounds i8, ptr %t1, i64 %t2
  %t4 = getelementptr inbounds i8, ptr %t3, i64 0
  call void @"M.Enter"(ptr %t4)
  %t5 = call ptr @"System._TlsBase"()
  %t6 = load i64, ptr getelementptr inbounds (i8, ptr @"M.$tls", i64 8), align 8
  %t7 = getelementptr inbounds i8, ptr %t5, i64 %t6
  %t8 = getelementptr inbounds i8, ptr %t7, i64 4
  store i8 2, ptr %t8, align 1
  %t9 = call ptr @"System._TlsBase"()
  %t10 = load i64, ptr getelementptr inbounds (i8, ptr @"M.$tls", i64 8), align 8
  %t11 = getelementptr inbounds i8, ptr %t9, i64 %t10
  %t12 = getelementptr inbounds i8, ptr %t11, i64 8
  store ptr %t12, ptr %P.addr, align 8
  %t13 = call ptr @"System._TlsBase"()
  %t14 = load i64, ptr getelementptr inbounds (i8, ptr @"M.$tls", i64 8), align 8
  %t15 = getelementptr inbounds i8, ptr %t13, i64 %t14
  %t16 = getelementptr inbounds i8, ptr %t15, i64 0
  %t17 = load i32, ptr %t16, align 4
  %t18 = call ptr @"System._TlsBase"()
  %t19 = load i64, ptr getelementptr inbounds (i8, ptr @"M.$tls", i64 8), align 8
  %t20 = getelementptr inbounds i8, ptr %t18, i64 %t19
  %t21 = getelementptr inbounds i8, ptr %t20, i64 4
  %t22 = load i8, ptr %t21, align 1
  %t23 = zext i8 %t22 to i32
  %t24 = add i32 %t17, %t23
  %t25 = sext i32 %t24 to i64
  %t26 = load ptr, ptr %P.addr, align 8
  store i64 %t25, ptr %t26, align 8
  %t27 = call ptr @"System._TlsBase"()
  %t28 = load i64, ptr getelementptr inbounds (i8, ptr @"M.$tls", i64 8), align 8
  %t29 = getelementptr inbounds i8, ptr %t27, i64 %t28
  %t30 = getelementptr inbounds i8, ptr %t29, i64 8
  %t31 = load i64, ptr %t30, align 8
  store i64 %t31, ptr %Result.addr, align 8
  br label %exit
exit:
  %t32 = load i64, ptr %Result.addr, align 8
  ret i64 %t32
}

define void @"M.$init"() #0 {
entry:
  call void @"System._TlsRegister"(ptr @"M.$tls")
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
