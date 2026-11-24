; ModuleID = 'mathlib'
source_filename = "mathlib.pas"
target triple = "x86_64-pc-windows-msvc"

%"mathlib.TVec3" = type { i32, i32, i32 }

@"$units" = internal constant [2 x { ptr, ptr }] [{ ptr, ptr } { ptr null, ptr null }, { ptr, ptr } { ptr null, ptr null }], align 8
@"mathlib.Loaded" = internal global i32 zeroinitializer, align 4

declare i32 @"MathUnit.Twice"(i32)
declare ptr @"System._BeginCatch"()
declare i32 @"System._DllEntry"(ptr, i32, ptr, ptr, i64)
declare void @"System._DllInitDone"()
declare void @"System._DllInitFailed"()
declare void @"System._InitUnits"()
declare i32 @"System._SehFilter"(ptr, ptr)
declare i32 @__C_specific_handler(...)

define internal i32 @"mathlib.Add"(i32 %A, i32 %B) #0 {
entry:
  %A.addr = alloca i32, align 4
  %B.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %A, ptr %A.addr, align 4
  store i32 %B, ptr %B.addr, align 4
  %t1 = load i32, ptr %A.addr, align 4
  %t2 = load i32, ptr %B.addr, align 4
  %t3 = add i32 %t1, %t2
  store i32 %t3, ptr %Result.addr, align 4
  br label %exit
exit:
  %t4 = load i32, ptr %Result.addr, align 4
  ret i32 %t4
}

define internal void @"mathlib.MakeVec"(ptr %Result, i32 %X, i32 %Y, i32 %Z) #0 {
entry:
  %X.addr = alloca i32, align 4
  %Y.addr = alloca i32, align 4
  %Z.addr = alloca i32, align 4
  store i32 %X, ptr %X.addr, align 4
  store i32 %Y, ptr %Y.addr, align 4
  store i32 %Z, ptr %Z.addr, align 4
  %t1 = load i32, ptr %X.addr, align 4
  %t2 = getelementptr inbounds %"mathlib.TVec3", ptr %Result, i32 0, i32 0
  store i32 %t1, ptr %t2, align 4
  %t3 = load i32, ptr %Y.addr, align 4
  %t4 = getelementptr inbounds %"mathlib.TVec3", ptr %Result, i32 0, i32 1
  store i32 %t3, ptr %t4, align 4
  %t5 = load i32, ptr %Z.addr, align 4
  %t6 = getelementptr inbounds %"mathlib.TVec3", ptr %Result, i32 0, i32 2
  store i32 %t5, ptr %t6, align 4
  br label %exit
exit:
  ret void
}

define internal void @"mathlib.Reset"() #0 {
entry:
  store i32 0, ptr @"mathlib.Loaded", align 4
  br label %exit
exit:
  ret void
}

define internal void @"$main"() #0 {
entry:
  call void @"System._InitUnits"()
  %t1 = call i32 @"MathUnit.Twice"(i32 21)
  store i32 %t1, ptr @"mathlib.Loaded", align 4
  br label %exit
exit:
  ret void
}

define i32 @_dllstart(ptr %hinst, i32 %reason, ptr %reserved) #0 personality ptr @__C_specific_handler {
entry:
  %init = call i32 @"System._DllEntry"(ptr %hinst, i32 %reason, ptr %reserved, ptr @"$units", i64 2)
  %run = icmp ne i32 %init, 0
  br i1 %run, label %attach, label %ok
attach:
  invoke void @"$main"()
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

define i32 @Add(i32 %A, i32 %B) #0 {
entry:
  %r = call i32 @"mathlib.Add"(i32 %A, i32 %B)
  ret i32 %r
}

define void @reset_1() #0 {
entry:
  call void @"mathlib.Reset"()
  ret void
}

define i32 @Twice(i32 %A) #0 {
entry:
  %r = call i32 @"MathUnit.Twice"(i32 %A)
  ret i32 %r
}

define void @Reset() #0 {
entry:
  call void @"mathlib.Reset"()
  ret void
}

define ptr @make_vec(ptr %Result, i32 %X, i32 %Y, i32 %Z) #0 {
entry:
  call void @"mathlib.MakeVec"(ptr %Result, i32 %X, i32 %Y, i32 %Z)
  ret ptr %Result
}

attributes #0 = { uwtable "frame-pointer"="all" }
