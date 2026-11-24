; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 110, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 84, i16 70, i16 111, i16 111, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 80, i16 105, i16 110, i16 103, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [9 x i16] } { i32 -1, i32 8, i16 1200, i16 2, i32 0, [9 x i16] [i16 71, i16 101, i16 116, i16 86, i16 97, i16 108, i16 117, i16 101, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 71, i16 101, i16 116, i16 78, i16 97, i16 109, i16 101, i16 0] }, align 16
@.str.5 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@"M.TFoo.$intftable" = private unnamed_addr constant { i64, [1 x { [16 x i8], ptr, i64 }] } { i64 1, [1 x { [16 x i8], ptr, i64 }] [{ [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.TFoo.$imt.M.IBar", i64 8 }] }, align 8
@"M.TFoo.$rtti.methods" = private unnamed_addr constant [3 x { ptr, ptr, i8, i8, [6 x i8] }] [{ ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.2, i32 0, i32 5), ptr @"M.TFoo.Ping", i8 2, i8 0, [6 x i8] zeroinitializer }, { ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [9 x i16] }, ptr @.str.3, i32 0, i32 5), ptr @"M.TFoo.GetValue", i8 2, i8 1, [6 x i8] zeroinitializer }, { ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.4, i32 0, i32 5), ptr @"M.TFoo.GetName", i8 2, i8 1, [6 x i8] zeroinitializer }], align 8
@"M.TFoo.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.1, i32 0, i32 5), ptr @"M.TFoo.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.5, i32 0, i32 5), i64 0, ptr null, i64 3, ptr @"M.TFoo.$rtti.methods", i64 0, ptr null, i64 0, ptr null }, align 8
@"M.TFoo.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr null, i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.1, i32 0, i32 5), ptr @"M.TFoo.$rtti", ptr @"M.TFoo.$intftable", ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.TFoo.$imt.M.IBar" = constant [3 x ptr] [ptr @"M.TFoo.$thunk.M.IBar.Ping", ptr @"M.TFoo.$thunk.M.IBar.GetValue", ptr @"M.TFoo.$thunk.M.IBar.GetName"], align 8

declare ptr @"System._BeginCatch"()
declare void @"System._EndCatch"()
declare void @"System._SafecallError"(i32) #1
declare i32 @"System._SafecallException"(ptr)
declare i32 @"System._SehFilter"(ptr, ptr)
declare void @"System._UStrAsg"(ptr, ptr)
declare void @"System._UStrClr"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.memmove.p0.p0.i64(ptr, ptr, i64, i1)
declare void @llvm.memset.p0.i64(ptr, i8, i64, i1)

define i32 @"M.TFoo.Ping"(ptr %Self) #0 personality ptr @__C_specific_handler {
entry:
  %Self.addr = alloca ptr, align 8
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
  store i32 0, ptr %$tmp1.addr, align 4
  br label %try.end.1
ex.dispatch.1:
  %cs.1 = catchswitch within none [label %ex.pad.1] unwind to caller
ex.pad.1:
  %cp.1 = catchpad within %cs.1 [ptr @"System._SehFilter"]
  catchret from %cp.1 to label %ex.body.1
ex.body.1:
  %t1 = call ptr @"System._BeginCatch"()
  store ptr %t1, ptr %$tmp2.addr, align 8
  %t2 = load ptr, ptr %Self.addr, align 8
  %t3 = invoke i32 @"System._SafecallException"(ptr %t2)
          to label %cont.1 unwind label %ex.cleanup.1
cont.1:
  store i32 %t3, ptr %$tmp1.addr, align 4
  call void @"System._EndCatch"()
  br label %try.end.1
ex.cleanup.1:
  %cl.1 = cleanuppad within none []
  call void @"System._EndCatch"() [ "funclet"(token %cl.1) ]
  cleanupret from %cl.1 unwind to caller
try.end.1:
  br label %exit
exit:
  %t4 = load i32, ptr %$tmp1.addr, align 4
  ret i32 %t4
}

define i32 @"M.TFoo.GetValue"(ptr %Self, ptr %$ret) #0 personality ptr @__C_specific_handler {
entry:
  %Self.addr = alloca ptr, align 8
  %$ret.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
  store ptr %$ret, ptr %$ret.addr, align 8
  store i32 0, ptr %$tmp1.addr, align 4
  store i32 5, ptr %Result.addr, align 4
  br label %try.end.1
ex.dispatch.1:
  %cs.1 = catchswitch within none [label %ex.pad.1] unwind label %cleanup.pad
ex.pad.1:
  %cp.1 = catchpad within %cs.1 [ptr @"System._SehFilter"]
  catchret from %cp.1 to label %ex.body.1
ex.body.1:
  %t1 = call ptr @"System._BeginCatch"()
  store ptr %t1, ptr %$tmp2.addr, align 8
  %t2 = load ptr, ptr %Self.addr, align 8
  %t3 = invoke i32 @"System._SafecallException"(ptr %t2)
          to label %cont.1 unwind label %ex.cleanup.1
cont.1:
  store i32 %t3, ptr %$tmp1.addr, align 4
  invoke void @"System._EndCatch"()
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  br label %try.end.1
ex.cleanup.1:
  %cl.1 = cleanuppad within none []
  invoke void @"System._EndCatch"() [ "funclet"(token %cl.1) ]
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  cleanupret from %cl.1 unwind label %cleanup.pad
try.end.1:
  br label %cleanup
cleanup:
  %t4 = load i32, ptr %$tmp1.addr, align 4
  %t5 = icmp sge i32 %t4, 0
  br i1 %t5, label %if.then.2, label %if.end.2
if.then.2:
  %t6 = load ptr, ptr %$ret.addr, align 8
  call void @llvm.memmove.p0.p0.i64(ptr align 4 %t6, ptr align 4 %Result.addr, i64 4, i1 false)
  br label %if.end.2
if.end.2:
  br label %exit
exit:
  %t7 = load i32, ptr %$tmp1.addr, align 4
  ret i32 %t7
cleanup.pad:
  %cl = cleanuppad within none []
  %t8 = load i32, ptr %$tmp1.addr, align 4
  %t9 = icmp sge i32 %t8, 0
  br i1 %t9, label %if.then.3, label %if.end.3
if.then.3:
  %t10 = load ptr, ptr %$ret.addr, align 8
  call void @llvm.memmove.p0.p0.i64(ptr align 4 %t10, ptr align 4 %Result.addr, i64 4, i1 false)
  br label %if.end.3
if.end.3:
  cleanupret from %cl unwind to caller
}

define i32 @"M.TFoo.GetName"(ptr %Self, i32 %N, ptr %$ret) #0 personality ptr @__C_specific_handler {
entry:
  %Self.addr = alloca ptr, align 8
  %N.addr = alloca i32, align 4
  %$ret.addr = alloca ptr, align 8
  %Result.addr = alloca ptr, align 8
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
  store i32 %N, ptr %N.addr, align 4
  store ptr %$ret, ptr %$ret.addr, align 8
  store ptr null, ptr %Result.addr, align 8
  store i32 0, ptr %$tmp1.addr, align 4
  %t1 = load i32, ptr %N.addr, align 4
  %t2 = icmp eq i32 %t1, 0
  br i1 %t2, label %if.then.2, label %if.end.2
if.then.2:
  invoke void @"System._UStrAsg"(ptr %Result.addr, ptr null)
          to label %cont.1 unwind label %ex.dispatch.1
cont.1:
  br label %cleanup
if.end.2:
  invoke void @"System._UStrAsg"(ptr %Result.addr, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.0, i32 0, i32 5))
          to label %cont.2 unwind label %ex.dispatch.1
cont.2:
  br label %try.end.1
ex.dispatch.1:
  %cs.1 = catchswitch within none [label %ex.pad.1] unwind label %cleanup.pad
ex.pad.1:
  %cp.1 = catchpad within %cs.1 [ptr @"System._SehFilter"]
  catchret from %cp.1 to label %ex.body.1
ex.body.1:
  %t3 = call ptr @"System._BeginCatch"()
  store ptr %t3, ptr %$tmp2.addr, align 8
  %t4 = load ptr, ptr %Self.addr, align 8
  %t5 = invoke i32 @"System._SafecallException"(ptr %t4)
          to label %cont.3 unwind label %ex.cleanup.1
cont.3:
  store i32 %t5, ptr %$tmp1.addr, align 4
  invoke void @"System._EndCatch"()
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  br label %try.end.1
ex.cleanup.1:
  %cl.1 = cleanuppad within none []
  invoke void @"System._EndCatch"() [ "funclet"(token %cl.1) ]
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  cleanupret from %cl.1 unwind label %cleanup.pad
try.end.1:
  br label %cleanup
cleanup:
  %t6 = load i32, ptr %$tmp1.addr, align 4
  %t7 = icmp sge i32 %t6, 0
  br i1 %t7, label %if.then.3, label %if.else.3
if.then.3:
  %t8 = load ptr, ptr %$ret.addr, align 8
  call void @llvm.memmove.p0.p0.i64(ptr align 8 %t8, ptr align 8 %Result.addr, i64 8, i1 false)
  store ptr null, ptr %Result.addr, align 8
  br label %if.end.3
if.else.3:
  %t9 = load ptr, ptr %$ret.addr, align 8
  store ptr null, ptr %t9, align 8
  br label %if.end.3
if.end.3:
  call void @"System._UStrClr"(ptr %Result.addr)
  br label %exit
exit:
  %t10 = load i32, ptr %$tmp1.addr, align 4
  ret i32 %t10
cleanup.pad:
  %cl = cleanuppad within none []
  %t11 = load i32, ptr %$tmp1.addr, align 4
  %t12 = icmp sge i32 %t11, 0
  br i1 %t12, label %if.then.4, label %if.else.4
if.then.4:
  %t13 = load ptr, ptr %$ret.addr, align 8
  call void @llvm.memmove.p0.p0.i64(ptr align 8 %t13, ptr align 8 %Result.addr, i64 8, i1 false)
  store ptr null, ptr %Result.addr, align 8
  br label %if.end.4
if.else.4:
  %t14 = load ptr, ptr %$ret.addr, align 8
  store ptr null, ptr %t14, align 8
  br label %if.end.4
if.end.4:
  call void @"System._UStrClr"(ptr %Result.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define i32 @"M.Twice"(i32 %N, ptr %$ret) #0 personality ptr @__C_specific_handler {
entry:
  %N.addr = alloca i32, align 4
  %$ret.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca ptr, align 8
  store i32 %N, ptr %N.addr, align 4
  store ptr %$ret, ptr %$ret.addr, align 8
  store i32 0, ptr %$tmp1.addr, align 4
  %t1 = load i32, ptr %N.addr, align 4
  %t2 = mul i32 %t1, 2
  store i32 %t2, ptr %Result.addr, align 4
  br label %try.end.1
ex.dispatch.1:
  %cs.1 = catchswitch within none [label %ex.pad.1] unwind label %cleanup.pad
ex.pad.1:
  %cp.1 = catchpad within %cs.1 [ptr @"System._SehFilter"]
  catchret from %cp.1 to label %ex.body.1
ex.body.1:
  %t3 = call ptr @"System._BeginCatch"()
  store ptr %t3, ptr %$tmp2.addr, align 8
  %t4 = invoke i32 @"System._SafecallException"(ptr null)
          to label %cont.1 unwind label %ex.cleanup.1
cont.1:
  store i32 %t4, ptr %$tmp1.addr, align 4
  invoke void @"System._EndCatch"()
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  br label %try.end.1
ex.cleanup.1:
  %cl.1 = cleanuppad within none []
  invoke void @"System._EndCatch"() [ "funclet"(token %cl.1) ]
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  cleanupret from %cl.1 unwind label %cleanup.pad
try.end.1:
  br label %cleanup
cleanup:
  %t5 = load i32, ptr %$tmp1.addr, align 4
  %t6 = icmp sge i32 %t5, 0
  br i1 %t6, label %if.then.2, label %if.end.2
if.then.2:
  %t7 = load ptr, ptr %$ret.addr, align 8
  call void @llvm.memmove.p0.p0.i64(ptr align 4 %t7, ptr align 4 %Result.addr, i64 4, i1 false)
  br label %if.end.2
if.end.2:
  br label %exit
exit:
  %t8 = load i32, ptr %$tmp1.addr, align 4
  ret i32 %t8
cleanup.pad:
  %cl = cleanuppad within none []
  %t9 = load i32, ptr %$tmp1.addr, align 4
  %t10 = icmp sge i32 %t9, 0
  br i1 %t10, label %if.then.3, label %if.end.3
if.then.3:
  %t11 = load ptr, ptr %$ret.addr, align 8
  call void @llvm.memmove.p0.p0.i64(ptr align 4 %t11, ptr align 4 %Result.addr, i64 4, i1 false)
  br label %if.end.3
if.end.3:
  cleanupret from %cl unwind to caller
}

define i32 @"M.Run"(ptr %Obj) #0 personality ptr @__C_specific_handler {
entry:
  %Obj.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  %V.addr = alloca i32, align 4
  %S.addr = alloca ptr, align 8
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  %$tmp3.addr = alloca i32, align 4
  %$tmp4.addr = alloca ptr, align 8
  %$tmp5.addr = alloca i32, align 4
  %$tmp6.addr = alloca i32, align 4
  %$tmp7.addr = alloca i32, align 4
  %$tmp8.addr = alloca ptr, align 8
  %$tmp9.addr = alloca i32, align 4
  %$tmp10.addr = alloca i32, align 4
  store ptr %Obj, ptr %Obj.addr, align 8
  store ptr null, ptr %S.addr, align 8
  store ptr null, ptr %$tmp4.addr, align 8
  %t1 = load ptr, ptr %Obj.addr, align 8
  %t2 = load ptr, ptr %t1, align 8
  %t3 = getelementptr inbounds i8, ptr %t2, i64 0
  %t4 = load ptr, ptr %t3, align 8
  %t5 = invoke i32 %t4(ptr %t1)
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store i32 %t5, ptr %$tmp1.addr, align 4
  %t6 = load i32, ptr %$tmp1.addr, align 4
  %t7 = icmp slt i32 %t6, 0
  br i1 %t7, label %if.then.1, label %if.end.1
if.then.1:
  %t8 = load i32, ptr %$tmp1.addr, align 4
  invoke void @"System._SafecallError"(i32 %t8) #1
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  unreachable
if.end.1:
  call void @llvm.memset.p0.i64(ptr align 4 %$tmp2.addr, i8 0, i64 4, i1 false)
  %t9 = load ptr, ptr %Obj.addr, align 8
  %t10 = load ptr, ptr %t9, align 8
  %t11 = getelementptr inbounds i8, ptr %t10, i64 8
  %t12 = load ptr, ptr %t11, align 8
  %t13 = invoke i32 %t12(ptr %t9, ptr %$tmp2.addr)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  store i32 %t13, ptr %$tmp3.addr, align 4
  %t14 = load i32, ptr %$tmp3.addr, align 4
  %t15 = icmp slt i32 %t14, 0
  br i1 %t15, label %if.then.2, label %if.end.2
if.then.2:
  %t16 = load i32, ptr %$tmp3.addr, align 4
  invoke void @"System._SafecallError"(i32 %t16) #1
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  unreachable
if.end.2:
  %t17 = load i32, ptr %$tmp2.addr, align 4
  store i32 %t17, ptr %V.addr, align 4
  invoke void @"System._UStrClr"(ptr %$tmp4.addr)
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  %t18 = load ptr, ptr %Obj.addr, align 8
  %t19 = load ptr, ptr %t18, align 8
  %t20 = getelementptr inbounds i8, ptr %t19, i64 16
  %t21 = load ptr, ptr %t20, align 8
  %t22 = load i32, ptr %V.addr, align 4
  %t23 = invoke i32 %t21(ptr %t18, i32 %t22, ptr %$tmp4.addr)
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  store i32 %t23, ptr %$tmp5.addr, align 4
  %t24 = load i32, ptr %$tmp5.addr, align 4
  %t25 = icmp slt i32 %t24, 0
  br i1 %t25, label %if.then.3, label %if.end.3
if.then.3:
  %t26 = load i32, ptr %$tmp5.addr, align 4
  invoke void @"System._SafecallError"(i32 %t26) #1
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  unreachable
if.end.3:
  %t27 = load ptr, ptr %$tmp4.addr, align 8
  invoke void @"System._UStrAsg"(ptr %S.addr, ptr %t27)
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  %t28 = load i32, ptr %V.addr, align 4
  call void @llvm.memset.p0.i64(ptr align 4 %$tmp6.addr, i8 0, i64 4, i1 false)
  %t29 = load ptr, ptr %Obj.addr, align 8
  %t30 = load ptr, ptr %t29, align 8
  %t31 = getelementptr inbounds i8, ptr %t30, i64 8
  %t32 = load ptr, ptr %t31, align 8
  %t33 = invoke i32 %t32(ptr %t29, ptr %$tmp6.addr)
          to label %cont.9 unwind label %ex.dispatch.4
cont.9:
  store i32 %t33, ptr %$tmp7.addr, align 4
  %t34 = load i32, ptr %$tmp7.addr, align 4
  %t35 = icmp slt i32 %t34, 0
  br i1 %t35, label %if.then.5, label %if.end.5
if.then.5:
  %t36 = load i32, ptr %$tmp7.addr, align 4
  invoke void @"System._SafecallError"(i32 %t36) #1
          to label %cont.10 unwind label %ex.dispatch.4
cont.10:
  unreachable
if.end.5:
  %t37 = load i32, ptr %$tmp6.addr, align 4
  %t38 = add i32 %t28, %t37
  store i32 %t38, ptr %V.addr, align 4
  br label %try.end.4
ex.dispatch.4:
  %cs.4 = catchswitch within none [label %ex.pad.4] unwind label %cleanup.pad
ex.pad.4:
  %cp.4 = catchpad within %cs.4 [ptr @"System._SehFilter"]
  catchret from %cp.4 to label %ex.body.4
ex.body.4:
  %t39 = call ptr @"System._BeginCatch"()
  store ptr %t39, ptr %$tmp8.addr, align 8
  store i32 0, ptr %V.addr, align 4
  invoke void @"System._EndCatch"()
          to label %cont.11 unwind label %cleanup.pad
cont.11:
  br label %try.end.4
ex.cleanup.4:
  %cl.4 = cleanuppad within none []
  invoke void @"System._EndCatch"() [ "funclet"(token %cl.4) ]
          to label %cont.12 unwind label %cleanup.pad
cont.12:
  cleanupret from %cl.4 unwind label %cleanup.pad
try.end.4:
  call void @llvm.memset.p0.i64(ptr align 4 %$tmp9.addr, i8 0, i64 4, i1 false)
  %t40 = load i32, ptr %V.addr, align 4
  %t41 = invoke i32 @"M.Twice"(i32 %t40, ptr %$tmp9.addr)
          to label %cont.13 unwind label %cleanup.pad
cont.13:
  store i32 %t41, ptr %$tmp10.addr, align 4
  %t42 = load i32, ptr %$tmp10.addr, align 4
  %t43 = icmp slt i32 %t42, 0
  br i1 %t43, label %if.then.6, label %if.end.6
if.then.6:
  %t44 = load i32, ptr %$tmp10.addr, align 4
  invoke void @"System._SafecallError"(i32 %t44) #1
          to label %cont.14 unwind label %cleanup.pad
cont.14:
  unreachable
if.end.6:
  %t45 = load i32, ptr %$tmp9.addr, align 4
  store i32 %t45, ptr %Result.addr, align 4
  br label %cleanup
cleanup:
  call void @"System._UStrClr"(ptr %S.addr)
  call void @"System._UStrClr"(ptr %$tmp4.addr)
  br label %exit
exit:
  %t46 = load i32, ptr %Result.addr, align 4
  ret i32 %t46
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %S.addr) [ "funclet"(token %cl) ]
  call void @"System._UStrClr"(ptr %$tmp4.addr) [ "funclet"(token %cl) ]
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

define internal i32 @"M.TFoo.$thunk.M.IBar.Ping"(ptr %iref) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -8
  %r = musttail call i32 @"M.TFoo.Ping"(ptr %self)
  ret i32 %r
}

define internal i32 @"M.TFoo.$thunk.M.IBar.GetValue"(ptr %iref, ptr %p1) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -8
  %r = musttail call i32 @"M.TFoo.GetValue"(ptr %self, ptr %p1)
  ret i32 %r
}

define internal i32 @"M.TFoo.$thunk.M.IBar.GetName"(ptr %iref, i32 %p1, ptr %p2) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -8
  %r = musttail call i32 @"M.TFoo.GetName"(ptr %self, i32 %p1, ptr %p2)
  ret i32 %r
}

attributes #0 = { uwtable "frame-pointer"="all" }
attributes #1 = { noreturn uwtable }
