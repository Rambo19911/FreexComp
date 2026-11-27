; ModuleID = 'cdlib'
source_filename = "cdlib.pas"
target triple = "x86_64-pc-windows-msvc"

@"$units" = internal constant [3 x { ptr, ptr }] [{ ptr, ptr } { ptr null, ptr null }, { ptr, ptr } { ptr @"CdUnit.$init", ptr null }, { ptr, ptr } { ptr null, ptr @"cdlib.$final" }], align 8
@"cdlib.Runs" = internal global i32 zeroinitializer, align 4
@"cdlib.$ccdone" = internal global [1 x i8] zeroinitializer, align 1
@"CdUnit.Shared" = external global i32, align 4

declare void @"CdUnit.$init"()
declare ptr @"System._BeginCatch"()
declare i32 @"System._DllEntry"(ptr, i32, ptr, ptr, i64)
declare void @"System._DllInitDone"()
declare void @"System._DllInitFailed"()
declare void @"System._InitUnits"()
declare i32 @"System._SehFilter"(ptr, ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define internal void @"cdlib.TLib.$cctor"() #0 {
entry:
  %t1 = load i32, ptr @"cdlib.Runs", align 4
  %t2 = add i32 %t1, 1
  store i32 %t2, ptr @"cdlib.Runs", align 4
  br label %exit
exit:
  ret void
}

define internal void @"cdlib.TLib.$cdtor"() #0 {
entry:
  store i32 0, ptr @"cdlib.Runs", align 4
  br label %exit
exit:
  ret void
}

define internal i32 @"cdlib.Ping"() #0 {
entry:
  %Result.addr = alloca i32, align 4
  %t1 = load i32, ptr @"cdlib.Runs", align 4
  %t2 = load i32, ptr @"CdUnit.Shared", align 4
  %t3 = add i32 %t1, %t2
  store i32 %t3, ptr %Result.addr, align 4
  br label %exit
exit:
  %t4 = load i32, ptr %Result.addr, align 4
  ret i32 %t4
}

define internal void @"$main"() #0 {
entry:
  call void @"System._InitUnits"()
  call void @"cdlib.TLib.$cctor"()
  store i8 1, ptr @"cdlib.$ccdone", align 1
  %t1 = load i32, ptr @"cdlib.Runs", align 4
  %t2 = add i32 %t1, 10
  store i32 %t2, ptr @"cdlib.Runs", align 4
  br label %exit
exit:
  ret void
}

define internal void @"cdlib.$final"() #0 {
entry:
  %cd0.done = load i8, ptr @"cdlib.$ccdone", align 1
  %cd0.ran = icmp ne i8 %cd0.done, 0
  br i1 %cd0.ran, label %cd0.run, label %cd0.skip
cd0.run:
  call void @"cdlib.TLib.$cdtor"()
  br label %cd0.skip
cd0.skip:
  br label %exit
exit:
  ret void
}

define i32 @_dllstart(ptr %hinst, i32 %reason, ptr %reserved) #0 personality ptr @__C_specific_handler {
entry:
  %init = call i32 @"System._DllEntry"(ptr %hinst, i32 %reason, ptr %reserved, ptr @"$units", i64 3)
  %run = icmp ne i32 %init, 0
  br i1 %run, label %attach, label %ok
attach:
  invoke void @llvm.seh.try.begin()
          to label %try.begun unwind label %catch.dispatch
try.begun:
  invoke void @"$main"()
          to label %try.called unwind label %catch.dispatch
try.called:
  invoke void @llvm.seh.try.end()
          to label %attached unwind label %catch.dispatch
attached:
  call void @"System._DllInitDone"()
  br label %ok
ok:
  ret i32 1
catch.dispatch:
  %cs = catchswitch within none [label %catch] unwind to caller
catch:
  %cp = catchpad within %cs [ptr @"System._SehFilter"]
  catchret from %cp to label %failed
failed:
  %t1 = call ptr @"System._BeginCatch"()
  call void @"System._DllInitFailed"()
  ret i32 0
}

define i32 @Ping() #0 {
entry:
  %r = call i32 @"cdlib.Ping"()
  ret i32 %r
}

attributes #0 = { uwtable "frame-pointer"="all" }

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
