; ModuleID = 'M'
source_filename = "M.pas"
target triple = "x86_64-pc-windows-msvc"

%"M.Sum.$frame" = type { ptr, i32, ptr }

@.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [9 x i16] } { i32 -1, i32 8, i16 1200, i16 2, i32 0, [9 x i16] [i16 84, i16 73, i16 110, i16 116, i16 70, i16 117, i16 110, i16 99, i16 0] }, align 16
@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [11 x i16] } { i32 -1, i32 10, i16 1200, i16 2, i32 0, [11 x i16] [i16 73, i16 73, i16 110, i16 116, i16 101, i16 114, i16 102, i16 97, i16 99, i16 101, i16 0] }, align 16
@.str.2 = private unnamed_addr constant { i32, i32, i16, i16, i32, [9 x i16] } { i32 -1, i32 8, i16 1200, i16 2, i32 0, [9 x i16] [i16 84, i16 67, i16 111, i16 117, i16 110, i16 116, i16 101, i16 114, i16 0] }, align 16
@.str.3 = private unnamed_addr constant { i32, i32, i16, i16, i32, [10 x i16] } { i32 -1, i32 9, i16 1200, i16 2, i32 0, [10 x i16] [i16 77, i16 97, i16 107, i16 101, i16 65, i16 100, i16 100, i16 101, i16 114, i16 0] }, align 16
@.str.4 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 70, i16 66, i16 97, i16 115, i16 101, i16 0] }, align 16
@.str.5 = private unnamed_addr constant { i32, i32, i16, i16, i32, [8 x i16] } { i32 -1, i32 7, i16 1200, i16 2, i32 0, [8 x i16] [i16 73, i16 110, i16 116, i16 101, i16 103, i16 101, i16 114, i16 0] }, align 16
@.str.6 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 77, i16 0] }, align 16
@.str.7 = private unnamed_addr constant { i32, i32, i16, i16, i32, [20 x i16] } { i32 -1, i32 19, i16 1200, i16 2, i32 0, [20 x i16] [i16 77, i16 97, i16 107, i16 101, i16 65, i16 100, i16 100, i16 101, i16 114, i16 46, i16 36, i16 99, i16 108, i16 111, i16 115, i16 117, i16 114, i16 101, i16 49, i16 0] }, align 16
@.str.8 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 67, i16 111, i16 117, i16 110, i16 116, i16 0] }, align 16
@.str.9 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 83, i16 116, i16 97, i16 114, i16 116, i16 0] }, align 16
@.str.10 = private unnamed_addr constant { i32, i32, i16, i16, i32, [5 x i16] } { i32 -1, i32 4, i16 1200, i16 2, i32 0, [5 x i16] [i16 83, i16 101, i16 108, i16 102, i16 0] }, align 16
@.str.11 = private unnamed_addr constant { i32, i32, i16, i16, i32, [16 x i16] } { i32 -1, i32 15, i16 1200, i16 2, i32 0, [16 x i16] [i16 65, i16 110, i16 111, i16 110, i16 49, i16 46, i16 36, i16 99, i16 108, i16 111, i16 115, i16 117, i16 114, i16 101, i16 50, i16 0] }, align 16
@.str.12 = private unnamed_addr constant { i32, i32, i16, i16, i32, [7 x i16] } { i32 -1, i32 6, i16 1200, i16 2, i32 0, [7 x i16] [i16 36, i16 111, i16 117, i16 116, i16 101, i16 114, i16 0] }, align 16
@.str.13 = private unnamed_addr constant { i32, i32, i16, i16, i32, [10 x i16] } { i32 -1, i32 9, i16 1200, i16 2, i32 0, [10 x i16] [i16 36, i16 111, i16 117, i16 116, i16 101, i16 114, i16 82, i16 101, i16 102, i16 0] }, align 16
@.str.14 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 119, i16 105, i16 99, i16 101, i16 0] }, align 16
@.str.15 = private unnamed_addr constant { i32, i32, i16, i16, i32, [14 x i16] } { i32 -1, i32 13, i16 1200, i16 2, i32 0, [14 x i16] [i16 83, i16 117, i16 109, i16 46, i16 36, i16 99, i16 108, i16 111, i16 115, i16 117, i16 114, i16 101, i16 51, i16 0] }, align 16
@.str.16 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, [6 x i16] [i16 84, i16 111, i16 116, i16 97, i16 108, i16 0] }, align 16
@.str.17 = private unnamed_addr constant { i32, i32, i16, i16, i32, [12 x i16] } { i32 -1, i32 11, i16 1200, i16 2, i32 0, [12 x i16] [i16 83, i16 117, i16 109, i16 46, i16 36, i16 66, i16 108, i16 111, i16 99, i16 107, i16 52, i16 0] }, align 16
@.str.18 = private unnamed_addr constant { i32, i32, i16, i16, i32, [2 x i16] } { i32 -1, i32 1, i16 1200, i16 2, i32 0, [2 x i16] [i16 88, i16 0] }, align 16
@.str.19 = private unnamed_addr constant { i32, i32, i16, i16, i32, [12 x i16] } { i32 -1, i32 11, i16 1200, i16 2, i32 0, [12 x i16] [i16 83, i16 117, i16 109, i16 46, i16 36, i16 66, i16 108, i16 111, i16 99, i16 107, i16 53, i16 0] }, align 16
@"M.TCounter.$rtti.methods" = private unnamed_addr constant [1 x { ptr, ptr, i8, i8, [6 x i8] }] [{ ptr, ptr, i8, i8, [6 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [10 x i16] }, ptr @.str.3, i32 0, i32 5), ptr @"M.TCounter.MakeAdder", i8 2, i8 1, [6 x i8] zeroinitializer }], align 8
@"M.TCounter.$rtti.fields" = private unnamed_addr constant [1 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.4, i32 0, i32 5), i64 8, ptr @"System.Integer.$rtti", i8 2, [7 x i8] zeroinitializer }], align 8
@"M.MakeAdder.$closure1.$intftable" = private unnamed_addr constant { i64, [2 x { [16 x i8], ptr, i64 }] } { i64 2, [2 x { [16 x i8], ptr, i64 }] [{ [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.MakeAdder.$closure1.$imt.M.MakeAdder.$closure1.$I1", i64 32 }, { [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.MakeAdder.$closure1.$imt.M.MakeAdder.$closure1.$I1", i64 32 }] }, align 8
@"M.MakeAdder.$closure1.$rtti.fields" = private unnamed_addr constant [3 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.8, i32 0, i32 5), i64 16, ptr @"System.Integer.$rtti", i8 0, [7 x i8] zeroinitializer }, { ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.9, i32 0, i32 5), i64 20, ptr @"System.Integer.$rtti", i8 0, [7 x i8] zeroinitializer }, { ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.10, i32 0, i32 5), i64 24, ptr @"M.TCounter.$rtti", i8 0, [7 x i8] zeroinitializer }], align 8
@"M.Anon1.$closure2.$inittable" = private unnamed_addr constant { i64, [1 x { ptr, i64 }] } { i64 1, [1 x { ptr, i64 }] [{ ptr, i64 } { ptr @"System.IInterface.$rtti", i64 24 }] }, align 8
@"M.Anon1.$closure2.$intftable" = private unnamed_addr constant { i64, [2 x { [16 x i8], ptr, i64 }] } { i64 2, [2 x { [16 x i8], ptr, i64 }] [{ [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.Anon1.$closure2.$imt.M.Anon1.$closure2.$I1", i64 40 }, { [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.Anon1.$closure2.$imt.M.Anon1.$closure2.$I1", i64 40 }] }, align 8
@"M.Anon1.$closure2.$rtti.fields" = private unnamed_addr constant [3 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.12, i32 0, i32 5), i64 16, ptr @"M.MakeAdder.$closure1.$rtti", i8 0, [7 x i8] zeroinitializer }, { ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [10 x i16] }, ptr @.str.13, i32 0, i32 5), i64 24, ptr @"System.IInterface.$rtti", i8 0, [7 x i8] zeroinitializer }, { ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.14, i32 0, i32 5), i64 32, ptr @"System.Integer.$rtti", i8 0, [7 x i8] zeroinitializer }], align 8
@"M.Sum.$closure3.$intftable" = private unnamed_addr constant { i64, [2 x { [16 x i8], ptr, i64 }] } { i64 2, [2 x { [16 x i8], ptr, i64 }] [{ [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.Sum.$closure3.$imt.M.Sum.$closure3.$I2", i64 24 }, { [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.Sum.$closure3.$imt.M.Sum.$closure3.$I2", i64 24 }] }, align 8
@"M.Sum.$closure3.$rtti.fields" = private unnamed_addr constant [1 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.16, i32 0, i32 5), i64 16, ptr @"System.Integer.$rtti", i8 0, [7 x i8] zeroinitializer }], align 8
@"M.Sum.$Block4.$inittable" = private unnamed_addr constant { i64, [1 x { ptr, i64 }] } { i64 1, [1 x { ptr, i64 }] [{ ptr, i64 } { ptr @"System.IInterface.$rtti", i64 24 }] }, align 8
@"M.Sum.$Block4.$rtti.fields" = private unnamed_addr constant [3 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.12, i32 0, i32 5), i64 16, ptr @"M.Sum.$closure3.$rtti", i8 0, [7 x i8] zeroinitializer }, { ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [10 x i16] }, ptr @.str.13, i32 0, i32 5), i64 24, ptr @"System.IInterface.$rtti", i8 0, [7 x i8] zeroinitializer }, { ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.18, i32 0, i32 5), i64 32, ptr @"System.Integer.$rtti", i8 0, [7 x i8] zeroinitializer }], align 8
@"M.Sum.$Block5.$inittable" = private unnamed_addr constant { i64, [1 x { ptr, i64 }] } { i64 1, [1 x { ptr, i64 }] [{ ptr, i64 } { ptr @"System.IInterface.$rtti", i64 24 }] }, align 8
@"M.Sum.$Block5.$intftable" = private unnamed_addr constant { i64, [2 x { [16 x i8], ptr, i64 }] } { i64 2, [2 x { [16 x i8], ptr, i64 }] [{ [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.Sum.$Block5.$imt.M.Sum.$closure3.$I1", i64 40 }, { [16 x i8], ptr, i64 } { [16 x i8] zeroinitializer, ptr @"M.Sum.$Block5.$imt.M.Sum.$closure3.$I1", i64 40 }] }, align 8
@"M.Sum.$Block5.$rtti.fields" = private unnamed_addr constant [3 x { ptr, i64, ptr, i8, [7 x i8] }] [{ ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [7 x i16] }, ptr @.str.12, i32 0, i32 5), i64 16, ptr @"M.Sum.$Block4.$rtti", i8 0, [7 x i8] zeroinitializer }, { ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [10 x i16] }, ptr @.str.13, i32 0, i32 5), i64 24, ptr @"System.IInterface.$rtti", i8 0, [7 x i8] zeroinitializer }, { ptr, i64, ptr, i8, [7 x i8] } { ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.14, i32 0, i32 5), i64 32, ptr @"System.Integer.$rtti", i8 0, [7 x i8] zeroinitializer }], align 8
$"System.IInterface.$rtti" = comdat any
@"System.IInterface.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, ptr, [16 x i8], i64 } { i8 15, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [11 x i16] }, ptr @.str.1, i32 0, i32 5), ptr null, [16 x i8] zeroinitializer, i64 0 }, comdat, align 8
$"M.TIntFunc.$rtti" = comdat any
@"M.TIntFunc.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, ptr, [16 x i8], i64 } { i8 15, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [9 x i16] }, ptr @.str.0, i32 0, i32 5), ptr @"System.IInterface.$rtti", [16 x i8] zeroinitializer, i64 1 }, comdat, align 8
$"M.$rtti.array[0..3] of TIntFunc" = comdat any
@"M.$rtti.array[0..3] of TIntFunc" = linkonce_odr constant { i8, [7 x i8], ptr, i64, i64, ptr } { i8 13, [7 x i8] zeroinitializer, ptr null, i64 32, i64 4, ptr @"M.TIntFunc.$rtti" }, comdat, align 8
$"System.Integer.$rtti" = comdat any
@"System.Integer.$rtti" = linkonce_odr constant { i8, [7 x i8], ptr, i8, [7 x i8], i64, i64 } { i8 1, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [8 x i16] }, ptr @.str.5, i32 0, i32 5), i8 4, [7 x i8] zeroinitializer, i64 -2147483648, i64 2147483647 }, comdat, align 8
@"M.TCounter.$rtti" = constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [9 x i16] }, ptr @.str.2, i32 0, i32 5), ptr @"M.TCounter.$vmt", ptr @"System.TObject.$rtti", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.6, i32 0, i32 5), i64 0, ptr null, i64 1, ptr @"M.TCounter.$rtti.methods", i64 1, ptr @"M.TCounter.$rtti.fields", i64 0, ptr null }, align 8
@"M.MakeAdder.$closure1.$rtti" = internal constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [20 x i16] }, ptr @.str.7, i32 0, i32 5), ptr @"M.MakeAdder.$closure1.$vmt", ptr @"System.TInterfacedObject.$rtti", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.6, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 3, ptr @"M.MakeAdder.$closure1.$rtti.fields", i64 0, ptr null }, align 8
@"M.Anon1.$closure2.$rtti" = internal constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [16 x i16] }, ptr @.str.11, i32 0, i32 5), ptr @"M.Anon1.$closure2.$vmt", ptr @"System.TInterfacedObject.$rtti", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.6, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 3, ptr @"M.Anon1.$closure2.$rtti.fields", i64 0, ptr null }, align 8
@"M.Sum.$closure3.$rtti" = internal constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [14 x i16] }, ptr @.str.15, i32 0, i32 5), ptr @"M.Sum.$closure3.$vmt", ptr @"System.TInterfacedObject.$rtti", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.6, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 1, ptr @"M.Sum.$closure3.$rtti.fields", i64 0, ptr null }, align 8
@"M.Sum.$Block4.$rtti" = internal constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [12 x i16] }, ptr @.str.17, i32 0, i32 5), ptr @"M.Sum.$Block4.$vmt", ptr @"System.TInterfacedObject.$rtti", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.6, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 3, ptr @"M.Sum.$Block4.$rtti.fields", i64 0, ptr null }, align 8
@"M.Sum.$Block5.$rtti" = internal constant { i8, [7 x i8], ptr, ptr, ptr, ptr, i64, ptr, i64, ptr, i64, ptr, i64, ptr } { i8 6, [7 x i8] zeroinitializer, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [12 x i16] }, ptr @.str.19, i32 0, i32 5), ptr @"M.Sum.$Block5.$vmt", ptr @"System.TInterfacedObject.$rtti", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [2 x i16] }, ptr @.str.6, i32 0, i32 5), i64 0, ptr null, i64 0, ptr null, i64 3, ptr @"M.Sum.$Block5.$rtti.fields", i64 0, ptr null }, align 8
@"M.TCounter.$vmt" = constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr @"System.TObject.$vmt", i64 16, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [9 x i16] }, ptr @.str.2, i32 0, i32 5), ptr @"M.TCounter.$rtti", ptr null, ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.MakeAdder.$closure1.$vmt" = internal constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr @"System.TInterfacedObject.$vmt", i64 40, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [20 x i16] }, ptr @.str.7, i32 0, i32 5), ptr @"M.MakeAdder.$closure1.$rtti", ptr @"M.MakeAdder.$closure1.$intftable", ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.Anon1.$closure2.$vmt" = internal constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr @"System.TInterfacedObject.$vmt", i64 48, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [16 x i16] }, ptr @.str.11, i32 0, i32 5), ptr @"M.Anon1.$closure2.$rtti", ptr @"M.Anon1.$closure2.$intftable", ptr null, ptr @"M.Anon1.$closure2.$inittable", ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.Sum.$closure3.$vmt" = internal constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr @"System.TInterfacedObject.$vmt", i64 32, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [14 x i16] }, ptr @.str.15, i32 0, i32 5), ptr @"M.Sum.$closure3.$rtti", ptr @"M.Sum.$closure3.$intftable", ptr null, ptr null, ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.Sum.$Block4.$vmt" = internal constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr @"System.TInterfacedObject.$vmt", i64 40, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [12 x i16] }, ptr @.str.17, i32 0, i32 5), ptr @"M.Sum.$Block4.$rtti", ptr null, ptr null, ptr @"M.Sum.$Block4.$inittable", ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.Sum.$Block5.$vmt" = internal constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [0 x ptr] } { ptr @"System.TInterfacedObject.$vmt", i64 48, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [12 x i16] }, ptr @.str.19, i32 0, i32 5), ptr @"M.Sum.$Block5.$rtti", ptr @"M.Sum.$Block5.$intftable", ptr null, ptr @"M.Sum.$Block5.$inittable", ptr null, [0 x ptr] zeroinitializer }, align 8
@"M.MakeAdder.$closure1.$imt.M.MakeAdder.$closure1.$I1" = internal constant [1 x ptr] [ptr @"M.MakeAdder.$closure1.$thunk.M.MakeAdder.$closure1.$I1.Invoke"], align 8
@"M.Anon1.$closure2.$imt.M.Anon1.$closure2.$I1" = internal constant [1 x ptr] [ptr @"M.Anon1.$closure2.$thunk.M.Anon1.$closure2.$I1.Invoke"], align 8
@"M.Sum.$closure3.$imt.M.Sum.$closure3.$I2" = internal constant [1 x ptr] [ptr @"M.Sum.$closure3.$thunk.M.Sum.$closure3.$I2.Invoke"], align 8
@"M.Sum.$Block5.$imt.M.Sum.$closure3.$I1" = internal constant [1 x ptr] [ptr @"M.Sum.$Block5.$thunk.M.Sum.$closure3.$I1.Invoke"], align 8
@"M.Keep" = internal global [4 x ptr] zeroinitializer, align 8, !dbg !159
@"System.TInterfacedObject.$vmt" = external global ptr, align 8
@"System.TObject.$vmt" = external global ptr, align 8
@"System.TInterfacedObject.$rtti" = external global ptr, align 8
@"System.TObject.$rtti" = external global ptr, align 8

declare ptr @"System.TObject.Create"(ptr, i8)
declare void @"System._Finalize"(ptr, ptr)
declare void @"System._IntfAsg"(ptr, ptr)
declare void @"System._IntfClr"(ptr)
declare i32 @__C_specific_handler(...)
declare void @llvm.seh.try.begin()
declare void @llvm.seh.try.end()

define void @"M.TCounter.MakeAdder"(ptr %Self, ptr %Result, i32 %Start) #0 personality ptr @__C_specific_handler !dbg !4 {
entry:
  %Self.addr = alloca ptr, align 8, !dbg !18
  %Start.addr = alloca i32, align 4, !dbg !18
  %Count.addr = alloca i32, align 4, !dbg !18
  %$closure.addr = alloca ptr, align 8, !dbg !18
  %$closureRef.addr = alloca ptr, align 8, !dbg !18
  store ptr %Self, ptr %Self.addr, align 8, !dbg !18
  store i32 %Start, ptr %Start.addr, align 4, !dbg !18
  %Result.ref = alloca ptr, align 8, !dbg !18
  store ptr %Result, ptr %Result.ref, align 8, !dbg !18
    #dbg_declare(ptr %Self.addr, !20, !DIExpression(), !18)
    #dbg_declare(ptr %$closure.addr, !21, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 20), !18)
    #dbg_declare(ptr %Result.ref, !22, !DIExpression(DW_OP_deref), !18)
    #dbg_declare(ptr %$closure.addr, !23, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 16), !18)
    #dbg_declare(ptr %$closure.addr, !34, !DIExpression(), !18)
    #dbg_declare(ptr %$closureRef.addr, !35, !DIExpression(), !18)
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad, !dbg !18
cont.1:
  store ptr null, ptr %$closureRef.addr, align 8, !dbg !18
  %t1 = invoke ptr @"System.TObject.Create"(ptr @"M.MakeAdder.$closure1.$vmt", i8 1)
          to label %cont.2 unwind label %cleanup.pad, !dbg !18
cont.2:
  store ptr %t1, ptr %$closure.addr, align 8, !dbg !18
  %t2 = load ptr, ptr %$closure.addr, align 8, !dbg !18
  %t3 = icmp eq ptr %t2, null, !dbg !18
  %t5 = getelementptr inbounds i8, ptr %t2, i64 32, !dbg !18
  %t4 = select i1 %t3, ptr null, ptr %t5, !dbg !18
  invoke void @"System._IntfAsg"(ptr %$closureRef.addr, ptr %t4)
          to label %cont.3 unwind label %cleanup.pad, !dbg !18
cont.3:
  %t6 = load i32, ptr %Start.addr, align 4, !dbg !18
  %t7 = load ptr, ptr %$closure.addr, align 8, !dbg !18
  %t8 = getelementptr inbounds i8, ptr %t7, i64 20, !dbg !18
  store i32 %t6, ptr %t8, align 4, !dbg !18
  %t9 = load ptr, ptr %Self.addr, align 8, !dbg !18
  %t10 = load ptr, ptr %$closure.addr, align 8, !dbg !18
  %t11 = getelementptr inbounds i8, ptr %t10, i64 24, !dbg !18
  store ptr %t9, ptr %t11, align 8, !dbg !18
  %t12 = load ptr, ptr %$closure.addr, align 8, !dbg !36
  %t13 = getelementptr inbounds i8, ptr %t12, i64 16, !dbg !36
  store i32 0, ptr %t13, align 4, !dbg !36
  %t14 = load ptr, ptr %$closure.addr, align 8, !dbg !37
  %t15 = icmp eq ptr %t14, null, !dbg !37
  %t17 = getelementptr inbounds i8, ptr %t14, i64 32, !dbg !37
  %t16 = select i1 %t15, ptr null, ptr %t17, !dbg !37
  invoke void @"System._IntfAsg"(ptr %Result, ptr %t16)
          to label %cont.4 unwind label %cleanup.pad, !dbg !37
cont.4:
  br label %cleanup, !dbg !19
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.5 unwind label %cleanup.pad, !dbg !19
cont.5:
  call void @"System._IntfClr"(ptr %$closureRef.addr), !dbg !19
  br label %exit, !dbg !19
exit:
  ret void, !dbg !19
cleanup.pad:
  %cl = cleanuppad within none [], !dbg !19
  call void @"System._IntfClr"(ptr %$closureRef.addr) [ "funclet"(token %cl) ], !dbg !19
  cleanupret from %cl unwind to caller, !dbg !19
}

define internal i32 @"M.MakeAdder.$closure1.Anon1"(ptr %Self, i32 %Delta) #0 personality ptr @__C_specific_handler !dbg !39 {
entry:
  %Self.addr = alloca ptr, align 8, !dbg !42
  %Delta.addr = alloca i32, align 4, !dbg !42
  %Result.addr = alloca i32, align 4, !dbg !42
  %Twice.addr = alloca i32, align 4, !dbg !42
  %Log.addr = alloca ptr, align 8, !dbg !42
  %$closure.addr = alloca ptr, align 8, !dbg !42
  %$closureRef.addr = alloca ptr, align 8, !dbg !42
  store ptr %Self, ptr %Self.addr, align 8, !dbg !42
  store i32 %Delta, ptr %Delta.addr, align 4, !dbg !42
    #dbg_declare(ptr %Self.addr, !44, !DIExpression(), !42)
    #dbg_declare(ptr %Delta.addr, !45, !DIExpression(), !42)
    #dbg_declare(ptr %Result.addr, !46, !DIExpression(), !42)
    #dbg_declare(ptr %$closure.addr, !47, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 32), !42)
    #dbg_declare(ptr %Log.addr, !48, !DIExpression(), !42)
    #dbg_declare(ptr %$closure.addr, !56, !DIExpression(), !42)
    #dbg_declare(ptr %$closureRef.addr, !57, !DIExpression(), !42)
    #dbg_declare(ptr %Self.addr, !58, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 16), !42)
    #dbg_declare(ptr %Self.addr, !59, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 20), !42)
    #dbg_declare(ptr %Self.addr, !60, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 24), !42)
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad, !dbg !42
cont.1:
  store ptr null, ptr %Log.addr, align 8, !dbg !42
  store ptr null, ptr %$closureRef.addr, align 8, !dbg !42
  %t1 = invoke ptr @"System.TObject.Create"(ptr @"M.Anon1.$closure2.$vmt", i8 1)
          to label %cont.2 unwind label %cleanup.pad, !dbg !42
cont.2:
  store ptr %t1, ptr %$closure.addr, align 8, !dbg !42
  %t2 = load ptr, ptr %$closure.addr, align 8, !dbg !42
  %t3 = icmp eq ptr %t2, null, !dbg !42
  %t5 = getelementptr inbounds i8, ptr %t2, i64 40, !dbg !42
  %t4 = select i1 %t3, ptr null, ptr %t5, !dbg !42
  invoke void @"System._IntfAsg"(ptr %$closureRef.addr, ptr %t4)
          to label %cont.3 unwind label %cleanup.pad, !dbg !42
cont.3:
  %t6 = load ptr, ptr %Self.addr, align 8, !dbg !42
  %t7 = load ptr, ptr %$closure.addr, align 8, !dbg !42
  %t8 = getelementptr inbounds i8, ptr %t7, i64 16, !dbg !42
  store ptr %t6, ptr %t8, align 8, !dbg !42
  %t9 = load ptr, ptr %$closure.addr, align 8, !dbg !42
  %t10 = getelementptr inbounds i8, ptr %t9, i64 24, !dbg !42
  %t11 = load ptr, ptr %Self.addr, align 8, !dbg !42
  %t12 = icmp eq ptr %t11, null, !dbg !42
  %t14 = getelementptr inbounds i8, ptr %t11, i64 32, !dbg !42
  %t13 = select i1 %t12, ptr null, ptr %t14, !dbg !42
  invoke void @"System._IntfAsg"(ptr %t10, ptr %t13)
          to label %cont.4 unwind label %cleanup.pad, !dbg !42
cont.4:
  %t15 = load i32, ptr %Delta.addr, align 4, !dbg !61
  %t16 = mul i32 %t15, 2, !dbg !61
  %t17 = load ptr, ptr %$closure.addr, align 8, !dbg !61
  %t18 = getelementptr inbounds i8, ptr %t17, i64 32, !dbg !61
  store i32 %t16, ptr %t18, align 4, !dbg !61
  %t19 = load ptr, ptr %Self.addr, align 8, !dbg !62
  %t20 = getelementptr inbounds i8, ptr %t19, i64 16, !dbg !62
  %t21 = load i32, ptr %t20, align 4, !dbg !62
  %t22 = add i32 %t21, 1, !dbg !62
  %t23 = load ptr, ptr %Self.addr, align 8, !dbg !62
  %t24 = getelementptr inbounds i8, ptr %t23, i64 16, !dbg !62
  store i32 %t22, ptr %t24, align 4, !dbg !62
  %t25 = load ptr, ptr %Self.addr, align 8, !dbg !63
  %t26 = getelementptr inbounds i8, ptr %t25, i64 20, !dbg !63
  %t27 = load i32, ptr %t26, align 4, !dbg !63
  %t28 = load ptr, ptr %$closure.addr, align 8, !dbg !63
  %t29 = getelementptr inbounds i8, ptr %t28, i64 32, !dbg !63
  %t30 = load i32, ptr %t29, align 4, !dbg !63
  %t31 = add i32 %t27, %t30, !dbg !63
  %t32 = load ptr, ptr %Self.addr, align 8, !dbg !63
  %t33 = getelementptr inbounds i8, ptr %t32, i64 20, !dbg !63
  store i32 %t31, ptr %t33, align 4, !dbg !63
  %t34 = load ptr, ptr %$closure.addr, align 8, !dbg !64
  %t35 = icmp eq ptr %t34, null, !dbg !64
  %t37 = getelementptr inbounds i8, ptr %t34, i64 40, !dbg !64
  %t36 = select i1 %t35, ptr null, ptr %t37, !dbg !64
  invoke void @"System._IntfAsg"(ptr %Log.addr, ptr %t36)
          to label %cont.5 unwind label %cleanup.pad, !dbg !64
cont.5:
  %t38 = load ptr, ptr %Log.addr, align 8, !dbg !65
  %t39 = load ptr, ptr %t38, align 8, !dbg !65
  %t40 = getelementptr inbounds i8, ptr %t39, i64 0, !dbg !65
  %t41 = load ptr, ptr %t40, align 8, !dbg !65
  invoke void %t41(ptr %t38)
          to label %cont.6 unwind label %cleanup.pad, !dbg !65
cont.6:
  %t42 = load ptr, ptr %Self.addr, align 8, !dbg !66
  %t43 = getelementptr inbounds i8, ptr %t42, i64 20, !dbg !66
  %t44 = load i32, ptr %t43, align 4, !dbg !66
  store i32 %t44, ptr %Result.addr, align 4, !dbg !66
  br label %cleanup, !dbg !43
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.7 unwind label %cleanup.pad, !dbg !43
cont.7:
  call void @"System._IntfClr"(ptr %Log.addr), !dbg !43
  call void @"System._IntfClr"(ptr %$closureRef.addr), !dbg !43
  br label %exit, !dbg !43
exit:
  %t45 = load i32, ptr %Result.addr, align 4, !dbg !43
  ret i32 %t45, !dbg !43
cleanup.pad:
  %cl = cleanuppad within none [], !dbg !43
  call void @"System._IntfClr"(ptr %Log.addr) [ "funclet"(token %cl) ], !dbg !43
  call void @"System._IntfClr"(ptr %$closureRef.addr) [ "funclet"(token %cl) ], !dbg !43
  cleanupret from %cl unwind to caller, !dbg !43
}

define internal void @"M.Anon1.$closure2.Anon1"(ptr %Self) #0 !dbg !68 {
entry:
  %Self.addr = alloca ptr, align 8, !dbg !71
  store ptr %Self, ptr %Self.addr, align 8, !dbg !71
  %$frame2 = alloca ptr, align 8, !dbg !71
  %t1 = getelementptr inbounds i8, ptr %Self, i64 16, !dbg !71
  %t2 = load ptr, ptr %t1, align 8, !dbg !71
  store ptr %t2, ptr %$frame2, align 8, !dbg !71
    #dbg_declare(ptr %Self.addr, !73, !DIExpression(), !71)
    #dbg_declare(ptr %Self.addr, !74, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 32), !71)
    #dbg_declare(ptr %$frame2, !75, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 16), !71)
    #dbg_declare(ptr %$frame2, !76, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 20), !71)
    #dbg_declare(ptr %$frame2, !77, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 24), !71)
  %t3 = load ptr, ptr %Self.addr, align 8, !dbg !78
  %t4 = getelementptr inbounds i8, ptr %t3, i64 16, !dbg !78
  %t5 = load ptr, ptr %t4, align 8, !dbg !78
  %t6 = getelementptr inbounds i8, ptr %t5, i64 16, !dbg !78
  %t7 = load i32, ptr %t6, align 4, !dbg !78
  %t8 = load ptr, ptr %Self.addr, align 8, !dbg !78
  %t9 = getelementptr inbounds i8, ptr %t8, i64 16, !dbg !78
  %t10 = load ptr, ptr %t9, align 8, !dbg !78
  %t11 = getelementptr inbounds i8, ptr %t10, i64 24, !dbg !78
  %t12 = load ptr, ptr %t11, align 8, !dbg !78
  %t13 = getelementptr inbounds i8, ptr %t12, i64 8, !dbg !78
  %t14 = load i32, ptr %t13, align 4, !dbg !78
  %t15 = add i32 %t7, %t14, !dbg !78
  %t16 = load ptr, ptr %Self.addr, align 8, !dbg !78
  %t17 = getelementptr inbounds i8, ptr %t16, i64 16, !dbg !78
  %t18 = load ptr, ptr %t17, align 8, !dbg !78
  %t19 = getelementptr inbounds i8, ptr %t18, i64 20, !dbg !78
  %t20 = load i32, ptr %t19, align 4, !dbg !78
  %t21 = add i32 %t15, %t20, !dbg !78
  %t22 = load ptr, ptr %Self.addr, align 8, !dbg !78
  %t23 = getelementptr inbounds i8, ptr %t22, i64 32, !dbg !78
  %t24 = load i32, ptr %t23, align 4, !dbg !78
  %t25 = add i32 %t21, %t24, !dbg !78
  %t26 = load ptr, ptr %Self.addr, align 8, !dbg !78
  %t27 = getelementptr inbounds i8, ptr %t26, i64 16, !dbg !78
  %t28 = load ptr, ptr %t27, align 8, !dbg !78
  %t29 = getelementptr inbounds i8, ptr %t28, i64 16, !dbg !78
  store i32 %t25, ptr %t29, align 4, !dbg !78
  br label %exit, !dbg !72
exit:
  ret void, !dbg !72
}

define void @"M.Sum"(ptr %Result, ptr %Values, i64 %Values$high, i32 %Scale) #0 personality ptr @__C_specific_handler !dbg !80 {
entry:
  %$frame = alloca %"M.Sum.$frame", align 8, !dbg !85
  %Values.addr = alloca ptr, align 8, !dbg !85
  %Values$high.addr = alloca i64, align 8, !dbg !85
  %Total.addr = alloca i32, align 4, !dbg !85
  %$closureRef.addr = alloca ptr, align 8, !dbg !85
  %$block4.addr = alloca ptr, align 8, !dbg !85
  %$block4Ref.addr = alloca ptr, align 8, !dbg !85
  %$i1.addr = alloca i32, align 4, !dbg !85
  %$tmp1.addr = alloca i32, align 4, !dbg !85
  %$tmp2.addr = alloca i32, align 4, !dbg !85
  %$block5.addr = alloca ptr, align 8, !dbg !85
  %$block5Ref.addr = alloca ptr, align 8, !dbg !85
  store ptr null, ptr %$frame, align 8, !dbg !85
  %Scale.addr = getelementptr inbounds %"M.Sum.$frame", ptr %$frame, i32 0, i32 1, !dbg !85
  %$closure.addr = getelementptr inbounds %"M.Sum.$frame", ptr %$frame, i32 0, i32 2, !dbg !85
  store ptr %Values, ptr %Values.addr, align 8, !dbg !85
  store i64 %Values$high, ptr %Values$high.addr, align 8, !dbg !85
  store i32 %Scale, ptr %Scale.addr, align 4, !dbg !85
  %Result.ref = alloca ptr, align 8, !dbg !85
  store ptr %Result, ptr %Result.ref, align 8, !dbg !85
    #dbg_declare(ptr %Values.addr, !87, !DIExpression(), !85)
    #dbg_declare(ptr %Values$high.addr, !88, !DIExpression(), !85)
    #dbg_declare(ptr %$frame, !89, !DIExpression(DW_OP_plus_uconst, 8), !85)
    #dbg_declare(ptr %Result.ref, !90, !DIExpression(DW_OP_deref), !85)
    #dbg_declare(ptr %$frame, !91, !DIExpression(DW_OP_plus_uconst, 16, DW_OP_deref, DW_OP_plus_uconst, 16), !85)
    #dbg_declare(ptr %$frame, !97, !DIExpression(DW_OP_plus_uconst, 16), !85)
    #dbg_declare(ptr %$closureRef.addr, !98, !DIExpression(), !85)
    #dbg_declare(ptr %$block4.addr, !106, !DIExpression(), !85)
    #dbg_declare(ptr %$block4Ref.addr, !107, !DIExpression(), !85)
    #dbg_declare(ptr %$i1.addr, !108, !DIExpression(), !85)
    #dbg_declare(ptr %$block5.addr, !116, !DIExpression(), !85)
    #dbg_declare(ptr %$block5Ref.addr, !117, !DIExpression(), !85)
    #dbg_declare(ptr %$block4.addr, !118, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 32), !85)
    #dbg_declare(ptr %$block5.addr, !119, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 32), !85)
  invoke void @llvm.seh.try.begin()
          to label %cont.1 unwind label %cleanup.pad, !dbg !85
cont.1:
  store ptr null, ptr %$closureRef.addr, align 8, !dbg !85
  store ptr null, ptr %$block4.addr, align 8, !dbg !85
  store ptr null, ptr %$block4Ref.addr, align 8, !dbg !85
  store ptr null, ptr %$block5.addr, align 8, !dbg !85
  store ptr null, ptr %$block5Ref.addr, align 8, !dbg !85
  %t1 = invoke ptr @"System.TObject.Create"(ptr @"M.Sum.$closure3.$vmt", i8 1)
          to label %cont.2 unwind label %cleanup.pad, !dbg !85
cont.2:
  store ptr %t1, ptr %$closure.addr, align 8, !dbg !85
  %t2 = load ptr, ptr %$closure.addr, align 8, !dbg !85
  %t3 = icmp eq ptr %t2, null, !dbg !85
  %t5 = getelementptr inbounds i8, ptr %t2, i64 24, !dbg !85
  %t4 = select i1 %t3, ptr null, ptr %t5, !dbg !85
  invoke void @"System._IntfAsg"(ptr %$closureRef.addr, ptr %t4)
          to label %cont.3 unwind label %cleanup.pad, !dbg !85
cont.3:
  %t6 = load ptr, ptr %$closure.addr, align 8, !dbg !120
  %t7 = getelementptr inbounds i8, ptr %t6, i64 16, !dbg !120
  store i32 0, ptr %t7, align 4, !dbg !120
  store ptr null, ptr %$block4.addr, align 8, !dbg !121
  invoke void @llvm.seh.try.begin()
          to label %cont.4 unwind label %fin.pad.1, !dbg !121
cont.4:
  store i32 0, ptr %$tmp1.addr, align 4, !dbg !121
  %t8 = load i64, ptr %Values$high.addr, align 8, !dbg !121
  %t9 = trunc i64 %t8 to i32, !dbg !121
  store i32 %t9, ptr %$tmp2.addr, align 4, !dbg !121
  %t10 = load i32, ptr %$tmp1.addr, align 4, !dbg !121
  %t11 = load i32, ptr %$tmp2.addr, align 4, !dbg !121
  %t12 = icmp sle i32 %t10, %t11, !dbg !121
  br i1 %t12, label %if.then.2, label %if.end.2, !dbg !121
if.then.2:
  %t13 = load i32, ptr %$tmp1.addr, align 4, !dbg !121
  store i32 %t13, ptr %$i1.addr, align 4, !dbg !121
  br label %for.body.3, !dbg !121
for.body.3:
  %t14 = invoke ptr @"System.TObject.Create"(ptr @"M.Sum.$Block4.$vmt", i8 1)
          to label %cont.5 unwind label %fin.pad.1, !dbg !121
cont.5:
  store ptr %t14, ptr %$block4.addr, align 8, !dbg !121
  %t15 = load ptr, ptr %$block4.addr, align 8, !dbg !121
  %t16 = icmp eq ptr %t15, null, !dbg !121
  %t18 = getelementptr inbounds i8, ptr %t15, i64 8, !dbg !121
  %t17 = select i1 %t16, ptr null, ptr %t18, !dbg !121
  invoke void @"System._IntfAsg"(ptr %$block4Ref.addr, ptr %t17)
          to label %cont.6 unwind label %fin.pad.1, !dbg !121
cont.6:
  %t19 = load ptr, ptr %$closure.addr, align 8, !dbg !121
  %t20 = load ptr, ptr %$block4.addr, align 8, !dbg !121
  %t21 = getelementptr inbounds i8, ptr %t20, i64 16, !dbg !121
  store ptr %t19, ptr %t21, align 8, !dbg !121
  %t22 = load ptr, ptr %$block4.addr, align 8, !dbg !121
  %t23 = getelementptr inbounds i8, ptr %t22, i64 24, !dbg !121
  %t24 = load ptr, ptr %$closure.addr, align 8, !dbg !121
  %t25 = icmp eq ptr %t24, null, !dbg !121
  %t27 = getelementptr inbounds i8, ptr %t24, i64 24, !dbg !121
  %t26 = select i1 %t25, ptr null, ptr %t27, !dbg !121
  invoke void @"System._IntfAsg"(ptr %t23, ptr %t26)
          to label %cont.7 unwind label %fin.pad.1, !dbg !121
cont.7:
  %t28 = load ptr, ptr %Values.addr, align 8, !dbg !121
  %t29 = load i32, ptr %$i1.addr, align 4, !dbg !121
  %t30 = sext i32 %t29 to i64, !dbg !121
  %t31 = getelementptr inbounds i32, ptr %t28, i64 %t30, !dbg !121
  %t32 = load i32, ptr %t31, align 4, !dbg !121
  %t33 = load ptr, ptr %$block4.addr, align 8, !dbg !121
  %t34 = getelementptr inbounds i8, ptr %t33, i64 32, !dbg !121
  store i32 %t32, ptr %t34, align 4, !dbg !121
  %t35 = invoke ptr @"System.TObject.Create"(ptr @"M.Sum.$Block5.$vmt", i8 1)
          to label %cont.8 unwind label %fin.pad.1, !dbg !122
cont.8:
  store ptr %t35, ptr %$block5.addr, align 8, !dbg !122
  %t36 = load ptr, ptr %$block5.addr, align 8, !dbg !122
  %t37 = icmp eq ptr %t36, null, !dbg !122
  %t39 = getelementptr inbounds i8, ptr %t36, i64 40, !dbg !122
  %t38 = select i1 %t37, ptr null, ptr %t39, !dbg !122
  invoke void @"System._IntfAsg"(ptr %$block5Ref.addr, ptr %t38)
          to label %cont.9 unwind label %fin.pad.1, !dbg !122
cont.9:
  %t40 = load ptr, ptr %$block4.addr, align 8, !dbg !122
  %t41 = load ptr, ptr %$block5.addr, align 8, !dbg !122
  %t42 = getelementptr inbounds i8, ptr %t41, i64 16, !dbg !122
  store ptr %t40, ptr %t42, align 8, !dbg !122
  %t43 = load ptr, ptr %$block5.addr, align 8, !dbg !122
  %t44 = getelementptr inbounds i8, ptr %t43, i64 24, !dbg !122
  %t45 = load ptr, ptr %$block4.addr, align 8, !dbg !122
  %t46 = icmp eq ptr %t45, null, !dbg !122
  %t48 = getelementptr inbounds i8, ptr %t45, i64 8, !dbg !122
  %t47 = select i1 %t46, ptr null, ptr %t48, !dbg !122
  invoke void @"System._IntfAsg"(ptr %t44, ptr %t47)
          to label %cont.10 unwind label %fin.pad.1, !dbg !122
cont.10:
  invoke void @llvm.seh.try.begin()
          to label %cont.11 unwind label %fin.pad.4, !dbg !122
cont.11:
  %t49 = load ptr, ptr %$block4.addr, align 8, !dbg !122
  %t50 = getelementptr inbounds i8, ptr %t49, i64 32, !dbg !122
  %t51 = load i32, ptr %t50, align 4, !dbg !122
  %t52 = mul i32 %t51, 2, !dbg !122
  %t53 = load ptr, ptr %$block5.addr, align 8, !dbg !122
  %t54 = getelementptr inbounds i8, ptr %t53, i64 32, !dbg !122
  store i32 %t52, ptr %t54, align 4, !dbg !122
  %t55 = load ptr, ptr %$block5.addr, align 8, !dbg !123
  %t56 = getelementptr inbounds i8, ptr %t55, i64 32, !dbg !123
  %t57 = load i32, ptr %t56, align 4, !dbg !123
  invoke void @"M.Sum.Add"(ptr %$frame, i32 %t57)
          to label %cont.12 unwind label %fin.pad.4, !dbg !123
cont.12:
  %t58 = getelementptr inbounds [4 x ptr], ptr @"M.Keep", i64 0, i64 0, !dbg !124
  %t59 = load ptr, ptr %$block5.addr, align 8, !dbg !124
  %t60 = icmp eq ptr %t59, null, !dbg !124
  %t62 = getelementptr inbounds i8, ptr %t59, i64 40, !dbg !124
  %t61 = select i1 %t60, ptr null, ptr %t62, !dbg !124
  invoke void @"System._IntfAsg"(ptr %t58, ptr %t61)
          to label %cont.13 unwind label %fin.pad.4, !dbg !124
cont.13:
  invoke void @llvm.seh.try.end()
          to label %cont.14 unwind label %fin.pad.4, !dbg !122
cont.14:
  invoke void @"System._IntfClr"(ptr %$block5Ref.addr)
          to label %cont.15 unwind label %fin.pad.1, !dbg !122
cont.15:
  br label %try.end.4, !dbg !122
fin.pad.4:
  %cl.4 = cleanuppad within none [], !dbg !122
  invoke void @"System._IntfClr"(ptr %$block5Ref.addr) [ "funclet"(token %cl.4) ]
          to label %cont.16 unwind label %fin.pad.1, !dbg !122
cont.16:
  cleanupret from %cl.4 unwind label %fin.pad.1, !dbg !122
try.end.4:
  br label %for.inc.3, !dbg !121
for.inc.3:
  %t63 = load i32, ptr %$i1.addr, align 4, !dbg !121
  %t64 = load i32, ptr %$tmp2.addr, align 4, !dbg !121
  %t65 = icmp eq i32 %t63, %t64, !dbg !121
  br i1 %t65, label %for.end.3, label %if.end.5, !dbg !121
if.end.5:
  %t66 = load i32, ptr %$i1.addr, align 4, !dbg !121
  %t67 = add i32 %t66, 1, !dbg !121
  store i32 %t67, ptr %$i1.addr, align 4, !dbg !121
  br label %for.body.3, !dbg !121
for.end.3:
  br label %if.end.2, !dbg !121
if.end.2:
  invoke void @llvm.seh.try.end()
          to label %cont.17 unwind label %fin.pad.1, !dbg !121
cont.17:
  invoke void @"System._IntfClr"(ptr %$block4Ref.addr)
          to label %cont.18 unwind label %cleanup.pad, !dbg !121
cont.18:
  br label %try.end.1, !dbg !121
fin.pad.1:
  %cl.1 = cleanuppad within none [], !dbg !121
  invoke void @"System._IntfClr"(ptr %$block4Ref.addr) [ "funclet"(token %cl.1) ]
          to label %cont.19 unwind label %cleanup.pad, !dbg !121
cont.19:
  cleanupret from %cl.1 unwind label %cleanup.pad, !dbg !121
try.end.1:
  %t68 = load ptr, ptr %$closure.addr, align 8, !dbg !125
  %t69 = icmp eq ptr %t68, null, !dbg !125
  %t71 = getelementptr inbounds i8, ptr %t68, i64 24, !dbg !125
  %t70 = select i1 %t69, ptr null, ptr %t71, !dbg !125
  invoke void @"System._IntfAsg"(ptr %Result, ptr %t70)
          to label %cont.20 unwind label %cleanup.pad, !dbg !125
cont.20:
  br label %cleanup, !dbg !86
cleanup:
  invoke void @llvm.seh.try.end()
          to label %cont.21 unwind label %cleanup.pad, !dbg !86
cont.21:
  call void @"System._IntfClr"(ptr %$closureRef.addr), !dbg !86
  br label %exit, !dbg !86
exit:
  ret void, !dbg !86
cleanup.pad:
  %cl = cleanuppad within none [], !dbg !86
  call void @"System._IntfClr"(ptr %$closureRef.addr) [ "funclet"(token %cl) ], !dbg !86
  cleanupret from %cl unwind to caller, !dbg !86
}

define internal void @"M.Sum.Add"(ptr %$link, i32 %V) #0 !dbg !127 {
entry:
  %V.addr = alloca i32, align 4, !dbg !130
  store i32 %V, ptr %V.addr, align 4, !dbg !130
    #dbg_declare(ptr %V.addr, !132, !DIExpression(), !130)
  %t1 = getelementptr inbounds %"M.Sum.$frame", ptr %$link, i32 0, i32 2, !dbg !133
  %t2 = load ptr, ptr %t1, align 8, !dbg !133
  %t3 = getelementptr inbounds i8, ptr %t2, i64 16, !dbg !133
  %t4 = load i32, ptr %t3, align 4, !dbg !133
  %t5 = load i32, ptr %V.addr, align 4, !dbg !133
  %t6 = getelementptr inbounds %"M.Sum.$frame", ptr %$link, i32 0, i32 1, !dbg !133
  %t7 = load i32, ptr %t6, align 4, !dbg !133
  %t8 = mul i32 %t5, %t7, !dbg !133
  %t9 = add i32 %t4, %t8, !dbg !133
  %t10 = getelementptr inbounds %"M.Sum.$frame", ptr %$link, i32 0, i32 2, !dbg !133
  %t11 = load ptr, ptr %t10, align 8, !dbg !133
  %t12 = getelementptr inbounds i8, ptr %t11, i64 16, !dbg !133
  store i32 %t9, ptr %t12, align 4, !dbg !133
  br label %exit, !dbg !131
exit:
  ret void, !dbg !131
}

define internal i32 @"M.Sum.$Block5.Anon1"(ptr %Self, i32 %Delta) #0 !dbg !135 {
entry:
  %Self.addr = alloca ptr, align 8, !dbg !138
  %Delta.addr = alloca i32, align 4, !dbg !138
  %Result.addr = alloca i32, align 4, !dbg !138
  store ptr %Self, ptr %Self.addr, align 8, !dbg !138
  store i32 %Delta, ptr %Delta.addr, align 4, !dbg !138
  %$frame2 = alloca ptr, align 8, !dbg !138
  %t1 = getelementptr inbounds i8, ptr %Self, i64 16, !dbg !138
  %t2 = load ptr, ptr %t1, align 8, !dbg !138
  store ptr %t2, ptr %$frame2, align 8, !dbg !138
  %$frame3 = alloca ptr, align 8, !dbg !138
  %t3 = getelementptr inbounds i8, ptr %t2, i64 16, !dbg !138
  %t4 = load ptr, ptr %t3, align 8, !dbg !138
  store ptr %t4, ptr %$frame3, align 8, !dbg !138
    #dbg_declare(ptr %Self.addr, !140, !DIExpression(), !138)
    #dbg_declare(ptr %Delta.addr, !141, !DIExpression(), !138)
    #dbg_declare(ptr %Result.addr, !142, !DIExpression(), !138)
    #dbg_declare(ptr %Self.addr, !143, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 32), !138)
    #dbg_declare(ptr %$frame2, !144, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 32), !138)
    #dbg_declare(ptr %$frame3, !145, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 16), !138)
  %t5 = load ptr, ptr %Self.addr, align 8, !dbg !146
  %t6 = getelementptr inbounds i8, ptr %t5, i64 16, !dbg !146
  %t7 = load ptr, ptr %t6, align 8, !dbg !146
  %t8 = getelementptr inbounds i8, ptr %t7, i64 32, !dbg !146
  %t9 = load i32, ptr %t8, align 4, !dbg !146
  %t10 = load ptr, ptr %Self.addr, align 8, !dbg !146
  %t11 = getelementptr inbounds i8, ptr %t10, i64 32, !dbg !146
  %t12 = load i32, ptr %t11, align 4, !dbg !146
  %t13 = add i32 %t9, %t12, !dbg !146
  %t14 = load i32, ptr %Delta.addr, align 4, !dbg !146
  %t15 = add i32 %t13, %t14, !dbg !146
  %t16 = load ptr, ptr %Self.addr, align 8, !dbg !146
  %t17 = getelementptr inbounds i8, ptr %t16, i64 16, !dbg !146
  %t18 = load ptr, ptr %t17, align 8, !dbg !146
  %t19 = getelementptr inbounds i8, ptr %t18, i64 16, !dbg !146
  %t20 = load ptr, ptr %t19, align 8, !dbg !146
  %t21 = getelementptr inbounds i8, ptr %t20, i64 16, !dbg !146
  %t22 = load i32, ptr %t21, align 4, !dbg !146
  %t23 = add i32 %t15, %t22, !dbg !146
  store i32 %t23, ptr %Result.addr, align 4, !dbg !146
  br label %exit, !dbg !139
exit:
  %t24 = load i32, ptr %Result.addr, align 4, !dbg !139
  ret i32 %t24, !dbg !139
}

define internal i32 @"M.Sum.$closure3.Anon2"(ptr %Self, i32 %Delta) #0 !dbg !148 {
entry:
  %Self.addr = alloca ptr, align 8, !dbg !151
  %Delta.addr = alloca i32, align 4, !dbg !151
  %Result.addr = alloca i32, align 4, !dbg !151
  store ptr %Self, ptr %Self.addr, align 8, !dbg !151
  store i32 %Delta, ptr %Delta.addr, align 4, !dbg !151
    #dbg_declare(ptr %Self.addr, !153, !DIExpression(), !151)
    #dbg_declare(ptr %Delta.addr, !154, !DIExpression(), !151)
    #dbg_declare(ptr %Result.addr, !155, !DIExpression(), !151)
    #dbg_declare(ptr %Self.addr, !156, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, 16), !151)
  %t1 = load ptr, ptr %Self.addr, align 8, !dbg !157
  %t2 = getelementptr inbounds i8, ptr %t1, i64 16, !dbg !157
  %t3 = load i32, ptr %t2, align 4, !dbg !157
  %t4 = load i32, ptr %Delta.addr, align 4, !dbg !157
  %t5 = add i32 %t3, %t4, !dbg !157
  store i32 %t5, ptr %Result.addr, align 4, !dbg !157
  br label %exit, !dbg !152
exit:
  %t6 = load i32, ptr %Result.addr, align 4, !dbg !152
  ret i32 %t6, !dbg !152
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
  call void @"System._Finalize"(ptr @"M.Keep", ptr @"M.$rtti.array[0..3] of TIntFunc")
  br label %exit
exit:
  ret void
cleanup.pad:
  %cl = cleanuppad within none []
  call void @"System._Finalize"(ptr @"M.Keep", ptr @"M.$rtti.array[0..3] of TIntFunc") [ "funclet"(token %cl) ]
  cleanupret from %cl unwind to caller
}

define internal i32 @"M.MakeAdder.$closure1.$thunk.M.MakeAdder.$closure1.$I1.Invoke"(ptr %iref, i32 %p1) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -32
  %r = musttail call i32 @"M.MakeAdder.$closure1.Anon1"(ptr %self, i32 %p1)
  ret i32 %r
}

define internal void @"M.Anon1.$closure2.$thunk.M.Anon1.$closure2.$I1.Invoke"(ptr %iref) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -40
  musttail call void @"M.Anon1.$closure2.Anon1"(ptr %self)
  ret void
}

define internal i32 @"M.Sum.$closure3.$thunk.M.Sum.$closure3.$I2.Invoke"(ptr %iref, i32 %p1) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -24
  %r = musttail call i32 @"M.Sum.$closure3.Anon2"(ptr %self, i32 %p1)
  ret i32 %r
}

define internal i32 @"M.Sum.$Block5.$thunk.M.Sum.$closure3.$I1.Invoke"(ptr %iref, i32 %p1) #0 {
entry:
  %self = getelementptr inbounds i8, ptr %iref, i64 -40
  %r = musttail call i32 @"M.Sum.$Block5.Anon1"(ptr %self, i32 %p1)
  ret i32 %r
}

attributes #0 = { uwtable "frame-pointer"="all" }

!llvm.dbg.cu = !{!0}
!llvm.module.flags = !{!1, !2, !164}
!0 = distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !3, producer: "FreexComp opc 0.1.0", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug, globals: !165, splitDebugInlining: false, nameTableKind: None)
!1 = !{i32 2, !"Debug Info Version", i32 3}
!2 = !{i32 2, !"CodeView", i32 1}
!3 = !DIFile(filename: "M.pas", directory: "C:\5Cp", checksumkind: CSK_MD5, checksum: "b24e24caa8add4a9b1f8eace52aa4513")
!4 = distinct !DISubprogram(name: "TCounter.MakeAdder", linkageName: "M.TCounter.MakeAdder", scope: !3, file: !3, line: 14, type: !17, scopeLine: 17, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !38)
!5 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !6, size: 64)
!6 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "TCounter", size: 128, elements: !14)
!7 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "TObject", size: 64, elements: !10)
!8 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: null, size: 64)
!9 = !DIDerivedType(tag: DW_TAG_member, name: "$vmt", scope: !7, baseType: !8, size: 64, offset: 0)
!10 = !{!9}
!11 = !DIDerivedType(tag: DW_TAG_inheritance, scope: !6, baseType: !7, offset: 0)
!12 = !DIBasicType(name: "Integer", size: 32, encoding: DW_ATE_signed)
!13 = !DIDerivedType(tag: DW_TAG_member, name: "FBase", scope: !6, baseType: !12, size: 32, offset: 64)
!14 = !{!11, !13}
!15 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !8, size: 64)
!16 = !{null, !5, !15, !12}
!17 = !DISubroutineType(types: !16)
!18 = !DILocation(line: 17, column: 1, scope: !4)
!19 = !DILocation(line: 34, column: 1, scope: !4)
!20 = !DILocalVariable(name: "Self", arg: 1, scope: !4, file: !3, line: 14, type: !5, flags: DIFlagArtificial | DIFlagObjectPointer)
!21 = !DILocalVariable(name: "Start", arg: 3, scope: !4, file: !3, line: 14, type: !12)
!22 = !DILocalVariable(name: "Result", scope: !4, file: !3, line: 14, type: !8)
!23 = !DILocalVariable(name: "Count", scope: !4, file: !3, line: 16, type: !12)
!24 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !25, size: 64)
!25 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "MakeAdder.$closure1", size: 320, elements: !33)
!26 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "TInterfacedObject", size: 128, elements: !28)
!27 = !DIDerivedType(tag: DW_TAG_inheritance, scope: !26, baseType: !7, offset: 0)
!28 = !{!27}
!29 = !DIDerivedType(tag: DW_TAG_inheritance, scope: !25, baseType: !26, offset: 0)
!30 = !DIDerivedType(tag: DW_TAG_member, name: "Count", scope: !25, baseType: !12, size: 32, offset: 128)
!31 = !DIDerivedType(tag: DW_TAG_member, name: "Start", scope: !25, baseType: !12, size: 32, offset: 160)
!32 = !DIDerivedType(tag: DW_TAG_member, name: "Self", scope: !25, baseType: !5, size: 64, offset: 192)
!33 = !{!29, !30, !31, !32}
!34 = !DILocalVariable(name: "$closure", scope: !4, file: !3, line: 14, type: !24, flags: DIFlagArtificial)
!35 = !DILocalVariable(name: "$closureRef", scope: !4, file: !3, line: 14, type: !8, flags: DIFlagArtificial)
!36 = !DILocation(line: 18, column: 3, scope: !4)
!37 = !DILocation(line: 19, column: 3, scope: !4)
!38 = !{!20, !21, !22, !23, !34, !35}
!39 = distinct !DISubprogram(name: "MakeAdder.$closure1.Anon1", linkageName: "M.MakeAdder.$closure1.Anon1", scope: !3, file: !3, line: 19, type: !41, scopeLine: 23, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0, retainedNodes: !67)
!40 = !{!12, !24, !12}
!41 = !DISubroutineType(types: !40)
!42 = !DILocation(line: 23, column: 5, scope: !39)
!43 = !DILocation(line: 33, column: 5, scope: !39)
!44 = !DILocalVariable(name: "$frame", arg: 1, scope: !39, file: !3, line: 19, type: !24, flags: DIFlagArtificial | DIFlagObjectPointer)
!45 = !DILocalVariable(name: "Delta", arg: 2, scope: !39, file: !3, line: 19, type: !12)
!46 = !DILocalVariable(name: "Result", scope: !39, file: !3, line: 19, type: !12)
!47 = !DILocalVariable(name: "Twice", scope: !39, file: !3, line: 21, type: !12)
!48 = !DILocalVariable(name: "Log", scope: !39, file: !3, line: 22, type: !8)
!49 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !50, size: 64)
!50 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "Anon1.$closure2", size: 384, elements: !55)
!51 = !DIDerivedType(tag: DW_TAG_inheritance, scope: !50, baseType: !26, offset: 0)
!52 = !DIDerivedType(tag: DW_TAG_member, name: "$outer", scope: !50, baseType: !24, size: 64, offset: 128)
!53 = !DIDerivedType(tag: DW_TAG_member, name: "$outerRef", scope: !50, baseType: !8, size: 64, offset: 192)
!54 = !DIDerivedType(tag: DW_TAG_member, name: "Twice", scope: !50, baseType: !12, size: 32, offset: 256)
!55 = !{!51, !52, !53, !54}
!56 = !DILocalVariable(name: "$closure", scope: !39, file: !3, line: 19, type: !49, flags: DIFlagArtificial)
!57 = !DILocalVariable(name: "$closureRef", scope: !39, file: !3, line: 19, type: !8, flags: DIFlagArtificial)
!58 = !DILocalVariable(name: "Count", scope: !39, file: !3, line: 16, type: !12)
!59 = !DILocalVariable(name: "Start", scope: !39, file: !3, line: 14, type: !12)
!60 = !DILocalVariable(name: "Self", scope: !39, file: !3, line: 14, type: !5, flags: DIFlagArtificial)
!61 = !DILocation(line: 24, column: 7, scope: !39)
!62 = !DILocation(line: 25, column: 7, scope: !39)
!63 = !DILocation(line: 26, column: 7, scope: !39)
!64 = !DILocation(line: 27, column: 7, scope: !39)
!65 = !DILocation(line: 31, column: 7, scope: !39)
!66 = !DILocation(line: 32, column: 7, scope: !39)
!67 = !{!44, !45, !46, !47, !48, !56, !57, !58, !59, !60}
!68 = distinct !DISubprogram(name: "Anon1.$closure2.Anon1", linkageName: "M.Anon1.$closure2.Anon1", scope: !3, file: !3, line: 27, type: !70, scopeLine: 28, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0, retainedNodes: !79)
!69 = !{null, !49}
!70 = !DISubroutineType(types: !69)
!71 = !DILocation(line: 28, column: 9, scope: !68)
!72 = !DILocation(line: 30, column: 9, scope: !68)
!73 = !DILocalVariable(name: "$frame", arg: 1, scope: !68, file: !3, line: 27, type: !49, flags: DIFlagArtificial | DIFlagObjectPointer)
!74 = !DILocalVariable(name: "Twice", scope: !68, file: !3, line: 21, type: !12)
!75 = !DILocalVariable(name: "Count", scope: !68, file: !3, line: 16, type: !12)
!76 = !DILocalVariable(name: "Start", scope: !68, file: !3, line: 14, type: !12)
!77 = !DILocalVariable(name: "Self", scope: !68, file: !3, line: 14, type: !5, flags: DIFlagArtificial)
!78 = !DILocation(line: 29, column: 11, scope: !68)
!79 = !{!73, !74, !75, !76, !77}
!80 = distinct !DISubprogram(name: "Sum", linkageName: "M.Sum", scope: !3, file: !3, line: 36, type: !84, scopeLine: 43, spFlags: DISPFlagDefinition, unit: !0, retainedNodes: !126)
!81 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !12, size: 64)
!82 = !DIBasicType(name: "Int64", size: 64, encoding: DW_ATE_signed)
!83 = !{null, !15, !81, !82, !12}
!84 = !DISubroutineType(types: !83)
!85 = !DILocation(line: 43, column: 1, scope: !80)
!86 = !DILocation(line: 52, column: 1, scope: !80)
!87 = !DILocalVariable(name: "Values", arg: 2, scope: !80, file: !3, line: 36, type: !81)
!88 = !DILocalVariable(name: "Values$high", arg: 3, scope: !80, file: !3, line: 36, type: !82, flags: DIFlagArtificial)
!89 = !DILocalVariable(name: "Scale", arg: 4, scope: !80, file: !3, line: 36, type: !12)
!90 = !DILocalVariable(name: "Result", scope: !80, file: !3, line: 36, type: !8)
!91 = !DILocalVariable(name: "Total", scope: !80, file: !3, line: 38, type: !12)
!92 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !93, size: 64)
!93 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "Sum.$closure3", size: 256, elements: !96)
!94 = !DIDerivedType(tag: DW_TAG_inheritance, scope: !93, baseType: !26, offset: 0)
!95 = !DIDerivedType(tag: DW_TAG_member, name: "Total", scope: !93, baseType: !12, size: 32, offset: 128)
!96 = !{!94, !95}
!97 = !DILocalVariable(name: "$closure", scope: !80, file: !3, line: 36, type: !92, flags: DIFlagArtificial)
!98 = !DILocalVariable(name: "$closureRef", scope: !80, file: !3, line: 36, type: !8, flags: DIFlagArtificial)
!99 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !100, size: 64)
!100 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "Sum.$Block4", size: 320, elements: !105)
!101 = !DIDerivedType(tag: DW_TAG_inheritance, scope: !100, baseType: !26, offset: 0)
!102 = !DIDerivedType(tag: DW_TAG_member, name: "$outer", scope: !100, baseType: !92, size: 64, offset: 128)
!103 = !DIDerivedType(tag: DW_TAG_member, name: "$outerRef", scope: !100, baseType: !8, size: 64, offset: 192)
!104 = !DIDerivedType(tag: DW_TAG_member, name: "X", scope: !100, baseType: !12, size: 32, offset: 256)
!105 = !{!101, !102, !103, !104}
!106 = !DILocalVariable(name: "$block4", scope: !80, file: !3, line: 36, type: !99, flags: DIFlagArtificial)
!107 = !DILocalVariable(name: "$block4Ref", scope: !80, file: !3, line: 36, type: !8, flags: DIFlagArtificial)
!108 = !DILocalVariable(name: "$i1", scope: !80, file: !3, line: 36, type: !12, flags: DIFlagArtificial)
!109 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !110, size: 64)
!110 = distinct !DICompositeType(tag: DW_TAG_structure_type, name: "Sum.$Block5", size: 384, elements: !115)
!111 = !DIDerivedType(tag: DW_TAG_inheritance, scope: !110, baseType: !26, offset: 0)
!112 = !DIDerivedType(tag: DW_TAG_member, name: "$outer", scope: !110, baseType: !99, size: 64, offset: 128)
!113 = !DIDerivedType(tag: DW_TAG_member, name: "$outerRef", scope: !110, baseType: !8, size: 64, offset: 192)
!114 = !DIDerivedType(tag: DW_TAG_member, name: "Twice", scope: !110, baseType: !12, size: 32, offset: 256)
!115 = !{!111, !112, !113, !114}
!116 = !DILocalVariable(name: "$block5", scope: !80, file: !3, line: 36, type: !109, flags: DIFlagArtificial)
!117 = !DILocalVariable(name: "$block5Ref", scope: !80, file: !3, line: 36, type: !8, flags: DIFlagArtificial)
!118 = !DILocalVariable(name: "X", scope: !80, file: !3, line: 45, type: !12)
!119 = !DILocalVariable(name: "Twice", scope: !80, file: !3, line: 47, type: !12)
!120 = !DILocation(line: 44, column: 3, scope: !80)
!121 = !DILocation(line: 45, column: 3, scope: !80)
!122 = !DILocation(line: 47, column: 5, scope: !80)
!123 = !DILocation(line: 48, column: 5, scope: !80)
!124 = !DILocation(line: 49, column: 5, scope: !80)
!125 = !DILocation(line: 51, column: 3, scope: !80)
!126 = !{!87, !88, !89, !90, !91, !97, !98, !106, !107, !108, !116, !117, !118, !119}
!127 = distinct !DISubprogram(name: "Sum.Add", linkageName: "M.Sum.Add", scope: !80, file: !3, line: 39, type: !129, scopeLine: 40, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0, retainedNodes: !134)
!128 = !{null, !8, !12}
!129 = !DISubroutineType(types: !128)
!130 = !DILocation(line: 40, column: 3, scope: !127)
!131 = !DILocation(line: 42, column: 3, scope: !127)
!132 = !DILocalVariable(name: "V", arg: 2, scope: !127, file: !3, line: 39, type: !12)
!133 = !DILocation(line: 41, column: 5, scope: !127)
!134 = !{!132}
!135 = distinct !DISubprogram(name: "Sum.$Block5.Anon1", linkageName: "M.Sum.$Block5.Anon1", scope: !3, file: !3, line: 49, type: !137, scopeLine: 49, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0, retainedNodes: !147)
!136 = !{!12, !109, !12}
!137 = !DISubroutineType(types: !136)
!138 = !DILocation(line: 49, column: 50, scope: !135)
!139 = !DILocation(line: 49, column: 92, scope: !135)
!140 = !DILocalVariable(name: "$frame", arg: 1, scope: !135, file: !3, line: 49, type: !109, flags: DIFlagArtificial | DIFlagObjectPointer)
!141 = !DILocalVariable(name: "Delta", arg: 2, scope: !135, file: !3, line: 49, type: !12)
!142 = !DILocalVariable(name: "Result", scope: !135, file: !3, line: 49, type: !12)
!143 = !DILocalVariable(name: "Twice", scope: !135, file: !3, line: 47, type: !12)
!144 = !DILocalVariable(name: "X", scope: !135, file: !3, line: 45, type: !12)
!145 = !DILocalVariable(name: "Total", scope: !135, file: !3, line: 38, type: !12)
!146 = !DILocation(line: 49, column: 56, scope: !135)
!147 = !{!140, !141, !142, !143, !144, !145}
!148 = distinct !DISubprogram(name: "Sum.$closure3.Anon2", linkageName: "M.Sum.$closure3.Anon2", scope: !3, file: !3, line: 51, type: !150, scopeLine: 51, spFlags: DISPFlagDefinition | DISPFlagLocalToUnit, unit: !0, retainedNodes: !158)
!149 = !{!12, !92, !12}
!150 = !DISubroutineType(types: !149)
!151 = !DILocation(line: 51, column: 47, scope: !148)
!152 = !DILocation(line: 51, column: 77, scope: !148)
!153 = !DILocalVariable(name: "$frame", arg: 1, scope: !148, file: !3, line: 51, type: !92, flags: DIFlagArtificial | DIFlagObjectPointer)
!154 = !DILocalVariable(name: "Delta", arg: 2, scope: !148, file: !3, line: 51, type: !12)
!155 = !DILocalVariable(name: "Result", scope: !148, file: !3, line: 51, type: !12)
!156 = !DILocalVariable(name: "Total", scope: !148, file: !3, line: 38, type: !12)
!157 = !DILocation(line: 51, column: 53, scope: !148)
!158 = !{!153, !154, !155, !156}
!159 = !DIGlobalVariableExpression(var: !163, expr: !DIExpression())
!160 = !DICompositeType(tag: DW_TAG_array_type, baseType: !8, size: 256, elements: !162)
!161 = !DISubrange(count: 4, lowerBound: 0)
!162 = !{!161}
!163 = distinct !DIGlobalVariable(name: "Keep", linkageName: "M.Keep", scope: !0, file: !3, line: 12, type: !160, isLocal: true, isDefinition: true)
!164 = !{i32 2, !"eh-asynch", i32 1}
!165 = !{!159}
