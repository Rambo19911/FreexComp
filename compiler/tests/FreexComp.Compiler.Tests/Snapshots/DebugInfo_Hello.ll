; ModuleID = 'hello'
source_filename = "hello.pas"
target triple = "x86_64-pc-windows-msvc"

@"$units" = internal constant [0 x { ptr, ptr }] [], align 8
@"hello.A" = internal global i32 zeroinitializer, align 4, !dbg !23
@"hello.B" = internal global i32 zeroinitializer, align 4, !dbg !25

declare ptr @"System._BeginCatch"()
declare void @"System._Halt0"() #1
declare void @"System._InitUnits"()
declare i32 @"System._SehFilter"(ptr, ptr)
declare void @"System._Startup"(ptr, i64)
declare void @"System._UnhandledException"() #1
declare i32 @__C_specific_handler(...)

define internal i32 @"hello.Twice"(i32 %X) #0 !dbg !4 {
entry:
  %X.addr = alloca i32, align 4, !dbg !8
  %Result.addr = alloca i32, align 4, !dbg !8
  store i32 %X, ptr %X.addr, align 4, !dbg !8
    #dbg_declare(ptr %X.addr, !10, !DIExpression(), !8)
    #dbg_declare(ptr %Result.addr, !11, !DIExpression(), !8)
  %t1 = load i32, ptr %X.addr, align 4, !dbg !12
  %t2 = mul i32 %t1, 2, !dbg !12
  store i32 %t2, ptr %Result.addr, align 4, !dbg !12
  br label %exit, !dbg !9
exit:
  %t3 = load i32, ptr %Result.addr, align 4, !dbg !9
  ret i32 %t3, !dbg !9
}

define internal void @"$main"() #0 !dbg !14 {
entry:
  call void @"System._InitUnits"(), !dbg !17
  store i32 21, ptr @"hello.A", align 4, !dbg !19
  %t1 = load i32, ptr @"hello.A", align 4, !dbg !20
  %t2 = call i32 @"hello.Twice"(i32 %t1), !dbg !20
  store i32 %t2, ptr @"hello.B", align 4, !dbg !20
  %t3 = load i32, ptr @"hello.B", align 4, !dbg !21
  %t4 = icmp sgt i32 %t3, 40, !dbg !21
  br i1 %t4, label %if.then.1, label %if.end.1, !dbg !21
if.then.1:
  store i32 0, ptr @"hello.A", align 4, !dbg !22
  br label %if.end.1, !dbg !22
if.end.1:
  br label %exit, !dbg !18
exit:
  ret void, !dbg !18
}

define void @_start() #0 personality ptr @__C_specific_handler {
entry:
  call void @"System._Startup"(ptr @"$units", i64 0)
  invoke void @"$main"()
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

!llvm.dbg.cu = !{!0}
!llvm.module.flags = !{!1, !2}
!0 = distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !3, producer: "FreexComp opc 0.1.0", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug, globals: !27, splitDebugInlining: false, nameTableKind: None)
!1 = !{i32 2, !"Debug Info Version", i32 3}
!2 = !{i32 2, !"CodeView", i32 1}
!3 = !DIFile(filename: "hello.pas", directory: "C:\5Cp", checksumkind: CSK_MD5, checksum: "eb7fff482e1998f98c6b0aadb6799a41")
!4 = distinct !DISubprogram(name: "Twice", linkageName: "hello.Twice", scope: !3, file: !3, line: 3, type: !7, scopeLine: 4, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0, retainedNodes: !13)
!5 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!6 = !{!5, !5}
!7 = !DISubroutineType(types: !6)
!8 = !DILocation(line: 4, column: 1, scope: !4)
!9 = !DILocation(line: 6, column: 1, scope: !4)
!10 = !DILocalVariable(name: "X", arg: 1, scope: !4, file: !3, line: 3, type: !5)
!11 = !DILocalVariable(name: "Result", scope: !4, file: !3, line: 3, type: !5)
!12 = !DILocation(line: 5, column: 3, scope: !4)
!13 = !{!10, !11}
!14 = distinct !DISubprogram(name: "hello", linkageName: "$main", scope: !3, file: !3, line: 1, type: !16, scopeLine: 10, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0)
!15 = !{null}
!16 = !DISubroutineType(types: !15)
!17 = !DILocation(line: 10, column: 1, scope: !14)
!18 = !DILocation(line: 15, column: 1, scope: !14)
!19 = !DILocation(line: 11, column: 3, scope: !14)
!20 = !DILocation(line: 12, column: 3, scope: !14)
!21 = !DILocation(line: 13, column: 3, scope: !14)
!22 = !DILocation(line: 14, column: 5, scope: !14)
!23 = !DIGlobalVariableExpression(var: !24, expr: !DIExpression())
!24 = distinct !DIGlobalVariable(name: "A", linkageName: "hello.A", scope: !0, file: !3, line: 9, type: !5, isLocal: true, isDefinition: true)
!25 = !DIGlobalVariableExpression(var: !26, expr: !DIExpression())
!26 = distinct !DIGlobalVariable(name: "B", linkageName: "hello.B", scope: !0, file: !3, line: 9, type: !5, isLocal: true, isDefinition: true)
!27 = !{!23, !25}
