define internal void @"P.Run"(ptr %Values, i64 %Values$high) #0 personality ptr @__C_specific_handler !dbg !4 {
entry:
  %Values.addr = alloca ptr, align 8, !dbg !10
  %Values$high.addr = alloca i64, align 8, !dbg !10
  %Fs.addr = alloca [3 x ptr], align 8, !dbg !10
  %$closure.addr = alloca ptr, align 8, !dbg !10
  %$closureRef.addr = alloca ptr, align 8, !dbg !10
  %$block2.addr = alloca ptr, align 8, !dbg !10
  %$block2Ref.addr = alloca ptr, align 8, !dbg !10
  %$tmp1.addr = alloca i32, align 4, !dbg !10
  %$tmp2.addr = alloca i32, align 4, !dbg !10
  %$for2.addr = alloca i32, align 4, !dbg !10
  %$block3.addr = alloca ptr, align 8, !dbg !10
  %$block3Ref.addr = alloca ptr, align 8, !dbg !10
  %$block4.addr = alloca ptr, align 8, !dbg !10
  %$block4Ref.addr = alloca ptr, align 8, !dbg !10
  %$i1.addr = alloca i32, align 4, !dbg !10
  %$tmp3.addr = alloca i32, align 4, !dbg !10
  %$tmp4.addr = alloca i32, align 4, !dbg !10
  store ptr %Values, ptr %Values.addr, align 8, !dbg !10
  store i64 %Values$high, ptr %Values$high.addr, align 8, !dbg !10
    #dbg_declare(ptr %Values.addr, !12, !DIExpression(), !10)
    #dbg_declare(ptr %Values$high.addr, !13, !DIExpression(), !10)
    #dbg_declare(ptr %Fs.addr, !18, !DIExpression(), !10)
    #dbg_declare(ptr %$closure.addr, !29, !DIExpression(), !10)
    #dbg_declare(ptr %$closureRef.addr, !30, !DIExpression(), !10)
    #dbg_declare(ptr %$block2.addr, !38, !DIExpression(), !10)
    #dbg_declare(ptr %$block2Ref.addr, !39, !DIExpression(), !10)
    #dbg_declare(ptr %$for2.addr, !40, !DIExpression(), !10)
    #dbg_declare(ptr %$block3.addr, !48, !DIExpression(), !10)
    #dbg_declare(ptr %$block3Ref.addr, !49, !DIExpression(), !10)
    #dbg_declare(ptr %$block4.addr, !57, !DIExpression(), !10)
    #dbg_declare(ptr %$block4Ref.addr, !58, !DIExpression(), !10)
    #dbg_declare(ptr %$i1.addr, !59, !DIExpression(), !10)
    #dbg_declare(ptr %$block2.addr, !60, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 32), !10)
    #dbg_declare(ptr %$block3.addr, !61, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 32), !10)
    #dbg_declare(ptr %$block4.addr, !62, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 32), !10)
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad, !dbg !10
cont.1:
  call void @llvm.memset.p0.i64(ptr align 8 %Fs.addr, i8 0, i64 24, i1 false), !dbg !10
  store ptr null, ptr %$closureRef.addr, align 8, !dbg !10
  store ptr null, ptr %$block2.addr, align 8, !dbg !10
  store ptr null, ptr %$block2Ref.addr, align 8, !dbg !10
  store ptr null, ptr %$block3.addr, align 8, !dbg !10
  store ptr null, ptr %$block3Ref.addr, align 8, !dbg !10
  store ptr null, ptr %$block4.addr, align 8, !dbg !10
  store ptr null, ptr %$block4Ref.addr, align 8, !dbg !10
  %t1 = invoke ptr @"System.TObject.Create"(ptr @"P.Run.$closure1.$vmt", i8 1)
          to label %cont.2 unwind label %cleanup.pad, !dbg !10
cont.2:
  store ptr %t1, ptr %$closure.addr, align 8, !dbg !10
  %t2 = load ptr, ptr %$closure.addr, align 8, !dbg !10
  %t3 = icmp eq ptr %t2, null, !dbg !10
  %t5 = getelementptr inbounds i8, ptr %t2, i64 8, !dbg !10
  %t4 = select i1 %t3, ptr null, ptr %t5, !dbg !10
  invoke void @"System._IntfAsg"(ptr %$closureRef.addr, ptr %t4)
          to label %cont.3 unwind label %cleanup.pad, !dbg !10
cont.3:
  store ptr null, ptr %$block2.addr, align 8, !dbg !63
  invoke void @llvm.seh.try.begin()
          to label %cont.4 unwind label %fin.pad.1, !dbg !63
cont.4:
  store i32 0, ptr %$tmp1.addr, align 4, !dbg !63
  store i32 2, ptr %$tmp2.addr, align 4, !dbg !63
  %t6 = load i32, ptr %$tmp1.addr, align 4, !dbg !63
  %t7 = load i32, ptr %$tmp2.addr, align 4, !dbg !63
  %t8 = icmp sle i32 %t6, %t7, !dbg !63
  br i1 %t8, label %if.then.2, label %if.end.2, !dbg !63
if.then.2:
  %t9 = load i32, ptr %$tmp1.addr, align 4, !dbg !63
  store i32 %t9, ptr %$for2.addr, align 4, !dbg !63
  br label %for.body.3, !dbg !63
for.body.3:
  %t10 = invoke ptr @"System.TObject.Create"(ptr @"P.Run.$Block2.$vmt", i8 1)
          to label %cont.5 unwind label %fin.pad.1, !dbg !63
cont.5:
  store ptr %t10, ptr %$block2.addr, align 8, !dbg !63
  %t11 = load ptr, ptr %$block2.addr, align 8, !dbg !63
  %t12 = icmp eq ptr %t11, null, !dbg !63
  %t14 = getelementptr inbounds i8, ptr %t11, i64 8, !dbg !63
  %t13 = select i1 %t12, ptr null, ptr %t14, !dbg !63
  invoke void @"System._IntfAsg"(ptr %$block2Ref.addr, ptr %t13)
          to label %cont.6 unwind label %fin.pad.1, !dbg !63
cont.6:
  %t15 = load ptr, ptr %$closure.addr, align 8, !dbg !63
  %t16 = load ptr, ptr %$block2.addr, align 8, !dbg !63
  %t17 = getelementptr inbounds i8, ptr %t16, i64 16, !dbg !63
  store ptr %t15, ptr %t17, align 8, !dbg !63
  %t18 = load ptr, ptr %$block2.addr, align 8, !dbg !63
  %t19 = getelementptr inbounds i8, ptr %t18, i64 24, !dbg !63
  %t20 = load ptr, ptr %$closure.addr, align 8, !dbg !63
  %t21 = icmp eq ptr %t20, null, !dbg !63
  %t23 = getelementptr inbounds i8, ptr %t20, i64 8, !dbg !63
  %t22 = select i1 %t21, ptr null, ptr %t23, !dbg !63
  invoke void @"System._IntfAsg"(ptr %t19, ptr %t22)
          to label %cont.7 unwind label %fin.pad.1, !dbg !63
cont.7:
  %t24 = load i32, ptr %$for2.addr, align 4, !dbg !63
  %t25 = load ptr, ptr %$block2.addr, align 8, !dbg !63
  %t26 = getelementptr inbounds i8, ptr %t25, i64 32, !dbg !63
  store i32 %t24, ptr %t26, align 4, !dbg !63
  %t27 = invoke ptr @"System.TObject.Create"(ptr @"P.Run.$Block3.$vmt", i8 1)
          to label %cont.8 unwind label %fin.pad.1, !dbg !64
cont.8:
  store ptr %t27, ptr %$block3.addr, align 8, !dbg !64
  %t28 = load ptr, ptr %$block3.addr, align 8, !dbg !64
  %t29 = icmp eq ptr %t28, null, !dbg !64
  %t31 = getelementptr inbounds i8, ptr %t28, i64 40, !dbg !64
  %t30 = select i1 %t29, ptr null, ptr %t31, !dbg !64
  invoke void @"System._IntfAsg"(ptr %$block3Ref.addr, ptr %t30)
          to label %cont.9 unwind label %fin.pad.1, !dbg !64
cont.9:
  %t32 = load ptr, ptr %$block2.addr, align 8, !dbg !64
  %t33 = load ptr, ptr %$block3.addr, align 8, !dbg !64
  %t34 = getelementptr inbounds i8, ptr %t33, i64 16, !dbg !64
  store ptr %t32, ptr %t34, align 8, !dbg !64
  %t35 = load ptr, ptr %$block3.addr, align 8, !dbg !64
  %t36 = getelementptr inbounds i8, ptr %t35, i64 24, !dbg !64
  %t37 = load ptr, ptr %$block2.addr, align 8, !dbg !64
  %t38 = icmp eq ptr %t37, null, !dbg !64
  %t40 = getelementptr inbounds i8, ptr %t37, i64 8, !dbg !64
  %t39 = select i1 %t38, ptr null, ptr %t40, !dbg !64
  invoke void @"System._IntfAsg"(ptr %t36, ptr %t39)
          to label %cont.10 unwind label %fin.pad.1, !dbg !64
cont.10:
  invoke void @llvm.seh.try.begin()
          to label %cont.11 unwind label %fin.pad.4, !dbg !64
cont.11:
  %t41 = load ptr, ptr %$block2.addr, align 8, !dbg !64
  %t42 = getelementptr inbounds i8, ptr %t41, i64 32, !dbg !64
  %t43 = load i32, ptr %t42, align 4, !dbg !64
  %t44 = mul i32 %t43, 10, !dbg !64
  %t45 = load ptr, ptr %$block3.addr, align 8, !dbg !64
  %t46 = getelementptr inbounds i8, ptr %t45, i64 32, !dbg !64
  store i32 %t44, ptr %t46, align 4, !dbg !64
  %t47 = load ptr, ptr %$block2.addr, align 8, !dbg !65
  %t48 = getelementptr inbounds i8, ptr %t47, i64 32, !dbg !65
  %t49 = load i32, ptr %t48, align 4, !dbg !65
  %t50 = sext i32 %t49 to i64, !dbg !65
  %t51 = getelementptr inbounds [3 x ptr], ptr %Fs.addr, i64 0, i64 %t50, !dbg !65
  %t52 = load ptr, ptr %$block3.addr, align 8, !dbg !65
  %t53 = icmp eq ptr %t52, null, !dbg !65
  %t55 = getelementptr inbounds i8, ptr %t52, i64 40, !dbg !65
  %t54 = select i1 %t53, ptr null, ptr %t55, !dbg !65
  invoke void @"System._IntfAsg"(ptr %t51, ptr %t54)
          to label %cont.12 unwind label %fin.pad.4, !dbg !65
cont.12:
  invoke void @llvm.seh.try.end()
          to label %cont.13 unwind label %fin.pad.4, !dbg !64
cont.13:
  invoke void @"System._IntfClr"(ptr %$block3Ref.addr)
          to label %cont.14 unwind label %fin.pad.1, !dbg !64
cont.14:
  br label %try.end.4, !dbg !64
fin.pad.4:
  %cl.4 = cleanuppad within none [], !dbg !64
  invoke void @"System._IntfClr"(ptr %$block3Ref.addr) [ "funclet"(token %cl.4) ]
          to label %cont.15 unwind label %fin.pad.1, !dbg !64
cont.15:
  cleanupret from %cl.4 unwind label %fin.pad.1, !dbg !64
try.end.4:
  br label %for.inc.3, !dbg !63
for.inc.3:
  %t56 = load i32, ptr %$for2.addr, align 4, !dbg !63
  %t57 = load i32, ptr %$tmp2.addr, align 4, !dbg !63
  %t58 = icmp eq i32 %t56, %t57, !dbg !63
  br i1 %t58, label %for.end.3, label %if.end.5, !dbg !63
if.end.5:
  %t59 = load i32, ptr %$for2.addr, align 4, !dbg !63
  %t60 = add i32 %t59, 1, !dbg !63
  store i32 %t60, ptr %$for2.addr, align 4, !dbg !63
  br label %for.body.3, !dbg !63
for.end.3:
  br label %if.end.2, !dbg !63
if.end.2:
  invoke void @llvm.seh.try.end()
          to label %cont.16 unwind label %fin.pad.1, !dbg !63
cont.16:
  invoke void @"System._IntfClr"(ptr %$block2Ref.addr)
          to label %cont.17 unwind label %cleanup.pad, !dbg !63
cont.17:
  br label %try.end.1, !dbg !63
fin.pad.1:
  %cl.1 = cleanuppad within none [], !dbg !63
  invoke void @"System._IntfClr"(ptr %$block2Ref.addr) [ "funclet"(token %cl.1) ]
          to label %cont.18 unwind label %cleanup.pad, !dbg !63
cont.18:
  cleanupret from %cl.1 unwind label %cleanup.pad, !dbg !63
try.end.1:
  store ptr null, ptr %$block4.addr, align 8, !dbg !66
  invoke void @llvm.seh.try.begin()
          to label %cont.19 unwind label %fin.pad.6, !dbg !66
cont.19:
  store i32 0, ptr %$tmp3.addr, align 4, !dbg !66
  %t61 = load i64, ptr %Values$high.addr, align 8, !dbg !66
  %t62 = trunc i64 %t61 to i32, !dbg !66
  store i32 %t62, ptr %$tmp4.addr, align 4, !dbg !66
  %t63 = load i32, ptr %$tmp3.addr, align 4, !dbg !66
  %t64 = load i32, ptr %$tmp4.addr, align 4, !dbg !66
  %t65 = icmp sle i32 %t63, %t64, !dbg !66
  br i1 %t65, label %if.then.7, label %if.end.7, !dbg !66
if.then.7:
  %t66 = load i32, ptr %$tmp3.addr, align 4, !dbg !66
  store i32 %t66, ptr %$i1.addr, align 4, !dbg !66
  br label %for.body.8, !dbg !66
for.body.8:
  %t67 = invoke ptr @"System.TObject.Create"(ptr @"P.Run.$Block4.$vmt", i8 1)
          to label %cont.20 unwind label %fin.pad.6, !dbg !66
cont.20:
  store ptr %t67, ptr %$block4.addr, align 8, !dbg !66
  %t68 = load ptr, ptr %$block4.addr, align 8, !dbg !66
  %t69 = icmp eq ptr %t68, null, !dbg !66
  %t71 = getelementptr inbounds i8, ptr %t68, i64 40, !dbg !66
  %t70 = select i1 %t69, ptr null, ptr %t71, !dbg !66
  invoke void @"System._IntfAsg"(ptr %$block4Ref.addr, ptr %t70)
          to label %cont.21 unwind label %fin.pad.6, !dbg !66
cont.21:
  %t72 = load ptr, ptr %$closure.addr, align 8, !dbg !66
  %t73 = load ptr, ptr %$block4.addr, align 8, !dbg !66
  %t74 = getelementptr inbounds i8, ptr %t73, i64 16, !dbg !66
  store ptr %t72, ptr %t74, align 8, !dbg !66
  %t75 = load ptr, ptr %$block4.addr, align 8, !dbg !66
  %t76 = getelementptr inbounds i8, ptr %t75, i64 24, !dbg !66
  %t77 = load ptr, ptr %$closure.addr, align 8, !dbg !66
  %t78 = icmp eq ptr %t77, null, !dbg !66
  %t80 = getelementptr inbounds i8, ptr %t77, i64 8, !dbg !66
  %t79 = select i1 %t78, ptr null, ptr %t80, !dbg !66
  invoke void @"System._IntfAsg"(ptr %t76, ptr %t79)
          to label %cont.22 unwind label %fin.pad.6, !dbg !66
cont.22:
  %t81 = load ptr, ptr %Values.addr, align 8, !dbg !66
  %t82 = load i32, ptr %$i1.addr, align 4, !dbg !66
  %t83 = sext i32 %t82 to i64, !dbg !66
  %t84 = getelementptr inbounds i32, ptr %t81, i64 %t83, !dbg !66
  %t85 = load i32, ptr %t84, align 4, !dbg !66
  %t86 = load ptr, ptr %$block4.addr, align 8, !dbg !66
  %t87 = getelementptr inbounds i8, ptr %t86, i64 32, !dbg !66
  store i32 %t85, ptr %t87, align 4, !dbg !66
  %t88 = getelementptr inbounds [3 x ptr], ptr %Fs.addr, i64 0, i64 0, !dbg !67
  %t89 = load ptr, ptr %$block4.addr, align 8, !dbg !67
  %t90 = icmp eq ptr %t89, null, !dbg !67
  %t92 = getelementptr inbounds i8, ptr %t89, i64 40, !dbg !67
  %t91 = select i1 %t90, ptr null, ptr %t92, !dbg !67
  invoke void @"System._IntfAsg"(ptr %t88, ptr %t91)
          to label %cont.23 unwind label %fin.pad.6, !dbg !67
cont.23:
  br label %for.inc.8, !dbg !66
for.inc.8:
  %t93 = load i32, ptr %$i1.addr, align 4, !dbg !66
  %t94 = load i32, ptr %$tmp4.addr, align 4, !dbg !66
  %t95 = icmp eq i32 %t93, %t94, !dbg !66
  br i1 %t95, label %for.end.8, label %if.end.9, !dbg !66
if.end.9:
  %t96 = load i32, ptr %$i1.addr, align 4, !dbg !66
  %t97 = add i32 %t96, 1, !dbg !66
  store i32 %t97, ptr %$i1.addr, align 4, !dbg !66
  br label %for.body.8, !dbg !66
for.end.8:
  br label %if.end.7, !dbg !66
if.end.7:
  invoke void @llvm.seh.try.end()
          to label %cont.24 unwind label %fin.pad.6, !dbg !66
cont.24:
  invoke void @"System._IntfClr"(ptr %$block4Ref.addr)
          to label %cont.25 unwind label %cleanup.pad, !dbg !66
cont.25:
  br label %try.end.6, !dbg !66
fin.pad.6:
  %cl.6 = cleanuppad within none [], !dbg !66
  invoke void @"System._IntfClr"(ptr %$block4Ref.addr) [ "funclet"(token %cl.6) ]
          to label %cont.26 unwind label %cleanup.pad, !dbg !66
cont.26:
  cleanupret from %cl.6 unwind label %cleanup.pad, !dbg !66
try.end.6:
  br label %cleanup, !dbg !11
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.27 unwind label %cleanup.pad, !dbg !11
cont.27:
  call void @"System._Finalize"(ptr %Fs.addr, ptr @"P.$rtti.array[0..2] of TF"), !dbg !11
  call void @"System._IntfClr"(ptr %$closureRef.addr), !dbg !11
  br label %exit, !dbg !11
exit:
  ret void, !dbg !11
cleanup.pad:
  %cl = cleanuppad within none [], !dbg !11
  call void @"System._Finalize"(ptr %Fs.addr, ptr @"P.$rtti.array[0..2] of TF") [ "funclet"(token %cl) ], !dbg !11
  call void @"System._IntfClr"(ptr %$closureRef.addr) [ "funclet"(token %cl) ], !dbg !11
  cleanupret from %cl unwind to caller, !dbg !11
}
!DILocalVariable(name: "Values", arg: 1, scope: !4, file: !3, line: 4, type: !6)
!DILocalVariable(name: "Values$high", arg: 2, scope: !4, file: !3, line: 4, type: !7, flags: DIFlagArtificial)
!DILocalVariable(name: "Fs", scope: !4, file: !3, line: 5, type: !14)
!DILocalVariable(name: "$closure", scope: !4, file: !3, line: 4, type: !19, flags: DIFlagArtificial)
!DILocalVariable(name: "$closureRef", scope: !4, file: !3, line: 4, type: !15, flags: DIFlagArtificial)
!DILocalVariable(name: "$block2", scope: !4, file: !3, line: 4, type: !31, flags: DIFlagArtificial)
!DILocalVariable(name: "$block2Ref", scope: !4, file: !3, line: 4, type: !15, flags: DIFlagArtificial)
!DILocalVariable(name: "$for2", scope: !4, file: !3, line: 4, type: !5, flags: DIFlagArtificial)
!DILocalVariable(name: "$block3", scope: !4, file: !3, line: 4, type: !41, flags: DIFlagArtificial)
!DILocalVariable(name: "$block3Ref", scope: !4, file: !3, line: 4, type: !15, flags: DIFlagArtificial)
!DILocalVariable(name: "$block4", scope: !4, file: !3, line: 4, type: !50, flags: DIFlagArtificial)
!DILocalVariable(name: "$block4Ref", scope: !4, file: !3, line: 4, type: !15, flags: DIFlagArtificial)
!DILocalVariable(name: "$i1", scope: !4, file: !3, line: 4, type: !5, flags: DIFlagArtificial)
!DILocalVariable(name: "I", scope: !4, file: !3, line: 7, type: !5)
!DILocalVariable(name: "N", scope: !4, file: !3, line: 9, type: !5)
!DILocalVariable(name: "X", scope: !4, file: !3, line: 12, type: !5)
; {
;   "displayName": "Run",
;   "linkageName": "P.Run",
;   "file": "C:\\p\\M.pas",
;   "line": 4,
;   "bodyLine": 6,
;   "endLine": 14,
;   "self": null,
;   "vars": [
;     {
;       "name": "Values",
;       "kind": "param",
;       "type": 1
;     },
;     {
;       "name": "Values$high",
;       "kind": "param",
;       "type": 3
;     },
;     {
;       "name": "Fs",
;       "kind": "local",
;       "type": 4
;     },
;     {
;       "name": "I",
;       "kind": "local",
;       "type": 2,
;       "fromLine": 7,
;       "toLine": 11
;     },
;     {
;       "name": "N",
;       "kind": "local",
;       "type": 2,
;       "fromLine": 9,
;       "toLine": 11
;     },
;     {
;       "name": "X",
;       "kind": "local",
;       "type": 2,
;       "fromLine": 12,
;       "toLine": 13
;     }
;   ]
; }
