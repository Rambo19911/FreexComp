; ModuleID = 'HelloWorld'
source_filename = "HelloWorld.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [14 x i16] } { i32 -1, i32 13, i16 1200, i16 2, i32 0, [14 x i16] [i16 72, i16 101, i16 108, i16 108, i16 111, i16 44, i16 32, i16 119, i16 111, i16 114, i16 108, i16 100, i16 33, i16 0] }, align 16
@"$units" = internal constant [1 x { ptr, ptr }] [{ ptr, ptr } { ptr null, ptr null }], align 8
@"System.Output" = external global [624 x i8], align 8

declare void @"System._Halt0"() #1
declare void @"System._IOCheck"()
declare void @"System._InitUnits"()
declare void @"System._Startup"(ptr, i64)
declare void @"System._WriteEnd"(ptr)
declare void @"System._WriteLn"(ptr)
declare void @"System._WriteUStr"(ptr, ptr, i32)

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

define void @_start() #0 {
entry:
  call void @"System._Startup"(ptr @"$units", i64 1)
  call void @"$main"()
  call void @"System._Halt0"() #1
  unreachable
}

attributes #0 = { uwtable "frame-pointer"="all" }
attributes #1 = { noreturn uwtable }
