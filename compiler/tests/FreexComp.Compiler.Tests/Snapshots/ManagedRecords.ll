; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.TPerson" = type { ptr, i32, ptr }
%"M.TSmall" = type { ptr }

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 84, i16 83, i16 109, i16 97, i16 108, i16 108, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 115, i16 116, i16 114, i16 105, i16 110, i16 103, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 84, i16 80, i16 101, i16 114, i16 115, i16 111, i16 110, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [11 x i16] } { i32 -1, i32 10, i16 1200, i16 2, i32 0, [11 x i16] [i16 65, i16 110, i16 115, i16 105, i16 83, i16 116, i16 114, i16 105, i16 110, i16 103, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 120, i16 0] }, align 16
@.str.5 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 84, i16 78, i16 97, i16 109, i16 101, i16 115, i16 0] }, align 16
@"M.TSmall.$rtti.fields" = private unnamed_addr constant [1 x { ptr, i64 }] [{ ptr, i64 } { ptr @"System.string.$rtti", i64 0 }], align 8
@"M.TPerson.$rtti.fields" = private unnamed_addr constant [2 x { ptr, i64 }] [{ ptr, i64 } { ptr @"System.string.$rtti", i64 0 }, { ptr, i64 } { ptr @"M.$rtti.array of AnsiString", i64 16 }], align 8
$"System.string.$rtti" = comdat any
@"System.string.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i16, [6 x i8] } { i8 9, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.1, i32 0, i32 5), i16 1200, [6 x i8] zeroinitializer }, comdat, align 8
$"M.TSmall.$rtti" = comdat any
@"M.TSmall.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, i64, ptr } { i8 12, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.0, i32 0, i32 5), i64 8, i64 1, ptr @"M.TSmall.$rtti.fields" }, comdat, align 8
$"System.AnsiString.$rtti" = comdat any
@"System.AnsiString.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i16, [6 x i8] } { i8 10, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [11 x i16] }, ptr @.str.3, i32 0, i32 5), i16 -535, [6 x i8] zeroinitializer }, comdat, align 8
$"M.$rtti.array of AnsiString" = comdat any
@"M.$rtti.array of AnsiString" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr null, i64 8, ptr @"System.AnsiString.$rtti", ptr @"System.AnsiString.$rtti" }, comdat, align 8
$"M.TPerson.$rtti" = comdat any
@"M.TPerson.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, i64, ptr } { i8 12, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.2, i32 0, i32 5), i64 24, i64 2, ptr @"M.TPerson.$rtti.fields" }, comdat, align 8
$"M.TNames.$rtti" = comdat any
@"M.TNames.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i64, i64, ptr } { i8 13, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.5, i32 0, i32 5), i64 16, i64 2, ptr @"System.string.$rtti" }, comdat, align 8

declare void @"System._AddRef"(ptr, ptr)
declare void @"System._CopyRecord"(ptr, ptr, ptr)
declare void @"System._Finalize"(ptr, ptr)
declare void @"System._FreeMem"(ptr)
declare void @"System._UStrAsg"(ptr, ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.memset.p0.i64(ptr, i8, i64, i1)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.Make"(ptr %Result, ptr %n) #0 {
entry:
  %n.addr = alloca ptr, align 8
  store ptr %n, ptr %n.addr, align 8
  %t1 = getelementptr inbounds %"M.TPerson", ptr %Result, i32 0, i32 0
  %t2 = load ptr, ptr %n.addr, align 8
  call void @"System._UStrAsg"(ptr %t1, ptr %t2)
  br label %exit
exit:
  ret void
}

define void @"M.Take"(ptr %p, i64 %s, ptr %o) #0 personality ptr @__C_specific_handler {
entry:
  %s.addr = alloca %"M.TSmall", align 8
  store i64 %s, ptr %s.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  invoke void @"System._AddRef"(ptr %s.addr, ptr @"M.TSmall.$rtti")
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  invoke void @"System._Finalize"(ptr %o, ptr @"M.TPerson.$rtti")
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  invoke void @"System._CopyRecord"(ptr %o, ptr %p, ptr @"M.TPerson.$rtti")
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  %t1 = getelementptr inbounds %"M.TSmall", ptr %s.addr, i32 0, i32 0
  %t2 = getelementptr inbounds %"M.TPerson", ptr %p, i32 0, i32 0
  %t3 = load ptr, ptr %t2, align 8
  invoke void @"System._UStrAsg"(ptr %t1, ptr %t3)
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  call void @"System._Finalize"(ptr %s.addr, ptr @"M.TSmall.$rtti")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._Finalize"(ptr %s.addr, ptr @"M.TSmall.$rtti") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define void @"M.Use"(ptr %q) #0 personality ptr @__C_specific_handler {
entry:
  %q.addr = alloca ptr, align 8
  %a.addr = alloca %"M.TPerson", align 8
  %names.addr = alloca [2 x ptr], align 8
  %copy.addr = alloca [2 x ptr], align 8
  %small.addr = alloca %"M.TSmall", align 8
  %$tmp1.addr = alloca %"M.TPerson", align 8
  %$tmp2.addr = alloca ptr, align 8
  store ptr %q, ptr %q.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  call void @llvm.memset.p0.i64(ptr align 8 %a.addr, i8 0, i64 24, i1 false)
  call void @llvm.memset.p0.i64(ptr align 8 %names.addr, i8 0, i64 16, i1 false)
  call void @llvm.memset.p0.i64(ptr align 8 %copy.addr, i8 0, i64 16, i1 false)
  call void @llvm.memset.p0.i64(ptr align 8 %small.addr, i8 0, i64 8, i1 false)
  call void @llvm.memset.p0.i64(ptr align 8 %$tmp1.addr, i8 0, i64 24, i1 false)
  invoke void @"M.Make"(ptr %a.addr, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.4, i32 0, i32 5))
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t1 = getelementptr inbounds [2 x ptr], ptr %names.addr, i64 0, i64 0
  %t2 = getelementptr inbounds %"M.TPerson", ptr %a.addr, i32 0, i32 0
  %t3 = load ptr, ptr %t2, align 8
  invoke void @"System._UStrAsg"(ptr %t1, ptr %t3)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  invoke void @"System._CopyRecord"(ptr %copy.addr, ptr %names.addr, ptr @"M.TNames.$rtti")
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  invoke void @"System._CopyRecord"(ptr %$tmp1.addr, ptr %a.addr, ptr @"M.TPerson.$rtti")
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  %t4 = load i64, ptr %small.addr, align 8
  %t5 = load ptr, ptr %q.addr, align 8
  invoke void @"M.Take"(ptr %$tmp1.addr, i64 %t4, ptr %t5)
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  %t6 = load ptr, ptr %q.addr, align 8
  store ptr %t6, ptr %$tmp2.addr, align 8
  %t7 = load ptr, ptr %$tmp2.addr, align 8
  invoke void @"System._Finalize"(ptr %t7, ptr @"M.TPerson.$rtti")
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  %t8 = load ptr, ptr %$tmp2.addr, align 8
  call void @"System._FreeMem"(ptr %t8)
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  call void @"System._Finalize"(ptr %a.addr, ptr @"M.TPerson.$rtti")
  call void @"System._Finalize"(ptr %names.addr, ptr @"M.TNames.$rtti")
  call void @"System._Finalize"(ptr %copy.addr, ptr @"M.TNames.$rtti")
  call void @"System._Finalize"(ptr %small.addr, ptr @"M.TSmall.$rtti")
  call void @"System._Finalize"(ptr %$tmp1.addr, ptr @"M.TPerson.$rtti")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._Finalize"(ptr %a.addr, ptr @"M.TPerson.$rtti") [ "funclet"(token %cl) ]
  call void @"System._Finalize"(ptr %names.addr, ptr @"M.TNames.$rtti") [ "funclet"(token %cl) ]
  call void @"System._Finalize"(ptr %copy.addr, ptr @"M.TNames.$rtti") [ "funclet"(token %cl) ]
  call void @"System._Finalize"(ptr %small.addr, ptr @"M.TSmall.$rtti") [ "funclet"(token %cl) ]
  call void @"System._Finalize"(ptr %$tmp1.addr, ptr @"M.TPerson.$rtti") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
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

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
