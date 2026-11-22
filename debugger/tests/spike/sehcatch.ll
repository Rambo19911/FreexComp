; Spike D6-0, informational step I4: does a Pascal-style raise still reach its except handler when the program
; runs under the debug adapter? Mirrors the RTL mechanism (rtl-abi.md ABI-125): RaiseException with the Pascal
; code and EXCEPTION_NONCONTINUABLE, caught by an SEH filter through __C_specific_handler (ADR 001).
; Exit code 42 = handler ran with the right code; 1 = RaiseException returned (exception swallowed); other = crash.
; No debug metadata (not needed for this question).
target triple = "x86_64-pc-windows-msvc"

declare i32 @__C_specific_handler(...)
declare void @RaiseException(i32, i32, i32, ptr)
declare void @ExitProcess(i32)

@seen = global i32 0

; SEH filter (EXCEPTION_POINTERS*, establisher frame): remembers the exception code, returns 1 = run the handler.
define internal i32 @catch_filter(ptr %pointers, ptr %frame) {
  %record = load ptr, ptr %pointers
  %code = load i32, ptr %record
  store i32 %code, ptr @seen
  ret i32 1
}

define void @do_raise() {
  call void @RaiseException(i32 -531611309, i32 1, i32 0, ptr null)   ; 0xE0504153, EXCEPTION_NONCONTINUABLE
  ret void
}

define void @_start() personality ptr @__C_specific_handler {
entry:
  invoke void @do_raise() to label %returned unwind label %dispatch
dispatch:
  %cs = catchswitch within none [label %handler] unwind to caller
handler:
  %cp = catchpad within %cs [ptr @catch_filter]
  catchret from %cp to label %caught
caught:
  %c = load i32, ptr @seen
  %ok = icmp eq i32 %c, -531611309
  %rc = select i1 %ok, i32 42, i32 2
  call void @ExitProcess(i32 %rc)
  unreachable
returned:
  call void @ExitProcess(i32 1)
  unreachable
}
