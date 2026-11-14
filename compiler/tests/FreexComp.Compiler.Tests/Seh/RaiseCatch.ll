; C-079 SEH spike, scenario raise -> catch: exactly the CMP-391 pattern ('try S except on E: E1 do H end').
; The raise happens in a callee without EH scope. Expected stdout "AC1~Z", exit code 0.
target triple = "x86_64-pc-windows-msvc"

declare i32 @__C_specific_handler(...)
declare void @out(i8)
declare void @outid(ptr)
declare void @"System._Raise"(ptr, ptr) #1
declare void @"System._ReRaise"() #1
declare i32 @"System._SehFilter"(ptr, ptr)
declare ptr @"System._BeginCatch"()
declare void @"System._EndCatch"()
declare i8 @"System._IsClass"(ptr, ptr)
@Obj1 = external global { ptr, i8 }
@E1.vmt = external constant { ptr }

define void @Fail(ptr %o) #0 {
entry:
  call void @"System._Raise"(ptr %o, ptr null) #1
  unreachable
}

define void @run() #0 personality ptr @__C_specific_handler {
entry:
  %E.addr = alloca ptr, align 8
  call void @out(i8 65)
  invoke void @Fail(ptr @Obj1)
          to label %try.cont.1 unwind label %ex.dispatch.1
try.cont.1:
  invoke void @out(i8 88)
          to label %try.cont.2 unwind label %ex.dispatch.1
try.cont.2:
  br label %try.end.1
ex.dispatch.1:
  %cs1 = catchswitch within none [label %ex.pad.1] unwind to caller
ex.pad.1:
  %cp1 = catchpad within %cs1 [ptr @"System._SehFilter"]
  catchret from %cp1 to label %ex.body.1
ex.body.1:
  %e = call ptr @"System._BeginCatch"()
  %t1 = call i8 @"System._IsClass"(ptr %e, ptr @E1.vmt)
  %t2 = icmp ne i8 %t1, 0
  br i1 %t2, label %ex.on.1, label %ex.nomatch.1
ex.on.1:
  store ptr %e, ptr %E.addr, align 8
  invoke void @out(i8 67)
          to label %ex.on.1.a unwind label %ex.cleanup.1
ex.on.1.a:
  %e2 = load ptr, ptr %E.addr, align 8
  invoke void @outid(ptr %e2)
          to label %ex.on.1.b unwind label %ex.cleanup.1
ex.on.1.b:
  call void @"System._EndCatch"()
  br label %try.end.1
ex.nomatch.1:
  invoke void @"System._ReRaise"() #1
          to label %unreachable.1 unwind label %ex.cleanup.1
unreachable.1:
  unreachable
ex.cleanup.1:
  %cl1 = cleanuppad within none []
  call void @"System._EndCatch"() [ "funclet"(token %cl1) ]
  cleanupret from %cl1 unwind to caller
try.end.1:
  call void @out(i8 90)
  ret void
}

attributes #0 = { uwtable }
attributes #1 = { noreturn }
