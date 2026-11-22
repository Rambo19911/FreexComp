; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

define i32 @"M.Flow"(i32 %N) #0 !dbg !4 {
entry:
  %N.addr = alloca i32, align 4, !dbg !8
  %Result.addr = alloca i32, align 4, !dbg !8
  %I.addr = alloca i32, align 4, !dbg !8
  %K.addr = alloca i32, align 4, !dbg !8
  %$tmp1.addr = alloca i32, align 4, !dbg !8
  %$tmp2.addr = alloca i32, align 4, !dbg !8
  store i32 %N, ptr %N.addr, align 4, !dbg !8
    #dbg_declare(ptr %N.addr, !10, !DIExpression(), !8)
    #dbg_declare(ptr %Result.addr, !11, !DIExpression(), !8)
    #dbg_declare(ptr %I.addr, !12, !DIExpression(), !8)
    #dbg_declare(ptr %K.addr, !13, !DIExpression(), !8)
  store i32 0, ptr %K.addr, align 4, !dbg !14
  %t1 = load i32, ptr %N.addr, align 4, !dbg !15
  %t2 = icmp sgt i32 %t1, 0, !dbg !15
  br i1 %t2, label %if.then.1, label %if.else.1, !dbg !15
if.then.1:
  store i32 1, ptr %K.addr, align 4, !dbg !16
  br label %if.end.1, !dbg !16
if.else.1:
  store i32 2, ptr %K.addr, align 4, !dbg !17
  %t3 = load i32, ptr %K.addr, align 4, !dbg !18
  %t4 = add i32 %t3, 1, !dbg !18
  store i32 %t4, ptr %K.addr, align 4, !dbg !18
  br label %if.end.1, !dbg !18
if.end.1:
  br label %while.cond.2, !dbg !19
while.cond.2:
  %t5 = load i32, ptr %K.addr, align 4, !dbg !19
  %t6 = icmp slt i32 %t5, 10, !dbg !19
  br i1 %t6, label %while.body.2, label %while.end.2, !dbg !19
while.body.2:
  %t7 = load i32, ptr %K.addr, align 4, !dbg !20
  %t8 = mul i32 %t7, 2, !dbg !20
  store i32 %t8, ptr %K.addr, align 4, !dbg !20
  br label %while.cond.2, !dbg !19
while.end.2:
  br label %repeat.body.3, !dbg !23
repeat.body.3:
  %t9 = load i32, ptr %K.addr, align 4, !dbg !23
  %t10 = sub i32 %t9, 1, !dbg !23
  store i32 %t10, ptr %K.addr, align 4, !dbg !23
  br label %repeat.cond.3, !dbg !22
repeat.cond.3:
  %t11 = load i32, ptr %K.addr, align 4, !dbg !22
  %t12 = icmp slt i32 %t11, 5, !dbg !22
  br i1 %t12, label %repeat.end.3, label %if.end.4, !dbg !22
if.end.4:
  br label %repeat.body.3, !dbg !22
repeat.end.3:
  %t13 = load i32, ptr %N.addr, align 4, !dbg !24
  switch i32 %t13, label %case.else.5 [
    i32 1, label %case.arm.5.1
    i32 2, label %case.arm.5.2
    i32 3, label %case.arm.5.2
  ], !dbg !24
case.arm.5.1:
  store i32 10, ptr %K.addr, align 4, !dbg !25
  br label %case.end.5, !dbg !25
case.arm.5.2:
  store i32 20, ptr %K.addr, align 4, !dbg !26
  br label %case.end.5, !dbg !26
case.else.5:
  store i32 30, ptr %K.addr, align 4, !dbg !27
  br label %case.end.5, !dbg !27
case.end.5:
  store i32 1, ptr %$tmp1.addr, align 4, !dbg !28
  %t14 = load i32, ptr %N.addr, align 4, !dbg !28
  store i32 %t14, ptr %$tmp2.addr, align 4, !dbg !28
  %t15 = load i32, ptr %$tmp1.addr, align 4, !dbg !28
  %t16 = load i32, ptr %$tmp2.addr, align 4, !dbg !28
  %t17 = icmp sle i32 %t15, %t16, !dbg !28
  br i1 %t17, label %if.then.6, label %if.end.6, !dbg !28
if.then.6:
  %t18 = load i32, ptr %$tmp1.addr, align 4, !dbg !28
  store i32 %t18, ptr %I.addr, align 4, !dbg !28
  br label %for.body.7, !dbg !28
for.body.7:
  %t19 = load i32, ptr %I.addr, align 4, !dbg !29
  %t20 = icmp eq i32 %t19, 3, !dbg !29
  br i1 %t20, label %for.inc.7, label %if.end.8, !dbg !29
if.end.8:
  %t21 = load i32, ptr %K.addr, align 4, !dbg !30
  %t22 = load i32, ptr %I.addr, align 4, !dbg !30
  %t23 = add i32 %t21, %t22, !dbg !30
  store i32 %t23, ptr %K.addr, align 4, !dbg !30
  br label %for.inc.7, !dbg !28
for.inc.7:
  %t24 = load i32, ptr %I.addr, align 4, !dbg !28
  %t25 = load i32, ptr %$tmp2.addr, align 4, !dbg !28
  %t26 = icmp eq i32 %t24, %t25, !dbg !28
  br i1 %t26, label %for.end.7, label %if.end.9, !dbg !28
if.end.9:
  %t27 = load i32, ptr %I.addr, align 4, !dbg !28
  %t28 = add i32 %t27, 1, !dbg !28
  store i32 %t28, ptr %I.addr, align 4, !dbg !28
  br label %for.body.7, !dbg !28
for.end.7:
  br label %if.end.6, !dbg !28
if.end.6:
  %t29 = load i32, ptr %K.addr, align 4, !dbg !31
  %t30 = icmp eq i32 %t29, 0, !dbg !31
  br i1 %t30, label %exit, label %if.end.10, !dbg !31
if.end.10:
  %t31 = load i32, ptr %K.addr, align 4, !dbg !32
  store i32 %t31, ptr %Result.addr, align 4, !dbg !32
  br label %exit, !dbg !9
exit:
  %t32 = load i32, ptr %Result.addr, align 4, !dbg !9
  ret i32 %t32, !dbg !9
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
!3 = !DIFile(filename: "M.pas", directory: "C:\5Cp", checksumkind: CSK_MD5, checksum: "6b4ae95a343d987cc0f7991aec184565")
!4 = distinct !DISubprogram(name: "Flow", linkageName: "M.Flow", scope: !3, file: !3, line: 5, type: !7, scopeLine: 8, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !33)
!5 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!6 = !{!5, !5}
!7 = !DISubroutineType(types: !6)
!8 = !DILocation(line: 8, column: 1, scope: !4)
!9 = !DILocation(line: 37, column: 1, scope: !4)
!10 = !DILocalVariable(name: "N", arg: 1, scope: !4, file: !3, line: 5, type: !5)
!11 = !DILocalVariable(name: "Result", scope: !4, file: !3, line: 5, type: !5)
!12 = !DILocalVariable(name: "I", scope: !4, file: !3, line: 7, type: !5)
!13 = !DILocalVariable(name: "K", scope: !4, file: !3, line: 7, type: !5)
!14 = !DILocation(line: 9, column: 3, scope: !4)
!15 = !DILocation(line: 10, column: 3, scope: !4)
!16 = !DILocation(line: 11, column: 5, scope: !4)
!17 = !DILocation(line: 14, column: 5, scope: !4)
!18 = !DILocation(line: 15, column: 5, scope: !4)
!19 = !DILocation(line: 17, column: 3, scope: !4)
!20 = !DILocation(line: 18, column: 5, scope: !4)
!21 = !DILocation(line: 19, column: 3, scope: !4)
!22 = !DILocation(line: 21, column: 3, scope: !4)
!23 = !DILocation(line: 20, column: 5, scope: !4)
!24 = !DILocation(line: 22, column: 3, scope: !4)
!25 = !DILocation(line: 23, column: 8, scope: !4)
!26 = !DILocation(line: 24, column: 11, scope: !4)
!27 = !DILocation(line: 26, column: 5, scope: !4)
!28 = !DILocation(line: 28, column: 3, scope: !4)
!29 = !DILocation(line: 30, column: 5, scope: !4)
!30 = !DILocation(line: 32, column: 5, scope: !4)
!31 = !DILocation(line: 34, column: 3, scope: !4)
!32 = !DILocation(line: 36, column: 3, scope: !4)
!33 = !{!10, !11, !12, !13}
