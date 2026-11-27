; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [3 x i16] } { i32 -1, i32 2, i16 1200, i16 2, i32 0, [3 x i16] [i16 84, i16 70, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [11 x i16] } { i32 -1, i32 10, i16 1200, i16 2, i32 0, [11 x i16] [i16 73, i16 73, i16 110, i16 116, i16 101, i16 114, i16 102, i16 97, i16 99, i16 101, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [15 x i16] } { i32 -1, i32 14, i16 1200, i16 2, i32 0, [15 x i16] [i16 70, i16 105, i16 108, i16 108, i16 46, i16 36, i16 99, i16 108, i16 111, i16 115, i16 117, i16 114, i16 101, i16 49, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [13 x i16] } { i32 -1, i32 12, i16 1200, i16 2, i32 0, [13 x i16] [i16 70, i16 105, i16 108, i16 108, i16 46, i16 36, i16 66, i16 108, i16 111, i16 99, i16 107, i16 50, i16 0] }, align 16
@.str.5 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 36, i16 111, i16 117, i16 116, i16 101, i16 114, i16 0] }, align 16
@.str.6 = private unnamed_addr constant { i32, i32, i16, i16, i32, [10 x i16] } { i32 -1, i32 9, i16 1200, i16 2, i32 0, [10 x i16] [i16 36, i16 111, i16 117, i16 116, i16 101, i16 114, i16 82, i16 101, i16 102, i16 0] }, align 16
@.str.7 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 73, i16 0] }, align 16
@.str.8 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
@"M.Fill.$Block2.$inittable" = private unnamed_addr constant { i64, [1 x { ptr, i64 }] } { i64 1, [1 x { ptr, i64 }] [{ ptr, i64 } { ptr @"System.IInterface.$rtti", i64 24 }] }, align 8
@"M.Fill.$Block2.$intftable" = private unnamed_addr constant { i64, [2 x { [16 x i8], ptr, i64 }] } { i64 2, [2 x { [16 x i8], ptr, i64 }] [{ [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.Fill.$Block2.$imt.M.Fill.$closure1.$I1", i64 40 }, { [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.Fill.$Block2.$imt.M.Fill.$closure1.$I1", i64 40 }] }, align 8
@"M.Fill.$Block2.$rtti.fields" = private unnamed_addr constant [3 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.5, i32 0, i32 5), i64 16, ptr @"M.Fill.$closure1.$rtti", i8 0, [7 x i8] zeroinitializer }, { ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [10 x i16] }, ptr @.str.6, i32 0, i32 5), i64 24, ptr @"System.IInterface.$rtti", i8 0, [7 x i8] zeroinitializer }, { ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.7, i32 0, i32 5), i64 32, ptr @"System.Integer.$rtti", i8 0, [7 x i8] zeroinitializer }], align 8
$"System.IInterface.$rtti" = comdat any
@"System.IInterface.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, ptr, [16 x i8], i64 } { i8 15, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [11 x i16] }, ptr @.str.1, i32 0, i32 5), ptr null, [16 x i8] zeroinitializer, i64 0 }, comdat, align 8
$"M.TF.$rtti" = comdat any
@"M.TF.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, ptr, [16 x i8], i64 } { i8 15, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"System.IInterface.$rtti", [16 x i8] zeroinitializer, i64 1 }, comdat, align 8
$"M.$rtti.array[0..9] of TF" = comdat any
@"M.$rtti.array[0..9] of TF" = linkonce_odr constant { i8, [7 x i8], ptr, i64, i64, ptr } { i8 13, [7 x i8] zeroinitializer, ptr null, i64 80, i64 10, ptr @"M.TF.$rtti" }, comdat, align 8
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.8, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
@"M.Fill.$closure1.$rtti" = internal constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [15 x i16] }, ptr @.str.2, i32 0, i32 5), ptr @"M.Fill.$closure1.$vmt", ptr @"System.TInterfacedObject.$rtti", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.3, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 0, ptr null, i64 0, ptr null }, align 8
@"M.Fill.$Block2.$rtti" = internal constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [13 x i16] }, ptr @.str.4, i32 0, i32 5), ptr @"M.Fill.$Block2.$vmt", ptr @"System.TInterfacedObject.$rtti", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.3, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 3, ptr @"M.Fill.$Block2.$rtti.fields", i64 0, ptr null }, align 8
@"M.Fill.$closure1.$vmt" = internal constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr @"System.TInterfacedObject.$vmt", i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [15 x i16] }, ptr @.str.2, i32 0, i32 5), ptr @"M.Fill.$closure1.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.Fill.$Block2.$vmt" = internal constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr @"System.TInterfacedObject.$vmt", i64 48, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [13 x i16] }, ptr @.str.4, i32 0, i32 5), ptr @"M.Fill.$Block2.$rtti", ptr @"M.Fill.$Block2.$intftable", ptr null, ptr @"M.Fill.$Block2.$inittable", ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.Fill.$Block2.$imt.M.Fill.$closure1.$I1" = internal constant [1 x ptr] [ptr @"M.Fill.$Block2.$thunk.M.Fill.$closure1.$I1.Invoke"], align 8
@"M.Fs" = internal global [10 x ptr] zeroinitializer, align 8
@"System.TInterfacedObject.$vmt" = external global ptr, align 8
@"System.TInterfacedObject.$rtti" = external global ptr, align 8

declare ptr @"System.TObject.Create"(ptr, i8)
declare void @"System._Finalize"(ptr, ptr)
declare void @"System._IntfAsg"(ptr, ptr)
declare void @"System._IntfClr"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.Fill"(i32 %N) #0 personality ptr @__C_specific_handler {
entry:
  %N.addr = alloca i32, align 4
  %$closure.addr = alloca ptr, align 8
  %$closureRef.addr = alloca ptr, align 8
  %$block2.addr = alloca ptr, align 8
  %$block2Ref.addr = alloca ptr, align 8
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  %$for2.addr = alloca i32, align 4
  store i32 %N, ptr %N.addr, align 4
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %$closureRef.addr, align 8
  store ptr null, ptr %$block2.addr, align 8
  store ptr null, ptr %$block2Ref.addr, align 8
  %t1 = invoke ptr @"System.TObject.Create"(ptr @"M.Fill.$closure1.$vmt", i8 1)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  store ptr %t1, ptr %$closure.addr, align 8
  %t2 = load ptr, ptr %$closure.addr, align 8
  %t3 = icmp eq ptr %t2, null
  %t5 = getelementptr inbounds i8, ptr %t2, i64 8
  %t4 = select i1 %t3, ptr null, ptr %t5
  invoke void @"System._IntfAsg"(ptr %$closureRef.addr, ptr %t4)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  store ptr null, ptr %$block2.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.4 unwind label %fin.pad.1
cont.4:
  store i32 0, ptr %$tmp1.addr, align 4
  %t6 = load i32, ptr %N.addr, align 4
  store i32 %t6, ptr %$tmp2.addr, align 4
  %t7 = load i32, ptr %$tmp1.addr, align 4
  %t8 = load i32, ptr %$tmp2.addr, align 4
  %t9 = icmp sle i32 %t7, %t8
  br i1 %t9, label %if.then.2, label %if.end.2
if.then.2:
  %t10 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t10, ptr %$for2.addr, align 4
  br label %for.body.3
for.body.3:
  %t11 = invoke ptr @"System.TObject.Create"(ptr @"M.Fill.$Block2.$vmt", i8 1)
          to label %cont.5 unwind label %fin.pad.1
cont.5:
  store ptr %t11, ptr %$block2.addr, align 8
  %t12 = load ptr, ptr %$block2.addr, align 8
  %t13 = icmp eq ptr %t12, null
  %t15 = getelementptr inbounds i8, ptr %t12, i64 40
  %t14 = select i1 %t13, ptr null, ptr %t15
  invoke void @"System._IntfAsg"(ptr %$block2Ref.addr, ptr %t14)
          to label %cont.6 unwind label %fin.pad.1
cont.6:
  %t16 = load ptr, ptr %$closure.addr, align 8
  %t17 = load ptr, ptr %$block2.addr, align 8
  %t18 = getelementptr inbounds i8, ptr %t17, i64 16
  store ptr %t16, ptr %t18, align 8
  %t19 = load ptr, ptr %$block2.addr, align 8
  %t20 = getelementptr inbounds i8, ptr %t19, i64 24
  %t21 = load ptr, ptr %$closure.addr, align 8
  %t22 = icmp eq ptr %t21, null
  %t24 = getelementptr inbounds i8, ptr %t21, i64 8
  %t23 = select i1 %t22, ptr null, ptr %t24
  invoke void @"System._IntfAsg"(ptr %t20, ptr %t23)
          to label %cont.7 unwind label %fin.pad.1
cont.7:
  %t25 = load i32, ptr %$for2.addr, align 4
  %t26 = load ptr, ptr %$block2.addr, align 8
  %t27 = getelementptr inbounds i8, ptr %t26, i64 32
  store i32 %t25, ptr %t27, align 4
  %t28 = load ptr, ptr %$block2.addr, align 8
  %t29 = getelementptr inbounds i8, ptr %t28, i64 32
  %t30 = load i32, ptr %t29, align 4
  %t31 = sext i32 %t30 to i64
  %t32 = getelementptr inbounds [10 x ptr], ptr @"M.Fs", i64 0, i64 %t31
  %t33 = load ptr, ptr %$block2.addr, align 8
  %t34 = icmp eq ptr %t33, null
  %t36 = getelementptr inbounds i8, ptr %t33, i64 40
  %t35 = select i1 %t34, ptr null, ptr %t36
  invoke void @"System._IntfAsg"(ptr %t32, ptr %t35)
          to label %cont.8 unwind label %fin.pad.1
cont.8:
  %t37 = load ptr, ptr %$block2.addr, align 8
  %t38 = getelementptr inbounds i8, ptr %t37, i64 32
  %t39 = load i32, ptr %t38, align 4
  %t40 = icmp eq i32 %t39, 5
  br i1 %t40, label %for.end.3, label %if.end.4
if.end.4:
  br label %for.inc.3
for.inc.3:
  %t41 = load i32, ptr %$for2.addr, align 4
  %t42 = load i32, ptr %$tmp2.addr, align 4
  %t43 = icmp eq i32 %t41, %t42
  br i1 %t43, label %for.end.3, label %if.end.5
if.end.5:
  %t44 = load i32, ptr %$for2.addr, align 4
  %t45 = add i32 %t44, 1
  store i32 %t45, ptr %$for2.addr, align 4
  br label %for.body.3
for.end.3:
  br label %if.end.2
if.end.2:
  invoke void @llvm.seh.try.end()
          to label %cont.9 unwind label %fin.pad.1
cont.9:
  invoke void @"System._IntfClr"(ptr %$block2Ref.addr)
          to label %cont.10 unwind label %cleanup.pad
cont.10:
  br label %try.end.1
fin.pad.1:
  %cl.1 = cleanuppad within none []
  invoke void @"System._IntfClr"(ptr %$block2Ref.addr) [ "funclet"(token %cl.1) ]
          to label %cont.11 unwind label %cleanup.pad
cont.11:
  cleanupret from %cl.1 unwind label %cleanup.pad
try.end.1:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.12 unwind label %cleanup.pad
cont.12:
  call void @"System._IntfClr"(ptr %$closureRef.addr)
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._IntfClr"(ptr %$closureRef.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define internal i32 @"M.Fill.$Block2.Anon1"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  store ptr %Self, ptr %Self.addr, align 8
  %t1 = load ptr, ptr %Self.addr, align 8
  %t2 = getelementptr inbounds i8, ptr %t1, i64 32
  %t3 = load i32, ptr %t2, align 4
  %t4 = mul i32 %t3, 10
  store i32 %t4, ptr %Result.addr, align 4
  br label %exit
exit:
  %t5 = load i32, ptr %Result.addr, align 4
  ret i32 %t5
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
  call void @"System._Finalize"(ptr @"M.Fs", ptr @"M.$rtti.array[0..9] of TF")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._Finalize"(ptr @"M.Fs", ptr @"M.$rtti.array[0..9] of TF") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define internal i32 @"M.Fill.$Block2.$thunk.M.Fill.$closure1.$I1.Invoke"(ptr %iref) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -40
  %r = musttail call i32 @"M.Fill.$Block2.Anon1"(ptr %self)
  ret i32 %r
}

attributes #0 = { uwtable "frame-pointer"="all" }

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
