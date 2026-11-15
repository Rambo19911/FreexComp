; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

declare i32 @"System.IOResult"()
declare void @"System.Reset"(ptr)
declare void @"System.SetInOutRes"(i32)
declare void @"System._IOCheck"()
declare void @"System._ReadEnd"(ptr)
declare i64 @"System._ReadInt"(ptr)
declare void @"System._WriteEnd"(ptr)
declare void @"System._WriteInt"(ptr, i64, i32)
declare void @"System._WriteLn"(ptr)

define void @"M.Checked"(ptr %F, ptr %I) #0 {
entry:
  %$tmp1.addr = alloca i64, align 8
  call void @"System.Reset"(ptr %F)
  call void @"System._IOCheck"()
  %t1 = call i64 @"System._ReadInt"(ptr %F)
  store i64 %t1, ptr %$tmp1.addr, align 8
  %t2 = load i64, ptr %$tmp1.addr, align 8
  %t3 = icmp slt i64 %t2, -2147483648
  br i1 %t3, label %or.end.1, label %or.rhs.1
or.rhs.1:
  %t4 = load i64, ptr %$tmp1.addr, align 8
  %t5 = icmp sgt i64 %t4, 2147483647
  br label %or.end.1
or.end.1:
  %t6 = phi i1 [ true, %entry ], [ %t5, %or.rhs.1 ]
  br i1 %t6, label %if.then.2, label %if.end.2
if.then.2:
  call void @"System.SetInOutRes"(i32 106)
  br label %if.end.2
if.end.2:
  %t7 = load i64, ptr %$tmp1.addr, align 8
  %t8 = trunc i64 %t7 to i32
  store i32 %t8, ptr %I, align 4
  call void @"System._ReadEnd"(ptr %F)
  call void @"System._IOCheck"()
  %t9 = load i32, ptr %I, align 4
  %t10 = sext i32 %t9 to i64
  call void @"System._WriteInt"(ptr %F, i64 %t10, i32 0)
  call void @"System._WriteLn"(ptr %F)
  call void @"System._WriteEnd"(ptr %F)
  call void @"System._IOCheck"()
  br label %exit
exit:
  ret void
}

define i32 @"M.Unchecked"(ptr %F, ptr %I) #0 {
entry:
  %Result.addr = alloca i32, align 4
  %$tmp1.addr = alloca i64, align 8
  call void @"System.Reset"(ptr %F)
  %t1 = call i64 @"System._ReadInt"(ptr %F)
  store i64 %t1, ptr %$tmp1.addr, align 8
  %t2 = load i64, ptr %$tmp1.addr, align 8
  %t3 = icmp slt i64 %t2, -2147483648
  br i1 %t3, label %or.end.1, label %or.rhs.1
or.rhs.1:
  %t4 = load i64, ptr %$tmp1.addr, align 8
  %t5 = icmp sgt i64 %t4, 2147483647
  br label %or.end.1
or.end.1:
  %t6 = phi i1 [ true, %entry ], [ %t5, %or.rhs.1 ]
  br i1 %t6, label %if.then.2, label %if.end.2
if.then.2:
  call void @"System.SetInOutRes"(i32 106)
  br label %if.end.2
if.end.2:
  %t7 = load i64, ptr %$tmp1.addr, align 8
  %t8 = trunc i64 %t7 to i32
  store i32 %t8, ptr %I, align 4
  call void @"System._ReadEnd"(ptr %F)
  %t9 = load i32, ptr %I, align 4
  %t10 = sext i32 %t9 to i64
  call void @"System._WriteInt"(ptr %F, i64 %t10, i32 0)
  call void @"System._WriteLn"(ptr %F)
  call void @"System._WriteEnd"(ptr %F)
  %t11 = call i32 @"System.IOResult"()
  store i32 %t11, ptr %Result.addr, align 4
  br label %exit
exit:
  %t12 = load i32, ptr %Result.addr, align 4
  ret i32 %t12
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
