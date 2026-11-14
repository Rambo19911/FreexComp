; C-079 SEH spike, scenario rethrow ('raise;' in a handler, CMP-391) inside a routine with a function-level
; managed cleanup scope (CMP-393, outermost):
;   procedure Inner;  var s: string;  begin try raise Obj1 except on E1 do begin Write('R'); raise end end end;
;   run: try Inner except on E1 do Write('H', id) end; Write('Z')
; Inner's cleanup pad clears 's' (stand-in helper writes 'X'). Expected stdout "RXH1~Z": the same object
; reaches the outer handler and is destroyed exactly once. Exit code 0.
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

; stand-in for System._UStrClr
define void @"System._UStrClr"(ptr %s) #0 {
entry:
  call void @out(i8 88)
  store ptr null, ptr %s, align 8
  ret void
}

define void @Inner() #0 personality ptr @__C_specific_handler {
entry:
  %s.addr = alloca ptr, align 8
  store ptr null, ptr %s.addr, align 8
  invoke void @"System._Raise"(ptr @Obj1, ptr null) #1
          to label %unreachable.0 unwind label %ex.dispatch.1
unreachable.0:
  unreachable
ex.dispatch.1:
  %cs1 = catchswitch within none [label %ex.pad.1] unwind label %cleanup.pad
ex.pad.1:
  %cp1 = catchpad within %cs1 [ptr @"System._SehFilter"]
  catchret from %cp1 to label %ex.body.1
ex.body.1:
  %e = call ptr @"System._BeginCatch"()
  %t1 = call i8 @"System._IsClass"(ptr %e, ptr @E1.vmt)
  %t2 = icmp ne i8 %t1, 0
  br i1 %t2, label %ex.on.1, label %ex.nomatch.1
ex.on.1:
  invoke void @out(i8 82)
          to label %ex.on.1.a unwind label %ex.cleanup.1
ex.on.1.a:
  invoke void @"System._ReRaise"() #1
          to label %unreachable.1 unwind label %ex.cleanup.1
ex.nomatch.1:
  invoke void @"System._ReRaise"() #1
          to label %unreachable.1 unwind label %ex.cleanup.1
unreachable.1:
  unreachable
ex.cleanup.1:
  %cl1 = cleanuppad within none []
  invoke void @"System._EndCatch"() [ "funclet"(token %cl1) ]
          to label %ex.cleanret.1 unwind label %cleanup.pad
ex.cleanret.1:
  cleanupret from %cl1 unwind label %cleanup.pad
cleanup:
  call void @"System._UStrClr"(ptr %s.addr)
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %s.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define void @run() #0 personality ptr @__C_specific_handler {
entry:
  invoke void @Inner()
          to label %try.cont.1 unwind label %ex.dispatch.1
try.cont.1:
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
  invoke void @out(i8 72)
          to label %ex.on.1.a unwind label %ex.cleanup.1
ex.on.1.a:
  invoke void @outid(ptr %e)
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
