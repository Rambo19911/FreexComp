; C-079 SEH spike, scenario nested scopes in one function (CMP-391 + CMP-392 with enclosing scopes):
;   try                                   // scope 3: except on E2 do Write('O', id)
;     try                                 // scope 2: finally Write('F')
;       try raise Obj2 (E2)               // scope 1: except on E1 do Write('X')  -> no match: Write('N'); re-raise
;       except on E1 do ... end
;     finally ... end
;   except on E2 do ... end;
;   Write('Z')
; Expected stdout "NFO2~Z" (the re-raised object is destroyed once, by scope 3's _EndCatch), exit code 0.
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
@Obj2 = external global { ptr, i8 }
@E1.vmt = external constant { ptr }
@E2.vmt = external constant { ptr }

define internal void @"run.$fin2"(ptr %$link) #0 {
entry:
  call void @out(i8 70)
  ret void
}

define void @run() #0 personality ptr @__C_specific_handler {
entry:
  %$frame = alloca %"run.$frame", align 8
  store ptr null, ptr %$frame, align 8
  invoke void @"System._Raise"(ptr @Obj2, ptr null) #1
          to label %unreachable.0 unwind label %ex.dispatch.1
unreachable.0:
  unreachable

; ---- scope 1: try..except on E1, enclosed by scope 2
ex.dispatch.1:
  %cs1 = catchswitch within none [label %ex.pad.1] unwind label %fin.pad.2
ex.pad.1:
  %cp1 = catchpad within %cs1 [ptr @"System._SehFilter"]
  catchret from %cp1 to label %ex.body.1
ex.body.1:
  %e1 = call ptr @"System._BeginCatch"()
  %t1 = call i8 @"System._IsClass"(ptr %e1, ptr @E1.vmt)
  %t2 = icmp ne i8 %t1, 0
  br i1 %t2, label %ex.on.1, label %ex.nomatch.1
ex.on.1:
  invoke void @out(i8 88)
          to label %ex.on.1.a unwind label %ex.cleanup.1
ex.on.1.a:
  call void @"System._EndCatch"()
  br label %try.end.1
ex.nomatch.1:
  invoke void @out(i8 78)
          to label %ex.nomatch.1.a unwind label %ex.cleanup.1
ex.nomatch.1.a:
  invoke void @"System._ReRaise"() #1
          to label %unreachable.1 unwind label %ex.cleanup.1
unreachable.1:
  unreachable
ex.cleanup.1:
  %cl1 = cleanuppad within none []
  call void @"System._EndCatch"() [ "funclet"(token %cl1) ]
  cleanupret from %cl1 unwind label %fin.pad.2
try.end.1:
  br label %try.cont.2

; ---- scope 2: try..finally, enclosed by scope 3
try.cont.2:
  invoke void @"run.$fin2"(ptr %$frame)
          to label %try.end.2 unwind label %ex.dispatch.3
fin.pad.2:
  %cl2 = cleanuppad within none []
  invoke void @"run.$fin2"(ptr %$frame) [ "funclet"(token %cl2) ]
          to label %fin.ret.2 unwind label %ex.dispatch.3
fin.ret.2:
  cleanupret from %cl2 unwind label %ex.dispatch.3
try.end.2:
  br label %try.end.3

; ---- scope 3: try..except on E2, outermost
ex.dispatch.3:
  %cs3 = catchswitch within none [label %ex.pad.3] unwind to caller
ex.pad.3:
  %cp3 = catchpad within %cs3 [ptr @"System._SehFilter"]
  catchret from %cp3 to label %ex.body.3
ex.body.3:
  %e3 = call ptr @"System._BeginCatch"()
  %t3 = call i8 @"System._IsClass"(ptr %e3, ptr @E2.vmt)
  %t4 = icmp ne i8 %t3, 0
  br i1 %t4, label %ex.on.3, label %ex.nomatch.3
ex.on.3:
  invoke void @out(i8 79)
          to label %ex.on.3.a unwind label %ex.cleanup.3
ex.on.3.a:
  invoke void @outid(ptr %e3)
          to label %ex.on.3.b unwind label %ex.cleanup.3
ex.on.3.b:
  call void @"System._EndCatch"()
  br label %try.end.3
ex.nomatch.3:
  invoke void @"System._ReRaise"() #1
          to label %unreachable.3 unwind label %ex.cleanup.3
unreachable.3:
  unreachable
ex.cleanup.3:
  %cl3 = cleanuppad within none []
  call void @"System._EndCatch"() [ "funclet"(token %cl3) ]
  cleanupret from %cl3 unwind to caller
try.end.3:
  call void @out(i8 90)
  ret void
}

attributes #0 = { uwtable }
attributes #1 = { noreturn }
