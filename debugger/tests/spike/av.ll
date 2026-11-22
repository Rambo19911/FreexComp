; Spike D6-0, informational step I5: an unhandled access violation (write to address 0) under the debug adapter.
; Shows what `continue` does after an OS-exception stop for a fault that re-executes on resume.
; No debug metadata (not needed for this question).
target triple = "x86_64-pc-windows-msvc"

declare void @ExitProcess(i32)

define void @_start() {
entry:
  store volatile i32 1, ptr null, align 4
  call void @ExitProcess(i32 0)
  unreachable
}
