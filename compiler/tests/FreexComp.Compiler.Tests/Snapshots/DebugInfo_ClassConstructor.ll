; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 84, i16 65, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@"M.TA.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TA.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.1, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 0, ptr null, i64 0, ptr null }, align 8
@"M.TA.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 8, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TA.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.TA.Count" = global i32 zeroinitializer, align 4, !dbg !15
@"M.$ccdone" = internal global [1 x i8] zeroinitializer, align 1

define internal void @"M.TA.$cctor"() #0 !dbg !4 {
entry:
  store i32 42, ptr @"M.TA.Count", align 4, !dbg !9
  br label %exit, !dbg !8
exit:
  ret void, !dbg !8
}

define void @"M.$init"() #0 !dbg !10 {
entry:
  call void @"M.TA.$cctor"(), !dbg !13
  store i8 1, ptr @"M.$ccdone", align 1, !dbg !11
  %t1 = load i32, ptr @"M.TA.Count", align 4, !dbg !14
  %t2 = add i32 %t1, 1, !dbg !14
  store i32 %t2, ptr @"M.TA.Count", align 4, !dbg !14
  br label %exit, !dbg !12
exit:
  ret void, !dbg !12
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
!0 = distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !3, producer: "FreexComp opc 0.1.0", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug, globals: !18, splitDebugInlining: false, nameTableKind: None)
!1 = !{i32 2, !"Debug Info Version", i32 3}
!2 = !{i32 2, !"CodeView", i32 1}
!3 = !DIFile(filename: "M.pas", directory: "C:\5Cp", checksumkind: CSK_MD5, checksum: "349716828062bb49ec5a4aadf285cb75")
!4 = distinct !DISubprogram(name: "TA.Create", linkageName: "M.TA.$cctor", scope: !3, file: !3, line: 9, type: !6, scopeLine: 10, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0)
!5 = !{null}
!6 = !DISubroutineType(types: !5)
!7 = !DILocation(line: 10, column: 1, scope: !4)
!8 = !DILocation(line: 12, column: 1, scope: !4)
!9 = !DILocation(line: 11, column: 3, scope: !4)
!10 = distinct !DISubprogram(name: "M.initialization", linkageName: "M.$init", scope: !3, file: !3, line: 13, type: !6, scopeLine: 13, spFlags: DISPFlagDefinition, unit: !0)
!11 = !DILocation(line: 13, column: 1, scope: !10)
!12 = !DILocation(line: 15, column: 1, scope: !10)
!13 = !DILocation(line: 6, column: 5, scope: !10)
!14 = !DILocation(line: 14, column: 3, scope: !10)
!15 = !DIGlobalVariableExpression(var: !17, expr: !DIExpression())
!16 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!17 = distinct !DIGlobalVariable(name: "Count", linkageName: "M.TA.Count", scope: !0, file: !3, line: 5, type: !16, isLocal: false, isDefinition: true)
!18 = !{!15}
