; Spike D6-0 part B (B5, B6): endless-loop variant, hand-written IR for loop.pas (debug-info.md §1-§5, DBG-123).
; run-spike.ps1 replaces @SRCDIR@, @MD5@ and @CONTAINER_FLAG@ (the CodeView or DWARF module flag).
target triple = "x86_64-pc-windows-msvc"

; Main block of program loop (begin line 5 ... end. line 9).
define void @_start() !dbg !20 {
entry:
  %N = alloca i32, align 4
    #dbg_declare(ptr %N, !24, !DIExpression(), !26)
  store i32 0, ptr %N, align 4, !dbg !27
  br label %cond, !dbg !27
cond:                                   ; while True do   (line 7, every iteration)
  br label %body, !dbg !28
body:                                   ; N := N + 1;     (line 8)
  %v = load volatile i32, ptr %N, align 4, !dbg !29
  %v1 = add i32 %v, 1, !dbg !29
  store volatile i32 %v1, ptr %N, align 4, !dbg !29
  br label %cond, !dbg !29
}

!llvm.dbg.cu = !{!0}
!llvm.module.flags = !{!1, !2}
!0 = distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !3, producer: "FreexComp opc spike", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug, splitDebugInlining: false, nameTableKind: None)
!1 = !{i32 2, !"Debug Info Version", i32 3}
!2 = @CONTAINER_FLAG@
!3 = !DIFile(filename: "loop.pas", directory: "@SRCDIR@", checksumkind: CSK_MD5, checksum: "@MD5@")
!4 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!20 = distinct !DISubprogram(name: "loop", linkageName: "_start", scope: !3, file: !3, line: 1, type: !21, scopeLine: 5, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !23)
!21 = !DISubroutineType(types: !22)
!22 = !{null}
!23 = !{!24}
!24 = !DILocalVariable(name: "N", scope: !20, file: !3, line: 4, type: !4)
!26 = !DILocation(line: 5, column: 1, scope: !20)
!27 = !DILocation(line: 6, column: 3, scope: !20)
!28 = !DILocation(line: 7, column: 3, scope: !20)
!29 = !DILocation(line: 8, column: 5, scope: !20)
