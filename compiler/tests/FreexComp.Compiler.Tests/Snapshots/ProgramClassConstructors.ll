; ModuleID = 'P'
source_filename = "P.pas"
target triple = "x86_64-pc-windows-msvc"

@"$units" = internal constant [2 x { ptr, ptr }] [{ ptr, ptr } { ptr null, ptr null }, { ptr, ptr } { ptr null, ptr @"P.$final" }], align 8
@"P.Runs" = internal global i32 zeroinitializer, align 4
@"P.$ccdone" = internal global [1 x i8] zeroinitializer, align 1

declare ptr @"System._BeginCatch"()
declare void @"System._Halt0"() #1
declare void @"System._InitUnits"()
declare i32 @"System._SehFilter"(ptr, ptr)
declare void @"System._Startup"(ptr, i64)
declare void @"System._UnhandledException"() #1
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define internal void @"P.TP.$cctor"() #0 {
entry:
  store i32 1, ptr @"P.Runs", align 4
  br label %exit
exit:
  ret void
}

define internal void @"P.TP.$cdtor"() #0 {
entry:
  store i32 0, ptr @"P.Runs", align 4
  br label %exit
exit:
  ret void
}

define internal void @"$main"() #0 {
entry:
  call void @"System._InitUnits"()
  call void @"P.TP.$cctor"()
  store i8 1, ptr @"P.$ccdone", align 1
  %t1 = load i32, ptr @"P.Runs", align 4
  %t2 = add i32 %t1, 1
  store i32 %t2, ptr @"P.Runs", align 4
  br label %exit
exit:
  ret void
}

define internal void @"P.$final"() #0 {
entry:
  %cd0.done = load i8, ptr @"P.$ccdone", align 1
  %cd0.ran = icmp ne i8 %cd0.done, 0
  br i1 %cd0.ran, label %cd0.run, label %cd0.skip
cd0.run:
  call void @"P.TP.$cdtor"()
  br label %cd0.skip
cd0.skip:
  br label %exit
exit:
  ret void
}

define void @_start() #0 personality ptr @__C_specific_handler {
entry:
  call void @"System._Startup"(ptr @"$units", i64 2)
  invoke void @llvm.seh.try.begin()
          to label %try.begun unwind label %catch.dispatch
try.begun:
  invoke void @"$main"()
          to label %try.called unwind label %catch.dispatch
try.called:
  invoke void @llvm.seh.try.end()
          to label %done unwind label %catch.dispatch
done:
  call void @"System._Halt0"() #1
  unreachable
catch.dispatch:
  %cs = catchswitch within none [label %catch] unwind to caller
catch:
  %cp = catchpad within %cs [ptr @"System._SehFilter"]
  catchret from %cp to label %unhandled
unhandled:
  %t1 = call ptr @"System._BeginCatch"()
  call void @"System._UnhandledException"() #1
  unreachable
}

attributes #0 = { uwtable "frame-pointer"="all" }
attributes #1 = { noreturn uwtable }

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
