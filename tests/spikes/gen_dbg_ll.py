import sys, os
kind = sys.argv[1]
d = os.getcwd().replace(os.sep, '/')
flag = '!{i32 2, !"CodeView", i32 1}' if kind == 'cv' else '!{i32 7, !"Dwarf Version", i32 5}'
open(f'dbg_{kind}.ll', 'w').write(f'''target triple = "x86_64-pc-windows-msvc"
@g = global i32 0, align 4, !dbg !0
declare void @ExitProcess(i32)
define i32 @Add(i32 %a, i32 %b) !dbg !10 {{
entry:
  %a.addr = alloca i32, align 4
  %b.addr = alloca i32, align 4
  %result = alloca i32, align 4
  store i32 %a, ptr %a.addr, align 4
  store i32 %b, ptr %b.addr, align 4
    #dbg_declare(ptr %a.addr, !14, !DIExpression(), !17)
    #dbg_declare(ptr %b.addr, !15, !DIExpression(), !17)
    #dbg_declare(ptr %result, !16, !DIExpression(), !17)
  %0 = load i32, ptr %a.addr, align 4, !dbg !18
  %1 = load i32, ptr %b.addr, align 4, !dbg !18
  %add = add i32 %0, %1, !dbg !18
  store i32 %add, ptr %result, align 4, !dbg !18
  %2 = load i32, ptr %result, align 4, !dbg !19
  ret i32 %2, !dbg !19
}}
define void @_start() !dbg !20 {{
entry:
  %c = call i32 @Add(i32 2, i32 3), !dbg !22
  store i32 %c, ptr @g, align 4, !dbg !22
  %v = load i32, ptr @g, align 4, !dbg !23
  %m = mul i32 %v, 2, !dbg !23
  store i32 %m, ptr @g, align 4, !dbg !23
  call void @ExitProcess(i32 %m), !dbg !24
  unreachable, !dbg !24
}}
!llvm.dbg.cu = !{{!2}}
!llvm.module.flags = !{{!5, !6}}
!0 = !DIGlobalVariableExpression(var: !1, expr: !DIExpression())
!1 = distinct !DIGlobalVariable(name: "g", scope: !2, file: !3, line: 2, type: !7, isLocal: false, isDefinition: true)
!2 = distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !3, producer: "opc spike", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug, globals: !4)
!3 = !DIFile(filename: "dbg.pas", directory: "{d}")
!4 = !{{!0}}
!5 = {flag}
!6 = !{{i32 2, !"Debug Info Version", i32 3}}
!7 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!10 = distinct !DISubprogram(name: "Add", scope: !3, file: !3, line: 3, type: !11, scopeLine: 4, spFlags: DISPFlagDefinition, unit: !2, retainedNodes: !13)
!11 = !DISubroutineType(types: !12)
!12 = !{{!7, !7, !7}}
!13 = !{{}}
!14 = !DILocalVariable(name: "a", arg: 1, scope: !10, file: !3, line: 3, type: !7)
!15 = !DILocalVariable(name: "b", arg: 2, scope: !10, file: !3, line: 3, type: !7)
!16 = !DILocalVariable(name: "Result", scope: !10, file: !3, line: 3, type: !7)
!17 = !DILocation(line: 3, column: 1, scope: !10)
!18 = !DILocation(line: 5, column: 3, scope: !10)
!19 = !DILocation(line: 6, column: 1, scope: !10)
!20 = distinct !DISubprogram(name: "dbg", linkageName: "_start", scope: !3, file: !3, line: 7, type: !21, scopeLine: 7, spFlags: DISPFlagDefinition, unit: !2, retainedNodes: !13)
!21 = !DISubroutineType(types: !25)
!22 = !DILocation(line: 8, column: 3, scope: !20)
!23 = !DILocation(line: 9, column: 3, scope: !20)
!24 = !DILocation(line: 10, column: 1, scope: !20)
!25 = !{{null}}
''')
