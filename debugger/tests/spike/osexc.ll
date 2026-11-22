; Spike D6-0 part B (B9): OS exception variant, hand-written IR for osexc.pas (debug-info.md §1-§5).
; Boom calls RaiseException(0xE0504153, 0, 0, nil); no routine installs an SEH handler.
; run-spike.ps1 replaces @SRCDIR@, @MD5@ and @CONTAINER_FLAG@ (the CodeView or DWARF module flag).
target triple = "x86_64-pc-windows-msvc"

declare void @RaiseException(i32, i32, i32, ptr)
declare void @ExitProcess(i32)

; procedure Boom;  (line 3; begin line 4; body line 5; end line 6)
define void @"osexc.Boom"() !dbg !10 {
entry:
  call void @RaiseException(i32 -531611309, i32 0, i32 0, ptr null), !dbg !17
  ret void, !dbg !18
}

; Main block of program osexc (begin line 8 ... end. line 10).
define void @_start() !dbg !20 {
entry:
  call void @"osexc.Boom"(), !dbg !27
  call void @ExitProcess(i32 0), !dbg !30
  unreachable, !dbg !30
}

!llvm.dbg.cu = !{!0}
!llvm.module.flags = !{!1, !2}
!0 = distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !3, producer: "FreexComp opc spike", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug, splitDebugInlining: false, nameTableKind: None)
!1 = !{i32 2, !"Debug Info Version", i32 3}
!2 = @CONTAINER_FLAG@
!3 = !DIFile(filename: "osexc.pas", directory: "@SRCDIR@", checksumkind: CSK_MD5, checksum: "@MD5@")
!10 = distinct !DISubprogram(name: "Boom", linkageName: "osexc.Boom", scope: !3, file: !3, line: 3, type: !21, scopeLine: 4, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !22)
!17 = !DILocation(line: 5, column: 3, scope: !10)
!18 = !DILocation(line: 6, column: 1, scope: !10)
!20 = distinct !DISubprogram(name: "osexc", linkageName: "_start", scope: !3, file: !3, line: 1, type: !21, scopeLine: 8, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !22)
!21 = !DISubroutineType(types: !23)
!23 = !{null}
!22 = !{}
!27 = !DILocation(line: 9, column: 3, scope: !20)
!30 = !DILocation(line: 10, column: 1, scope: !20)
