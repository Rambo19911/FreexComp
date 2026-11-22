; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.Outer.$frame" = type { ptr, ptr, i32 }

define i32 @"M.Outer"(ptr %X) #0 !dbg !4 {
entry:
  %$frame = alloca %"M.Outer.$frame", align 8, !dbg !9
  %Result.addr = alloca i32, align 4, !dbg !9
  store ptr null, ptr %$frame, align 8, !dbg !9
  %X.addr = getelementptr inbounds %"M.Outer.$frame", ptr %$frame, i32 0, i32 1, !dbg !9
  %Count.addr = getelementptr inbounds %"M.Outer.$frame", ptr %$frame, i32 0, i32 2, !dbg !9
  store ptr %X, ptr %X.addr, align 8, !dbg !9
    #dbg_declare(ptr %$frame, !11, !DIExpression(DW_OP_plus_uconst, 8, DW_OP_deref), !9)
    #dbg_declare(ptr %Result.addr, !12, !DIExpression(), !9)
    #dbg_declare(ptr %$frame, !13, !DIExpression(DW_OP_plus_uconst, 16), !9)
  store i32 0, ptr %Count.addr, align 4, !dbg !14
  call void @"M.Outer.Bump"(ptr %$frame), !dbg !15
  %t1 = load i32, ptr %Count.addr, align 4, !dbg !16
  store i32 %t1, ptr %Result.addr, align 4, !dbg !16
  br label %exit, !dbg !10
exit:
  %t2 = load i32, ptr %Result.addr, align 4, !dbg !10
  ret i32 %t2, !dbg !10
}

define internal void @"M.Outer.Bump"(ptr %$link) #0 !dbg !18 {
entry:
  %t1 = getelementptr inbounds %"M.Outer.$frame", ptr %$link, i32 0, i32 2, !dbg !24
  %t2 = load i32, ptr %t1, align 4, !dbg !24
  %t3 = add i32 %t2, 1, !dbg !24
  %t4 = getelementptr inbounds %"M.Outer.$frame", ptr %$link, i32 0, i32 2, !dbg !24
  store i32 %t3, ptr %t4, align 4, !dbg !24
  %t5 = getelementptr inbounds %"M.Outer.$frame", ptr %$link, i32 0, i32 1, !dbg !25
  %t6 = load ptr, ptr %t5, align 8, !dbg !25
  %t7 = load i32, ptr %t6, align 4, !dbg !25
  %t8 = getelementptr inbounds %"M.Outer.$frame", ptr %$link, i32 0, i32 2, !dbg !25
  %t9 = load i32, ptr %t8, align 4, !dbg !25
  %t10 = add i32 %t7, %t9, !dbg !25
  %t11 = getelementptr inbounds %"M.Outer.$frame", ptr %$link, i32 0, i32 1, !dbg !25
  %t12 = load ptr, ptr %t11, align 8, !dbg !25
  store i32 %t10, ptr %t12, align 4, !dbg !25
  br label %exit, !dbg !23
exit:
  ret void, !dbg !23
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
!3 = !DIFile(filename: "M.pas", directory: "C:\5Cp", checksumkind: CSK_MD5, checksum: "e4750ee9c7d6a4ad2bff167dec10b10a")
!4 = distinct !DISubprogram(name: "Outer", linkageName: "M.Outer", scope: !3, file: !3, line: 5, type: !8, scopeLine: 15, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !17)
!5 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!6 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !5, size: 64)
!7 = !{!5, !6}
!8 = !DISubroutineType(types: !7)
!9 = !DILocation(line: 15, column: 1, scope: !4)
!10 = !DILocation(line: 19, column: 1, scope: !4)
!11 = !DILocalVariable(name: "X", arg: 1, scope: !4, file: !3, line: 5, type: !5)
!12 = !DILocalVariable(name: "Result", scope: !4, file: !3, line: 5, type: !5)
!13 = !DILocalVariable(name: "Count", scope: !4, file: !3, line: 7, type: !5)
!14 = !DILocation(line: 16, column: 3, scope: !4)
!15 = !DILocation(line: 17, column: 3, scope: !4)
!16 = !DILocation(line: 18, column: 3, scope: !4)
!17 = !{!11, !12, !13}
!18 = distinct !DISubprogram(name: "Outer.Bump", linkageName: "M.Outer.Bump", scope: !4, file: !3, line: 9, type: !21, scopeLine: 10, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0)
!19 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: null, size: 64)
!20 = !{null, !19}
!21 = !DISubroutineType(types: !20)
!22 = !DILocation(line: 10, column: 3, scope: !18)
!23 = !DILocation(line: 13, column: 3, scope: !18)
!24 = !DILocation(line: 11, column: 5, scope: !18)
!25 = !DILocation(line: 12, column: 5, scope: !18)
