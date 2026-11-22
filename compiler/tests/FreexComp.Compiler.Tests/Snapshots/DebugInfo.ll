; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

define i32 @"M.Add"(i32 %a, i32 %b) #0 !dbg !4 {
entry:
  %a.addr = alloca i32, align 4, !dbg !8
  %b.addr = alloca i32, align 4, !dbg !8
  %Result.addr = alloca i32, align 4, !dbg !8
  store i32 %a, ptr %a.addr, align 4, !dbg !8
  store i32 %b, ptr %b.addr, align 4, !dbg !8
    #dbg_declare(ptr %a.addr, !10, !DIExpression(), !8)
    #dbg_declare(ptr %b.addr, !11, !DIExpression(), !8)
    #dbg_declare(ptr %Result.addr, !12, !DIExpression(), !8)
  %t1 = load i32, ptr %a.addr, align 4, !dbg !13
  %t2 = load i32, ptr %b.addr, align 4, !dbg !13
  %t3 = add i32 %t1, %t2, !dbg !13
  store i32 %t3, ptr %Result.addr, align 4, !dbg !13
  br label %exit, !dbg !9
exit:
  %t4 = load i32, ptr %Result.addr, align 4, !dbg !9
  ret i32 %t4, !dbg !9
}

define void @"M.$init"() #0 {
entry:
  br label %exit
exit:
  ret void
}

define void @"M.$final"() #0 {
entry:
  br label %exit
exit:
  ret void
}

attributes #0 = { uwtable "frame-pointer"="all" }

!llvm.dbg.cu = !{!0}
!llvm.module.flags = !{!1, !2}
!0 = distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !3, producer: "FreexComp opc 0.1.0", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug, splitDebugInlining: false, nameTableKind: None)
!1 = !{i32 2, !"Debug Info Version", i32 3}
!2 = !{i32 2, !"CodeView", i32 1}
!3 = !DIFile(filename: "M.pas", directory: "C:\5Cp", checksumkind: CSK_MD5, checksum: "0399142e1bcb014c6fe9c16e0167c6b3")
!4 = distinct !DISubprogram(name: "Add", linkageName: "M.Add", scope: !3, file: !3, line: 5, type: !7, scopeLine: 6, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !14)
!5 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!6 = !{!5, !5, !5}
!7 = !DISubroutineType(types: !6)
!8 = !DILocation(line: 6, column: 1, scope: !4)
!9 = !DILocation(line: 8, column: 1, scope: !4)
!10 = !DILocalVariable(name: "a", arg: 1, scope: !4, file: !3, line: 5, type: !5)
!11 = !DILocalVariable(name: "b", arg: 2, scope: !4, file: !3, line: 5, type: !5)
!12 = !DILocalVariable(name: "Result", scope: !4, file: !3, line: 5, type: !5)
!13 = !DILocation(line: 7, column: 3, scope: !4)
!14 = !{!10, !11, !12}
