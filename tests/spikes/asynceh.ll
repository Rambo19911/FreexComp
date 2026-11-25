; Spike C-312: hardware fault (store through nil) between two calls, caught in the routine of the try.
; Scope intrinsics + volatile store + module flag "eh-asynch": expected "before filter finally caught", exit 42 at -O0 and -O2.
target triple = "x86_64-pc-windows-msvc"
declare i32 @__C_specific_handler(...)
declare void @ExitProcess(i32)
declare ptr @GetStdHandle(i32)
declare i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()
@s_bef = private constant [8 x i8] c"before\0D\0A"
@s_aft = private constant [7 x i8] c"after\0D\0A"
@s_fin = private constant [9 x i8] c"finally\0D\0A"
@s_cat = private constant [8 x i8] c"caught\0D\0A"
@s_flt = private constant [8 x i8] c"filter\0D\0A"
@g_code = global i32 0
@g_cnt = global i32 0
@g_ptr = global ptr null

define void @say(ptr %p, i32 %n) {
  %w = alloca i32
  %h = call ptr @GetStdHandle(i32 -11)
  %r = call i32 @WriteFile(ptr %h, ptr %p, i32 %n, ptr %w, ptr null)
  ret void
}
define internal i32 @filter(ptr %xp, ptr %frame) {
  %rec = load ptr, ptr %xp
  %code = load i32, ptr %rec
  store i32 %code, ptr @g_code
  call void @say(ptr @s_flt, i32 8)
  ret i32 1
}
define void @before() {
  call void @say(ptr @s_bef, i32 8)
  ret void
}
define void @after() {
  call void @say(ptr @s_aft, i32 7)
  ret void
}
; try  try  before; p^ := 7; after  finally  say('finally')  end  except  say('caught')  end
define void @_start() personality ptr @__C_specific_handler {
entry:
  %p = load volatile ptr, ptr @g_ptr
  invoke void @llvm.seh.try.begin() to label %try unwind label %dispatch
try:
  invoke void @llvm.seh.try.begin() to label %try2 unwind label %cleanup
try2:
  call void @before()
  store volatile i32 7, ptr %p
  call void @after()
  invoke void @llvm.seh.try.end() to label %fin_normal unwind label %cleanup
fin_normal:
  call void @say(ptr @s_fin, i32 9)
  invoke void @llvm.seh.try.end() to label %cont unwind label %dispatch
cleanup:
  %cl = cleanuppad within none []
  call void @say(ptr @s_fin, i32 9) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind label %dispatch
dispatch:
  %cs = catchswitch within none [label %handler] unwind to caller
handler:
  %cp = catchpad within %cs [ptr @filter]
  catchret from %cp to label %caught
caught:
  call void @say(ptr @s_cat, i32 8)
  %c = load i32, ptr @g_code
  %ok = icmp eq i32 %c, -1073741819          ; 0xC0000005 access violation
  %rc = select i1 %ok, i32 42, i32 2
  call void @ExitProcess(i32 %rc)
  unreachable
cont:
  call void @ExitProcess(i32 1)
  unreachable
}
!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
