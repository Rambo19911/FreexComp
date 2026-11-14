; C-188 SEH spike, scenario "the destructor run by _EndCatch raises" (CMP-391 amendment, ABI-121 step 4):
;   try                                   // scope 2: except on E2 do Write('O', id)
;     try raise Obj4                      // scope 1: except on E1 do begin Write('H'); raise Obj2 end
;     except ... end                      //   Obj4 is an E4 (< E1) whose destructor writes 'D' and raises Obj3 (E2)
;   except ... end;
;   Write('Z')
; Leaving scope 1's handler by 'raise Obj2' runs ex.cleanup.1, whose _EndCatch destroys Obj4 ('~'); its destructor
; raises Obj3, which leaves the cleanup funclet through the invoke's unwind edge to scope 2. Obj2's unwind is
; abandoned: the filter for Obj3 drops the stale in-flight Obj2 ('S2' + destroy '~'), then scope 2 catches Obj3.
; ex.cleanup.1 has an enclosing scope, so _EndCatch (which may raise) is an invoke in the funclet (CMP-391 as amended).
; Expected stdout "H~DS2~O3~Z", exit code 0.
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
@Obj2 = external global { ptr, i8 }
@E1.vmt = external constant { ptr, ptr }
@E2.vmt = external constant { ptr, ptr }

; class E4(E1) with a raising destructor; Obj3 is a fresh E2
@E4.vmt = constant { ptr, ptr } { ptr @E1.vmt, ptr @"E4.Destroy" }, align 8
@Obj4 = global { ptr, i8 } { ptr @E4.vmt, i8 52 }, align 8
@Obj3 = global { ptr, i8 } { ptr @E2.vmt, i8 51 }, align 8

define void @"E4.Destroy"(ptr %self) #0 {
entry:
  call void @out(i8 68)
  call void @"System._Raise"(ptr @Obj3, ptr null) #1
  unreachable
}

define void @run() #0 personality ptr @__C_specific_handler {
entry:
  invoke void @"System._Raise"(ptr @Obj4, ptr null) #1
          to label %unreachable.0 unwind label %ex.dispatch.1
unreachable.0:
  unreachable

; ---- scope 1: try..except on E1, enclosed by scope 2
ex.dispatch.1:
  %cs1 = catchswitch within none [label %ex.pad.1] unwind label %ex.dispatch.2
ex.pad.1:
  %cp1 = catchpad within %cs1 [ptr @"System._SehFilter"]
  catchret from %cp1 to label %ex.body.1
ex.body.1:
  %e1 = call ptr @"System._BeginCatch"()
  %t1 = call i8 @"System._IsClass"(ptr %e1, ptr @E1.vmt)
  %t2 = icmp ne i8 %t1, 0
  br i1 %t2, label %ex.on.1, label %ex.nomatch.1
ex.on.1:
  invoke void @out(i8 72)
          to label %ex.on.1.a unwind label %ex.cleanup.1
ex.on.1.a:
  invoke void @"System._Raise"(ptr @Obj2, ptr null) #1
          to label %unreachable.1 unwind label %ex.cleanup.1
ex.nomatch.1:
  invoke void @"System._ReRaise"() #1
          to label %unreachable.1 unwind label %ex.cleanup.1
unreachable.1:
  unreachable
ex.cleanup.1:
  %cl1 = cleanuppad within none []
  invoke void @"System._EndCatch"() [ "funclet"(token %cl1) ]
          to label %ex.cleanret.1 unwind label %ex.dispatch.2
ex.cleanret.1:
  cleanupret from %cl1 unwind label %ex.dispatch.2

; ---- scope 2: try..except on E2, outermost
ex.dispatch.2:
  %cs2 = catchswitch within none [label %ex.pad.2] unwind to caller
ex.pad.2:
  %cp2 = catchpad within %cs2 [ptr @"System._SehFilter"]
  catchret from %cp2 to label %ex.body.2
ex.body.2:
  %e2 = call ptr @"System._BeginCatch"()
  %t3 = call i8 @"System._IsClass"(ptr %e2, ptr @E2.vmt)
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
