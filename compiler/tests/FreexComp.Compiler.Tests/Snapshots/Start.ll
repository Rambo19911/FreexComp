; ModuleID = 'HelloWorld'
source_filename = "HelloWorld.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [14 x i16] } { i32 -1, i32 13, i16 1200, i16 2, i32 0, [14 x i16] [i16 72, i16 101, i16 108, i16 108, i16 111, i16 44, i16 32, i16 119, i16 111, i16 114, i16 108, i16 100, i16 33, i16 0] }, align 16
@"$units" = internal constant [1 x { ptr, ptr }] [{ ptr, ptr } { ptr null, ptr null }], align 8
@"System.Output" = external global [624 x i8], align 8

declare ptr @"System._BeginCatch"()
declare void @"System._Halt0"() #1
declare void @"System._IOCheck"()
declare void @"System._InitUnits"()
declare i32 @"System._SehFilter"(ptr, ptr)
declare void @"System._Startup"(ptr, i64)
declare void @"System._UnhandledException"() #1
declare void @"System._WriteEnd"(ptr)
declare void @"System._WriteLn"(ptr)
declare void @"System._WriteUStr"(ptr, ptr, i32)
declare i32 @__C_specific_handler(...)

define internal void @"$main"() #0 {
entry:
  call void @"System._InitUnits"()
  call void @"System._WriteUStr"(ptr @"System.Output", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [14 x i16] }, ptr @.str.0, i32 0, i32 5), i32 0)
  call void @"System._WriteLn"(ptr @"System.Output")
  call void @"System._WriteEnd"(ptr @"System.Output")
  call void @"System._IOCheck"()
  br label %exit
exit:
  ret void
}

define void @_start() #0 personality ptr @__C_specific_handler {
entry:
  call void @"System._Startup"(ptr @"$units", i64 1)
  invoke void @"$main"()
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
