; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

@"M.G" = internal global i32 zeroinitializer, align 4

define i64 @"M.Counters"(ptr %i, ptr %q, i32 %n) #0 {
entry:
  %n.addr = alloca i32, align 4
  %Result.addr = alloca i64, align 8
  %$tmp1.addr = alloca i32, align 4
  store i32 %n, ptr %n.addr, align 4
  %t1 = atomicrmw add ptr %i, i32 1 seq_cst, align 4
  %t2 = add i32 %t1, 1
  %t3 = sext i32 %t2 to i64
  store i64 %t3, ptr %Result.addr, align 8
  %t4 = load i64, ptr %Result.addr, align 8
  %t5 = load i32, ptr %n.addr, align 4
  %t6 = sext i32 %t5 to i64
  %t7 = atomicrmw sub ptr %q, i64 %t6 seq_cst, align 8
  %t8 = sub i64 %t7, %t6
  %t9 = add i64 %t4, %t8
  store i64 %t9, ptr %Result.addr, align 8
  %t10 = atomicrmw add ptr @"M.G", i32 5 seq_cst, align 4
  %t11 = add i32 %t10, 5
  store i32 %t11, ptr %$tmp1.addr, align 4
  br label %exit
exit:
  %t12 = load i64, ptr %Result.addr, align 8
  ret i64 %t12
}

define ptr @"M.Swaps"(ptr %p, ptr %t, ptr %q, ptr %c) #0 {
entry:
  %q.addr = alloca ptr, align 8
  %Result.addr = alloca ptr, align 8
  %$tmp1.addr = alloca ptr, align 8
  store ptr %q, ptr %q.addr, align 8
  %t1 = load ptr, ptr %q.addr, align 8
  %t2 = atomicrmw xchg ptr %p, ptr %t1 seq_cst, align 8
  store ptr %t2, ptr %Result.addr, align 8
  %t3 = load ptr, ptr %Result.addr, align 8
  %t4 = cmpxchg ptr %p, ptr %t3, ptr null seq_cst seq_cst, align 8
  %t5 = extractvalue { ptr, i1 } %t4, 0
  store ptr %t5, ptr %Result.addr, align 8
  %t6 = atomicrmw add ptr %t, i64 8 seq_cst, align 8
  %t7 = add i64 %t6, 8
  %t8 = inttoptr i64 %t7 to ptr
  store ptr %t8, ptr %$tmp1.addr, align 8
  %t9 = atomicrmw xchg ptr %c, i32 7 seq_cst, align 4
  %t10 = cmpxchg ptr %c, i32 7, i32 1 seq_cst seq_cst, align 4
  %t11 = extractvalue { i32, i1 } %t10, 0
  %t12 = add i32 %t9, %t11
  store i32 %t12, ptr %c, align 4
  br label %exit
exit:
  %t13 = load ptr, ptr %Result.addr, align 8
  ret ptr %t13
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
