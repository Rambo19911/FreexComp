target triple = "x86_64-pc-windows-msvc"
declare i32 @sqlite3_libversion_number()
declare i32 @sqlite3_open16(ptr, ptr)
declare i32 @sqlite3_close(ptr)
declare void @ExitProcess(i32)
@mem = private constant [9 x i16] [i16 58, i16 109, i16 101, i16 109, i16 111, i16 114, i16 121, i16 58, i16 0]   ; ":memory:" UTF-16
define void @_start() {
  %db = alloca ptr
  %rc = call i32 @sqlite3_open16(ptr @mem, ptr %db)
  %h = load ptr, ptr %db
  %rc2 = call i32 @sqlite3_close(ptr %h)
  %v = call i32 @sqlite3_libversion_number()
  %bad = or i32 %rc, %rc2
  %ok = icmp eq i32 %bad, 0
  %res = select i1 %ok, i32 %v, i32 -1
  call void @ExitProcess(i32 %res)
  unreachable
}
