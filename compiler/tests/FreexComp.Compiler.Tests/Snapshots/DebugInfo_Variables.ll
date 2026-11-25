; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.TPoint" = type { i32, i32 }

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 87, i16 111, i16 114, i16 100, i16 0] }, align 16
$"System.Word.$rtti" = comdat any
@"System.Word.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.0, i32 0, i32 5), i8 3, [7 x i8] zeroinitializer, i64 0, i64 65535 }, comdat, align 8
$"M.$rtti.array of Word" = comdat any
@"M.$rtti.array of Word" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr } { i8 14, [7 x i8] zeroinitializer, ptr null, i64 2, ptr @"System.Word.$rtti", ptr null }, comdat, align 8
@"M.Counter" = global i32 zeroinitializer, align 4, !dbg !29
@"M.Hidden" = internal global i8 1, align 1, !dbg !31
@"M.Grid" = internal global [3 x [2 x i8]] zeroinitializer, align 1, !dbg !39
@"M.Flags" = internal global i8 zeroinitializer, align 1, !dbg !47
@"M.Name" = internal global ptr zeroinitializer, align 8, !dbg !50
@"M.Short" = internal global [11 x i8] zeroinitializer, align 1, !dbg !55
@"M.Head" = internal global ptr zeroinitializer, align 8, !dbg !65
@"M.Ok" = internal global i8 zeroinitializer, align 1, !dbg !72
@"M.Letter" = internal global i16 zeroinitializer, align 2, !dbg !75
@"M.Ratio" = internal global double zeroinitializer, align 8, !dbg !77
@"M.Words" = internal global ptr zeroinitializer, align 8, !dbg !80

declare void @"System._DynArrayClr"(ptr, ptr)
declare void @"System._UStrClr"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define i32 @"M.Sum"(i64 %P, ptr %Total, ptr %Items, i64 %Items$high) #0 !dbg !4 {
entry:
  %P.addr = alloca %"M.TPoint", align 4, !dbg !15
  %Items.addr = alloca ptr, align 8, !dbg !15
  %Items$high.addr = alloca i64, align 8, !dbg !15
  %Result.addr = alloca i32, align 4, !dbg !15
  %I.addr = alloca i32, align 4, !dbg !15
  %$tmp1.addr = alloca i32, align 4, !dbg !15
  %$tmp2.addr = alloca i32, align 4, !dbg !15
  store i64 %P, ptr %P.addr, align 4, !dbg !15
  store ptr %Items, ptr %Items.addr, align 8, !dbg !15
  store i64 %Items$high, ptr %Items$high.addr, align 8, !dbg !15
  %Total.ref = alloca ptr, align 8, !dbg !15
  store ptr %Total, ptr %Total.ref, align 8, !dbg !15
    #dbg_declare(ptr %P.addr, !17, !DIExpression(), !15)
    #dbg_declare(ptr %Total.ref, !18, !DIExpression(DW_OP_deref), !15)
    #dbg_declare(ptr %Items.addr, !19, !DIExpression(), !15)
    #dbg_declare(ptr %Items$high.addr, !20, !DIExpression(), !15)
    #dbg_declare(ptr %Result.addr, !21, !DIExpression(), !15)
    #dbg_declare(ptr %I.addr, !22, !DIExpression(), !15)
  %t1 = getelementptr inbounds %"M.TPoint", ptr %P.addr, i32 0, i32 0, !dbg !23
  %t2 = load i32, ptr %t1, align 4, !dbg !23
  %t3 = getelementptr inbounds %"M.TPoint", ptr %P.addr, i32 0, i32 1, !dbg !23
  %t4 = load i32, ptr %t3, align 4, !dbg !23
  %t5 = add i32 %t2, %t4, !dbg !23
  store i32 %t5, ptr %Result.addr, align 4, !dbg !23
  store i32 0, ptr %$tmp1.addr, align 4, !dbg !24
  %t6 = load i64, ptr %Items$high.addr, align 8, !dbg !24
  %t7 = trunc i64 %t6 to i32, !dbg !24
  store i32 %t7, ptr %$tmp2.addr, align 4, !dbg !24
  %t8 = load i32, ptr %$tmp1.addr, align 4, !dbg !24
  %t9 = load i32, ptr %$tmp2.addr, align 4, !dbg !24
  %t10 = icmp sle i32 %t8, %t9, !dbg !24
  br i1 %t10, label %if.then.1, label %if.end.1, !dbg !24
if.then.1:
  %t11 = load i32, ptr %$tmp1.addr, align 4, !dbg !24
  store i32 %t11, ptr %I.addr, align 4, !dbg !24
  br label %for.body.2, !dbg !24
for.body.2:
  %t12 = load i32, ptr %Result.addr, align 4, !dbg !25
  %t13 = load ptr, ptr %Items.addr, align 8, !dbg !25
  %t14 = load i32, ptr %I.addr, align 4, !dbg !25
  %t15 = sext i32 %t14 to i64, !dbg !25
  %t16 = getelementptr inbounds i32, ptr %t13, i64 %t15, !dbg !25
  %t17 = load i32, ptr %t16, align 4, !dbg !25
  %t18 = add i32 %t12, %t17, !dbg !25
  store i32 %t18, ptr %Result.addr, align 4, !dbg !25
  br label %for.inc.2, !dbg !24
for.inc.2:
  %t19 = load i32, ptr %I.addr, align 4, !dbg !24
  %t20 = load i32, ptr %$tmp2.addr, align 4, !dbg !24
  %t21 = icmp eq i32 %t19, %t20, !dbg !24
  br i1 %t21, label %for.end.2, label %if.end.3, !dbg !24
if.end.3:
  %t22 = load i32, ptr %I.addr, align 4, !dbg !24
  %t23 = add i32 %t22, 1, !dbg !24
  store i32 %t23, ptr %I.addr, align 4, !dbg !24
  br label %for.body.2, !dbg !24
for.end.2:
  br label %if.end.1, !dbg !24
if.end.1:
  %t24 = load i64, ptr %Total, align 8, !dbg !26
  %t25 = load i32, ptr %Result.addr, align 4, !dbg !26
  %t26 = sext i32 %t25 to i64, !dbg !26
  %t27 = add i64 %t24, %t26, !dbg !26
  store i64 %t27, ptr %Total, align 8, !dbg !26
  %t28 = load i32, ptr @"M.Counter", align 4, !dbg !27
  %t29 = add i32 %t28, 1, !dbg !27
  store i32 %t29, ptr @"M.Counter", align 4, !dbg !27
  br label %exit, !dbg !16
exit:
  %t30 = load i32, ptr %Result.addr, align 4, !dbg !16
  ret i32 %t30, !dbg !16
}

define void @"M.$init"() #0 {
entry:
  br label %exit
exit:
  ret void
}

define void @"M.$final"() #0 personality ptr @__C_specific_handler {
entry:
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  call void @"System._DynArrayClr"(ptr @"M.Words", ptr @"M.$rtti.array of Word")
  call void @"System._UStrClr"(ptr @"M.Name")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._DynArrayClr"(ptr @"M.Words", ptr @"M.$rtti.array of Word") [ "funclet"(token %cl) ]
  call void @"System._UStrClr"(ptr @"M.Name") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

attributes #0 = { uwtable "frame-pointer"="all" }

!llvm.dbg.cu = !{!0}
!llvm.module.flags = !{!1, !2, !84}
!0 = distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !3, producer: "FreexComp opc 0.1.0", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug, enums: !85, globals: !86, splitDebugInlining: false, nameTableKind: None)
!1 = !{i32 2, !"Debug Info Version", i32 3}
!2 = !{i32 2, !"CodeView", i32 1}
!3 = !DIFile(filename: "M.pas", directory: "C:\5Cp", checksumkind: CSK_MD5, checksum: "5fd3d4ab624306f7ee901f05d377183b")
!4 = distinct !DISubprogram(name: "Sum", linkageName: "M.Sum", scope: !3, file: !3, line: 28, type: !14, scopeLine: 31, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !28)
!5 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!6 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "TPoint", size: 64, elements: !9)
!7 = !DIDerivedType(tag: DW_TAG_member, name: "X", scope: !6, baseType: !5, size: 32, offset: 0)
!8 = !DIDerivedType(tag: DW_TAG_member, name: "Y", scope: !6, baseType: !5, size: 32, offset: 32)
!9 = !{!7, !8}
!10 = !DIBasicType(name: "Int64", size: 64, encoding: DW_ATE_signed)
!11 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !10, size: 64)
!12 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !5, size: 64)
!13 = !{!5, !6, !11, !12, !10}
!14 = !DISubroutineType(types: !13)
!15 = !DILocation(line: 31, column: 1, scope: !4)
!16 = !DILocation(line: 37, column: 1, scope: !4)
!17 = !DILocalVariable(name: "P", arg: 1, scope: !4, file: !3, line: 28, type: !6)
!18 = !DILocalVariable(name: "Total", arg: 2, scope: !4, file: !3, line: 28, type: !10)
!19 = !DILocalVariable(name: "Items", arg: 3, scope: !4, file: !3, line: 28, type: !12)
!20 = !DILocalVariable(name: "Items$high", arg: 4, scope: !4, file: !3, line: 28, type: !10, flags: DIFlagArtificial)
!21 = !DILocalVariable(name: "Result", scope: !4, file: !3, line: 28, type: !5)
!22 = !DILocalVariable(name: "I", scope: !4, file: !3, line: 30, type: !5)
!23 = !DILocation(line: 32, column: 3, scope: !4)
!24 = !DILocation(line: 33, column: 3, scope: !4)
!25 = !DILocation(line: 34, column: 5, scope: !4)
!26 = !DILocation(line: 35, column: 3, scope: !4)
!27 = !DILocation(line: 36, column: 3, scope: !4)
!28 = !{!17, !18, !19, !20, !21, !22}
!29 = !DIGlobalVariableExpression(var: !30, expr: !DIExpression())
!30 = distinct !DIGlobalVariable(name: "Counter", linkageName: "M.Counter", scope: !0, file: !3, line: 14, type: !5, isLocal: false, isDefinition: true)
!31 = !DIGlobalVariableExpression(var: !38, expr: !DIExpression())
!32 = !DICompositeType(tag: DW_TAG_enumeration_type, name: "TColor", baseType: !33, size: 8, elements: !37)
!33 = !DIBasicType(name: "Byte", size: 8, encoding: DW_ATE_unsigned)
!34 = !DIEnumerator(name: "clRed", value: 0, isUnsigned: true)
!35 = !DIEnumerator(name: "clGreen", value: 1, isUnsigned: true)
!36 = !DIEnumerator(name: "clBlue", value: 2, isUnsigned: true)
!37 = !{!34, !35, !36}
!38 = distinct !DIGlobalVariable(name: "Hidden", linkageName: "M.Hidden", scope: !0, file: !3, line: 18, type: !32, isLocal: true, isDefinition: true)
!39 = !DIGlobalVariableExpression(var: !46, expr: !DIExpression())
!40 = !DICompositeType(tag: DW_TAG_array_type, baseType: !41, size: 48, elements: !45)
!41 = !DICompositeType(tag: DW_TAG_array_type, baseType: !33, size: 16, elements: !43)
!42 = !DISubrange(count: 2, lowerBound: 0)
!43 = !{!42}
!44 = !DISubrange(count: 3, lowerBound: 1)
!45 = !{!44}
!46 = distinct !DIGlobalVariable(name: "Grid", linkageName: "M.Grid", scope: !0, file: !3, line: 19, type: !40, isLocal: true, isDefinition: true)
!47 = !DIGlobalVariableExpression(var: !49, expr: !DIExpression())
!48 = !DIBasicType(name: "set of TColor", size: 8, encoding: DW_ATE_unsigned)
!49 = distinct !DIGlobalVariable(name: "Flags", linkageName: "M.Flags", scope: !0, file: !3, line: 20, type: !48, isLocal: true, isDefinition: true)
!50 = !DIGlobalVariableExpression(var: !54, expr: !DIExpression())
!51 = !DIBasicType(name: "Char", size: 16, encoding: DW_ATE_UTF)
!52 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !51, size: 64)
!53 = !DIDerivedType(tag: DW_TAG_typedef, name: "string", baseType: !52)
!54 = distinct !DIGlobalVariable(name: "Name", linkageName: "M.Name", scope: !0, file: !3, line: 21, type: !53, isLocal: true, isDefinition: true)
!55 = !DIGlobalVariableExpression(var: !64, expr: !DIExpression())
!56 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "string[10]", size: 88, elements: !63)
!57 = !DIDerivedType(tag: DW_TAG_member, name: "length", scope: !56, baseType: !33, size: 8, offset: 0)
!58 = !DIBasicType(name: "AnsiChar", size: 8, encoding: DW_ATE_unsigned_char)
!59 = !DISubrange(count: 10, lowerBound: 1)
!60 = !{!59}
!61 = !DICompositeType(tag: DW_TAG_array_type, baseType: !58, size: 80, elements: !60)
!62 = !DIDerivedType(tag: DW_TAG_member, name: "chars", scope: !56, baseType: !61, size: 80, offset: 8)
!63 = !{!57, !62}
!64 = distinct !DIGlobalVariable(name: "Short", linkageName: "M.Short", scope: !0, file: !3, line: 22, type: !56, isLocal: true, isDefinition: true)
!65 = !DIGlobalVariableExpression(var: !71, expr: !DIExpression())
!66 = !DIDerivedType(tag: DW_TAG_pointer_type, name: "PNode", baseType: !67, size: 64)
!67 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "TNode", size: 128, elements: !70)
!68 = !DIDerivedType(tag: DW_TAG_member, name: "Value", scope: !67, baseType: !10, size: 64, offset: 0)
!69 = !DIDerivedType(tag: DW_TAG_member, name: "Next", scope: !67, baseType: !66, size: 64, offset: 64)
!70 = !{!68, !69}
!71 = distinct !DIGlobalVariable(name: "Head", linkageName: "M.Head", scope: !0, file: !3, line: 23, type: !66, isLocal: true, isDefinition: true)
!72 = !DIGlobalVariableExpression(var: !74, expr: !DIExpression())
!73 = !DIBasicType(name: "Boolean", size: 8, encoding: DW_ATE_boolean)
!74 = distinct !DIGlobalVariable(name: "Ok", linkageName: "M.Ok", scope: !0, file: !3, line: 24, type: !73, isLocal: true, isDefinition: true)
!75 = !DIGlobalVariableExpression(var: !76, expr: !DIExpression())
!76 = distinct !DIGlobalVariable(name: "Letter", linkageName: "M.Letter", scope: !0, file: !3, line: 25, type: !51, isLocal: true, isDefinition: true)
!77 = !DIGlobalVariableExpression(var: !79, expr: !DIExpression())
!78 = !DIBasicType(name: "Double", size: 64, encoding: DW_ATE_float)
!79 = distinct !DIGlobalVariable(name: "Ratio", linkageName: "M.Ratio", scope: !0, file: !3, line: 26, type: !78, isLocal: true, isDefinition: true)
!80 = !DIGlobalVariableExpression(var: !83, expr: !DIExpression())
!81 = !DIDerivedType(tag: DW_TAG_pointer_type, name: "array of Word", baseType: !82, size: 64)
!82 = !DIBasicType(name: "Word", size: 16, encoding: DW_ATE_unsigned)
!83 = distinct !DIGlobalVariable(name: "Words", linkageName: "M.Words", scope: !0, file: !3, line: 27, type: !81, isLocal: true, isDefinition: true)
!84 = !{i32 2, !"eh-asynch", i32 1}
!85 = !{!32}
!86 = !{!29, !31, !39, !47, !50, !55, !65, !72, !75, !77, !80}
