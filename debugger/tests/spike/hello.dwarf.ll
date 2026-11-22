; Spike D6-0 (ADR 005): hand-written IR for hello.pas, following docs/spec/debugger/debug-info.md §1-§5.
; Container: DWARF 5 in COFF (module flag "Dwarf Version" 5). Link with lld-link /DEBUG:DWARF.
; run-spike.ps1 replaces two placeholders before compiling:
;   @SRCDIR@  absolute directory of hello.pas (backslashes doubled)
;   @MD5@     lowercase hex MD5 of hello.pas as stored on disk (DBG-103)
target triple = "x86_64-pc-windows-msvc"

declare ptr @GetStdHandle(i32)
declare i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare void @ExitProcess(i32)

; function Twice(X: Integer): Integer;  (line 3; begin line 4; body line 5; end line 6)
define i32 @"hello.Twice"(i32 %X) !dbg !10 {
entry:
  %X.addr = alloca i32, align 4
  %Result = alloca i32, align 4
  store i32 %X, ptr %X.addr, align 4, !dbg !16
    #dbg_declare(ptr %X.addr, !14, !DIExpression(), !16)
    #dbg_declare(ptr %Result, !15, !DIExpression(), !16)
  %x = load i32, ptr %X.addr, align 4, !dbg !17
  %m = mul i32 %x, 2, !dbg !17
  store i32 %m, ptr %Result, align 4, !dbg !17
  %r = load i32, ptr %Result, align 4, !dbg !18
  ret i32 %r, !dbg !18
}

; Main block of program hello (begin line 10 ... end. line 14). A and B are locals of the main routine.
define void @_start() !dbg !20 {
entry:
  %A = alloca i32, align 4
  %B = alloca i32, align 4
    #dbg_declare(ptr %A, !24, !DIExpression(), !26)
    #dbg_declare(ptr %B, !25, !DIExpression(), !26)
  store i32 21, ptr %A, align 4, !dbg !27
  %a = load i32, ptr %A, align 4, !dbg !28
  %t = call i32 @"hello.Twice"(i32 %a), !dbg !28
  store i32 %t, ptr %B, align 4, !dbg !28
  %b = load i32, ptr %B, align 4, !dbg !29
  call void @"spike.WriteLnInt"(i32 %b), !dbg !29
  call void @ExitProcess(i32 0), !dbg !30
  unreachable, !dbg !30
}

; Helper without source (DBG-114: no DISubprogram, no !dbg): writes a decimal Integer and CR LF to stdout.
define void @"spike.WriteLnInt"(i32 %v) {
entry:
  %buf = alloca [16 x i8], align 1
  %written = alloca i32, align 4
  %neg = icmp slt i32 %v, 0
  %w = sext i32 %v to i64
  %n = sub i64 0, %w
  %abs = select i1 %neg, i64 %n, i64 %w
  %e15 = getelementptr inbounds [16 x i8], ptr %buf, i64 0, i64 15
  store i8 10, ptr %e15, align 1
  %e14 = getelementptr inbounds [16 x i8], ptr %buf, i64 0, i64 14
  store i8 13, ptr %e14, align 1
  br label %digit
digit:
  %p = phi i64 [ 14, %entry ], [ %p1, %digit ]
  %x = phi i64 [ %abs, %entry ], [ %q, %digit ]
  %p1 = sub i64 %p, 1
  %d = urem i64 %x, 10
  %d8 = trunc i64 %d to i8
  %c = add i8 %d8, 48
  %dp = getelementptr inbounds [16 x i8], ptr %buf, i64 0, i64 %p1
  store i8 %c, ptr %dp, align 1
  %q = udiv i64 %x, 10
  %more = icmp ne i64 %q, 0
  br i1 %more, label %digit, label %sign
sign:
  %p2 = sub i64 %p1, 1
  %sp = getelementptr inbounds [16 x i8], ptr %buf, i64 0, i64 %p2
  br i1 %neg, label %minus, label %out
minus:
  store i8 45, ptr %sp, align 1
  br label %out
out:
  %start = phi i64 [ %p2, %minus ], [ %p1, %sign ]
  %sptr = getelementptr inbounds [16 x i8], ptr %buf, i64 0, i64 %start
  %len64 = sub i64 16, %start
  %len = trunc i64 %len64 to i32
  %h = call ptr @GetStdHandle(i32 -11)
  %ok = call i32 @WriteFile(ptr %h, ptr %sptr, i32 %len, ptr %written, ptr null)
  ret void
}

!llvm.dbg.cu = !{!0}
!llvm.module.flags = !{!1, !2}
!0 = distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !3, producer: "FreexComp opc spike", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug, splitDebugInlining: false, nameTableKind: None)
!1 = !{i32 2, !"Debug Info Version", i32 3}
!2 = !{i32 7, !"Dwarf Version", i32 5}
!3 = !DIFile(filename: "hello.pas", directory: "@SRCDIR@", checksumkind: CSK_MD5, checksum: "@MD5@")
!4 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!10 = distinct !DISubprogram(name: "Twice", linkageName: "hello.Twice", scope: !3, file: !3, line: 3, type: !11, scopeLine: 4, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !13)
!11 = !DISubroutineType(types: !12)
!12 = !{!4, !4}
!13 = !{!14, !15}
!14 = !DILocalVariable(name: "X", arg: 1, scope: !10, file: !3, line: 3, type: !4)
!15 = !DILocalVariable(name: "Result", scope: !10, file: !3, line: 3, type: !4)
!16 = !DILocation(line: 4, column: 1, scope: !10)
!17 = !DILocation(line: 5, column: 3, scope: !10)
!18 = !DILocation(line: 6, column: 1, scope: !10)
!20 = distinct !DISubprogram(name: "hello", linkageName: "_start", scope: !3, file: !3, line: 1, type: !21, scopeLine: 10, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !23)
!21 = !DISubroutineType(types: !22)
!22 = !{null}
!23 = !{!24, !25}
!24 = !DILocalVariable(name: "A", scope: !20, file: !3, line: 9, type: !4)
!25 = !DILocalVariable(name: "B", scope: !20, file: !3, line: 9, type: !4)
!26 = !DILocation(line: 10, column: 1, scope: !20)
!27 = !DILocation(line: 11, column: 3, scope: !20)
!28 = !DILocation(line: 12, column: 3, scope: !20)
!29 = !DILocation(line: 13, column: 3, scope: !20)
!30 = !DILocation(line: 14, column: 1, scope: !20)
