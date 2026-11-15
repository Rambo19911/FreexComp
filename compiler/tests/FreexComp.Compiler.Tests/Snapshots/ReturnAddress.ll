; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

declare ptr @llvm.returnaddress(i32)

define ptr @"M.Caller"() #0 {
entry:
  %Result.addr = alloca ptr, align 8
  %t1 = call ptr @llvm.returnaddress(i32 0)
  store ptr %t1, ptr %Result.addr, align 8
  %t2 = call ptr @llvm.returnaddress(i32 0)
  %t3 = icmp eq ptr %t2, null
  br i1 %t3, label %if.then.1, label %if.end.1
if.then.1:
  store ptr null, ptr %Result.addr, align 8
  br label %if.end.1
if.end.1:
  br label %exit
exit:
  %t4 = load ptr, ptr %Result.addr, align 8
  ret ptr %t4
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
