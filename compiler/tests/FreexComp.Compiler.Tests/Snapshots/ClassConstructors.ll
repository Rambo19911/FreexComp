; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 105, i16 110, i16 105, i16 116, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 84, i16 65, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 84, i16 78, i16 0] }, align 16
@"M.TA.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.1, i32 0, i32 5), ptr @"M.TA.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.2, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 0, ptr null, i64 0, ptr null }, align 8
@"M.TA.TN.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.3, i32 0, i32 5), ptr @"M.TA.TN.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.2, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 0, ptr null, i64 0, ptr null }, align 8
@"M.TA.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 8, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.1, i32 0, i32 5), ptr @"M.TA.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.TA.TN.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 8, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.3, i32 0, i32 5), ptr @"M.TA.TN.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.Name" = global ptr zeroinitializer, align 8
@"M.TA.Count" = global i32 zeroinitializer, align 4
@"M.$ccdone" = internal global [3 x i8] zeroinitializer, align 1

declare void @"System._UStrAsg"(ptr, ptr)
declare void @"System._UStrClr"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define internal void @"M.TA.$cctor"() #0 {
entry:
  store i32 1, ptr @"M.TA.Count", align 4
  br label %exit
exit:
  ret void
}

define internal void @"M.TA.$cdtor"() #0 {
entry:
  store i32 0, ptr @"M.TA.Count", align 4
  br label %exit
exit:
  ret void
}

define internal void @"M.TA.TN.$cdtor"() #0 {
entry:
  store i32 2, ptr @"M.TA.Count", align 4
  br label %exit
exit:
  ret void
}

define internal void @"M.TR.$cctor"() #0 {
entry:
  store i32 3, ptr @"M.TA.Count", align 4
  br label %exit
exit:
  ret void
}

define void @"M.$init"() #0 {
entry:
  call void @"M.TA.$cctor"()
  store i8 1, ptr @"M.$ccdone", align 1
  store i8 1, ptr getelementptr inbounds ([3 x i8], ptr @"M.$ccdone", i64 0, i64 1), align 1
  call void @"M.TR.$cctor"()
  store i8 1, ptr getelementptr inbounds ([3 x i8], ptr @"M.$ccdone", i64 0, i64 2), align 1
  call void @"System._UStrAsg"(ptr @"M.Name", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.0, i32 0, i32 5))
  br label %exit
exit:
  ret void
}

define void @"M.$final"() #0 personality ptr @__C_specific_handler {
entry:
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  invoke void @"System._UStrAsg"(ptr @"M.Name", ptr null)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %cd1.done = load i8, ptr getelementptr inbounds ([3 x i8], ptr @"M.$ccdone", i64 0, i64 1), align 1
  %cd1.ran = icmp ne i8 %cd1.done, 0
  br i1 %cd1.ran, label %cd1.run, label %cd1.skip
cd1.run:
  invoke void @"M.TA.TN.$cdtor"()
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  br label %cd1.skip
cd1.skip:
  %cd0.done = load i8, ptr @"M.$ccdone", align 1
  %cd0.ran = icmp ne i8 %cd0.done, 0
  br i1 %cd0.ran, label %cd0.run, label %cd0.skip
cd0.run:
  invoke void @"M.TA.$cdtor"()
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  br label %cd0.skip
cd0.skip:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  call void @"System._UStrClr"(ptr @"M.Name")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr @"M.Name") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

attributes #0 = { uwtable "frame-pointer"="all" }

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
