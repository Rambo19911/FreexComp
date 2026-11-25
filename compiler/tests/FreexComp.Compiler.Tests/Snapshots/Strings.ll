; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [4 x i16] } { i32 -1, i32 3, i16 1200, i16 2, i32 0, [4 x i16] [i16 108, i16 105, i16 116, i16 0] }, align 16
@"M.G" = internal global ptr zeroinitializer, align 8
@"System.Output" = external global [624 x i8], align 8

declare void @"System._IOCheck"()
declare void @"System._RangeError"() #1
declare void @"System._UStrAddRef"(ptr)
declare void @"System._UStrAsg"(ptr, ptr)
declare void @"System._UStrClr"(ptr)
declare void @"System._UStrFromWChar"(ptr, i16)
declare void @"System._WriteEnd"(ptr)
declare void @"System._WriteLn"(ptr)
declare void @"System._WriteUStr"(ptr, ptr, i32)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.Echo"(ptr %Result, ptr %s) #0 personality ptr @__C_specific_handler {
entry:
  %s.addr = alloca ptr, align 8
  store ptr %s, ptr %s.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  %t1 = load ptr, ptr %s.addr, align 8
  call void @"System._UStrAddRef"(ptr %t1)
  %t2 = load ptr, ptr %s.addr, align 8
  invoke void @"System._UStrAsg"(ptr %Result, ptr %t2)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  call void @"System._UStrClr"(ptr %s.addr)
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %s.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define void @"M.Use"(ptr %c, ptr %v, ptr %o) #0 personality ptr @__C_specific_handler {
entry:
  %c.addr = alloca ptr, align 8
  %t.addr = alloca ptr, align 8
  %ch.addr = alloca i16, align 2
  %$tmp1.addr = alloca ptr, align 8
  %$tmp2.addr = alloca ptr, align 8
  store ptr %c, ptr %c.addr, align 8
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad
cont.1:
  store ptr null, ptr %t.addr, align 8
  store ptr null, ptr %$tmp1.addr, align 8
  store ptr null, ptr %$tmp2.addr, align 8
  invoke void @"System._UStrClr"(ptr %o)
          to label %cont.2 unwind label %cleanup.pad
cont.2:
  %t1 = load ptr, ptr %c.addr, align 8
  invoke void @"System._UStrAsg"(ptr %t.addr, ptr %t1)
          to label %cont.3 unwind label %cleanup.pad
cont.3:
  invoke void @"System._UStrAsg"(ptr %v, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.0, i32 0, i32 5))
          to label %cont.4 unwind label %cleanup.pad
cont.4:
  %t2 = load ptr, ptr %t.addr, align 8
  invoke void @"M.Echo"(ptr %$tmp1.addr, ptr %t2)
          to label %cont.5 unwind label %cleanup.pad
cont.5:
  %t3 = load ptr, ptr %$tmp1.addr, align 8
  invoke void @"System._UStrAsg"(ptr %o, ptr %t3)
          to label %cont.6 unwind label %cleanup.pad
cont.6:
  %t4 = load ptr, ptr %v, align 8
  invoke void @"M.Echo"(ptr %t.addr, ptr %t4)
          to label %cont.7 unwind label %cleanup.pad
cont.7:
  %t5 = load ptr, ptr %t.addr, align 8
  %t6 = getelementptr inbounds i16, ptr %t5, i64 0
  %t7 = load i16, ptr %t6, align 2
  store i16 %t7, ptr %ch.addr, align 2
  %t8 = load i16, ptr %ch.addr, align 2
  invoke void @"System._UStrFromWChar"(ptr %t.addr, i16 %t8)
          to label %cont.8 unwind label %cleanup.pad
cont.8:
  %t9 = load ptr, ptr %t.addr, align 8
  invoke void @"System._UStrAsg"(ptr @"M.G", ptr %t9)
          to label %cont.9 unwind label %cleanup.pad
cont.9:
  %t10 = load ptr, ptr %t.addr, align 8
  invoke void @"System._WriteUStr"(ptr @"System.Output", ptr %t10, i32 0)
          to label %cont.10 unwind label %cleanup.pad
cont.10:
  invoke void @"System._WriteLn"(ptr @"System.Output")
          to label %cont.11 unwind label %cleanup.pad
cont.11:
  invoke void @"System._WriteEnd"(ptr @"System.Output")
          to label %cont.12 unwind label %cleanup.pad
cont.12:
  invoke void @"System._IOCheck"()
          to label %cont.13 unwind label %cleanup.pad
cont.13:
  %t11 = load ptr, ptr %t.addr, align 8
  %t12 = icmp eq ptr %t11, null
  br i1 %t12, label %len.end.1, label %len.load.1
len.load.1:
  %t13 = getelementptr inbounds i8, ptr %t11, i64 -12
  %t14 = load i32, ptr %t13, align 4
  br label %len.end.1
len.end.1:
  %t15 = phi i32 [ 0, %cont.13 ], [ %t14, %len.load.1 ]
  %t16 = icmp sgt i32 %t15, 2
  br i1 %t16, label %cleanup, label %if.end.2
if.end.2:
  %t17 = load ptr, ptr @"M.G", align 8
  invoke void @"M.Echo"(ptr %$tmp2.addr, ptr %t17)
          to label %cont.14 unwind label %cleanup.pad
cont.14:
  br label %cleanup
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.15 unwind label %cleanup.pad
cont.15:
  call void @"System._UStrClr"(ptr %t.addr)
  call void @"System._UStrClr"(ptr %$tmp1.addr)
  call void @"System._UStrClr"(ptr %$tmp2.addr)
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr %t.addr) [ "funclet"(token %cl) ]
  call void @"System._UStrClr"(ptr %$tmp1.addr) [ "funclet"(token %cl) ]
  call void @"System._UStrClr"(ptr %$tmp2.addr) [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define i32 @"M.Len"(ptr %s, i32 %i) #0 {
entry:
  %s.addr = alloca ptr, align 8
  %i.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store ptr %s, ptr %s.addr, align 8
  store i32 %i, ptr %i.addr, align 4
  %t1 = load ptr, ptr %s.addr, align 8
  %t2 = icmp eq ptr %t1, null
  br i1 %t2, label %len.end.1, label %len.load.1
len.load.1:
  %t3 = getelementptr inbounds i8, ptr %t1, i64 -12
  %t4 = load i32, ptr %t3, align 4
  br label %len.end.1
len.end.1:
  %t5 = phi i32 [ 0, %entry ], [ %t4, %len.load.1 ]
  %t6 = load ptr, ptr %s.addr, align 8
  %t7 = load i32, ptr %i.addr, align 4
  %t8 = sext i32 %t7 to i64
  %t9 = sub i64 %t8, 1
  %t10 = icmp eq ptr %t6, null
  br i1 %t10, label %len.end.2, label %len.load.2
len.load.2:
  %t11 = getelementptr inbounds i8, ptr %t6, i64 -12
  %t12 = load i32, ptr %t11, align 4
  br label %len.end.2
len.end.2:
  %t13 = phi i32 [ 0, %len.end.1 ], [ %t12, %len.load.2 ]
  %t14 = zext i32 %t13 to i64
  %t15 = icmp ult i64 %t9, %t14
  br i1 %t15, label %range.ok.3, label %range.error.3
range.error.3:
  call void @"System._RangeError"() #1
  unreachable
range.ok.3:
  %t16 = getelementptr inbounds i16, ptr %t6, i64 %t9
  %t17 = load i16, ptr %t16, align 2
  %t18 = zext i16 %t17 to i32
  %t19 = add i32 %t5, %t18
  store i32 %t19, ptr %Result.addr, align 4
  br label %exit
exit:
  %t20 = load i32, ptr %Result.addr, align 4
  ret i32 %t20
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
  call void @"System._UStrClr"(ptr @"M.G")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._UStrClr"(ptr @"M.G") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

attributes #0 = { uwtable "frame-pointer"="all" }
attributes #1 = { noreturn uwtable }

!llvm.module.flags = !{!0}
!0 = !{i32 2, !"eh-asynch", i32 1}
