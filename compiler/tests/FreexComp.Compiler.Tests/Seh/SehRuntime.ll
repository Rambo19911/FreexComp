; C-079 SEH spike: a tiny hand-written stand-in for the ABI §9.7 helpers (rtl-abi.md ABI-120…124), linked
; with each scenario file. Objects are { ptr vmt, i8 id }; a "VMT" is { ptr parent, ptr destructor-or-null }.
; Output = one byte per out() call on stdout. Destroying an object writes '~', then calls its destructor if any.
; In-flight entries are { Obj, RaiseSP } (ABI-120); the filter does ABI-121 steps 1, 3, 4 and 5, writing 'S' + id
; for every stale entry it drops (step 4, C-188).
target triple = "x86_64-pc-windows-msvc"

declare dllimport ptr @GetStdHandle(i32)
declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare dllimport void @ExitProcess(i32) #1
declare dllimport void @RaiseException(i32, i32, i32, ptr)
declare i32 @__C_specific_handler(...)
declare void @run()

@Exception.vmt = constant { ptr, ptr } { ptr null, ptr null }, align 8
@E1.vmt = constant { ptr, ptr } { ptr @Exception.vmt, ptr null }, align 8
@E2.vmt = constant { ptr, ptr } { ptr @Exception.vmt, ptr null }, align 8
@Obj1 = global { ptr, i8 } { ptr @E1.vmt, i8 49 }, align 8
@Obj2 = global { ptr, i8 } { ptr @E2.vmt, i8 50 }, align 8

@ifobj = global [8 x ptr] zeroinitializer, align 8
@ifsp = global [8 x i64] zeroinitializer, align 8
@ifdepth = global i64 0, align 8
@hstack = global [8 x ptr] zeroinitializer, align 8
@hflags = global [8 x i8] zeroinitializer, align 1
@hdepth = global i64 0, align 8

define void @out(i8 %c) #0 {
entry:
  %b = alloca i8, align 1
  %w = alloca i32, align 4
  store i8 %c, ptr %b, align 1
  %h = call ptr @GetStdHandle(i32 -11)
  %r = call i32 @WriteFile(ptr %h, ptr %b, i32 1, ptr %w, ptr null)
  ret void
}

define void @outid(ptr %o) #0 {
entry:
  %p = getelementptr inbounds i8, ptr %o, i64 8
  %c = load i8, ptr %p, align 1
  call void @out(i8 %c)
  ret void
}

; "Free": '~', then the destructor of the object's class if it has one.
define void @destroy(ptr %o) #0 {
entry:
  call void @out(i8 126)
  %vmt = load ptr, ptr %o, align 8
  %dp = getelementptr inbounds i8, ptr %vmt, i64 8
  %d = load ptr, ptr %dp, align 8
  %has = icmp ne ptr %d, null
  br i1 %has, label %call, label %done
call:
  call void %d(ptr %o)
  br label %done
done:
  ret void
}

; ABI-121 reduced to steps 1, 3, 4 and 5: Pascal code -> drop stale in-flight entries (RaiseSP below Frame),
; push { Obj, RaiseSP = ContextRecord.Rsp }, execute handler; any other code -> continue search.
define i32 @"System._SehFilter"(ptr %info, ptr %frame) #0 {
entry:
  %rec = load ptr, ptr %info, align 8
  %code = load i32, ptr %rec, align 4
  %is = icmp eq i32 %code, -531611309
  br i1 %is, label %stale, label %other
stale:
  %d = load i64, ptr @ifdepth, align 8
  %any = icmp ne i64 %d, 0
  br i1 %any, label %check, label %push
check:
  %top = sub i64 %d, 1
  %spp = getelementptr inbounds [8 x i64], ptr @ifsp, i64 0, i64 %top
  %sp = load i64, ptr %spp, align 8
  %fr = ptrtoint ptr %frame to i64
  %lower = icmp ult i64 %sp, %fr
  br i1 %lower, label %pop, label %push
pop:
  store i64 %top, ptr @ifdepth, align 8
  %op = getelementptr inbounds [8 x ptr], ptr @ifobj, i64 0, i64 %top
  %o = load ptr, ptr %op, align 8
  call void @out(i8 83)
  call void @outid(ptr %o)
  call void @destroy(ptr %o)
  br label %stale
push:
  %pi = getelementptr inbounds i8, ptr %rec, i64 32
  %obj = load ptr, ptr %pi, align 8
  %ctxp = getelementptr inbounds i8, ptr %info, i64 8
  %ctx = load ptr, ptr %ctxp, align 8
  %rspp = getelementptr inbounds i8, ptr %ctx, i64 152
  %rsp = load i64, ptr %rspp, align 8
  %n = load i64, ptr @ifdepth, align 8
  %no = getelementptr inbounds [8 x ptr], ptr @ifobj, i64 0, i64 %n
  store ptr %obj, ptr %no, align 8
  %ns = getelementptr inbounds [8 x i64], ptr @ifsp, i64 0, i64 %n
  store i64 %rsp, ptr %ns, align 8
  %n1 = add i64 %n, 1
  store i64 %n1, ptr @ifdepth, align 8
  ret i32 1
other:
  ret i32 0
}

define void @"System._Raise"(ptr %obj, ptr %addr) #0 {
entry:
  %args = alloca [2 x ptr], align 8
  store ptr %obj, ptr %args, align 8
  %a1 = getelementptr inbounds i8, ptr %args, i64 8
  store ptr %addr, ptr %a1, align 8
  call void @RaiseException(i32 -531611309, i32 1, i32 2, ptr %args)
  unreachable
}

define ptr @"System._BeginCatch"() #0 {
entry:
  %i0 = load i64, ptr @ifdepth, align 8
  %i = sub i64 %i0, 1
  store i64 %i, ptr @ifdepth, align 8
  %ip = getelementptr inbounds [8 x ptr], ptr @ifobj, i64 0, i64 %i
  %obj = load ptr, ptr %ip, align 8
  %d = load i64, ptr @hdepth, align 8
  %s = getelementptr inbounds [8 x ptr], ptr @hstack, i64 0, i64 %d
  store ptr %obj, ptr %s, align 8
  %f = getelementptr inbounds [8 x i8], ptr @hflags, i64 0, i64 %d
  store i8 0, ptr %f, align 1
  %d1 = add i64 %d, 1
  store i64 %d1, ptr @hdepth, align 8
  ret ptr %obj
}

define void @"System._EndCatch"() #0 {
entry:
  %d0 = load i64, ptr @hdepth, align 8
  %d = sub i64 %d0, 1
  store i64 %d, ptr @hdepth, align 8
  %f = getelementptr inbounds [8 x i8], ptr @hflags, i64 0, i64 %d
  %flag = load i8, ptr %f, align 1
  %keep = icmp ne i8 %flag, 0
  br i1 %keep, label %done, label %destroy
destroy:
  %s = getelementptr inbounds [8 x ptr], ptr @hstack, i64 0, i64 %d
  %obj = load ptr, ptr %s, align 8
  call void @destroy(ptr %obj)
  br label %done
done:
  ret void
}

define void @"System._ReRaise"() #0 {
entry:
  %d0 = load i64, ptr @hdepth, align 8
  %d = sub i64 %d0, 1
  %f = getelementptr inbounds [8 x i8], ptr @hflags, i64 0, i64 %d
  store i8 1, ptr %f, align 1
  %s = getelementptr inbounds [8 x ptr], ptr @hstack, i64 0, i64 %d
  %obj = load ptr, ptr %s, align 8
  call void @"System._Raise"(ptr %obj, ptr null) #1
  unreachable
}

define i8 @"System._IsClass"(ptr %obj, ptr %vmt) #0 {
entry:
  %nil = icmp eq ptr %obj, null
  br i1 %nil, label %no, label %start
start:
  %v0 = load ptr, ptr %obj, align 8
  br label %loop
loop:
  %v = phi ptr [ %v0, %start ], [ %parent, %next ]
  %eq = icmp eq ptr %v, %vmt
  br i1 %eq, label %yes, label %next
next:
  %parent = load ptr, ptr %v, align 8
  %end = icmp eq ptr %parent, null
  br i1 %end, label %no, label %loop
yes:
  ret i8 1
no:
  ret i8 0
}

; ABI-124 reduced: 'U', the object's id, exit code 217.
define void @"System._UnhandledException"() #0 {
entry:
  %d0 = load i64, ptr @hdepth, align 8
  %d = sub i64 %d0, 1
  %s = getelementptr inbounds [8 x ptr], ptr @hstack, i64 0, i64 %d
  %obj = load ptr, ptr %s, align 8
  call void @out(i8 85)
  call void @outid(ptr %obj)
  call void @ExitProcess(i32 217) #1
  unreachable
}

; exactly the CMP-355 _start shape ('run' stands for $main, ExitProcess(0) for _Halt0)
define void @_start() #0 personality ptr @__C_specific_handler {
entry:
  invoke void @run()
          to label %done unwind label %catch.dispatch
done:
  call void @ExitProcess(i32 0) #1
  unreachable
catch.dispatch:
  %cs = catchswitch within none [label %catch] unwind to caller
catch:
  %cp = catchpad within %cs [ptr @"System._SehFilter"]
  catchret from %cp to label %unhandled
unhandled:
  %t1 = call ptr @"System._BeginCatch"()
  call void @"System._UnhandledException"() #1
  unreachable
}

attributes #0 = { uwtable }
attributes #1 = { noreturn }
