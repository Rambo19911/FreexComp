; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 120, i16 61, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 72, i16 101, i16 108, i16 108, i16 111, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [9 x i16] } { i32 -1, i32 8, i16 1200, i16 2, i32 0, [9 x i16] [i16 71, i16 114, i16 252, i16 223, i16 101, i16 32, i16 -10179, i16 -8704, i16 0] }, align 16
@"M.Log" = internal global [624 x i8] zeroinitializer, align 8
@"M.Flag" = internal global i32 zeroinitializer, align 4
@"System.Output" = external global [624 x i8], align 8

declare void @"System._IOCheck"()
declare void @"System._WriteAChar"(ptr, i8, i32)
declare void @"System._WriteBool"(ptr, i8, i32)
declare void @"System._WriteEnd"(ptr)
declare void @"System._WriteInt"(ptr, i64, i32)
declare void @"System._WriteLn"(ptr)
declare void @"System._WriteUInt"(ptr, i64, i32)
declare void @"System._WriteUStr"(ptr, ptr, i32)
declare void @"System._WriteWChar"(ptr, i16, i32)

define void @"M.Show"(i32 %x, i32 %c, i8 %b, i16 %ch) #0 {
entry:
  %x.addr = alloca i32, align 4
  %c.addr = alloca i32, align 4
  %b.addr = alloca i8, align 1
  %ch.addr = alloca i16, align 2
  store i32 %x, ptr %x.addr, align 4
  store i32 %c, ptr %c.addr, align 4
  store i8 %b, ptr %b.addr, align 1
  store i16 %ch, ptr %ch.addr, align 2
  call void @"System._WriteUStr"(ptr @"System.Output", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), i32 0)
  %t1 = load i32, ptr %x.addr, align 4
  %t2 = sext i32 %t1 to i64
  call void @"System._WriteInt"(ptr @"System.Output", i64 %t2, i32 5)
  call void @"System._WriteLn"(ptr @"System.Output")
  call void @"System._WriteEnd"(ptr @"System.Output")
  %t3 = load i32, ptr %c.addr, align 4
  %t4 = zext i32 %t3 to i64
  call void @"System._WriteUInt"(ptr @"System.Output", i64 %t4, i32 0)
  %t5 = load i8, ptr %b.addr, align 1
  %t6 = icmp ne i8 %t5, 0
  %t7 = zext i1 %t6 to i8
  call void @"System._WriteBool"(ptr @"System.Output", i8 %t7, i32 6)
  %t8 = load i16, ptr %ch.addr, align 2
  call void @"System._WriteWChar"(ptr @"System.Output", i16 %t8, i32 0)
  %t9 = load i16, ptr %ch.addr, align 2
  call void @"System._WriteWChar"(ptr @"System.Output", i16 %t9, i32 3)
  call void @"System._WriteEnd"(ptr @"System.Output")
  call void @"System._WriteLn"(ptr @"System.Output")
  call void @"System._WriteEnd"(ptr @"System.Output")
  call void @"System._WriteLn"(ptr @"System.Output")
  call void @"System._WriteEnd"(ptr @"System.Output")
  call void @"System._WriteUStr"(ptr @"System.Output", ptr null, i32 0)
  call void @"System._WriteEnd"(ptr @"System.Output")
  call void @"System._WriteUStr"(ptr @"System.Output", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.1, i32 0, i32 5), i32 0)
  call void @"System._WriteUStr"(ptr @"System.Output", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), i32 0)
  %t10 = load i32, ptr %x.addr, align 4
  call void @"System._WriteUStr"(ptr @"System.Output", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.1, i32 0, i32 5), i32 %t10)
  call void @"System._WriteLn"(ptr @"System.Output")
  call void @"System._WriteEnd"(ptr @"System.Output")
  br label %exit
exit:
  ret void
}

define void @"M.ToFile"(ptr %T, i64 %n, i64 %u) #0 {
entry:
  %n.addr = alloca i64, align 8
  %u.addr = alloca i64, align 8
  %w.addr = alloca i8, align 1
  %a.addr = alloca i8, align 1
  %s.addr = alloca i8, align 1
  store i64 %n, ptr %n.addr, align 8
  store i64 %u, ptr %u.addr, align 8
  store i8 4, ptr %w.addr, align 1
  store i8 113, ptr %a.addr, align 1
  store i8 -1, ptr %s.addr, align 1
  %t1 = load i64, ptr %n.addr, align 8
  call void @"System._WriteInt"(ptr %T, i64 %t1, i32 0)
  call void @"System._WriteWChar"(ptr %T, i16 32, i32 0)
  %t2 = load i32, ptr @"M.Flag", align 4
  %t3 = icmp ne i32 %t2, 0
  %t4 = zext i1 %t3 to i8
  call void @"System._WriteBool"(ptr %T, i8 %t4, i32 0)
  %t5 = load i8, ptr %a.addr, align 1
  %t6 = load i8, ptr %w.addr, align 1
  %t7 = zext i8 %t6 to i32
  call void @"System._WriteAChar"(ptr %T, i8 %t5, i32 %t7)
  %t8 = load i8, ptr %s.addr, align 1
  %t9 = sext i8 %t8 to i64
  call void @"System._WriteInt"(ptr %T, i64 %t9, i32 0)
  %t10 = load i64, ptr %u.addr, align 8
  call void @"System._WriteUInt"(ptr %T, i64 %t10, i32 0)
  call void @"System._WriteLn"(ptr %T)
  call void @"System._WriteEnd"(ptr %T)
  call void @"System._IOCheck"()
  call void @"System._WriteUStr"(ptr @"M.Log", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [9 x i16] }, ptr @.str.2, i32 0, i32 5), i32 0)
  call void @"System._WriteBool"(ptr @"M.Log", i8 1, i32 0)
  call void @"System._WriteEnd"(ptr @"M.Log")
  call void @"System._IOCheck"()
  call void @"System._WriteEnd"(ptr %T)
  call void @"System._IOCheck"()
  br label %exit
exit:
  ret void
}

attributes #0 = { uwtable "frame-pointer"="all" }
