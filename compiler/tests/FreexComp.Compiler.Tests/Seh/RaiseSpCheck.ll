; C-188 SEH spike, scenario ABI-121 step 4 "a nested exception caught inside a finally leaves the older in-flight
; entry alone":
;   try                                   // scope 2: except on E1 do Write('O', id)
;     try raise Obj1                      // scope 1: finally $fin1
;     finally
;       try raise Obj2 except on E2 do Write('I', id) end      // $fin1's own function-level try
;     end
;   except ... end;
;   Write('Z')
; The filter call for Obj2 comes from $fin1's frame, which lies below Obj1's RaiseSP, so Obj1 stays in flight and
; is the object scope 2 catches. Expected stdout "I2~O1~Z", exit code 0.
target triple = "x86_64-pc-windows-msvc"

%"run.$frame" = type { ptr }

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
@Obj2 = external global { ptr, i8 }
@E1.vmt = external constant { ptr, ptr }
@E2.vmt = external constant { ptr, ptr }

define internal void @"run.$fin1"(ptr %$link) #0 personality ptr @__C_specific_handler {
entry:
  invoke void @"System._Raise"(ptr @Obj2, ptr null) #1
          to label %unreachable.0 unwind label %ex.dispatch.1
unreachable.0:
  unreachable
ex.dispatch.1:
  %cs1 = catchswitch within none [label %ex.pad.1] unwind to caller
ex.pad.1:
  %cp1 = catchpad within %cs1 [ptr @"System._SehFilter"]
  catchret from %cp1 to label %ex.body.1
ex.body.1:
  %e = call ptr @"System._BeginCatch"()
  %t1 = call i8 @"System._IsClass"(ptr %e, ptr @E2.vmt)
  %t2 = icmp ne i8 %t1, 0
  br i1 %t2, label %ex.on.1, label %ex.nomatch.1
ex.on.1:
  invoke void @out(i8 73)
          to label %ex.on.1.a unwind label %ex.cleanup.1
ex.on.1.a:
  invoke void @outid(ptr %e)
          to label %ex.on.1.b unwind label %ex.cleanup.1
ex.on.1.b:
  call void @"System._EndCatch"()
  ret void
ex.nomatch.1:
  invoke void @"System._ReRaise"() #1
          to label %unreachable.1 unwind label %ex.cleanup.1
unreachable.1:
  unreachable
ex.cleanup.1:
  %cl1 = cleanuppad within none []
  call void @"System._EndCatch"() [ "funclet"(token %cl1) ]
  cleanupret from %cl1 unwind to caller
}

define void @run() #0 personality ptr @__C_specific_handler {
entry:
  %$frame = alloca %"run.$frame", align 8
  store ptr null, ptr %$frame, align 8
  invoke void @"System._Raise"(ptr @Obj1, ptr null) #1
          to label %unreachable.0 unwind label %fin.pad.1
unreachable.0:
  unreachable

; ---- scope 1: try..finally, enclosed by scope 2
fin.pad.1:
  %cl1 = cleanuppad within none []
  invoke void @"run.$fin1"(ptr %$frame) [ "funclet"(token %cl1) ]
          to label %fin.ret.1 unwind label %ex.dispatch.2
fin.ret.1:
  cleanupret from %cl1 unwind label %ex.dispatch.2

; ---- scope 2: try..except on E1, outermost
ex.dispatch.2:
  %cs2 = catchswitch within none [label %ex.pad.2] unwind to caller
ex.pad.2:
  %cp2 = catchpad within %cs2 [ptr @"System._SehFilter"]
  catchret from %cp2 to label %ex.body.2
ex.body.2:
  %e2 = call ptr @"System._BeginCatch"()
  %t3 = call i8 @"System._IsClass"(ptr %e2, ptr @E1.vmt)
  %t4 = icmp ne i8 %t3, 0
  br i1 %t4, label %ex.on.2, label %ex.nomatch.2
ex.on.2:
  invoke void @out(i8 79)
          to label %ex.on.2.a unwind label %ex.cleanup.2
ex.on.2.a:
  invoke void @outid(ptr %e2)
          to label %ex.on.2.b unwind label %ex.cleanup.2
ex.on.2.b:
  call void @"System._EndCatch"()
  call void @out(i8 90)
  ret void
ex.nomatch.2:
  invoke void @"System._ReRaise"() #1
          to label %unreachable.2 unwind label %ex.cleanup.2
unreachable.2:
  unreachable
ex.cleanup.2:
  %cl2 = cleanuppad within none []
  call void @"System._EndCatch"() [ "funclet"(token %cl2) ]
  cleanupret from %cl2 unwind to caller
}

attributes #0 = { uwtable }
attributes #1 = { noreturn }
