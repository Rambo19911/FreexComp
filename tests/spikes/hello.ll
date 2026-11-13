target triple = "x86_64-pc-windows-msvc"
declare ptr @GetStdHandle(i32)
declare i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare void @ExitProcess(i32)
@msg = private constant [22 x i8] c"hello from FreexComp\0D\0A"
define void @_start() {
  %w = alloca i32
  %h = call ptr @GetStdHandle(i32 -11)
  %r = call i32 @WriteFile(ptr %h, ptr @msg, i32 22, ptr %w, ptr null)
  call void @ExitProcess(i32 7)
  unreachable
}
