; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.Run.$frame" = type { ptr, i32 }

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 69, i16 65, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@"M.EA.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.EA.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.1, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 0, ptr null, i64 0, ptr null }, align 8
@"M.EA.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 8, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.EA.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8

declare ptr @"System._BeginCatch"()
declare void @"System._EndCatch"()
declare i8 @"System._IsClass"(ptr, ptr)
declare void @"System._ReRaise"() #1
declare i32 @"System._SehFilter"(ptr, ptr)
declare i32 @__C_specific_handler(...)

define void @"M.Step"(i32 %N) #0 !dbg !4 {
entry:
  %N.addr = alloca i32, align 4, !dbg !8
  store i32 %N, ptr %N.addr, align 4, !dbg !8
    #dbg_declare(ptr %N.addr, !10, !DIExpression(), !8)
  br label %exit, !dbg !9
exit:
  ret void, !dbg !9
}

define i32 @"M.Run"(i32 %N) #0 personality ptr @__C_specific_handler !dbg !12 {
entry:
  %$frame = alloca %"M.Run.$frame", align 8, !dbg !15
  %N.addr = alloca i32, align 4, !dbg !15
  %X.addr = alloca ptr, align 8, !dbg !15
  %$tmp1.addr = alloca ptr, align 8, !dbg !15
  %E.addr = alloca ptr, align 8, !dbg !15
  store ptr null, ptr %$frame, align 8, !dbg !15
  %Result.addr = getelementptr inbounds %"M.Run.$frame", ptr %$frame, i32 0, i32 1, !dbg !15
  store i32 %N, ptr %N.addr, align 4, !dbg !15
    #dbg_declare(ptr %N.addr, !17, !DIExpression(), !15)
    #dbg_declare(ptr %$frame, !18, !DIExpression(DW_OP_plus_uconst, 8), !15)
    #dbg_declare(ptr %X.addr, !24, !DIExpression(), !15)
    #dbg_declare(ptr %E.addr, !25, !DIExpression(), !15)
  store i32 0, ptr %Result.addr, align 4, !dbg !26
  %t1 = load i32, ptr %N.addr, align 4, !dbg !28
  invoke void @"M.Step"(i32 %t1)
          to label %cont.1 unwind label %fin.pad.1, !dbg !28
cont.1:
  call void @"M.Run.$fin1"(ptr %$frame), !dbg !29
  br label %try.end.1, !dbg !29
fin.pad.1:
  %cl.1 = cleanuppad within none [], !dbg !29
  call void @"M.Run.$fin1"(ptr %$frame) [ "funclet"(token %cl.1) ], !dbg !29
  cleanupret from %cl.1 unwind to caller, !dbg !29
try.end.1:
  %t2 = load i32, ptr %Result.addr, align 4, !dbg !31
  invoke void @"M.Step"(i32 %t2)
          to label %cont.2 unwind label %ex.dispatch.2, !dbg !31
cont.2:
  br label %try.end.2, !dbg !30
ex.dispatch.2:
  %cs.2 = catchswitch within none [label %ex.pad.2] unwind to caller, !dbg !32
ex.pad.2:
  %cp.2 = catchpad within %cs.2 [ptr @"System._SehFilter"], !dbg !32
  catchret from %cp.2 to label %ex.body.2, !dbg !32
ex.body.2:
  %t3 = call ptr @"System._BeginCatch"(), !dbg !32
  store ptr %t3, ptr %$tmp1.addr, align 8, !dbg !32
  %t4 = load ptr, ptr %$tmp1.addr, align 8, !dbg !32
  %t5 = call i8 @"System._IsClass"(ptr %t4, ptr @"M.EA.$vmt"), !dbg !32
  %t6 = icmp ne i8 %t5, 0, !dbg !32
  br i1 %t6, label %if.then.3, label %if.else.3, !dbg !32
if.then.3:
  %t7 = load ptr, ptr %$tmp1.addr, align 8, !dbg !32
  store ptr %t7, ptr %E.addr, align 8, !dbg !32
  %t8 = load ptr, ptr %E.addr, align 8, !dbg !33
  store ptr %t8, ptr %X.addr, align 8, !dbg !33
  br label %if.end.3, !dbg !33
if.else.3:
  invoke void @"System._ReRaise"() #1
          to label %cont.3 unwind label %ex.cleanup.2, !dbg !32
cont.3:
  unreachable, !dbg !32
if.end.3:
  call void @"System._EndCatch"(), !dbg !34
  br label %try.end.2, !dbg !34
ex.cleanup.2:
  %cl.2 = cleanuppad within none [], !dbg !34
  call void @"System._EndCatch"() [ "funclet"(token %cl.2) ], !dbg !34
  cleanupret from %cl.2 unwind to caller, !dbg !34
try.end.2:
  br label %exit, !dbg !16
exit:
  %t9 = load i32, ptr %Result.addr, align 4, !dbg !16
  ret i32 %t9, !dbg !16
}

define internal void @"M.Run.$fin1"(ptr %$link) #3 !dbg !36 {
entry:
  %t1 = getelementptr inbounds %"M.Run.$frame", ptr %$link, i32 0, i32 1, !dbg !42
  %t2 = load i32, ptr %t1, align 4, !dbg !42
  %t3 = add i32 %t2, 1, !dbg !42
  %t4 = getelementptr inbounds %"M.Run.$frame", ptr %$link, i32 0, i32 1, !dbg !42
  store i32 %t3, ptr %t4, align 4, !dbg !42
  br label %exit, !dbg !40
exit:
  ret void, !dbg !40
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
attributes #1 = { noreturn uwtable }
attributes #3 = { noinline optnone uwtable "frame-pointer"="all" }

!llvm.dbg.cu = !{!0}
!llvm.module.flags = !{!1, !2}
!0 = distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !3, producer: "FreexComp opc 0.1.0", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug, splitDebugInlining: false, nameTableKind: None)
!1 = !{i32 2, !"Debug Info Version", i32 3}
!2 = !{i32 2, !"CodeView", i32 1}
!3 = !DIFile(filename: "M.pas", directory: "C:\5Cp", checksumkind: CSK_MD5, checksum: "7b45470cbadf95cde20d4e77147cc121")
!4 = distinct !DISubprogram(name: "Step", linkageName: "M.Step", scope: !3, file: !3, line: 9, type: !7, scopeLine: 9, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !11)
!5 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!6 = !{null, !5}
!7 = !DISubroutineType(types: !6)
!8 = !DILocation(line: 9, column: 29, scope: !4)
!9 = !DILocation(line: 9, column: 35, scope: !4)
!10 = !DILocalVariable(name: "N", arg: 1, scope: !4, file: !3, line: 9, type: !5)
!11 = !{!10}
!12 = distinct !DISubprogram(name: "Run", linkageName: "M.Run", scope: !3, file: !3, line: 10, type: !14, scopeLine: 13, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !35)
!13 = !{!5, !5}
!14 = !DISubroutineType(types: !13)
!15 = !DILocation(line: 13, column: 1, scope: !12)
!16 = !DILocation(line: 26, column: 1, scope: !12)
!17 = !DILocalVariable(name: "N", arg: 1, scope: !12, file: !3, line: 10, type: !5)
!18 = !DILocalVariable(name: "Result", scope: !12, file: !3, line: 10, type: !5)
!19 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !20, size: 64)
!20 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "EA", size: 64, elements: !23)
!21 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: null, size: 64)
!22 = !DIDerivedType(tag: DW_TAG_member, name: "$vmt", scope: !20, baseType: !21, size: 64, offset: 0)
!23 = !{!22}
!24 = !DILocalVariable(name: "X", scope: !12, file: !3, line: 12, type: !19)
!25 = !DILocalVariable(name: "E", scope: !12, file: !3, line: 23, type: !19)
!26 = !DILocation(line: 14, column: 3, scope: !12)
!27 = !DILocation(line: 15, column: 3, scope: !12)
!28 = !DILocation(line: 16, column: 5, scope: !12)
!29 = !DILocation(line: 17, column: 3, scope: !12)
!30 = !DILocation(line: 20, column: 3, scope: !12)
!31 = !DILocation(line: 21, column: 5, scope: !12)
!32 = !DILocation(line: 22, column: 3, scope: !12)
!33 = !DILocation(line: 24, column: 7, scope: !12)
!34 = !DILocation(line: 25, column: 3, scope: !12)
!35 = !{!17, !18, !24, !25}
!36 = distinct !DISubprogram(name: "Run.$fin1", linkageName: "M.Run.$fin1", scope: !12, file: !3, line: 17, type: !38, scopeLine: 17, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0)
!37 = !{null, !21}
!38 = !DISubroutineType(types: !37)
!39 = !DILocation(line: 17, column: 3, scope: !36)
!40 = !DILocation(line: 19, column: 3, scope: !36)
!41 = !DILocation(line: 15, column: 3, scope: !36)
!42 = !DILocation(line: 18, column: 5, scope: !36)
