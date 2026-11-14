; C-079 SEH spike, scenario finally on unwind: exactly the CMP-392 pattern. 'Work' holds 'V' in its frame
; record and outlines its finally part into 'Work.$fin1', which reads 'V' through the static link.
;   Work(0): normal completion -> "F2E";  Work(1) inside try..except: unwind runs the finally first -> "F1H1~".
; Expected stdout "F2EF1H1~Z", exit code 0.
target triple = "x86_64-pc-windows-msvc"

%"Work.$frame" = type { ptr, i32 }

declare i32 @__C_specific_handler(...)
declare void @out(i8)
declare void @outid(ptr)
declare void @"System._Raise"(ptr, ptr) #1
declare void @"System._ReRaise"() #1
declare i32 @"System._SehFilter"(ptr, ptr)
declare ptr @"System._BeginCatch"()
declare void @"System._EndCatch"()
@Obj1 = external global { ptr, i8 }

define void @MaybeFail(i32 %f) #0 {
entry:
  %z = icmp eq i32 %f, 0
  br i1 %z, label %ok, label %fail
fail:
  call void @"System._Raise"(ptr @Obj1, ptr null) #1
  unreachable
ok:
  ret void
}

define internal void @"Work.$fin1"(ptr %$link) #0 {
entry:
  %vp = getelementptr inbounds %"Work.$frame", ptr %$link, i32 0, i32 1
  %v = load i32, ptr %vp, align 4
  %c = trunc i32 %v to i8
  call void @out(i8 70)
  call void @out(i8 %c)
  ret void
}

define void @Work(i32 %f) #0 personality ptr @__C_specific_handler {
entry:
  %f.addr = alloca i32, align 4
  %$frame = alloca %"Work.$frame", align 8
  store i32 %f, ptr %f.addr, align 4
  %vp = getelementptr inbounds %"Work.$frame", ptr %$frame, i32 0, i32 1
  store i32 49, ptr %vp, align 4
  %f1 = load i32, ptr %f.addr, align 4
  invoke void @MaybeFail(i32 %f1)
          to label %try.cont.0 unwind label %fin.pad.1
try.cont.0:
  store i32 50, ptr %vp, align 4
  br label %try.cont.1
try.cont.1:
  call void @"Work.$fin1"(ptr %$frame)
  br label %try.end.1
fin.pad.1:
  %cl1 = cleanuppad within none []
  call void @"Work.$fin1"(ptr %$frame) [ "funclet"(token %cl1) ]
  cleanupret from %cl1 unwind to caller
try.end.1:
  call void @out(i8 69)
  ret void
}

define void @run() #0 personality ptr @__C_specific_handler {
entry:
  call void @Work(i32 0)
  invoke void @Work(i32 1)
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
  invoke void @out(i8 72)
          to label %h.a unwind label %ex.cleanup.1
h.a:
  invoke void @outid(ptr %e)
          to label %h.b unwind label %ex.cleanup.1
h.b:
  call void @"System._EndCatch"()
  br label %try.end.1
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
