; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [14 x i16] } { i32 -1, i32 13, i16 1200, i16 2, i32 0, [14 x i16] [i16 82, i16 117, i16 110, i16 46, i16 36, i16 99, i16 108, i16 111, i16 115, i16 117, i16 114, i16 101, i16 49, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 111, i16 116, i16 97, i16 108, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 80, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@"M.Run.$closure1.$intftable" = private unnamed_addr constant { i64, [2 x { [16 x i8], ptr, i64 }] } { i64 2, [2 x { [16 x i8], ptr, i64 }] [{ [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.Run.$closure1.$imt.M.Run.$closure1.$I1", i64 24 }, { [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.Run.$closure1.$imt.M.Run.$closure1.$I1", i64 24 }] }, align 8
@"M.Run.$closure1.$rtti.fields" = private unnamed_addr constant [2 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.1, i32 0, i32 5), i64 16, ptr @"System.Integer.$rtti", i8 0, [7 x i8] zeroinitializer }, { ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.3, i32 0, i32 5), i64 20, ptr @"System.Integer.$rtti", i8 0, [7 x i8] zeroinitializer }], align 8
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.2, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
@"M.Run.$closure1.$rtti" = internal constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [14 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.Run.$closure1.$vmt", ptr @"System.TInterfacedObject.$rtti", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.4, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 2, ptr @"M.Run.$closure1.$rtti.fields", i64 0, ptr null }, align 8
@"M.Run.$closure1.$vmt" = internal constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr @"System.TInterfacedObject.$vmt", i64 32, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [14 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"M.Run.$closure1.$rtti", ptr @"M.Run.$closure1.$intftable", ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.Run.$closure1.$imt.M.Run.$closure1.$I1" = internal constant [1 x ptr] [ptr @"M.Run.$closure1.$thunk.M.Run.$closure1.$I1.Invoke"], align 8
@"System.TInterfacedObject.$vmt" = external global ptr, align 8
@"System.TInterfacedObject.$rtti" = external global ptr, align 8

declare ptr @"System.TObject.Create"(ptr, i8)
declare void @"System._IntfAsg"(ptr, ptr)
declare void @"System._IntfClr"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.Run"(ptr %Result, ptr %A, i64 %A$high, i32 %P) #0 personality ptr @__C_specific_handler {
entry:
  %A.addr = alloca ptr, align 8
  %A$high.addr = alloca i64, align 8
  %P.addr = alloca i32, align 4
  %Total.addr = alloca i32, align 4
  %$closure.addr = alloca ptr, align 8
  %$closureRef.addr = alloca ptr, align 8
  %I.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  %X.addr = alloca i32, align 4
  %$i1.addr = alloca i32, align 4
  %$tmp3.addr = alloca i32, align 4
  %$tmp4.addr = alloca i32, align 4
  store ptr %A, ptr %A.addr, align 8
  store i64 %A$high, ptr %A$high.addr, align 8
  store i32 %P, ptr %P.addr, align 4
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %$closureRef.addr, align 8
  %t1 = invoke ptr @"System.TObject.Create"(ptr @"M.Run.$closure1.$vmt", i8 1)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  store ptr %t1, ptr %$closure.addr, align 8
  %t2 = load ptr, ptr %$closure.addr, align 8
  %t3 = icmp eq ptr %t2, null
  %t5 = getelementptr inbounds i8, ptr %t2, i64 24
  %t4 = select i1 %t3, ptr null, ptr %t5
  invoke void @"System._IntfAsg"(ptr %$closureRef.addr, ptr %t4)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t6 = load i32, ptr %P.addr, align 4
  %t7 = load ptr, ptr %$closure.addr, align 8
  %t8 = getelementptr inbounds i8, ptr %t7, i64 20
  store i32 %t6, ptr %t8, align 4
  %t9 = load ptr, ptr %$closure.addr, align 8
  %t10 = getelementptr inbounds i8, ptr %t9, i64 16
  store i32 0, ptr %t10, align 4
  store i32 1, ptr %$tmp1.addr, align 4
  %t11 = load ptr, ptr %$closure.addr, align 8
  %t12 = getelementptr inbounds i8, ptr %t11, i64 20
  %t13 = load i32, ptr %t12, align 4
  store i32 %t13, ptr %$tmp2.addr, align 4
  %t14 = load i32, ptr %$tmp1.addr, align 4
  %t15 = load i32, ptr %$tmp2.addr, align 4
  %t16 = icmp sle i32 %t14, %t15
  br i1 %t16, label %if.then.1, label %if.end.1
if.then.1:
  %t17 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t17, ptr %I.addr, align 4
  br label %for.body.2
for.body.2:
  %t18 = load ptr, ptr %$closure.addr, align 8
  %t19 = getelementptr inbounds i8, ptr %t18, i64 16
  %t20 = load i32, ptr %t19, align 4
  %t21 = load i32, ptr %I.addr, align 4
  %t22 = add i32 %t20, %t21
  %t23 = load ptr, ptr %$closure.addr, align 8
  %t24 = getelementptr inbounds i8, ptr %t23, i64 16
  store i32 %t22, ptr %t24, align 4
  br label %for.inc.2
for.inc.2:
  %t25 = load i32, ptr %I.addr, align 4
  %t26 = load i32, ptr %$tmp2.addr, align 4
  %t27 = icmp eq i32 %t25, %t26
  br i1 %t27, label %for.end.2, label %if.end.3
if.end.3:
  %t28 = load i32, ptr %I.addr, align 4
  %t29 = add i32 %t28, 1
  store i32 %t29, ptr %I.addr, align 4
  br label %for.body.2
for.end.2:
  br label %if.end.1
if.end.1:
  store i32 0, ptr %$tmp3.addr, align 4
  %t30 = load i64, ptr %A$high.addr, align 8
  %t31 = trunc i64 %t30 to i32
  store i32 %t31, ptr %$tmp4.addr, align 4
  %t32 = load i32, ptr %$tmp3.addr, align 4
  %t33 = load i32, ptr %$tmp4.addr, align 4
  %t34 = icmp sle i32 %t32, %t33
  br i1 %t34, label %if.then.4, label %if.end.4
if.then.4:
  %t35 = load i32, ptr %$tmp3.addr, align 4
  store i32 %t35, ptr %$i1.addr, align 4
  br label %for.body.5
for.body.5:
  %t36 = load ptr, ptr %A.addr, align 8
  %t37 = load i32, ptr %$i1.addr, align 4
  %t38 = sext i32 %t37 to i64
  %t39 = getelementptr inbounds i32, ptr %t36, i64 %t38
  %t40 = load i32, ptr %t39, align 4
  store i32 %t40, ptr %X.addr, align 4
  %t41 = load ptr, ptr %$closure.addr, align 8
  %t42 = getelementptr inbounds i8, ptr %t41, i64 16
  %t43 = load i32, ptr %t42, align 4
  %t44 = load i32, ptr %X.addr, align 4
  %t45 = add i32 %t43, %t44
  %t46 = load ptr, ptr %$closure.addr, align 8
  %t47 = getelementptr inbounds i8, ptr %t46, i64 16
  store i32 %t45, ptr %t47, align 4
  br label %for.inc.5
for.inc.5:
  %t48 = load i32, ptr %$i1.addr, align 4
  %t49 = load i32, ptr %$tmp4.addr, align 4
  %t50 = icmp eq i32 %t48, %t49
  br i1 %t50, label %for.end.5, label %if.end.6
if.end.6:
  %t51 = load i32, ptr %$i1.addr, align 4
  %t52 = add i32 %t51, 1
  store i32 %t52, ptr %$i1.addr, align 4
  br label %for.body.5
for.end.5:
  br label %if.end.4
if.end.4:
  %t53 = load ptr, ptr %$closure.addr, align 8
  %t54 = icmp eq ptr %t53, null
  %t56 = getelementptr inbounds i8, ptr %t53, i64 24
  %t55 = select i1 %t54, ptr null, ptr %t56
  invoke void @"System._IntfAsg"(ptr %Result, ptr %t55)
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  call void @"System._IntfClr"(ptr %$closureRef.addr)
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._IntfClr"(ptr %$closureRef.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define internal i32 @"M.Run.$closure1.Anon1"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  store ptr %Self, ptr %Self.addr, align 8
  %t1 = load ptr, ptr %Self.addr, align 8
  %t2 = getelementptr inbounds i8, ptr %t1, i64 16
  %t3 = load i32, ptr %t2, align 4
  %t4 = load ptr, ptr %Self.addr, align 8
  %t5 = getelementptr inbounds i8, ptr %t4, i64 20
  %t6 = load i32, ptr %t5, align 4
  %t7 = add i32 %t3, %t6
  store i32 %t7, ptr %Result.addr, align 4
  br label %exit
exit:
  %t8 = load i32, ptr %Result.addr, align 4
  ret i32 %t8
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

define internal i32 @"M.Run.$closure1.$thunk.M.Run.$closure1.$I1.Invoke"(ptr %iref) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -24
  %r = musttail call i32 @"M.Run.$closure1.Anon1"(ptr %self)
  ret i32 %r
}

attributes #0 = { uwtable "frame-pointer"="all" }

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
