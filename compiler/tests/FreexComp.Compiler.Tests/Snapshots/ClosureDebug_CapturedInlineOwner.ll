define internal void @"P.Run"(i32 %P) #0 personality ptr @__C_specific_handler !dbg !4 {
entry:
  %P.addr = alloca i32, align 4, !dbg !8
  %Fs.addr = alloca [3 x ptr], align 8, !dbg !8
  %I.addr = alloca i32, align 4, !dbg !8
  %$closure.addr = alloca ptr, align 8, !dbg !8
  %$closureRef.addr = alloca ptr, align 8, !dbg !8
  %$tmp1.addr = alloca i32, align 4, !dbg !8
  %$tmp2.addr = alloca i32, align 4, !dbg !8
  %Plain.addr = alloca i32, align 4, !dbg !8
  %$block2.addr = alloca ptr, align 8, !dbg !8
  %$block2Ref.addr = alloca ptr, align 8, !dbg !8
  store i32 %P, ptr %P.addr, align 4, !dbg !8
    #dbg_declare(ptr %P.addr, !10, !DIExpression(), !8)
    #dbg_declare(ptr %Fs.addr, !15, !DIExpression(), !8)
    #dbg_declare(ptr %I.addr, !16, !DIExpression(), !8)
    #dbg_declare(ptr %$closure.addr, !28, !DIExpression(), !8)
    #dbg_declare(ptr %$closureRef.addr, !29, !DIExpression(), !8)
    #dbg_declare(ptr %Plain.addr, !30, !DIExpression(), !8)
    #dbg_declare(ptr %$block2.addr, !38, !DIExpression(), !8)
    #dbg_declare(ptr %$block2Ref.addr, !39, !DIExpression(), !8)
    #dbg_declare(ptr %$block2.addr, !40, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 32), !8)
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad, !dbg !8
cont.1:
  call void @llvm.memset.p0.i64(ptr align 8 %Fs.addr, i8 0, i64 24, i1 false), !dbg !8
  store ptr null, ptr %$closureRef.addr, align 8, !dbg !8
  store ptr null, ptr %$block2.addr, align 8, !dbg !8
  store ptr null, ptr %$block2Ref.addr, align 8, !dbg !8
  %t1 = invoke ptr @"System.TObject.Create"(ptr @"P.Run.$closure1.$vmt", i8 1)
          to label %cont.2 unwind label %cleanup.pad, !dbg !8
cont.2:
  store ptr %t1, ptr %$closure.addr, align 8, !dbg !8
  %t2 = load ptr, ptr %$closure.addr, align 8, !dbg !8
  %t3 = icmp eq ptr %t2, null, !dbg !8
  %t5 = getelementptr inbounds i8, ptr %t2, i64 8, !dbg !8
  %t4 = select i1 %t3, ptr null, ptr %t5, !dbg !8
  invoke void @"System._IntfAsg"(ptr %$closureRef.addr, ptr %t4)
          to label %cont.3 unwind label %cleanup.pad, !dbg !8
cont.3:
  %t6 = load i32, ptr %P.addr, align 4, !dbg !8
  %t7 = load ptr, ptr %$closure.addr, align 8, !dbg !8
  %t8 = getelementptr inbounds i8, ptr %t7, i64 16, !dbg !8
  store i32 %t6, ptr %t8, align 4, !dbg !8
  store i32 0, ptr %$tmp1.addr, align 4, !dbg !41
  store i32 2, ptr %$tmp2.addr, align 4, !dbg !41
  %t9 = load i32, ptr %$tmp1.addr, align 4, !dbg !41
  %t10 = load i32, ptr %$tmp2.addr, align 4, !dbg !41
  %t11 = icmp sle i32 %t9, %t10, !dbg !41
  br i1 %t11, label %if.then.1, label %if.end.1, !dbg !41
if.then.1:
  %t12 = load i32, ptr %$tmp1.addr, align 4, !dbg !41
  store i32 %t12, ptr %I.addr, align 4, !dbg !41
  br label %for.body.2, !dbg !41
for.body.2:
  %t13 = load i32, ptr %I.addr, align 4, !dbg !42
  store i32 %t13, ptr %Plain.addr, align 4, !dbg !42
  %t14 = invoke ptr @"System.TObject.Create"(ptr @"P.Run.$Block2.$vmt", i8 1)
          to label %cont.4 unwind label %cleanup.pad, !dbg !43
cont.4:
  store ptr %t14, ptr %$block2.addr, align 8, !dbg !43
  %t15 = load ptr, ptr %$block2.addr, align 8, !dbg !43
  %t16 = icmp eq ptr %t15, null, !dbg !43
  %t18 = getelementptr inbounds i8, ptr %t15, i64 40, !dbg !43
  %t17 = select i1 %t16, ptr null, ptr %t18, !dbg !43
  invoke void @"System._IntfAsg"(ptr %$block2Ref.addr, ptr %t17)
          to label %cont.5 unwind label %cleanup.pad, !dbg !43
cont.5:
  %t19 = load ptr, ptr %$closure.addr, align 8, !dbg !43
  %t20 = load ptr, ptr %$block2.addr, align 8, !dbg !43
  %t21 = getelementptr inbounds i8, ptr %t20, i64 16, !dbg !43
  store ptr %t19, ptr %t21, align 8, !dbg !43
  %t22 = load ptr, ptr %$block2.addr, align 8, !dbg !43
  %t23 = getelementptr inbounds i8, ptr %t22, i64 24, !dbg !43
  %t24 = load ptr, ptr %$closure.addr, align 8, !dbg !43
  %t25 = icmp eq ptr %t24, null, !dbg !43
  %t27 = getelementptr inbounds i8, ptr %t24, i64 8, !dbg !43
  %t26 = select i1 %t25, ptr null, ptr %t27, !dbg !43
  invoke void @"System._IntfAsg"(ptr %t23, ptr %t26)
          to label %cont.6 unwind label %cleanup.pad, !dbg !43
cont.6:
  invoke void @llvm.seh.try.begin()
          to label %cont.7 unwind label %fin.pad.3, !dbg !43
cont.7:
  %t28 = load i32, ptr %I.addr, align 4, !dbg !43
  %t29 = mul i32 %t28, 10, !dbg !43
  %t30 = load ptr, ptr %$block2.addr, align 8, !dbg !43
  %t31 = getelementptr inbounds i8, ptr %t30, i64 32, !dbg !43
  store i32 %t29, ptr %t31, align 4, !dbg !43
  %t32 = load i32, ptr %I.addr, align 4, !dbg !44
  %t33 = sext i32 %t32 to i64, !dbg !44
  %t34 = getelementptr inbounds [3 x ptr], ptr %Fs.addr, i64 0, i64 %t33, !dbg !44
  %t35 = load ptr, ptr %$block2.addr, align 8, !dbg !44
  %t36 = icmp eq ptr %t35, null, !dbg !44
  %t38 = getelementptr inbounds i8, ptr %t35, i64 40, !dbg !44
  %t37 = select i1 %t36, ptr null, ptr %t38, !dbg !44
  invoke void @"System._IntfAsg"(ptr %t34, ptr %t37)
          to label %cont.8 unwind label %fin.pad.3, !dbg !44
cont.8:
  %t39 = load ptr, ptr %$block2.addr, align 8, !dbg !45
  %t40 = getelementptr inbounds i8, ptr %t39, i64 32, !dbg !45
  %t41 = load i32, ptr %t40, align 4, !dbg !45
  %t42 = load i32, ptr %Plain.addr, align 4, !dbg !45
  %t43 = add i32 %t41, %t42, !dbg !45
  %t44 = load ptr, ptr %$block2.addr, align 8, !dbg !45
  %t45 = getelementptr inbounds i8, ptr %t44, i64 32, !dbg !45
  store i32 %t43, ptr %t45, align 4, !dbg !45
  invoke void @llvm.seh.try.end()
          to label %cont.9 unwind label %fin.pad.3, !dbg !43
cont.9:
  invoke void @"System._IntfClr"(ptr %$block2Ref.addr)
          to label %cont.10 unwind label %cleanup.pad, !dbg !43
cont.10:
  br label %try.end.3, !dbg !43
fin.pad.3:
  %cl.3 = cleanuppad within none [], !dbg !43
  invoke void @"System._IntfClr"(ptr %$block2Ref.addr) [ "funclet"(token %cl.3) ]
          to label %cont.11 unwind label %cleanup.pad, !dbg !43
cont.11:
  cleanupret from %cl.3 unwind label %cleanup.pad, !dbg !43
try.end.3:
  br label %for.inc.2, !dbg !41
for.inc.2:
  %t46 = load i32, ptr %I.addr, align 4, !dbg !41
  %t47 = load i32, ptr %$tmp2.addr, align 4, !dbg !41
  %t48 = icmp eq i32 %t46, %t47, !dbg !41
  br i1 %t48, label %for.end.2, label %if.end.4, !dbg !41
if.end.4:
  %t49 = load i32, ptr %I.addr, align 4, !dbg !41
  %t50 = add i32 %t49, 1, !dbg !41
  store i32 %t50, ptr %I.addr, align 4, !dbg !41
  br label %for.body.2, !dbg !41
for.end.2:
  br label %if.end.1, !dbg !41
if.end.1:
  br label %cleanup, !dbg !9
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.12 unwind label %cleanup.pad, !dbg !9
cont.12:
  call void @"System._Finalize"(ptr %Fs.addr, ptr @"P.$rtti.array[0..2] of TF"), !dbg !9
  call void @"System._IntfClr"(ptr %$closureRef.addr), !dbg !9
  br label %exit, !dbg !9
exit:
  ret void, !dbg !9
cleanup.pad:
  %cl = cleanuppad within none [], !dbg !9
  call void @"System._Finalize"(ptr %Fs.addr, ptr @"P.$rtti.array[0..2] of TF") [ "funclet"(token %cl) ], !dbg !9
  call void @"System._IntfClr"(ptr %$closureRef.addr) [ "funclet"(token %cl) ], !dbg !9
  cleanupret from %cl unwind to caller, !dbg !9
}
!DILocalVariable(name: "P", arg: 1, scope: !4, file: !3, line: 4, type: !5)
!DILocalVariable(name: "Fs", scope: !4, file: !3, line: 5, type: !11)
!DILocalVariable(name: "I", scope: !4, file: !3, line: 5, type: !5)
!DILocalVariable(name: "$closure", scope: !4, file: !3, line: 4, type: !17, flags: DIFlagArtificial)
!DILocalVariable(name: "$closureRef", scope: !4, file: !3, line: 4, type: !12, flags: DIFlagArtificial)
!DILocalVariable(name: "Plain", scope: !4, file: !3, line: 9, type: !5)
!DILocalVariable(name: "$block2", scope: !4, file: !3, line: 4, type: !31, flags: DIFlagArtificial)
!DILocalVariable(name: "$block2Ref", scope: !4, file: !3, line: 4, type: !12, flags: DIFlagArtificial)
!DILocalVariable(name: "N", scope: !4, file: !3, line: 10, type: !5)
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
;       "name": "P",
;       "kind": "param",
;       "type": 1
;     },
;     {
;       "name": "Fs",
;       "kind": "local",
;       "type": 2
;     },
;     {
;       "name": "I",
;       "kind": "local",
;       "type": 1
;     },
;     {
;       "name": "Plain",
;       "kind": "local",
;       "type": 1,
;       "fromLine": 9,
;       "toLine": 13
;     },
;     {
;       "name": "N",
;       "kind": "local",
;       "type": 1,
;       "fromLine": 10,
;       "toLine": 13
;     }
;   ]
; }
