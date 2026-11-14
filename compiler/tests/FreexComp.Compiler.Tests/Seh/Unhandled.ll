; C-079 SEH spike, scenario unhandled: 'try raise Obj1 finally Write('F') end' with no handler anywhere but
; _start's catch-all (SehRuntime.ll), which calls _UnhandledException ('U', id, exit 217). The finally part
; runs during the unwind to _start. Expected stdout "FU1", exit code 217.
target triple = "x86_64-pc-windows-msvc"

%"run.$frame" = type { ptr }

declare i32 @__C_specific_handler(...)
declare void @out(i8)
declare void @"System._Raise"(ptr, ptr) #1
@Obj1 = external global { ptr, i8 }

define internal void @"run.$fin1"(ptr %$link) #0 {
entry:
  call void @out(i8 70)
  ret void
}

define void @run() #0 personality ptr @__C_specific_handler {
entry:
  %$frame = alloca %"run.$frame", align 8
  store ptr null, ptr %$frame, align 8
  invoke void @"System._Raise"(ptr @Obj1, ptr null) #1
          to label %unreachable.0 unwind label %fin.pad.1
unreachable.0:
  unreachable
fin.pad.1:
  %cl1 = cleanuppad within none []
  call void @"run.$fin1"(ptr %$frame) [ "funclet"(token %cl1) ]
  cleanupret from %cl1 unwind to caller
}

attributes #0 = { uwtable }
attributes #1 = { noreturn }
