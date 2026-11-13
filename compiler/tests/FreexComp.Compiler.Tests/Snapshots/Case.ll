; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

define i32 @"M.Classify"(i32 %x) #0 {
entry:
  %x.addr = alloca i32, align 4
  %Result.addr = alloca i32, align 4
  store i32 %x, ptr %x.addr, align 4
  %t1 = load i32, ptr %x.addr, align 4
  %t2 = sub i32 %t1, 1000
  %t3 = icmp ule i32 %t2, 99000
  br i1 %t3, label %case.arm.1.4, label %case.range.1.1
case.range.1.1:
  switch i32 %t1, label %case.else.1 [
    i32 1, label %case.arm.1.1
    i32 2, label %case.arm.1.2
    i32 4, label %case.arm.1.2
    i32 5, label %case.arm.1.3
    i32 6, label %case.arm.1.3
    i32 7, label %case.arm.1.3
    i32 -3, label %case.arm.1.4
  ]
case.arm.1.1:
  store i32 10, ptr %Result.addr, align 4
  br label %case.end.1
case.arm.1.2:
  store i32 20, ptr %Result.addr, align 4
  br label %case.end.1
case.arm.1.3:
  store i32 30, ptr %Result.addr, align 4
  br label %case.end.1
case.arm.1.4:
  store i32 40, ptr %Result.addr, align 4
  br label %case.end.1
case.else.1:
  store i32 0, ptr %Result.addr, align 4
  br label %case.end.1
case.end.1:
  br label %exit
exit:
  %t4 = load i32, ptr %Result.addr, align 4
  ret i32 %t4
}

define i8 @"M.Letter"(i16 %c) #0 {
entry:
  %c.addr = alloca i16, align 2
  %Result.addr = alloca i8, align 1
  store i16 %c, ptr %c.addr, align 2
  store i8 0, ptr %Result.addr, align 1
  %t1 = load i16, ptr %c.addr, align 2
  %t2 = zext i16 %t1 to i32
  switch i32 %t2, label %case.end.1 [
    i32 97, label %case.arm.1.1
    i32 98, label %case.arm.1.1
    i32 99, label %case.arm.1.1
    i32 120, label %case.arm.1.1
  ]
case.arm.1.1:
  store i8 1, ptr %Result.addr, align 1
  br label %case.end.1
case.end.1:
  br label %exit
exit:
  %t3 = load i8, ptr %Result.addr, align 1
  ret i8 %t3
}

define void @"M.NoElse"(ptr %x) #0 {
entry:
  %t1 = load i8, ptr %x, align 1
  %t2 = zext i8 %t1 to i32
  switch i32 %t2, label %case.end.1 [
    i32 0, label %case.arm.1.1
    i32 200, label %case.arm.1.2
    i32 201, label %case.arm.1.2
    i32 202, label %case.arm.1.2
    i32 203, label %case.arm.1.2
    i32 204, label %case.arm.1.2
    i32 205, label %case.arm.1.2
    i32 206, label %case.arm.1.2
    i32 207, label %case.arm.1.2
    i32 208, label %case.arm.1.2
    i32 209, label %case.arm.1.2
    i32 210, label %case.arm.1.2
    i32 211, label %case.arm.1.2
    i32 212, label %case.arm.1.2
    i32 213, label %case.arm.1.2
    i32 214, label %case.arm.1.2
    i32 215, label %case.arm.1.2
    i32 216, label %case.arm.1.2
    i32 217, label %case.arm.1.2
    i32 218, label %case.arm.1.2
    i32 219, label %case.arm.1.2
    i32 220, label %case.arm.1.2
    i32 221, label %case.arm.1.2
    i32 222, label %case.arm.1.2
    i32 223, label %case.arm.1.2
    i32 224, label %case.arm.1.2
    i32 225, label %case.arm.1.2
    i32 226, label %case.arm.1.2
    i32 227, label %case.arm.1.2
    i32 228, label %case.arm.1.2
    i32 229, label %case.arm.1.2
    i32 230, label %case.arm.1.2
    i32 231, label %case.arm.1.2
    i32 232, label %case.arm.1.2
    i32 233, label %case.arm.1.2
    i32 234, label %case.arm.1.2
    i32 235, label %case.arm.1.2
    i32 236, label %case.arm.1.2
    i32 237, label %case.arm.1.2
    i32 238, label %case.arm.1.2
    i32 239, label %case.arm.1.2
    i32 240, label %case.arm.1.2
    i32 241, label %case.arm.1.2
    i32 242, label %case.arm.1.2
    i32 243, label %case.arm.1.2
    i32 244, label %case.arm.1.2
    i32 245, label %case.arm.1.2
    i32 246, label %case.arm.1.2
    i32 247, label %case.arm.1.2
    i32 248, label %case.arm.1.2
    i32 249, label %case.arm.1.2
    i32 250, label %case.arm.1.2
    i32 251, label %case.arm.1.2
    i32 252, label %case.arm.1.2
    i32 253, label %case.arm.1.2
    i32 254, label %case.arm.1.2
    i32 255, label %case.arm.1.2
  ]
case.arm.1.1:
  store i8 1, ptr %x, align 1
  br label %case.end.1
case.arm.1.2:
  br label %exit
case.end.1:
  store i8 2, ptr %x, align 1
  br label %exit
exit:
  ret void
}

attributes #0 = { uwtable "frame-pointer"="all" }
