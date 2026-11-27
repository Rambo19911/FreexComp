; ModuleID = 'P'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"P.Run.$frame" = type { ptr, i32, i32 }

@"$units" = internal constant [0 x { ptr, ptr }] [], align 8

declare ptr @"System._BeginCatch"()
declare void @"System._Halt0"() #1
declare void @"System._InitUnits"()
declare i32 @"System._SehFilter"(ptr, ptr)
declare void @"System._Startup"(ptr, i64)
declare void @"System._UnhandledException"() #1
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define internal void @"P.Run"(i32 %N) #0 personality ptr @__C_specific_handler !dbg !4 {
entry:
  %$frame = alloca %"P.Run.$frame", align 8, !dbg !8
  %N.addr = alloca i32, align 4, !dbg !8
  %Top.addr = alloca i32, align 4, !dbg !8
  %Inner.addr = alloca i32, align 4, !dbg !8
  %Limit.addr = alloca i32, align 4, !dbg !8
  %R.addr = alloca i32, align 4, !dbg !8
  %T.addr = alloca i32, align 4, !dbg !8
  %C.addr = alloca i32, align 4, !dbg !8
  %I.addr = alloca i32, align 4, !dbg !8
  %$tmp1.addr = alloca i32, align 4, !dbg !8
  %$tmp2.addr = alloca i32, align 4, !dbg !8
  %J.addr = alloca i32, align 4, !dbg !8
  %$tmp3.addr = alloca i32, align 4, !dbg !8
  %$tmp4.addr = alloca i32, align 4, !dbg !8
  %A.addr = alloca i32, align 4, !dbg !8
  %B.addr = alloca i32, align 4, !dbg !8
  store volatile ptr null, ptr %$frame, align 8, !dbg !8
  %F.addr = getelementptr inbounds %"P.Run.$frame", ptr %$frame, i32 0, i32 1, !dbg !8
  %Total.addr = getelementptr inbounds %"P.Run.$frame", ptr %$frame, i32 0, i32 2, !dbg !8
  store volatile i32 %N, ptr %N.addr, align 4, !dbg !8
    #dbg_declare(ptr %N.addr, !10, !DIExpression(), !8)
    #dbg_declare(ptr %$frame, !11, !DIExpression(DW_OP_plus_uconst, 12), !8)
    #dbg_declare(ptr %Top.addr, !12, !DIExpression(), !8)
    #dbg_declare(ptr %Inner.addr, !13, !DIExpression(), !8)
    #dbg_declare(ptr %Limit.addr, !14, !DIExpression(), !8)
    #dbg_declare(ptr %R.addr, !15, !DIExpression(), !8)
    #dbg_declare(ptr %T.addr, !16, !DIExpression(), !8)
    #dbg_declare(ptr %$frame, !17, !DIExpression(DW_OP_plus_uconst, 8), !8)
    #dbg_declare(ptr %C.addr, !18, !DIExpression(), !8)
    #dbg_declare(ptr %I.addr, !19, !DIExpression(), !8)
    #dbg_declare(ptr %J.addr, !20, !DIExpression(), !8)
    #dbg_declare(ptr %A.addr, !21, !DIExpression(), !8)
    #dbg_declare(ptr %B.addr, !22, !DIExpression(), !8)
  %t1 = load volatile i32, ptr %N.addr, align 4, !dbg !23
  store volatile i32 %t1, ptr %Top.addr, align 4, !dbg !23
  %t2 = load volatile i32, ptr %Top.addr, align 4, !dbg !24
  store volatile i32 %t2, ptr %Total.addr, align 4, !dbg !24
  %t3 = load volatile i32, ptr %N.addr, align 4, !dbg !25
  %t4 = icmp sgt i32 %t3, 0, !dbg !25
  br i1 %t4, label %if.then.1, label %if.end.1, !dbg !25
if.then.1:
  store volatile i32 1, ptr %Inner.addr, align 4, !dbg !26
  store volatile i32 5, ptr %Limit.addr, align 4, !dbg !27
  %t5 = load volatile i32, ptr %Total.addr, align 4, !dbg !28
  %t6 = load volatile i32, ptr %Inner.addr, align 4, !dbg !28
  %t7 = add i32 %t5, %t6, !dbg !28
  %t8 = load volatile i32, ptr %Limit.addr, align 4, !dbg !28
  %t9 = add i32 %t7, %t8, !dbg !28
  store volatile i32 %t9, ptr %Total.addr, align 4, !dbg !28
  br label %if.end.1, !dbg !28
if.end.1:
  br label %repeat.body.2, !dbg !31
repeat.body.2:
  %t10 = load volatile i32, ptr %Total.addr, align 4, !dbg !31
  store volatile i32 %t10, ptr %R.addr, align 4, !dbg !31
  %t11 = load volatile i32, ptr %R.addr, align 4, !dbg !32
  %t12 = sub i32 %t11, 1, !dbg !32
  store volatile i32 %t12, ptr %Total.addr, align 4, !dbg !32
  br label %repeat.cond.2, !dbg !30
repeat.cond.2:
  %t13 = load volatile i32, ptr %Total.addr, align 4, !dbg !30
  %t14 = icmp slt i32 %t13, 0, !dbg !30
  br i1 %t14, label %repeat.end.2, label %if.end.3, !dbg !30
if.end.3:
  br label %repeat.body.2, !dbg !30
repeat.end.2:
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %fin.pad.4, !dbg !33
cont.1:
  store volatile i32 1, ptr %T.addr, align 4, !dbg !34
  %t15 = load volatile i32, ptr %T.addr, align 4, !dbg !35
  store volatile i32 %t15, ptr %Total.addr, align 4, !dbg !35
  invoke void @llvm.seh.try.end()
          to label %cont.2 unwind label %fin.pad.4, !dbg !36
cont.2:
  call void @"P.Run.$fin1"(ptr %$frame), !dbg !36
  br label %try.end.4, !dbg !36
fin.pad.4:
  %cl.4 = cleanuppad within none [], !dbg !36
  call void @"P.Run.$fin1"(ptr %$frame) [ "funclet"(token %cl.4) ], !dbg !36
  cleanupret from %cl.4 unwind to caller, !dbg !36
try.end.4:
  %t16 = load volatile i32, ptr %N.addr, align 4, !dbg !37
  switch i32 %t16, label %case.else.5 [
    i32 1, label %case.arm.5.1
  ], !dbg !37
case.arm.5.1:
  store volatile i32 1, ptr %Total.addr, align 4, !dbg !38
  br label %case.end.5, !dbg !38
case.else.5:
  store volatile i32 3, ptr %C.addr, align 4, !dbg !39
  %t17 = load volatile i32, ptr %C.addr, align 4, !dbg !40
  store volatile i32 %t17, ptr %Total.addr, align 4, !dbg !40
  br label %case.end.5, !dbg !40
case.end.5:
  store volatile i32 1, ptr %$tmp1.addr, align 4, !dbg !41
  store volatile i32 3, ptr %$tmp2.addr, align 4, !dbg !41
  %t18 = load volatile i32, ptr %$tmp1.addr, align 4, !dbg !41
  %t19 = load volatile i32, ptr %$tmp2.addr, align 4, !dbg !41
  %t20 = icmp sle i32 %t18, %t19, !dbg !41
  br i1 %t20, label %if.then.6, label %if.end.6, !dbg !41
if.then.6:
  %t21 = load volatile i32, ptr %$tmp1.addr, align 4, !dbg !41
  store volatile i32 %t21, ptr %I.addr, align 4, !dbg !41
  br label %for.body.7, !dbg !41
for.body.7:
  %t22 = load volatile i32, ptr %Total.addr, align 4, !dbg !42
  %t23 = load volatile i32, ptr %I.addr, align 4, !dbg !42
  %t24 = add i32 %t22, %t23, !dbg !42
  store volatile i32 %t24, ptr %Total.addr, align 4, !dbg !42
  br label %for.inc.7, !dbg !41
for.inc.7:
  %t25 = load volatile i32, ptr %I.addr, align 4, !dbg !41
  %t26 = load volatile i32, ptr %$tmp2.addr, align 4, !dbg !41
  %t27 = icmp eq i32 %t25, %t26, !dbg !41
  br i1 %t27, label %for.end.7, label %if.end.8, !dbg !41
if.end.8:
  %t28 = load volatile i32, ptr %I.addr, align 4, !dbg !41
  %t29 = add i32 %t28, 1, !dbg !41
  store volatile i32 %t29, ptr %I.addr, align 4, !dbg !41
  br label %for.body.7, !dbg !41
for.end.7:
  br label %if.end.6, !dbg !41
if.end.6:
  store volatile i32 1, ptr %$tmp3.addr, align 4, !dbg !43
  store volatile i32 2, ptr %$tmp4.addr, align 4, !dbg !43
  %t30 = load volatile i32, ptr %$tmp3.addr, align 4, !dbg !43
  %t31 = load volatile i32, ptr %$tmp4.addr, align 4, !dbg !43
  %t32 = icmp sle i32 %t30, %t31, !dbg !43
  br i1 %t32, label %if.then.9, label %if.end.9, !dbg !43
if.then.9:
  %t33 = load volatile i32, ptr %$tmp3.addr, align 4, !dbg !43
  store volatile i32 %t33, ptr %J.addr, align 4, !dbg !43
  br label %for.body.10, !dbg !43
for.body.10:
  %t34 = load volatile i32, ptr %J.addr, align 4, !dbg !45
  store volatile i32 %t34, ptr %A.addr, align 4, !dbg !45
  %t35 = load volatile i32, ptr %A.addr, align 4, !dbg !46
  store volatile i32 %t35, ptr %B.addr, align 4, !dbg !46
  %t36 = load volatile i32, ptr %Total.addr, align 4, !dbg !47
  %t37 = load volatile i32, ptr %B.addr, align 4, !dbg !47
  %t38 = add i32 %t36, %t37, !dbg !47
  store volatile i32 %t38, ptr %Total.addr, align 4, !dbg !47
  br label %for.inc.10, !dbg !43
for.inc.10:
  %t39 = load volatile i32, ptr %J.addr, align 4, !dbg !43
  %t40 = load volatile i32, ptr %$tmp4.addr, align 4, !dbg !43
  %t41 = icmp eq i32 %t39, %t40, !dbg !43
  br i1 %t41, label %for.end.10, label %if.end.11, !dbg !43
if.end.11:
  %t42 = load volatile i32, ptr %J.addr, align 4, !dbg !43
  %t43 = add i32 %t42, 1, !dbg !43
  store volatile i32 %t43, ptr %J.addr, align 4, !dbg !43
  br label %for.body.10, !dbg !43
for.end.10:
  br label %if.end.9, !dbg !43
if.end.9:
  br label %exit, !dbg !9
exit:
  ret void, !dbg !9
}

define internal void @"P.Run.$fin1"(ptr %$link) #3 !dbg !49 {
entry:
  %t1 = getelementptr inbounds %"P.Run.$frame", ptr %$link, i32 0, i32 1, !dbg !56
  store i32 2, ptr %t1, align 4, !dbg !56
  %t2 = getelementptr inbounds %"P.Run.$frame", ptr %$link, i32 0, i32 1, !dbg !57
  %t3 = load i32, ptr %t2, align 4, !dbg !57
  %t4 = getelementptr inbounds %"P.Run.$frame", ptr %$link, i32 0, i32 2, !dbg !57
  store i32 %t3, ptr %t4, align 4, !dbg !57
  br label %exit, !dbg !54
exit:
  ret void, !dbg !54
}

define internal void @"$main"() #0 !dbg !58 {
entry:
  call void @"System._InitUnits"(), !dbg !61
  call void @"P.Run"(i32 1), !dbg !63
  br label %exit, !dbg !62
exit:
  ret void, !dbg !62
}

define void @_start() #0 personality ptr @__C_specific_handler {
entry:
  call void @"System._Startup"(ptr @"$units", i64 0)
  invoke void @llvm.seh.try.begin()
          to label %try.begun unwind label %catch.dispatch
try.begun:
  invoke void @"$main"()
          to label %try.called unwind label %catch.dispatch
try.called:
  invoke void @llvm.seh.try.end()
          to label %done unwind label %catch.dispatch
done:
  call void @"System._Halt0"() #1
  unreachable
catch.dispatch:
  %cs = catchswitch within none [label %catch] unwind to caller
catch:
  %cp = catchpad within %cs [ptr @"System._SehFilter"]
  catchret from %cp to label %unhandled
unhandled:
  %t1 = call ptr @"System._BeginCatch"()
  call void @"System._UnhandledException"() #1
  unreachable
}

attributes #0 = { uwtable "frame-pointer"="all" }
attributes #1 = { noreturn uwtable }
attributes #3 = { noinline optnone uwtable "frame-pointer"="all" }

!llvm.dbg.cu = !{!0}
!llvm.module.flags = !{!1, !2, !64}
!0 = distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !3, producer: "FreexComp opc 0.1.0", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug, splitDebugInlining: false, nameTableKind: None)
!1 = !{i32 2, !"Debug Info Version", i32 3}
!2 = !{i32 2, !"CodeView", i32 1}
!3 = !DIFile(filename: "M.pas", directory: "C:\5Cp", checksumkind: CSK_MD5, checksum: "1f8aaa63f58f493b2aaf1d0c4fe72ed0")
!4 = distinct !DISubprogram(name: "Run", linkageName: "P.Run", scope: !3, file: !3, line: 3, type: !7, scopeLine: 5, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0, retainedNodes: !48)
!5 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!6 = !{null, !5}
!7 = !DISubroutineType(types: !6)
!8 = !DILocation(line: 5, column: 1, scope: !4)
!9 = !DILocation(line: 40, column: 1, scope: !4)
!10 = !DILocalVariable(name: "N", arg: 1, scope: !4, file: !3, line: 3, type: !5)
!11 = !DILocalVariable(name: "Total", scope: !4, file: !3, line: 4, type: !5)
!12 = !DILocalVariable(name: "Top", scope: !4, file: !3, line: 6, type: !5)
!13 = !DILocalVariable(name: "Inner", scope: !4, file: !3, line: 10, type: !5)
!14 = !DILocalVariable(name: "Limit", scope: !4, file: !3, line: 11, type: !5)
!15 = !DILocalVariable(name: "R", scope: !4, file: !3, line: 15, type: !5)
!16 = !DILocalVariable(name: "T", scope: !4, file: !3, line: 19, type: !5)
!17 = !DILocalVariable(name: "F", scope: !4, file: !3, line: 22, type: !5)
!18 = !DILocalVariable(name: "C", scope: !4, file: !3, line: 28, type: !5)
!19 = !DILocalVariable(name: "I", scope: !4, file: !3, line: 31, type: !5)
!20 = !DILocalVariable(name: "J", scope: !4, file: !3, line: 33, type: !5)
!21 = !DILocalVariable(name: "A", scope: !4, file: !3, line: 35, type: !5)
!22 = !DILocalVariable(name: "B", scope: !4, file: !3, line: 35, type: !5)
!23 = !DILocation(line: 6, column: 3, scope: !4)
!24 = !DILocation(line: 7, column: 3, scope: !4)
!25 = !DILocation(line: 8, column: 3, scope: !4)
!26 = !DILocation(line: 10, column: 5, scope: !4)
!27 = !DILocation(line: 11, column: 5, scope: !4)
!28 = !DILocation(line: 12, column: 5, scope: !4)
!29 = !DILocation(line: 14, column: 3, scope: !4)
!30 = !DILocation(line: 17, column: 3, scope: !4)
!31 = !DILocation(line: 15, column: 5, scope: !4)
!32 = !DILocation(line: 16, column: 5, scope: !4)
!33 = !DILocation(line: 18, column: 3, scope: !4)
!34 = !DILocation(line: 19, column: 5, scope: !4)
!35 = !DILocation(line: 20, column: 5, scope: !4)
!36 = !DILocation(line: 21, column: 3, scope: !4)
!37 = !DILocation(line: 25, column: 3, scope: !4)
!38 = !DILocation(line: 26, column: 8, scope: !4)
!39 = !DILocation(line: 28, column: 5, scope: !4)
!40 = !DILocation(line: 29, column: 5, scope: !4)
!41 = !DILocation(line: 31, column: 3, scope: !4)
!42 = !DILocation(line: 32, column: 5, scope: !4)
!43 = !DILocation(line: 33, column: 3, scope: !4)
!44 = !DILocation(line: 35, column: 5, scope: !4)
!45 = !DILocation(line: 36, column: 5, scope: !4)
!46 = !DILocation(line: 37, column: 5, scope: !4)
!47 = !DILocation(line: 38, column: 5, scope: !4)
!48 = !{!10, !11, !12, !13, !14, !15, !16, !17, !18, !19, !20, !21, !22}
!49 = distinct !DISubprogram(name: "Run.$fin1", linkageName: "P.Run.$fin1", scope: !4, file: !3, line: 21, type: !52, scopeLine: 21, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0)
!50 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: null, size: 64)
!51 = !{null, !50}
!52 = !DISubroutineType(types: !51)
!53 = !DILocation(line: 21, column: 3, scope: !49)
!54 = !DILocation(line: 24, column: 3, scope: !49)
!55 = !DILocation(line: 18, column: 3, scope: !49)
!56 = !DILocation(line: 22, column: 5, scope: !49)
!57 = !DILocation(line: 23, column: 5, scope: !49)
!58 = distinct !DISubprogram(name: "P", linkageName: "$main", scope: !3, file: !3, line: 1, type: !60, scopeLine: 42, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0)
!59 = !{null}
!60 = !DISubroutineType(types: !59)
!61 = !DILocation(line: 42, column: 1, scope: !58)
!62 = !DILocation(line: 44, column: 1, scope: !58)
!63 = !DILocation(line: 43, column: 3, scope: !58)
!64 = !{i32 2, !"eh-asynch", i32 1}
; {
;   "formatVersion": 1,
;   "program": "P",
;   "container": "codeview",
;   "abi": {
;     "pointerSize": 8,
;     "raiseHookSymbol": "System._Raise",
;     "pascalExceptionCode": 3763355987,
;     "exceptionMessageField": "FMessage",
;     "unicodeString": {
;       "lengthOffset": -12,
;       "lengthSize": 4,
;       "lengthUnit": "chars",
;       "charSize": 2
;     },
;     "ansiString": {
;       "lengthOffset": -12,
;       "lengthSize": 4,
;       "codePageOffset": -8,
;       "codePageSize": 2
;     },
;     "dynArray": {
;       "lengthOffset": -8,
;       "lengthSize": 8
;     },
;     "vmt": {
;       "classNameOffset": 16,
;       "classNameKind": "pointerToUnicodeString",
;       "parentOffset": 0,
;       "parentKind": "pointerToVmt"
;     },
;     "set": {
;       "bitOrder": "lsb0"
;     }
;   },
;   "types": [
;     {
;       "id": 1,
;       "name": "Integer",
;       "kind": "integer",
;       "size": 4,
;       "signed": true
;     }
;   ],
;   "units": [
;     {
;       "name": "P",
;       "file": "C:\\p\\M.pas",
;       "globals": [],
;       "routines": [
;         {
;           "displayName": "P",
;           "linkageName": "$main",
;           "file": "C:\\p\\M.pas",
;           "line": 1,
;           "bodyLine": 42,
;           "endLine": 44,
;           "self": null,
;           "vars": []
;         },
;         {
;           "displayName": "Run",
;           "linkageName": "P.Run",
;           "file": "C:\\p\\M.pas",
;           "line": 3,
;           "bodyLine": 5,
;           "endLine": 40,
;           "self": null,
;           "vars": [
;             {
;               "name": "N",
;               "kind": "param",
;               "type": 1
;             },
;             {
;               "name": "Total",
;               "kind": "local",
;               "type": 1
;             },
;             {
;               "name": "Top",
;               "kind": "local",
;               "type": 1,
;               "fromLine": 6
;             },
;             {
;               "name": "Inner",
;               "kind": "local",
;               "type": 1,
;               "fromLine": 10,
;               "toLine": 13
;             },
;             {
;               "name": "Limit",
;               "kind": "local",
;               "type": 1,
;               "fromLine": 11,
;               "toLine": 13
;             },
;             {
;               "name": "R",
;               "kind": "local",
;               "type": 1,
;               "fromLine": 15,
;               "toLine": 17
;             },
;             {
;               "name": "T",
;               "kind": "local",
;               "type": 1,
;               "fromLine": 19,
;               "toLine": 21
;             },
;             {
;               "name": "F",
;               "kind": "local",
;               "type": 1,
;               "fromLine": 22,
;               "toLine": 24
;             },
;             {
;               "name": "C",
;               "kind": "local",
;               "type": 1,
;               "fromLine": 28,
;               "toLine": 30
;             },
;             {
;               "name": "I",
;               "kind": "local",
;               "type": 1,
;               "fromLine": 31,
;               "toLine": 32
;             },
;             {
;               "name": "J",
;               "kind": "local",
;               "type": 1,
;               "fromLine": 33,
;               "toLine": 39
;             },
;             {
;               "name": "A",
;               "kind": "local",
;               "type": 1,
;               "fromLine": 35,
;               "toLine": 39
;             },
;             {
;               "name": "B",
;               "kind": "local",
;               "type": 1,
;               "fromLine": 35,
;               "toLine": 39
;             }
;           ]
;         },
;         {
;           "displayName": "Run.$fin1",
;           "linkageName": "P.Run.$fin1",
;           "file": "C:\\p\\M.pas",
;           "line": 21,
;           "bodyLine": 21,
;           "endLine": 24,
;           "self": null,
;           "parent": "P.Run",
;           "vars": []
;         }
;       ]
;     }
;   ]
; }
