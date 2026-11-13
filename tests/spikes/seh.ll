target triple = "x86_64-pc-windows-msvc"
declare i32 @__C_specific_handler(...)
declare void @RaiseException(i32, i32, i32, ptr)
declare void @ExitProcess(i32)
declare ptr @GetStdHandle(i32)
declare i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
@s_fin = private constant [9 x i8] c"finally\0D\0A"
@s_cat = private constant [8 x i8] c"caught\0D\0A"
@s_flt = private constant [8 x i8] c"filter\0D\0A"
@g_code = global i32 0

define void @say(ptr %p, i32 %n) {
  %w = alloca i32
  %h = call ptr @GetStdHandle(i32 -11)
  %r = call i32 @WriteFile(ptr %h, ptr %p, i32 %n, ptr %w, ptr null)
  ret void
}
; SEH filter: (EXCEPTION_POINTERS*, frame) -> 1 = execute handler. Records the exception code.
define internal i32 @filter(ptr %xp, ptr %frame) {
  %rec = load ptr, ptr %xp
  %code = load i32, ptr %rec
  store i32 %code, ptr @g_code
  call void @say(ptr @s_flt, i32 8)
  ret i32 1
}
define void @thrower() {
  call void @RaiseException(i32 -531606189, i32 1, i32 0, ptr null)   ; 0xE0504153, non-continuable
  ret void
}
define void @with_finally() personality ptr @__C_specific_handler {
entry:
  invoke void @thrower() to label %ok unwind label %cleanup
cleanup:
  %cl = cleanuppad within none []
  call void @say(ptr @s_fin, i32 9) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
ok:
  ret void
}
define void @_start() personality ptr @__C_specific_handler {
entry:
  invoke void @with_finally() to label %cont unwind label %dispatch
dispatch:
  %cs = catchswitch within none [label %handler] unwind to caller
handler:
  %cp = catchpad within %cs [ptr @filter]
  catchret from %cp to label %caught
caught:
  call void @say(ptr @s_cat, i32 8)
  %c = load i32, ptr @g_code
  %ok = icmp eq i32 %c, -531606189
  %rc = select i1 %ok, i32 42, i32 2
  call void @ExitProcess(i32 %rc)
  unreachable
cont:
  call void @ExitProcess(i32 1)
  unreachable
}
