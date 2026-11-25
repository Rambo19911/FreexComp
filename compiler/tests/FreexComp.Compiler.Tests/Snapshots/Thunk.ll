; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [4 x i16] } { i32 -1, i32 3, i16 1200, i16 2, i32 0, [4 x i16] [i16 102, i16 111, i16 111, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 84, i16 70, i16 111, i16 111, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [4 x i16] } { i32 -1, i32 3, i16 1200, i16 2, i32 0, [4 x i16] [i16 66, i16 97, i16 122, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [4 x i16] } { i32 -1, i32 3, i16 1200, i16 2, i32 0, [4 x i16] [i16 71, i16 101, i16 116, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 78, i16 97, i16 109, i16 101, i16 0] }, align 16
@.str.5 = private unnamed_addr constant { i32, i32, i16, i16, i32, [4 x i16] } { i32 -1, i32 3, i16 1200, i16 2, i32 0, [4 x i16] [i16 81, i16 117, i16 120, i16 0] }, align 16
@.str.6 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 78, i16 0] }, align 16
@.str.7 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
@.str.8 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@"M.TFoo.$intftable" = private unnamed_addr constant { i64, [2 x { [16 x i8], ptr, i64 }] } { i64 2, [2 x { [16 x i8], ptr, i64 }] [{ [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.TFoo.$imt.M.IQux", i64 16 }, { [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.TFoo.$imt.M.IQux", i64 16 }] }, align 8
@"M.TFoo.$rtti.methods" = private unnamed_addr constant [4 x { ptr, ptr, i8, i8, [6 x i8] }] [{ ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.2, i32 0, i32 5), ptr @"M.TFoo.Baz", i8 2, i8 0, [6 x i8] zeroinitializer }, { ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.3, i32 0, i32 5), ptr @"M.TFoo.Get", i8 2, i8 1, [6 x i8] zeroinitializer }, { ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.4, i32 0, i32 5), ptr @"M.TFoo.Name", i8 2, i8 1, [6 x i8] zeroinitializer }, { ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.5, i32 0, i32 5), ptr @"M.TFoo.Qux", i8 2, i8 0, [6 x i8] zeroinitializer }], align 8
@"M.TFoo.$rtti.fields" = private unnamed_addr constant [1 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.6, i32 0, i32 5), i64 8, ptr @"System.Integer.$rtti", i8 2, [7 x i8] zeroinitializer }], align 8
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.7, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
@"M.TFoo.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.1, i32 0, i32 5), ptr @"M.TFoo.$vmt", ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.8, i32 0, i32 5), i64 0, ptr null, i64 4, ptr @"M.TFoo.$rtti.methods", i64 1, ptr @"M.TFoo.$rtti.fields", i64 0, ptr null }, align 8
@"M.TFoo.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [1 x ptr] } { ptr null, i64 24, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.1, i32 0, i32 5), ptr @"M.TFoo.$rtti", ptr @"M.TFoo.$intftable", ptr null, ptr null, ptr null, [1 x ptr] [ptr @"M.TFoo.Get"] }, align 8
@"M.TFoo.$imt.M.IQux" = constant [4 x ptr] [ptr @"M.TFoo.$thunk.M.IQux.Baz", ptr @"M.TFoo.$thunk.M.IQux.Get", ptr @"M.TFoo.$thunk.M.IQux.Name", ptr @"M.TFoo.$thunk.M.IQux.Qux"], align 8

declare void @"System._IntfAsg"(ptr, ptr)
declare void @"System._IntfClr"(ptr)
declare void @"System._UStrAsg"(ptr, ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.TFoo.Baz"(ptr %Self, i32 %A) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %A.addr = alloca i32, align 4
  store ptr %Self, ptr %Self.addr, align 8
  store i32 %A, ptr %A.addr, align 4
  %t1 = load i32, ptr %A.addr, align 4
  %t2 = load ptr, ptr %Self.addr, align 8
  %t3 = getelementptr inbounds i8, ptr %t2, i64 8
  store i32 %t1, ptr %t3, align 4
  br label %exit
exit:
  ret void
}

define i32 @"M.TFoo.Get"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  store ptr %Self, ptr %Self.addr, align 8
  %t1 = load ptr, ptr %Self.addr, align 8
  %t2 = getelementptr inbounds i8, ptr %t1, i64 8
  %t3 = load i32, ptr %t2, align 4
  store i32 %t3, ptr %Result.addr, align 4
  br label %exit
exit:
  %t4 = load i32, ptr %Result.addr, align 4
  ret i32 %t4
}

define void @"M.TFoo.Name"(ptr %Self, ptr %Result) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
  call void @"System._UStrAsg"(ptr %Result, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.0, i32 0, i32 5))
  br label %exit
exit:
  ret void
}

define void @"M.TFoo.Qux"(ptr %Self) #0 {
entry:
  %Self.addr = alloca ptr, align 8
  store ptr %Self, ptr %Self.addr, align 8
  br label %exit
exit:
  ret void
}

define i32 @"M.Run"(ptr %Q) #0 personality ptr @__C_specific_handler {
entry:
  %Q.addr = alloca ptr, align 8
  %Result.addr = alloca i32, align 4
  %B.addr = alloca ptr, align 8
  store ptr %Q, ptr %Q.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %B.addr, align 8
  %t1 = load ptr, ptr %Q.addr, align 8
  %t2 = load ptr, ptr %t1, align 8
  %t3 = getelementptr inbounds i8, ptr %t2, i64 0
  %t4 = load ptr, ptr %t3, align 8
  invoke void %t4(ptr %t1, i32 2)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t5 = load ptr, ptr %Q.addr, align 8
  %t6 = load ptr, ptr %t5, align 8
  %t7 = getelementptr inbounds i8, ptr %t6, i64 24
  %t8 = load ptr, ptr %t7, align 8
  invoke void %t8(ptr %t5)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  %t9 = load ptr, ptr %Q.addr, align 8
  invoke void @"System._IntfAsg"(ptr %B.addr, ptr %t9)
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  %t10 = load ptr, ptr %B.addr, align 8
  %t11 = load ptr, ptr %t10, align 8
  %t12 = getelementptr inbounds i8, ptr %t11, i64 8
  %t13 = load ptr, ptr %t12, align 8
  %t14 = invoke i32 %t13(ptr %t10)
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  store i32 %t14, ptr %Result.addr, align 4
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  call void @"System._IntfClr"(ptr %B.addr)
  br label %exit
exit:
  %t15 = load i32, ptr %Result.addr, align 4
  ret i32 %t15
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._IntfClr"(ptr %B.addr) [ "funclet"(token %cl) ]
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

define internal void @"M.TFoo.$thunk.M.IQux.Baz"(ptr %iref, i32 %p1) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -16
  musttail call void @"M.TFoo.Baz"(ptr %self, i32 %p1)
  ret void
}

define internal i32 @"M.TFoo.$thunk.M.IQux.Get"(ptr %iref) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -16
  %vmt = load ptr, ptr %self, align 8
  %slot = getelementptr inbounds i8, ptr %vmt, i64 64
  %fn = load ptr, ptr %slot, align 8
  %r = musttail call i32 %fn(ptr %self)
  ret i32 %r
}

define internal void @"M.TFoo.$thunk.M.IQux.Name"(ptr %iref, ptr %p1) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -16
  musttail call void @"M.TFoo.Name"(ptr %self, ptr %p1)
  ret void
}

define internal void @"M.TFoo.$thunk.M.IQux.Qux"(ptr %iref) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -16
  musttail call void @"M.TFoo.Qux"(ptr %self)
  ret void
}

attributes #0 = { uwtable "frame-pointer"="all" }

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
