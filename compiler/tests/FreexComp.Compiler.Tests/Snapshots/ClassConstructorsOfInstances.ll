; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

$"M.TBox<Integer>.$cctor" = comdat any
$"M.TBox<Integer>.$cdtor" = comdat any
@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 84, i16 65, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [14 x i16] } { i32 -1, i32 13, i16 1200, i16 2, i32 0, [14 x i16] [i16 84, i16 66, i16 111, i16 120, i16 60, i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 62, i16 0] }, align 16
@"M.TA.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TA.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.1, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 0, ptr null, i64 0, ptr null }, align 8
$"M.TBox<Integer>.$rtti" = comdat any
@"M.TBox<Integer>.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [14 x i16] }, ptr @.str.2, i32 0, i32 5), ptr @"M.TBox<Integer>.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.1, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 0, ptr null, i64 0, ptr null }, comdat, align 8
@"M.TA.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 8, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TA.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
$"M.TBox<Integer>.$vmt" = comdat any
@"M.TBox<Integer>.$vmt" = linkonce_odr constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 8, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [14 x i16] }, ptr @.str.2, i32 0, i32 5), ptr @"M.TBox<Integer>.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, comdat, align 8
@"M.Box" = global ptr zeroinitializer, align 8
$"M.TBox<Integer>.Size" = comdat any
@"M.TBox<Integer>.Size" = linkonce_odr global i32 zeroinitializer, comdat, align 4
@"M.$ccdone" = internal global [2 x i8] zeroinitializer, align 1
$"M.TBox<Integer>.$ccstate" = comdat any
@"M.TBox<Integer>.$ccstate" = linkonce_odr global i8 0, comdat, align 1

define internal void @"M.TA.$cctor"() #0 {
entry:
  store i32 1, ptr @"M.TBox<Integer>.Size", align 4
  br label %exit
exit:
  ret void
}

define linkonce_odr void @"M.TBox<Integer>.$cctor"() #0 comdat {
entry:
  store i32 4, ptr @"M.TBox<Integer>.Size", align 4
  br label %exit
exit:
  ret void
}

define linkonce_odr void @"M.TBox<Integer>.$cdtor"() #0 comdat {
entry:
  store i32 0, ptr @"M.TBox<Integer>.Size", align 4
  br label %exit
exit:
  ret void
}

define void @"M.$init"() #0 {
entry:
  call void @"M.TA.$cctor"()
  store i8 1, ptr @"M.$ccdone", align 1
  %cc1.state = load i8, ptr @"M.TBox<Integer>.$ccstate", align 1
  %cc1.free = icmp eq i8 %cc1.state, 0
  br i1 %cc1.free, label %cc1.run, label %cc1.done
cc1.run:
  store i8 1, ptr @"M.TBox<Integer>.$ccstate", align 1
  call void @"M.TBox<Integer>.$cctor"()
  store i8 1, ptr getelementptr inbounds ([2 x i8], ptr @"M.$ccdone", i64 0, i64 1), align 1
  br label %cc1.done
cc1.done:
  br label %exit
exit:
  ret void
}

define void @"M.$final"() #0 {
entry:
  %cd1.done = load i8, ptr getelementptr inbounds ([2 x i8], ptr @"M.$ccdone", i64 0, i64 1), align 1
  %cd1.ran = icmp ne i8 %cd1.done, 0
  br i1 %cd1.ran, label %cd1.run, label %cd1.skip
cd1.run:
  call void @"M.TBox<Integer>.$cdtor"()
  br label %cd1.skip
cd1.skip:
  br label %exit
exit:
  ret void
}

attributes #0 = { uwtable "frame-pointer"="all" }
