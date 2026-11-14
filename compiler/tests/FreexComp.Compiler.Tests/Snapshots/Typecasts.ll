; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.TPacked" = type <{ i8, i32 }>
%"M.TRec8" = type { i32, i32 }

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 102, i16 108, i16 97, i16 103, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 77, i16 46, i16 112, i16 97, i16 115, i16 0] }, align 16

declare void @"System._Assert"(ptr, ptr, i32) #1

define void @"M.Use"(ptr %r, i32 %m) #0 {
entry:
  %m.addr = alloca i32, align 4
  %i.addr = alloca i32, align 4
  %b.addr = alloca i8, align 1
  %q.addr = alloca i64, align 8
  %p.addr = alloca ptr, align 8
  %pc.addr = alloca ptr, align 8
  %f.addr = alloca i8, align 1
  %k.addr = alloca %"M.TPacked", align 1
  store i32 %m, ptr %m.addr, align 4
  %t1 = load i8, ptr %b.addr, align 1
  %t2 = sext i8 %t1 to i64
  %t3 = inttoptr i64 %t2 to ptr
  store ptr %t3, ptr %p.addr, align 8
  %t4 = load ptr, ptr %p.addr, align 8
  store ptr %t4, ptr %pc.addr, align 8
  %t5 = load ptr, ptr %pc.addr, align 8
  %t6 = ptrtoint ptr %t5 to i64
  store i64 %t6, ptr %q.addr, align 8
  %t7 = load i64, ptr %q.addr, align 8
  %t8 = trunc i64 %t7 to i32
  store i32 %t8, ptr %i.addr, align 4
  %t9 = load i32, ptr %i.addr, align 4
  %t10 = icmp ne i32 %t9, 0
  %t11 = zext i1 %t10 to i8
  store i8 %t11, ptr %f.addr, align 1
  %t12 = load i64, ptr %r, align 4
  store i64 %t12, ptr %q.addr, align 8
  %t13 = load i32, ptr %m.addr, align 4
  %t14 = getelementptr inbounds %"M.TRec8", ptr %q.addr, i32 0, i32 1
  store i32 %t13, ptr %t14, align 4
  %t15 = getelementptr inbounds %"M.TPacked", ptr %k.addr, i32 0, i32 1
  store i32 7, ptr %t15, align 1
  %t16 = load i8, ptr %f.addr, align 1
  %t17 = icmp ne i8 %t16, 0
  %t18 = xor i1 %t17, true
  br i1 %t18, label %if.then.1, label %if.end.1
if.then.1:
  call void @"System._Assert"(ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.0, i32 0, i32 5), ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.1, i32 0, i32 5), i32 27) #1
  unreachable
if.end.1:
  %t19 = load i32, ptr %i.addr, align 4
  %t20 = load i32, ptr %m.addr, align 4
  %t21 = icmp sgt i32 %t19, %t20
  %t22 = xor i1 %t21, true
  br i1 %t22, label %if.then.2, label %if.end.2
if.then.2:
  call void @"System._Assert"(ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.1, i32 0, i32 5), i32 28) #1
  unreachable
if.end.2:
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
attributes #1 = { noreturn uwtable }
