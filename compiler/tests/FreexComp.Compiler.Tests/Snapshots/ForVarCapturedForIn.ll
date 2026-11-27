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
@.str.7 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 88, i16 0] }, align 16
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

define void @"M.Fill"(ptr %Values, i64 %Values$high) #0 personality ptr @__C_specific_handler {
entry:
  %Values.addr = alloca ptr, align 8
  %Values$high.addr = alloca i64, align 8
  %N.addr = alloca i32, align 4
  %$closure.addr = alloca ptr, align 8
  %$closureRef.addr = alloca ptr, align 8
  %$block2.addr = alloca ptr, align 8
  %$block2Ref.addr = alloca ptr, align 8
  %$i1.addr = alloca i32, align 4
  %$tmp1.addr = alloca i32, align 4
  %$tmp2.addr = alloca i32, align 4
  store ptr %Values, ptr %Values.addr, align 8
  store i64 %Values$high, ptr %Values$high.addr, align 8
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
  store i32 0, ptr %N.addr, align 4
  store ptr null, ptr %$block2.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.4 unwind label %fin.pad.1
cont.4:
  store i32 0, ptr %$tmp1.addr, align 4
  %t6 = load i64, ptr %Values$high.addr, align 8
  %t7 = trunc i64 %t6 to i32
  store i32 %t7, ptr %$tmp2.addr, align 4
  %t8 = load i32, ptr %$tmp1.addr, align 4
  %t9 = load i32, ptr %$tmp2.addr, align 4
  %t10 = icmp sle i32 %t8, %t9
  br i1 %t10, label %if.then.2, label %if.end.2
if.then.2:
  %t11 = load i32, ptr %$tmp1.addr, align 4
  store i32 %t11, ptr %$i1.addr, align 4
  br label %for.body.3
for.body.3:
  %t12 = invoke ptr @"System.TObject.Create"(ptr @"M.Fill.$Block2.$vmt", i8 1)
          to label %cont.5 unwind label %fin.pad.1
cont.5:
  store ptr %t12, ptr %$block2.addr, align 8
  %t13 = load ptr, ptr %$block2.addr, align 8
  %t14 = icmp eq ptr %t13, null
  %t16 = getelementptr inbounds i8, ptr %t13, i64 40
  %t15 = select i1 %t14, ptr null, ptr %t16
  invoke void @"System._IntfAsg"(ptr %$block2Ref.addr, ptr %t15)
          to label %cont.6 unwind label %fin.pad.1
cont.6:
  %t17 = load ptr, ptr %$closure.addr, align 8
  %t18 = load ptr, ptr %$block2.addr, align 8
  %t19 = getelementptr inbounds i8, ptr %t18, i64 16
  store ptr %t17, ptr %t19, align 8
  %t20 = load ptr, ptr %$block2.addr, align 8
  %t21 = getelementptr inbounds i8, ptr %t20, i64 24
  %t22 = load ptr, ptr %$closure.addr, align 8
  %t23 = icmp eq ptr %t22, null
  %t25 = getelementptr inbounds i8, ptr %t22, i64 8
  %t24 = select i1 %t23, ptr null, ptr %t25
  invoke void @"System._IntfAsg"(ptr %t21, ptr %t24)
          to label %cont.7 unwind label %fin.pad.1
cont.7:
  %t26 = load ptr, ptr %Values.addr, align 8
  %t27 = load i32, ptr %$i1.addr, align 4
  %t28 = sext i32 %t27 to i64
  %t29 = getelementptr inbounds i32, ptr %t26, i64 %t28
  %t30 = load i32, ptr %t29, align 4
  %t31 = load ptr, ptr %$block2.addr, align 8
  %t32 = getelementptr inbounds i8, ptr %t31, i64 32
  store i32 %t30, ptr %t32, align 4
  %t33 = load i32, ptr %N.addr, align 4
  %t34 = sext i32 %t33 to i64
  %t35 = getelementptr inbounds [10 x ptr], ptr @"M.Fs", i64 0, i64 %t34
  %t36 = load ptr, ptr %$block2.addr, align 8
  %t37 = icmp eq ptr %t36, null
  %t39 = getelementptr inbounds i8, ptr %t36, i64 40
  %t38 = select i1 %t37, ptr null, ptr %t39
  invoke void @"System._IntfAsg"(ptr %t35, ptr %t38)
          to label %cont.8 unwind label %fin.pad.1
cont.8:
  %t40 = load i32, ptr %N.addr, align 4
  %t41 = add i32 %t40, 1
  store i32 %t41, ptr %N.addr, align 4
  br label %for.inc.3
for.inc.3:
  %t42 = load i32, ptr %$i1.addr, align 4
  %t43 = load i32, ptr %$tmp2.addr, align 4
  %t44 = icmp eq i32 %t42, %t43
  br i1 %t44, label %for.end.3, label %if.end.4
if.end.4:
  %t45 = load i32, ptr %$i1.addr, align 4
  %t46 = add i32 %t45, 1
  store i32 %t46, ptr %$i1.addr, align 4
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
  store i32 %t3, ptr %Result.addr, align 4
  br label %exit
exit:
  %t4 = load i32, ptr %Result.addr, align 4
  ret i32 %t4
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
