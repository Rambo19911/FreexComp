; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 107, i16 0] }, align 16
@"M.$disp.1" = private unnamed_addr constant <{ i32, i16, i16, i16, [2 x i16] }> <{ i32 5, i16 1, i16 256, i16 2, [2 x i16] [i16 3, i16 16640] }>, align 4
@"M.$disp.2" = private unnamed_addr constant <{ i32, i16, i16, i16 }> <{ i32 1, i16 1, i16 0, i16 0 }>, align 4
@"M.$disp.3" = private unnamed_addr constant <{ i32, i16, i16, i16, [10 x i16] }> <{ i32 2, i16 1, i16 0, i16 10, [10 x i16] [i16 2, i16 17, i16 4, i16 5, i16 523, i16 3, i16 256, i16 9, i16 6, i16 20] }>, align 4
@"M.$disp.4" = private unnamed_addr constant <{ i32, i16, i16, i16, [3 x i16] }> <{ i32 3, i16 1, i16 0, i16 3, [3 x i16] [i16 16387, i16 16640, i16 16907] }>, align 4
@"M.$disp.5" = private unnamed_addr constant <{ i32, i16, i16, i16 }> <{ i32 4, i16 1, i16 3, i16 0 }>, align 4
@"M.$disp.6" = private unnamed_addr constant <{ i32, i16, i16, i16 }> <{ i32 6, i16 2, i16 3, i16 0 }>, align 4
@"M.$disp.7" = private unnamed_addr constant <{ i32, i16, i16, i16, [1 x i16] }> <{ i32 0, i16 2, i16 3, i16 1, [1 x i16] [i16 256] }>, align 4
@"M.$disp.8" = private unnamed_addr constant <{ i32, i16, i16, i16, [2 x i16] }> <{ i32 0, i16 4, i16 0, i16 2, [2 x i16] [i16 256, i16 3] }>, align 4
@"M.$disp.9" = private unnamed_addr constant <{ i32, i16, i16, i16 }> <{ i32 7, i16 2, i16 9, i16 0 }>, align 4
@"M.$disp.10" = private unnamed_addr constant <{ i32, i16, i16, i16, [1 x i16] }> <{ i32 7, i16 8, i16 0, i16 1, [1 x i16] [i16 9] }>, align 4

declare void @"System._DispCall"(ptr, ptr, ptr, ptr)
declare void @"System._IntfAsg"(ptr, ptr)
declare void @"System._IntfClr"(ptr)
declare void @"System._UStrAsg"(ptr, ptr)
declare void @"System._UStrClr"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.memset.p0.i64(ptr, i8, i64, i1)

define void @"M.Run"(ptr %D) #0 personality ptr @__C_specific_handler {
entry:
  %D.addr = alloca ptr, align 8
  %S.addr = alloca ptr, align 8
  %Name.addr = alloca ptr, align 8
  %C.addr = alloca i8, align 1
  %B.addr = alloca i8, align 1
  %N.addr = alloca i32, align 4
  %O.addr = alloca ptr, align 8
  %$tmp1.addr = alloca ptr, align 8
  %$tmp2.addr = alloca [2 x i64], align 8
  %$tmp3.addr = alloca ptr, align 8
  %$tmp4.addr = alloca ptr, align 8
  %$tmp5.addr = alloca ptr, align 8
  %$tmp6.addr = alloca [2 x i64], align 8
  %$tmp7.addr = alloca ptr, align 8
  %$tmp8.addr = alloca ptr, align 8
  %$tmp9.addr = alloca ptr, align 8
  %$tmp10.addr = alloca ptr, align 8
  %$tmp11.addr = alloca [10 x i64], align 8
  %$tmp12.addr = alloca ptr, align 8
  %$tmp13.addr = alloca [3 x i64], align 8
  %$tmp14.addr = alloca ptr, align 8
  %$tmp15.addr = alloca i32, align 4
  %$tmp16.addr = alloca ptr, align 8
  %$tmp17.addr = alloca ptr, align 8
  %$tmp18.addr = alloca ptr, align 8
  %$tmp19.addr = alloca i32, align 4
  %$tmp20.addr = alloca ptr, align 8
  %$tmp21.addr = alloca i32, align 4
  %$tmp22.addr = alloca ptr, align 8
  %$tmp23.addr = alloca [2 x i64], align 8
  %$tmp24.addr = alloca ptr, align 8
  %$tmp25.addr = alloca [1 x i64], align 8
  %$tmp26.addr = alloca i32, align 4
  %$tmp27.addr = alloca ptr, align 8
  %$tmp28.addr = alloca ptr, align 8
  %$tmp29.addr = alloca ptr, align 8
  %$tmp30.addr = alloca [1 x i64], align 8
  store ptr %D, ptr %D.addr, align 8
  store ptr null, ptr %S.addr, align 8
  store ptr null, ptr %Name.addr, align 8
  store ptr null, ptr %O.addr, align 8
  store ptr null, ptr %$tmp4.addr, align 8
  store ptr null, ptr %$tmp8.addr, align 8
  store ptr null, ptr %$tmp28.addr, align 8
  %t1 = load ptr, ptr %D.addr, align 8
  store ptr %t1, ptr %$tmp1.addr, align 8
  %t2 = getelementptr inbounds [2 x i64], ptr %$tmp2.addr, i64 0, i64 0
  store i64 42, ptr %t2, align 8
  store ptr %Name.addr, ptr %$tmp3.addr, align 8
  %t3 = load ptr, ptr %$tmp3.addr, align 8
  %t4 = getelementptr inbounds [2 x i64], ptr %$tmp2.addr, i64 0, i64 1
  store ptr %t3, ptr %t4, align 8
  %t5 = load ptr, ptr %$tmp1.addr, align 8
  invoke void @"System._DispCall"(ptr %$tmp4.addr, ptr %t5, ptr @"M.$disp.1", ptr %$tmp2.addr)
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  %t6 = load ptr, ptr %$tmp4.addr, align 8
  invoke void @"System._UStrAsg"(ptr %S.addr, ptr %t6)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t7 = load ptr, ptr %D.addr, align 8
  store ptr %t7, ptr %$tmp5.addr, align 8
  %t8 = getelementptr inbounds [2 x i64], ptr %$tmp6.addr, i64 0, i64 0
  store i64 42, ptr %t8, align 8
  store ptr %Name.addr, ptr %$tmp7.addr, align 8
  %t9 = load ptr, ptr %$tmp7.addr, align 8
  %t10 = getelementptr inbounds [2 x i64], ptr %$tmp6.addr, i64 0, i64 1
  store ptr %t9, ptr %t10, align 8
  %t11 = load ptr, ptr %$tmp5.addr, align 8
  invoke void @"System._DispCall"(ptr %$tmp8.addr, ptr %t11, ptr @"M.$disp.1", ptr %$tmp6.addr)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t12 = load ptr, ptr %D.addr, align 8
  store ptr %t12, ptr %$tmp9.addr, align 8
  %t13 = load ptr, ptr %$tmp9.addr, align 8
  invoke void @"System._DispCall"(ptr null, ptr %t13, ptr @"M.$disp.2", ptr null)
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  %t14 = load ptr, ptr %D.addr, align 8
  store ptr %t14, ptr %$tmp10.addr, align 8
  %t15 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 0
  store i64 -1, ptr %t15, align 8
  %t16 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 1
  store i64 0, ptr %t16, align 8
  %t17 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 1
  store i8 2, ptr %t17, align 1
  %t18 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 2
  store i64 0, ptr %t18, align 8
  %t19 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 2
  store float 0x3FF8000000000000, ptr %t19, align 4
  %t20 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 3
  store double 0x4004000000000000, ptr %t20, align 8
  %t21 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 4
  store i64 0, ptr %t21, align 8
  %t22 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 4
  store i8 1, ptr %t22, align 1
  %t23 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 5
  store i64 0, ptr %t23, align 8
  %t24 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 5
  store i8 1, ptr %t24, align 1
  %t25 = load ptr, ptr %S.addr, align 8
  %t26 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 6
  store ptr %t25, ptr %t26, align 8
  %t27 = load ptr, ptr %O.addr, align 8
  %t28 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 7
  store ptr %t27, ptr %t28, align 8
  %t29 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 8
  store i64 32500, ptr %t29, align 8
  %t30 = load i32, ptr %N.addr, align 4
  %t31 = sext i32 %t30 to i64
  %t32 = getelementptr inbounds [10 x i64], ptr %$tmp11.addr, i64 0, i64 9
  store i64 %t31, ptr %t32, align 8
  %t33 = load ptr, ptr %$tmp10.addr, align 8
  invoke void @"System._DispCall"(ptr null, ptr %t33, ptr @"M.$disp.3", ptr %$tmp11.addr)
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  %t34 = load ptr, ptr %D.addr, align 8
  store ptr %t34, ptr %$tmp12.addr, align 8
  store ptr %C.addr, ptr %$tmp14.addr, align 8
  %t35 = load ptr, ptr %$tmp14.addr, align 8
  %t36 = load i8, ptr %t35, align 1
  %t37 = zext i8 %t36 to i32
  store i32 %t37, ptr %$tmp15.addr, align 4
  %t38 = getelementptr inbounds [3 x i64], ptr %$tmp13.addr, i64 0, i64 0
  store ptr %$tmp15.addr, ptr %t38, align 8
  store ptr %S.addr, ptr %$tmp16.addr, align 8
  %t39 = load ptr, ptr %$tmp16.addr, align 8
  invoke void @"System._UStrClr"(ptr %t39)
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  %t40 = load ptr, ptr %$tmp16.addr, align 8
  %t41 = getelementptr inbounds [3 x i64], ptr %$tmp13.addr, i64 0, i64 1
  store ptr %t40, ptr %t41, align 8
  store ptr %B.addr, ptr %$tmp17.addr, align 8
  %t42 = load ptr, ptr %$tmp17.addr, align 8
  %t43 = getelementptr inbounds [3 x i64], ptr %$tmp13.addr, i64 0, i64 2
  store ptr %t42, ptr %t43, align 8
  %t44 = load ptr, ptr %$tmp12.addr, align 8
  invoke void @"System._DispCall"(ptr null, ptr %t44, ptr @"M.$disp.4", ptr %$tmp13.addr)
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  %t45 = load i32, ptr %$tmp15.addr, align 4
  %t46 = trunc i32 %t45 to i8
  %t47 = load ptr, ptr %$tmp14.addr, align 8
  store i8 %t46, ptr %t47, align 1
  %t48 = load ptr, ptr %D.addr, align 8
  store ptr %t48, ptr %$tmp18.addr, align 8
  call void @llvm.memset.p0.i64(ptr align 4 %$tmp19.addr, i8 0, i64 4, i1 false)
  %t49 = load ptr, ptr %$tmp18.addr, align 8
  invoke void @"System._DispCall"(ptr %$tmp19.addr, ptr %t49, ptr @"M.$disp.5", ptr null)
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  %t50 = load i32, ptr %$tmp19.addr, align 4
  %t51 = trunc i32 %t50 to i8
  store i8 %t51, ptr %C.addr, align 1
  %t52 = load ptr, ptr %D.addr, align 8
  store ptr %t52, ptr %$tmp20.addr, align 8
  call void @llvm.memset.p0.i64(ptr align 4 %$tmp21.addr, i8 0, i64 4, i1 false)
  %t53 = load ptr, ptr %$tmp20.addr, align 8
  invoke void @"System._DispCall"(ptr %$tmp21.addr, ptr %t53, ptr @"M.$disp.6", ptr null)
          to label %cont.9 unwind label %cleanup.pad
cont.9:
  %t54 = load i32, ptr %$tmp21.addr, align 4
  store i32 %t54, ptr %N.addr, align 4
  %t55 = load ptr, ptr %D.addr, align 8
  store ptr %t55, ptr %$tmp22.addr, align 8
  %t56 = getelementptr inbounds [2 x i64], ptr %$tmp23.addr, i64 0, i64 0
  store ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.0, i32 0, i32 5), ptr %t56, align 8
  %t57 = load ptr, ptr %D.addr, align 8
  store ptr %t57, ptr %$tmp24.addr, align 8
  %t58 = getelementptr inbounds [1 x i64], ptr %$tmp25.addr, i64 0, i64 0
  store ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.0, i32 0, i32 5), ptr %t58, align 8
  call void @llvm.memset.p0.i64(ptr align 4 %$tmp26.addr, i8 0, i64 4, i1 false)
  %t59 = load ptr, ptr %$tmp24.addr, align 8
  invoke void @"System._DispCall"(ptr %$tmp26.addr, ptr %t59, ptr @"M.$disp.7", ptr %$tmp25.addr)
          to label %cont.10 unwind label %cleanup.pad
cont.10:
  %t60 = load i32, ptr %$tmp26.addr, align 4
  %t61 = add i32 %t60, 1
  %t62 = sext i32 %t61 to i64
  %t63 = getelementptr inbounds [2 x i64], ptr %$tmp23.addr, i64 0, i64 1
  store i64 %t62, ptr %t63, align 8
  %t64 = load ptr, ptr %$tmp22.addr, align 8
  invoke void @"System._DispCall"(ptr null, ptr %t64, ptr @"M.$disp.8", ptr %$tmp23.addr)
          to label %cont.11 unwind label %cleanup.pad
cont.11:
  %t65 = load ptr, ptr %D.addr, align 8
  store ptr %t65, ptr %$tmp27.addr, align 8
  %t66 = load ptr, ptr %$tmp27.addr, align 8
  invoke void @"System._DispCall"(ptr %$tmp28.addr, ptr %t66, ptr @"M.$disp.9", ptr null)
          to label %cont.12 unwind label %cleanup.pad
cont.12:
  %t67 = load ptr, ptr %$tmp28.addr, align 8
  invoke void @"System._IntfAsg"(ptr %O.addr, ptr %t67)
          to label %cont.13 unwind label %cleanup.pad
cont.13:
  %t68 = load ptr, ptr %D.addr, align 8
  store ptr %t68, ptr %$tmp29.addr, align 8
  %t69 = load ptr, ptr %O.addr, align 8
  %t70 = getelementptr inbounds [1 x i64], ptr %$tmp30.addr, i64 0, i64 0
  store ptr %t69, ptr %t70, align 8
  %t71 = load ptr, ptr %$tmp29.addr, align 8
  invoke void @"System._DispCall"(ptr null, ptr %t71, ptr @"M.$disp.10", ptr %$tmp30.addr)
          to label %cont.14 unwind label %cleanup.pad
cont.14:
  br label %cleanup
cleanup:
  call void @"System._UStrClr"(ptr %S.addr)
  call void @"System._UStrClr"(ptr %Name.addr)
  call void @"System._IntfClr"(ptr %O.addr)
  call void @"System._UStrClr"(ptr %$tmp4.addr)
  call void @"System._UStrClr"(ptr %$tmp8.addr)
  call void @"System._IntfClr"(ptr %$tmp28.addr)
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %S.addr) [ "funclet"(token %cl) ]
  call void @"System._UStrClr"(ptr %Name.addr) [ "funclet"(token %cl) ]
  call void @"System._IntfClr"(ptr %O.addr) [ "funclet"(token %cl) ]
  call void @"System._UStrClr"(ptr %$tmp4.addr) [ "funclet"(token %cl) ]
  call void @"System._UStrClr"(ptr %$tmp8.addr) [ "funclet"(token %cl) ]
  call void @"System._IntfClr"(ptr %$tmp28.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
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
