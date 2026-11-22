; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [9 x i16] } { i32 -1, i32 8, i16 1200, i16 2, i32 0, [9 x i16] [i16 84, i16 67, i16 111, i16 117, i16 110, i16 116, i16 101, i16 114, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [4 x i16] } { i32 -1, i32 3, i16 1200, i16 2, i32 0, [4 x i16] [i16 65, i16 100, i16 100, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 86, i16 97, i16 108, i16 117, i16 101, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@"M.TCounter.$rtti.methods" = private unnamed_addr constant [1 x { ptr, ptr, i8, i8, [6 x i8] }] [{ ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.1, i32 0, i32 5), ptr @"M.TCounter.Add", i8 2, i8 0, [6 x i8] zeroinitializer }], align 8
@"M.TCounter.$rtti.fields" = private unnamed_addr constant [1 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.2, i32 0, i32 5), i64 8, ptr @"System.Integer.$rtti", i8 2, [7 x i8] zeroinitializer }], align 8
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.3, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
@"M.TCounter.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [9 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TCounter.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.4, i32 0, i32 5), i64 0, ptr null, i64 1, ptr @"M.TCounter.$rtti.methods", i64 1, ptr @"M.TCounter.$rtti.fields", i64 0, ptr null }, align 8
@"M.TCounter.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [9 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.TCounter.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8

define void @"M.TCounter.Add"(ptr %Self, i32 %N) #0 !dbg !4 {
entry:
  %Self.addr = alloca ptr, align 8, !dbg !14
  %N.addr = alloca i32, align 4, !dbg !14
  store ptr %Self, ptr %Self.addr, align 8, !dbg !14
  store i32 %N, ptr %N.addr, align 4, !dbg !14
    #dbg_declare(ptr %Self.addr, !16, !DIExpression(), !14)
    #dbg_declare(ptr %N.addr, !17, !DIExpression(), !14)
  %t1 = load ptr, ptr %Self.addr, align 8, !dbg !18
  %t2 = getelementptr inbounds i8, ptr %t1, i64 8, !dbg !18
  %t3 = load i32, ptr %t2, align 4, !dbg !18
  %t4 = load i32, ptr %N.addr, align 4, !dbg !18
  %t5 = add i32 %t3, %t4, !dbg !18
  %t6 = load ptr, ptr %Self.addr, align 8, !dbg !18
  %t7 = getelementptr inbounds i8, ptr %t6, i64 8, !dbg !18
  store i32 %t5, ptr %t7, align 4, !dbg !18
  br label %exit, !dbg !15
exit:
  ret void, !dbg !15
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
!3 = !DIFile(filename: "M.pas", directory: "C:\5Cp", checksumkind: CSK_MD5, checksum: "9c5fc84ec7a0a34638922573f39698c5")
!4 = distinct !DISubprogram(name: "TCounter.Add", linkageName: "M.TCounter.Add", scope: !3, file: !3, line: 9, type: !13, scopeLine: 10, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !19)
!5 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !6, size: 64)
!6 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "TCounter", size: 128, elements: !11)
!7 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: null, size: 64)
!8 = !DIDerivedType(tag: DW_TAG_member, name: "$vmt", scope: !6, baseType: !7, size: 64, offset: 0)
!9 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!10 = !DIDerivedType(tag: DW_TAG_member, name: "Value", scope: !6, baseType: !9, size: 32, offset: 64)
!11 = !{!8, !10}
!12 = !{null, !5, !9}
!13 = !DISubroutineType(types: !12)
!14 = !DILocation(line: 10, column: 1, scope: !4)
!15 = !DILocation(line: 12, column: 1, scope: !4)
!16 = !DILocalVariable(name: "Self", arg: 1, scope: !4, file: !3, line: 9, type: !5, flags: DIFlagArtificial | DIFlagObjectPointer)
!17 = !DILocalVariable(name: "N", arg: 2, scope: !4, file: !3, line: 9, type: !9)
!18 = !DILocation(line: 11, column: 3, scope: !4)
!19 = !{!16, !17}
