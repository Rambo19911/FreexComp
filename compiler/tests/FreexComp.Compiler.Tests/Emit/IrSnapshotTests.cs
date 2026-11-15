using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// Source → bind → lower → <see cref="IrEmitter"/> → <see cref="LlvmTextWriter"/>, compared with the verified <c>.ll</c>
/// files under <c>Snapshots/</c> (testing.md §3; regenerate deliberately with <c>UPDATE_SNAPSHOTS=1</c>).
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "Snapshot")]
public sealed class IrSnapshotTests
{
    /// <summary>CMP-310 verbatim (the example of codegen.md §3).</summary>
    private const string Cmp310Add =
        "define i32 @\"M.Add\"(i32 %a, i32 %b) #0 {\n" +
        "entry:\n" +
        "  %a.addr = alloca i32, align 4\n" +
        "  %b.addr = alloca i32, align 4\n" +
        "  %Result.addr = alloca i32, align 4\n" +
        "  store i32 %a, ptr %a.addr, align 4\n" +
        "  store i32 %b, ptr %b.addr, align 4\n" +
        "  %t1 = load i32, ptr %a.addr, align 4\n" +
        "  %t2 = load i32, ptr %b.addr, align 4\n" +
        "  %t3 = add i32 %t1, %t2\n" +
        "  store i32 %t3, ptr %Result.addr, align 4\n" +
        "  br label %exit\n" +
        "exit:\n" +
        "  %t4 = load i32, ptr %Result.addr, align 4\n" +
        "  ret i32 %t4\n" +
        "}\n";

    internal const string AddSource = """
        unit M;
        interface
        function Add(a, b: Integer): Integer;
        implementation
        function Add(a, b: Integer): Integer;
        begin
          Result := a + b;
        end;
        end.
        """;

    internal const string ShortCircuitSource = """
        unit M;
        interface
        function Both(a, b: Boolean): Boolean;
        function Either(a: Boolean; x: Integer): Boolean;
        implementation
        function Both(a, b: Boolean): Boolean;
        begin
          Result := a and b;
        end;
        function Either(a: Boolean; x: Integer): Boolean;
        begin
          Result := a or (x > 0);
        end;
        function Mixed(a, b, c: Boolean): Boolean;
        begin
          Result := (a and b) or not c;
        end;
        {$B+}
        function Complete(a, b: Boolean): Boolean;
        begin
          Result := (a and b) or (a xor b);
        end;
        end.
        """;

    internal const string DivisionSource = """
        unit M;
        interface
        function Quot(a, b: Integer): Integer;
        function Rem(a, b: Int64): Int64;
        function UQuot(a, b: Cardinal): Cardinal;
        function ByConstants(a: Integer): Integer;
        implementation
        function Quot(a, b: Integer): Integer;
        begin
          Result := a div b;
        end;
        function Rem(a, b: Int64): Int64;
        begin
          Result := a mod b;
        end;
        function UQuot(a, b: Cardinal): Cardinal;
        begin
          Result := a div b;
        end;
        function ByConstants(a: Integer): Integer;
        begin
          Result := a div 7 + a mod 3 + a div -1 + a mod -1;
        end;
        end.
        """;

    internal const string ConversionsSource = """
        unit M;
        interface
        var
          Total: Int64;
          Flag: Boolean = True;
        procedure Mix(b: Byte; var w: Word; c: Char; s: Single);
        function Less(i: Int64; u: UInt64): Boolean;
        implementation
        var
          Count: Integer = 42;
        procedure Mix(b: Byte; var w: Word; c: Char; s: Single);
        var
          i: Integer;
          d: Double;
          ok: Boolean;
          lb: LongBool;
        begin
          i := b + w;
          w := i;
          Total := i * Count;
          i := Ord(c) shl 3;
          i := -(i shr b) xor not i;
          d := s + i;
          s := d;
          ok := (i < 0) = Flag;
          lb := ok;
          Flag := lb and not ok;
          Count := Ord(Flag) + Abs(i);
        end;
        function Less(i: Int64; u: UInt64): Boolean;
        begin
          Result := (i < u) and (u >= i) or (i = u);
        end;
        end.
        """;

    internal const string ForSource = """
        unit M;
        interface
        function Sum(n: Integer): Integer;
        procedure Down(var b: Byte);
        implementation
        function Sum(n: Integer): Integer;
        var
          i: Integer;
        begin
          Result := 0;
          for i := 1 to n do
          begin
            if i = 3 then Continue;
            if i > 10 then Break;
            Result := Result + i;
          end;
        end;
        procedure Down(var b: Byte);
        var
          c: Byte;
        begin
          for c := 255 downto b do
            b := c;
        end;
        end.
        """;

    internal const string CaseSource = """
        unit M;
        interface
        function Classify(x: Integer): Integer;
        function Letter(c: Char): Boolean;
        procedure NoElse(var x: Byte);
        implementation
        function Classify(x: Integer): Integer;
        begin
          case x of
            1: Result := 10;
            2, 4: Result := 20;
            5..7: Result := 30;
            1000..100000, -3: Result := 40;
          else
            Result := 0;
          end;
        end;
        function Letter(c: Char): Boolean;
        begin
          Result := False;
          case c of
            'a'..'c', 'x': Result := True;
          end;
        end;
        procedure NoElse(var x: Byte);
        begin
          case x of
            0: x := 1;
            200..255: Exit;
          end;
          x := 2;
        end;
        end.
        """;

    internal const string LoopsSource = """
        unit M;
        interface
        function Count(n: Integer): Integer;
        function Digits(n: Cardinal): Integer;
        implementation
        function Count(n: Integer): Integer;
        begin
          Result := 0;
          while n > 0 do
          begin
            n := n - 1;
            if n = 5 then Continue;
            if n = 2 then Break;
            Result := Result + 1;
          end;
        end;
        function Digits(n: Cardinal): Integer;
        begin
          Result := 0;
          repeat
            Result := Result + 1;
            n := n div 10;
            if Result > 20 then Continue;
          until n = 0;
        end;
        end.
        """;

    internal const string IfExitSource = """
        unit M;
        interface
        function Sign(x: Integer): Integer;
        procedure Check(x: Integer);
        implementation
        function Sign(x: Integer): Integer;
        begin
          if x < 0 then
            Exit(-1)
          else if x = 0 then
            Result := 0
          else
            Result := 1;
        end;
        procedure Check(x: Integer);
        begin
          if x = 0 then
            Halt(3);
          if x > 0 then
          begin
            Exit;
            x := 1;
          end;
          RunError(201);
        end;
        end.
        """;

    internal const string CallsSource = """
        unit M;
        interface
        function Fact(n: Integer): Int64;
        procedure Swap2(var a, b: Integer);
        procedure Init(out x: Integer; flag: Boolean);
        function IsPos(x: Integer): Boolean;
        procedure Use;
        implementation
        function Fact(n: Integer): Int64;
        begin
          if n <= 1 then Result := 1 else Result := n * Fact(n - 1);
        end;
        procedure Swap2(var a, b: Integer);
        var t: Integer;
        begin
          t := a; a := b; b := t;
        end;
        procedure Init(out x: Integer; flag: Boolean);
        begin
          if flag then x := 1 else x := 0;
        end;
        function IsPos(x: Integer): Boolean;
        begin
          Result := x > 0;
        end;
        var g: Integer;
        procedure Use;
        var a, b: Integer;
        begin
          Init(a, IsPos(g));
          b := 2;
          Swap2(a, b);
          Swap2(g, a);
          Fact(3);
          if IsPos(a) then g := Fact(b);
        end;
        end.
        """;

    /// <summary>routines.md §9 <c>Outer</c>/<c>Add</c> (CMP-345 example) plus two levels, a captured <c>var</c> parameter and an outer <c>Result</c>.</summary>
    internal const string NestedFrameSource = """
        unit M;
        interface
        function Outer: Integer;
        procedure Twice(var v: Integer; k: Integer);
        implementation
        function Outer: Integer;
        var total: Integer;
          procedure Add(x: Integer);
          begin
            total := total + x;
          end;
        begin
          total := 0; Add(2); Add(40); Result := total;
        end;
        procedure Twice(var v: Integer; k: Integer);
        var
          calls: Integer;
          function Get: Integer;
            procedure Bump;
            begin
              calls := calls + 1;
              v := v + k;
              Get := calls;
            end;
          begin
            Bump;
            Bump;
          end;
          procedure Again;
          begin
            calls := Get;
          end;
        begin
          calls := 0;
          Again;
        end;
        end.
        """;

    /// <summary>
    /// CMP-314 range checks (implicit narrowing ordinal conversions, <c>Chr</c>, <c>Succ</c>, <c>Inc</c>; not explicit casts or
    /// widenings) and CMP-312 overflow checks (<c>+ - *</c>, unary <c>-</c>, <c>Abs</c>, <c>Inc</c>; never the <c>for</c> step).
    /// </summary>
    internal const string ChecksSource = """
        unit M;
        interface
        function Narrow(i: Integer): Byte;
        function Signed(c: Cardinal): Integer;
        function Both(a: Integer; b: ShortInt): ShortInt;
        function Sum(a, b: Integer): Integer;
        function USum(a, b: Cardinal): Cardinal;
        function Neg(x: Int64): Int64;
        function Total(n: Byte): Integer;
        function Wrap(a: Integer): Integer;
        implementation
        {$R+}
        function Narrow(i: Integer): Byte;
        begin
          Result := i;
        end;
        function Signed(c: Cardinal): Integer;
        begin
          Result := c;
        end;
        function Unchecked(i: Integer; w: Word): Integer;
        var b: Byte;
        begin
          b := Byte(i);
          Result := w + b;
        end;
        function Letter(i: Integer): Char;
        begin
          Result := Chr(i);
        end;
        procedure Bump(var b: Byte);
        begin
          Inc(b);
          b := Succ(b);
        end;
        {$Q+}
        function Both(a: Integer; b: ShortInt): ShortInt;
        begin
          Result := a * b;
        end;
        {$R-}
        function Sum(a, b: Integer): Integer;
        begin
          Result := a + b - 1;
        end;
        function USum(a, b: Cardinal): Cardinal;
        begin
          Result := a * b + 1;
        end;
        function Neg(x: Int64): Int64;
        begin
          Result := -x + Abs(x);
        end;
        procedure Next(var i: Integer);
        begin
          Inc(i);
        end;
        {$R+}
        function Total(n: Byte): Integer;
        var
          c: Byte;
        begin
          Result := 0;
          for c := 1 to n do
            Result := Result + c;
        end;
        {$Q-}{$R-}
        function Wrap(a: Integer): Integer;
        begin
          Result := a + 1;
        end;
        end.
        """;

    /// <summary>
    /// CMP-410 <c>Write</c>/<c>Writeln</c> to <c>Output</c> and to <c>Text</c> variables (global, <c>var</c> parameter), integer,
    /// Boolean, Char and string-constant arguments with and without widths, <c>Writeln()</c>, <c>{$I-}</c>/<c>{$I+}</c>; CMP-320
    /// literals (shared, first-use order, <c>''</c> = <c>ptr null</c>, non-ASCII as UTF-16 code units).
    /// </summary>
    internal const string WritelnSource = """
        unit M;
        interface
        procedure Show(x: Integer; c: Cardinal; b: Boolean; ch: Char);
        procedure ToFile(var T: Text; n: Int64; u: UInt64);
        implementation
        var
          Log: Text;
          Flag: LongBool;
        {$I-}
        procedure Show(x: Integer; c: Cardinal; b: Boolean; ch: Char);
        begin
          Writeln('x=', x:5);
          Write(c, b:6, ch, ch:3);
          Writeln();
          Writeln;
          Write('');
          Writeln('Hello', 'x=', 'Hello':x);
        end;
        {$I+}
        procedure ToFile(var T: Text; n: Int64; u: UInt64);
        var
          w: Byte;
          a: AnsiChar;
          s: ShortInt;
        begin
          w := 4;
          a := 'q';
          s := -1;
          Writeln(T, n, ' ', Flag, a:w, s, u);
          Write(Log, 'Grüße 😀', True);
          Write(T);
        end;
        end.
        """;

    /// <summary>LANG-462, 463, 470; CMP-430, 431, 433: imports, <c>external;</c>, <c>public name</c>, ignored calling conventions.</summary>
    internal const string ExternalDeclSource = """
        unit M;
        interface
        const kernel32 = 'kernel32.dll';
        function GetCurrentProcessId: Cardinal; stdcall; external kernel32 name 'GetCurrentProcessId';
        procedure Sleep(ms: Cardinal); external 'KERNEL32';
        procedure ExitProcess(code: Cardinal); winapi; external kernel32;
        function Helper(x: Integer): Integer; cdecl; external;
        function Renamed(x: Integer): Integer; external name 'other_helper';
        function Exported(x: Integer): Integer; stdcall;
        function Run: Integer; register;
        var Counter: Integer = 7; public name '_counter';
        implementation
        var Hidden: Integer = $9875; public name '_fltused';
        var Plain: Integer;
        function Impl(x: Integer): Integer; pascal; public name 'impl_sym';
        begin
          Result := x + Hidden + Plain;
        end;
        function Exported(x: Integer): Integer;
        begin
          Result := Helper(x) + Renamed(x) + Impl(x);
        end;
        function Run: Integer; safecall;
        begin
          Sleep(10);
          Result := Exported(Counter);
          if GetCurrentProcessId > 0 then
            Result := Result + 1;
          ExitProcess(0);
        end;
        end.
        """;

    /// <summary>C-049: enumerations (i8/i16 storage), enum and integer subranges, range checks into subranges, enum case/for.</summary>
    internal const string EnumsSource = """
        unit M;
        interface
        type
          TColor = (Red, Green, Blue);
          TBright = Green..Blue;
          TWide = (wLow, wHigh = 1000);
          TPercent = 0..100;
        var
          Color: TColor = Blue;
        function Next(C: TColor): TColor;
        function Bright(C: TColor): TBright;
        function Percent(I: Integer): TPercent;
        function Count(W: TWide): Integer;
        implementation
        function Next(C: TColor): TColor;
        begin
          case C of
            Red: Result := Green;
            Green..Blue: Result := Succ(Red);
          end;
          if C < Blue then
            Result := Succ(C);
        end;
        {$R+}
        function Bright(C: TColor): TBright;
        begin
          Result := C;
          Result := Succ(Result);
        end;
        function Percent(I: Integer): TPercent;
        begin
          Result := I;
        end;
        {$R-}
        function Count(W: TWide): Integer;
        var
          X: TWide;
        begin
          Result := 0;
          for X := wLow to W do
            Inc(Result, Ord(X));
        end;
        end.
        """;

    /// <summary>C-050 / CMP-335: small-set constants, constructors (element, range, guarded / {$R+}), operators, in, Include/Exclude.</summary>
    internal const string SetsSource = """
        unit M;
        interface
        type
          TColor = (Red, Green, Blue);
          TColors = set of TColor;
          TByteSet = set of 0..7;
          TWide = set of 0..63;
        var
          Primary: TColors = [Red, Blue];
        function Ops(a, b: TColors): TColors;
        function Compare(a, b: TColors): Boolean;
        function Has(c: TColor; s: TColors; i: Integer; w: TWide): Boolean;
        procedure Change(var s: TByteSet; i: Integer);
        function Range(lo, hi: Integer): TWide;
        implementation
        function Ops(a, b: TColors): TColors;
        begin
          Result := (a + b) * [Green, Blue] - b;
        end;
        function Compare(a, b: TColors): Boolean;
        begin
          Result := (a = b) or (a <> []) and (a <= b) and (b >= [Red]);
        end;
        function Has(c: TColor; s: TColors; i: Integer; w: TWide): Boolean;
        begin
          Result := (c in s) and (i in w) and (Blue in s);
        end;
        procedure Change(var s: TByteSet; i: Integer);
        begin
          s := [i];
          Include(s, i);
          Exclude(s, 3);
        {$R+}
          s := [i];
        end;
        {$R-}
        function Range(lo, hi: Integer): TWide;
        begin
          Result := [lo..hi];
        end;
        end.
        """;

    /// <summary>
    /// C-051 / CMP-336: big-set constants (private arrays, initialised global), helper calls with temporaries, inline
    /// element and range constructors, <c>in</c>, <c>Include</c>/<c>Exclude</c> ({$R-} guarded / {$R+} checked), small ↔ big.
    /// </summary>
    internal const string BigSetsSource = """
        unit M;
        interface
        type
          TChars = set of Char;
          TBytes = set of Byte;
          TSmall = set of 0..9;
        var
          Vowels: TChars = ['a', 'e', 'i', 'o', 'u'];
        procedure Ops(var r, a, b: TChars);
        function Compare(var a, b: TChars): Boolean;
        function Has(c: Char; var s: TChars; i: Integer; var w: TBytes): Boolean;
        procedure Change(var s: TChars; c, lo, hi: Char);
        procedure Mixed(var b: TBytes; var d: TSmall);
        implementation
        procedure Ops(var r, a, b: TChars);
        begin
          r := (a + b) * ['a'..'z'] - b;
          r := Vowels;
        end;
        function Compare(var a, b: TChars): Boolean;
        begin
          Result := (a = b) or (a <> []) and (a <= b) and (b >= ['x']);
        end;
        function Has(c: Char; var s: TChars; i: Integer; var w: TBytes): Boolean;
        begin
          Result := (c in s) and (i in w) and ('q' in s) and (#300 in s);
        end;
        procedure Change(var s: TChars; c, lo, hi: Char);
        begin
          s := [c, lo..hi, 'z'];
          Include(s, c);
          Exclude(s, 'a');
        {$R+}
          s := [c, lo..hi];
          Include(s, c);
        end;
        {$R-}
        procedure Mixed(var b: TBytes; var d: TSmall);
        begin
          b := d;
          d := b;
          b := b + d;
        end;
        end.
        """;

    /// <summary>
    /// C-052 / CMP-308, 326, 331: element GEPs (index − Low in i64), multi-dimensional, enum/Char/Boolean indexes, copies
    /// (memcpy, iN for 4 bytes, memmove for rows of one array), {$R+} index checks.
    /// </summary>
    internal const string StaticArraysSource = """
        unit M;
        interface
        type
          TColor = (Red, Green, Blue);
          TVec = array[1..4] of Integer;
          TGrid = array[0..2, 0..9] of Byte;
          TFlags = array[TColor] of Boolean;
          TCounts = array[Char] of Word;
          TPair = array[0..1] of SmallInt;
        var
          G: TGrid;
        procedure Fill(var v: TVec; x: Integer);
        function Sum(var v: TVec): Integer;
        procedure Copy2(var a, b: TVec; var p, q: TPair);
        procedure Grid(i, j: Integer; c: TColor; ch: Char; var f: TFlags; var n: TCounts);
        implementation
        procedure Fill(var v: TVec; x: Integer);
        var
          i: Integer;
          local: TVec;
        begin
          for i := 1 to 4 do
            local[i] := x + i;
          v := local;
          v[2] := v[3];
        end;
        function Sum(var v: TVec): Integer;
        var
          i: Integer;
        begin
          Result := 0;
          for i := Low(v) to High(v) do
            Inc(Result, v[i]);
        end;
        procedure Copy2(var a, b: TVec; var p, q: TPair);
        begin
          a := b;
          p := q;
        end;
        procedure Grid(i, j: Integer; c: TColor; ch: Char; var f: TFlags; var n: TCounts);
        begin
          G[i, j] := G[2][9];
          G[i] := G[j];
          f[c] := not f[Blue];
          Inc(n[ch]);
        {$R+}
          G[i, j] := 1;
          f[c] := True;
          n[ch] := 0;
        end;
        end.
        """;

    /// <summary>
    /// C-053 / CMP-306, 307, 309, 326: named struct types (plain, packed, padded for {$A4}), field GEPs through nested records
    /// and arrays, packed-field accesses with align 1, record copies (iN for 8 bytes, memcpy otherwise), a captured record.
    /// </summary>
    internal const string RecordsSource = """
        unit M;
        interface
        type
          TPoint = record X, Y: Integer; end;
          TRect = record A, B: TPoint; Tag: Char; end;
          TPacked = packed record B: Byte; I: Integer; end;
        {$A4}
          TFour = record B: Byte; D: Int64; end;
        {$A8}
          TLine = record P: array[0..1] of TPoint; F: TFour; end;
        var
          G: TRect;
        procedure Move(var p: TPoint; dx: Integer);
        procedure Copy(var r: TRect; var q: TPacked; var f: TFour);
        procedure Line(var l: TLine; i: Integer);
        procedure Outer;
        implementation
        procedure Move(var p: TPoint; dx: Integer);
        begin
          p.X := p.X + dx;
        end;
        procedure Copy(var r: TRect; var q: TPacked; var f: TFour);
        var
          local: TRect;
          pt: TPoint;
        begin
          local := r;
          r := G;
          G := local;
          pt := local.B;
          G.A := pt;
          q.I := q.B;
          f.D := 7;
        end;
        procedure Line(var l: TLine; i: Integer);
        begin
          l.P[i].Y := l.P[0].X;
          l.P[1] := l.P[i];
          l.F.B := 1;
        end;
        procedure Outer;
        var
          r: TPoint;
          b: Byte;
          f: TFour;
          procedure Inner;
          begin
            r.Y := 2;
            f.D := b;
          end;
        begin
          r.X := 1;
          b := 3;
          Inner;
        end;
        end.
        """;

    /// <summary>
    /// C-054 / CMP-341: typed constants are <c>constant</c> globals, initialised variables <c>global</c>s with the value; arrays
    /// element by element, records field by field (padding and omitted trailing fields zero), routine-local typed constants
    /// named after the routine.
    /// </summary>
    internal const string TypedConstantsSource = """
        unit M;
        interface
        type
          TPoint = record X, Y: Integer; end;
        {$A4}
          TFour = record B: Byte; D: Int64; end;
        {$A8}
        const
          Table: array[0..2] of Integer = (1, 2, 3);
          Origin: TPoint = (X: 5; Y: -6);
        var
          Grid: array[1..2, 1..2] of Byte = ((1, 2), (3, 4));
        function Pick(i: Integer): Integer;
        implementation
        const
          Four: TFour = (B: 1; D: 2);
          Half: TPoint = (X: 9);
          Flags: array[Boolean] of Boolean = (False, True);
          Small: set of 0..7 = [0, 7];
        var
          Start: TPoint = (X: 1; Y: 2);
        function Pick(i: Integer): Integer;
        const
          Local: array[0..1] of Word = (10, 20);
        begin
          Result := Table[i] + Local[i] + Origin.Y + Four.D + Half.X;
          Start := Origin;
        end;
        end.
        """;

    /// <summary>
    /// C-056: <c>@</c>/<c>^</c> (CMP-340), pointer arithmetic (CMP-342), <c>New</c>/<c>Dispose</c>/<c>GetMem</c>/<c>FreeMem</c>,
    /// <c>FillChar</c>/<c>Move</c> (CMP-412), <c>@</c> in typed constants, an empty record (one byte when allocated, ABI-060) and a
    /// <c>var</c> argument in a packed record (copied through an aligned temporary, CMP-309).
    /// </summary>
    internal const string PointersSource = """
        unit M;
        interface
        type
          PInt = ^Integer;
          PNode = ^TNode;
          TNode = record V: Integer; Next: PNode; end;
          TEmpty = record end;
          TPacked = packed record B: Byte; I: Integer; end;
        var
          G: Integer;
          E: TEmpty;
          K: TPacked;
        const
          CG: PInt = @G;
          Table: array[0..1] of Pointer = (@G, nil);
        procedure Bump(var V: Integer);
        procedure Use;
        implementation
        procedure Bump(var V: Integer);
        begin
          V := V + 1;
        end;
        procedure Use;
        var
          p, q: PInt;
          n: PNode;
          c: PChar;
          d: NativeInt;
          u: Pointer;
          e2: TEmpty;
        begin
          p := @G;
          p^ := p^ + 1;
          New(n);
          n.V := 1;
          n^.Next := nil;
          Dispose(n);
          GetMem(u, 16);
          FillChar(u^, 16, 0);
          Move(G, u^, 4);
          FreeMem(u);
          c := c + 2;
          d := c - c;
        {$POINTERMATH ON}
          q := p - 1;
          d := q - p;
        {$POINTERMATH OFF}
          Inc(p, 3);
          if Assigned(p) and (p <> CG) then
            u := @Bump;
          Bump(K.I);
          u := @E;
          u := @e2;
        end;
        end.
        """;

    /// <summary>
    /// C-057: value typecasts (ordinal → pointer after sign extension, pointer → pointer, pointer → integer, ordinal → ordinal,
    /// LANG-290 rows 1–4), variable typecasts as values and targets (also of a packed field: <c>align 1</c>), a distinct type
    /// (LANG-205) and <c>Assert</c> with and without message and under <c>{$C-}</c>.
    /// </summary>
    internal const string TypecastsSource = """
        unit M;
        interface
        type
          TMy = type Integer;
          TRec8 = record A, B: Integer; end;
          TPacked = packed record B: Byte; I: Integer; end;
        procedure Use(var r: TRec8; m: TMy);
        implementation
        procedure Use(var r: TRec8; m: TMy);
        var
          i: Integer;
          b: ShortInt;
          q: Int64;
          p: Pointer;
          pc: PChar;
          f: Boolean;
          k: TPacked;
        begin
          p := Pointer(b);
          pc := PChar(p);
          q := NativeInt(pc);
          i := Integer(Cardinal(q));
          f := Boolean(i);
          q := Int64(r);
          TRec8(q).B := m;
          Cardinal(k.I) := 7;
          Assert(f, 'flag');
          Assert(i > m);
          {$C-} Assert(i = 0); {$C+}
        end;
        end.
        """;

    /// <summary>
    /// C-058: real constants (hexadecimal, a Single one rounded), CMP-316 arithmetic in <c>double</c> and in <c>float</c> (both
    /// operands Single), <c>fneg</c>, CMP-313 conversions (<c>sitofp</c>/<c>uitofp</c>, <c>fpext</c>/<c>fptrunc</c>), CMP-315
    /// comparisons (<c>&lt;&gt;</c> is <c>une</c>), real parameters and results, an initialised Single global and <c>{$Q+}</c>
    /// (no checks on reals).
    /// </summary>
    internal const string RealsSource = """
        unit M;
        interface
        var
          G: Single = 0.1;
        function Mix(d: Double; s: Single; i: Integer; c: Cardinal; u: UInt64): Double;
        function Scale(s: Single): Single;
        function Order(a, b: Double): Integer;
        implementation
        function Mix(d: Double; s: Single; i: Integer; c: Cardinal; u: UInt64): Double;
        var
          t: Single;
        begin
        {$Q+}
          t := s * s - G;
          Result := (d + i) * c / u - t;
          Result := -Result + i / 2;
          G := Result;
        {$Q-}
        end;
        function Scale(s: Single): Single;
        begin
          Result := s * 2 + 0.5;
        end;
        function Order(a, b: Double): Integer;
        begin
          Result := 0;
          if a = b then Result := 1;
          if a <> b then Result := Result + 2;
          if a < b then Result := Result + 4;
          if a <= b then Result := Result + 8;
          if a > b then Result := Result + 16;
          if a >= b then Result := Result + 32;
        end;
        end.
        """;

    /// <summary>
    /// C-059: <c>Trunc</c>/<c>Round</c>/<c>Int</c>/<c>Frac</c> as RTL helper calls on a <c>double</c>, <c>Sqrt</c> as
    /// <c>llvm.sqrt.f64</c>, a Single operand extended first (CMP-412), and <c>_WriteFloat</c> with the CMP-410 widths.
    /// </summary>
    internal const string RealIntrinsicsSource = """
        unit M;
        interface
        procedure Show(d: Double; s: Single; w: Integer);
        function Parts(d: Double; s: Single; i: Integer): Int64;
        implementation
        procedure Show(d: Double; s: Single; w: Integer);
        begin
          Write(d);
          Write(s:w);
          Writeln(d:w:2, Sqrt(s));
        end;
        function Parts(d: Double; s: Single; i: Integer): Int64;
        var
          x: Double;
        begin
          x := Int(d) + Frac(s) + Sqrt(d) + Sqrt(i);
          Result := Trunc(x) + Round(s) + Trunc(2.5) + Round(2.5);
        end;
        end.
        """;

    /// <summary>
    /// C-060: UnicodeString variables (CMP-320/324/325): <c>_UStrAsg</c>, <c>_UStrFromWChar</c>, results through the hidden
    /// <c>Result</c> (into a local or a temporary), value parameters with <c>_UStrAddRef</c>, an <c>out</c> parameter cleared,
    /// zeroed locals and temporaries, the <c>cleanup</c> block on every exit, inline <c>Length</c> and characters with
    /// <c>{$R+}</c>, Write of a string value.
    /// </summary>
    internal const string StringsSource = """
        unit M;
        interface
        function Echo(s: string): string;
        procedure Use(const c: string; var v: string; out o: string);
        function Len(const s: string; i: Integer): Integer;
        implementation
        var
          G: string;
        function Echo(s: string): string;
        begin
          Result := s;
        end;
        procedure Use(const c: string; var v: string; out o: string);
        var
          t: string;
          ch: Char;
        begin
          t := c;
          v := 'lit';
          o := Echo(t);
          t := Echo(v);
          ch := t[1];
          t := ch;
          G := t;
          Writeln(t);
          if Length(t) > 2 then
            Exit;
          Echo(G);
        end;
        function Len(const s: string; i: Integer): Integer;
        begin
        {$R+}
          Result := Length(s) + Ord(s[i]);
        {$R-}
        end;
        end.
        """;

    /// <summary>
    /// C-061: CMP-324 string operators — <c>_UStrCat</c>, <c>_UStrCatN</c> over a stack <c>[n x ptr]</c>, a char part through
    /// <c>_UStrFromWChar</c>, <c>_UStrEq</c>/<c>_UStrCmp</c>, <c>''</c> tests without a call, and <c>s[i] := c</c> after
    /// <c>_UStrUnique</c> with and without <c>{$R+}</c>.
    /// </summary>
    internal const string StringOperatorsSource = """
        unit M;
        interface
        function Join(const a, b, c: string; ch: Char): string;
        function Order(const a, b: string): Integer;
        procedure Poke(var s: string; i: Integer; ch: Char);
        implementation
        function Join(const a, b, c: string; ch: Char): string;
        begin
          Result := a + b;
          Result := a + b + c;
          Result := Result + ch;
          Result := ch + ch;
        end;
        function Order(const a, b: string): Integer;
        begin
          Result := 0;
          if a = b then Result := 1;
          if a <> b then Result := Result + 2;
          if a < b then Result := Result + 4;
          if a >= b then Result := Result + 8;
          if a = '' then Result := Result + 16;
          if a + b <> '' then Result := Result + 32;
        end;
        procedure Poke(var s: string; i: Integer; ch: Char);
        begin
          s[i] := ch;
        {$R+}
          s[i + 1] := s[i];
        {$R-}
        end;
        end.
        """;

    /// <summary>
    /// C-168: atomic intrinsics inline (CMP-412, ABI-172): <c>atomicrmw add|sub</c> plus the operand for the new value,
    /// <c>atomicrmw xchg</c>, <c>cmpxchg</c> + <c>extractvalue 0</c>, all <c>seq_cst</c>; 32-/64-bit integers and pointers (a
    /// typed pointer moves by elements, through its <c>NativeInt</c> view), a global target, statement use.
    /// </summary>
    internal const string AtomicSource = """
        unit M;
        interface
        type
          PInt = ^Integer;
        function Counters(var i: Integer; var q: Int64; n: Integer): Int64;
        function Swaps(var p: Pointer; var t: PInt; q: Pointer; var c: Cardinal): Pointer;
        implementation
        var
          G: Integer;
        function Counters(var i: Integer; var q: Int64; n: Integer): Int64;
        begin
          Result := AtomicIncrement(i);
          Result := Result + AtomicDecrement(q, n);
          AtomicIncrement(G, 5);
        end;
        function Swaps(var p: Pointer; var t: PInt; q: Pointer; var c: Cardinal): Pointer;
        begin
          Result := AtomicExchange(p, q);
          Result := AtomicCmpExchange(p, nil, Result);
          AtomicIncrement(t, 2);
          c := AtomicExchange(c, 7) + AtomicCmpExchange(c, 1, 7);
        end;
        end.
        """;

    /// <summary>
    /// C-062: the string intrinsics through their rtl-abi.md helpers (<c>_UStrSetLen</c>, <c>_UStrDelete</c>, <c>_UStrInsert</c>,
    /// <c>_UStrCopy</c> into a target or a temporary, <c>_UStrPos</c>, <c>_StrInt</c>/<c>_StrFloat</c>, <c>_ValInt</c>/<c>_ValFloat</c> with the
    /// range test), <c>Concat</c> as <c>_UStrCatN</c>, <c>UpCase</c> inline on a written string element.
    /// </summary>
    internal const string StringFunctionsSource = """
        unit M;
        interface
        function Edit(var s: string; const t: string; c: Char; i: Integer): string;
        function Numbers(var s: string; i: Integer; d: Double; var code: Integer): Byte;
        implementation
        function Edit(var s: string; const t: string; c: Char; i: Integer): string;
        begin
          SetLength(s, i);
          Delete(s, 2, i);
          Insert(t, s, i);
          Insert(c, s, 1);
          Result := Copy(s, i, 2) + Concat(t, c);
          s := Copy(s, 2);
          Result[1] := UpCase(Result[1]);
          if Pos(t, s) > Pos('x', s, i) then
            Result := t;
        end;
        function Numbers(var s: string; i: Integer; d: Double; var code: Integer): Byte;
        begin
          Str(i, s);
          Str(i:5, s);
          Str(d, s);
          Str(d:10:3, s);
          Val(s, Result, code);
          Val(s, d, code);
        end;
        end.
        """;

    /// <summary>
    /// C-063: byte strings and PChar (LANG-243…247, CMP-320/324): AnsiString literals with code page and ElemSize 1, the
    /// <c>_AStr*</c> helpers (assign, concatenation into a temporary, bytewise compare, unique before a byte store), the
    /// conversions, short strings as <c>[N+1 x i8]</c> (constant copy, <c>_SStrAsg</c>, <c>_SStrCmp</c>, the length byte),
    /// <c>PChar(s)</c> as a select of <c>@.str.empty</c>, and the <c>Write</c> helpers.
    /// </summary>
    internal const string ByteStringsSource = """
        unit M;
        interface
        type
          TName = string[7];
        function Ansi(var a: AnsiString; const s: string; c: AnsiChar): Boolean;
        function Short(var n: TName; const h: ShortString; i: Integer): Integer;
        function Data(const s: string; const a: AnsiString; p: PChar; pa: PAnsiChar): PChar;
        implementation
        function Ansi(var a: AnsiString; const s: string; c: AnsiChar): Boolean;
        var
          u: UTF8String;
        begin
          a := 'grün';
          u := UTF8String(s);
          a := a + u + 'x';
          a[1] := c;
          Result := (a < u) or (a = '');
          Writeln(a, s);
        end;
        function Short(var n: TName; const h: ShortString; i: Integer): Integer;
        begin
          n := 'abcdefghij';
          n := h;
          n[i] := 'q';
          Result := Length(n) + Ord(h[0]);
          if n < h then
            Writeln(n);
        end;
        function Data(const s: string; const a: AnsiString; p: PChar; pa: PAnsiChar): PChar;
        var
          t: string;
        begin
          t := p;
          t := pa;
          Writeln(p, pa);
          Result := PChar(s);
          if Pointer(a) = nil then
            Result := '';
        end;
        end.
        """;

    /// <summary>
    /// C-064: dynamic arrays (CMP-330/331, ABI-040…044) and their RTTI blocks (CMP-405, rtl-abi.md §10): <c>_DynArraySetLen</c> with a
    /// dims array, <c>_DynArrayAsg</c>/<c>_DynArrayClr</c>/<c>_DynArrayCopy</c> with <c>linkonce_odr</c> type info, inline Length and
    /// element GEPs (checked under <c>{$R+}</c>), a hidden dynamic-array Result, and <c>tkDynArray</c> blocks over <c>tkUString</c>,
    /// <c>tkAString</c>, <c>tkRecord</c> and <c>tkArray</c> element blocks (anonymous types named per CMP-362).
    /// </summary>
    internal const string DynArraysSource = """
        unit M;
        interface
        type
          TPoint = record X, Y: Integer end;
          TInts = array of Integer;
          TGrid = array of TInts;
          TNames = array of string;
          TPoints = array of TPoint;
          TPairs = array of array[0..1] of Byte;
          TAnsis = array of AnsiString;
        function Build(n: Integer; const names: TNames): TInts;
        procedure Kinds(var p: TPoints; var t: TPairs; var a: TAnsis);
        implementation
        function Build(n: Integer; const names: TNames): TInts;
        var
          g: TGrid;
          rest: array of string;
        begin
          SetLength(Result, n);
          SetLength(g, n, 2);
          g[0] := Result;
          {$R+}
          Result[n - 1] := Length(names[0]);
          {$R-}
          rest := nil;
          if Copy(names, 1) <> nil then
            g := nil;
        end;
        procedure Kinds(var p: TPoints; var t: TPairs; var a: TAnsis);
        begin
          SetLength(p, 1);
          SetLength(t, 1);
          SetLength(a, 1);
          p[0].Y := t[0][1];
        end;
        end.
        """;

    /// <summary>
    /// C-065: managed records and static arrays (CMP-325 rules 1–3 and 6, ABI-080/081, ABI-110): <c>_CopyRecord</c> for assignment,
    /// the caller's referenced copy of a large value argument, <c>_AddRef</c>/<c>_Finalize</c> of an 8-byte value parameter the
    /// callee receives as <c>i64</c>, <c>_Finalize</c> of an <c>out</c> parameter and in the cleanup block, a hidden managed result,
    /// <c>Dispose</c> of a managed target, and the <c>tkRecord</c>/<c>tkArray</c> blocks with their managed-field tables.
    /// </summary>
    internal const string ManagedRecordsSource = """
        unit M;
        interface
        type
          TPerson = record
            Name: string;
            Age: Integer;
            Tags: array of AnsiString;
          end;
          TSmall = record S: string end;
          TNames = array[1..2] of string;
          PPerson = ^TPerson;
        function Make(const n: string): TPerson;
        procedure Take(p: TPerson; s: TSmall; out o: TPerson);
        procedure Use(q: PPerson);
        implementation
        function Make(const n: string): TPerson;
        begin
          Result.Name := n;
        end;
        procedure Take(p: TPerson; s: TSmall; out o: TPerson);
        begin
          o := p;
          s.S := p.Name;
        end;
        procedure Use(q: PPerson);
        var
          a: TPerson;
          names, copy: TNames;
          small: TSmall;
        begin
          a := Make('x');
          names[1] := a.Name;
          copy := names;
          Take(a, small, q^);
          Dispose(q);
        end;
        end.
        """;

    /// <summary>
    /// C-067 / C-178 / C-179: array constructors (<c>_DynArraySetLen</c> of a temporary, element stores, <c>_DynArrayAsg</c>;
    /// managed elements by <c>_UStrAsg</c>; <c>[]</c> clears), <c>Assigned</c> of a dynamic array, <c>Pointer(a)</c> /
    /// <c>NativeInt(a)</c> of a dynamic array (no reference counting) and a <c>^string</c> pointer.
    /// </summary>
    internal const string OpenArraysSource = """
        unit M;
        interface
        function Sum(const a: array of Integer): Integer;
        procedure Bump(s: array of string);
        procedure Caller;
        implementation
        function Sum(const a: array of Integer): Integer;
        var
          i: Integer;
        begin
          Result := 0;
          for i := 0 to High(a) do
            Result := Result + a[i];
        end;
        procedure Bump(s: array of string);
        begin
          {$R+}
          s[0] := s[0] + '!';
          {$R-}
        end;
        procedure Caller;
        var
          f: array[1..3] of Integer;
          d: array of Integer;
          n: Integer;
        begin
          n := Sum(f) + Sum(d) + Sum([1, n]) + Sum(Slice(f, 2)) + Sum([]);
          Bump(['x']);
        end;
        end.
        """;

    internal const string ArrayConstructorsSource = """
        unit M;
        interface
        type
          TInts = array of Integer;
          TNames = array of string;
          PStr = ^string;
        function Build(n: Integer; p: PStr): TInts;
        implementation
        function Count(const a: TInts): Integer;
        begin
          Result := Length(a);
        end;
        function Build(n: Integer; p: PStr): TInts;
        var
          names: TNames;
          q: Pointer;
          k: NativeInt;
        begin
          Result := [n, n + 1];
          names := [p^, 'x'];
          n := Count([7]);
          if Assigned(Result) then
            q := Pointer(Result);
          k := NativeInt(Result);
          p^ := names[0];
          Result := [];
        end;
        end.
        """;

    /// <summary>
    /// C-066: CMP-411 as emitted — <c>_ReadInt</c>/<c>_ReadUInt</c>/<c>_ReadFloat</c>/<c>_ReadWChar</c>/<c>_ReadUStr</c> on
    /// <c>System.Input</c> or a <c>Text</c> variable, the error-106 range check of a narrow integer, <c>fptrunc</c> for Single,
    /// <c>_ReadLn</c>/<c>_ReadEnd</c>/<c>_IOCheck</c>, and <c>_Eof</c>/<c>_Eoln</c> returning Boolean.
    /// </summary>
    internal const string ReadTextSource = """
        unit M;
        interface
        procedure Scan(var t: Text);
        implementation
        procedure Scan(var t: Text);
        var
          n: Integer;
          w: Word;
          q: UInt64;
          g: Single;
          c: Char;
          s: string;
        begin
          while not Eof do
            Readln(n, w, q, g, c, s);
          {$I-}
          if Eoln(t) then
            Read(t, s);
        end;
        end.
        """;

    internal const string ProcValueSource = """
        unit M;
        interface
        type
          TBinary = function(a, b: Integer): Integer;
          TNotify = procedure(code: Integer); stdcall;
          TCell = record
            Op: TBinary;
          end;
        function Add(a, b: Integer): Integer;
        function Apply(f: TBinary; x: Integer): Integer;
        function Pick(i: Integer): TBinary;
        procedure Run(var cell: TCell; n: TNotify);
        implementation
        var
          Table: array[0..1] of TBinary;
        function Add(a, b: Integer): Integer;
        begin
          Result := a + b;
        end;
        function Apply(f: TBinary; x: Integer): Integer;
        begin
          if Assigned(f) then
            Result := f(x, x)
          else
            Result := 0;
        end;
        function Pick(i: Integer): TBinary;
        begin
          Result := Table[i];
        end;
        procedure Run(var cell: TCell; n: TNotify);
        var
          p: TBinary;
        begin
          p := Add;
          cell.Op := @Add;
          Table[0] := p;
          Table[1] := nil;
          if p <> nil then
            n(Apply(cell.Op, 2) + Table[0](1, 2));
          n(Pick(0)(3, 4));
        end;
        end.
        """;

    /// <summary>
    /// C-083: VMTs (CMP-366, ABI §7.2) of a root class with a virtual, an abstract and a static method, an interface descendant
    /// overriding both and introducing a <c>dynamic</c> slot, and an implementation-only class overriding it <c>final</c>;
    /// virtual calls through a base reference (CMP-367), a static call, <c>inherited</c> (static), and creation on a class name.
    /// </summary>
    internal const string VmtSource = """
        unit M;
        interface
        type
          TA = class
            N: Integer;
            constructor Create;
            procedure P; virtual;
            function Q(X: Integer): Integer; virtual; abstract;
            procedure S;
          end;
          TB = class(TA)
            procedure P; override;
            function Q(X: Integer): Integer; override;
            procedure R; dynamic;
          end;
        function Run(A: TA): Integer;
        implementation
        type
          TC = class(TB)
            procedure R; override; final;
          end;
        constructor TA.Create; begin N := 1 end;
        procedure TA.P; begin end;
        procedure TA.S; begin P end;
        procedure TB.P; begin inherited P end;
        function TB.Q(X: Integer): Integer; begin Result := X + N end;
        procedure TB.R; begin end;
        procedure TC.R; begin end;
        function Run(A: TA): Integer;
        var B: TB;
        begin
          A.P;
          A.S;
          B := TC.Create;
          B.R;
          Result := A.Q(2) + B.Q(3);
        end;
        end.
        """;

    /// <summary>
    /// C-090: <c>raise</c> (CMP-395, with <c>at</c>), a handler-form <c>try…except</c> (class tests in order, variable, re-raise)
    /// nested in a plain one (CMP-391: the inner dispatch unwinds to the outer one, the inner cleanup's <c>_EndCatch</c> is an invoke).
    /// </summary>
    internal const string TryExceptSource = """
        unit M;
        interface
        type
          EA = class
            constructor Create;
          end;
          EB = class(EA)
          end;
        procedure Fail(N: Integer);
        function Run(N: Integer): Integer;
        implementation
        constructor EA.Create; begin end;
        procedure Fail(N: Integer);
        begin
          if N > 0 then raise EA.Create;
          if N < 0 then raise EB.Create at nil;
        end;
        function Run(N: Integer): Integer;
        var X: EA;
        begin
          Result := 0;
          try
            try
              Fail(N);
            except
              on E: EB do X := E;
              on EA do raise;
            end;
          except
            Result := 1;
          end;
        end;
        end.
        """;

    /// <summary>
    /// C-091/C-092: two <c>try…finally</c> statements of one routine, outlined to <c>$fin1</c>/<c>$fin2</c> reaching <c>Result</c> and
    /// <c>I</c> through the frame (CMP-392); <c>Continue</c>, <c>Break</c> and <c>Exit</c> call <c>$fin1</c> first; <c>$fin2</c> holds a
    /// function-level <c>try…except</c> of its own.
    /// </summary>
    internal const string TryFinallySource = """
        unit M;
        interface
        procedure Step(N: Integer);
        function Run(N: Integer): Integer;
        implementation
        procedure Step(N: Integer); begin end;
        function Run(N: Integer): Integer;
        var I: Integer;
        begin
          Result := 0;
          for I := 1 to N do
            try
              Step(I);
              if I = 2 then Continue;
              if I = 3 then Break;
              if I = 4 then Exit;
            finally
              Result := Result + I;
            end;
          try
            Step(0);
          finally
            try Step(Result) except end;
          end;
        end;
        end.
        """;

    /// <summary>
    /// C-165: threadvars of the interface (descriptor with external linkage) and of the implementation; a read, a write, a
    /// <c>var</c> argument and <c>@</c> each compute the address through <c>_TlsBase</c> and the descriptor (CMP-414); <c>$init</c> of a
    /// unit without initialization section registers the descriptor (ABI-180). Record: Depth 0, Mark 4, Sum 8; size 16.
    /// </summary>
    internal const string ThreadVarSource = """
        unit M;
        interface
        threadvar
          Depth: Integer;
        procedure Enter(var Level: Integer);
        function Run: Int64;
        implementation
        threadvar
          Mark: Byte;
          Sum: Int64;
        procedure Enter(var Level: Integer); begin Level := Level + 1 end;
        function Run: Int64;
        var P: ^Int64;
        begin
          Enter(Depth);
          Mark := 2;
          P := @Sum;
          P^ := Depth + Mark;
          Result := Sum;
        end;
        end.
        """;

    /// <summary>
    /// C-098: an interface descending from another, implemented by a class (no <c>TObject</c> in the stub System: the hidden IMT
    /// field follows <c>N</c> at offset 16): one thunk per IMT slot (CMP-372; <c>Get</c> is virtual, so its thunk loads the VMT slot;
    /// <c>Name</c> has a hidden <c>Result</c>), the IMT (CMP-371) and the <c>IntfTable</c> listing <c>IQux</c> and its ancestor <c>IBar</c>
    /// with the same IMT (ABI-095); interface calls through IMT slots (ABI-093), descendant → ancestor assignment (<c>_IntfAsg</c>).
    /// </summary>
    internal const string ThunkSource = """
        unit M;
        interface
        type
          IBar = interface
            procedure Baz(A: Integer);
            function Get: Integer;
            function Name: string;
          end;
          IQux = interface(IBar)
            procedure Qux;
          end;
          TFoo = class(IQux)
            N: Integer;
            procedure Baz(A: Integer);
            function Get: Integer; virtual;
            function Name: string;
            procedure Qux;
          end;
        function Run(const Q: IQux): Integer;
        implementation
        procedure TFoo.Baz(A: Integer); begin N := A end;
        function TFoo.Get: Integer; begin Result := N end;
        function TFoo.Name: string; begin Result := 'foo' end;
        procedure TFoo.Qux; begin end;
        function Run(const Q: IQux): Integer;
        var B: IBar;
        begin
          Q.Baz(2);
          Q.Qux;
          B := Q;
          Result := B.Get;
        end;
        end.
        """;

    [Fact]
    [Trait("Req", "CMP-371")]
    [Trait("Req", "CMP-372")]
    [Trait("Req", "CMP-373")]
    [Trait("Req", "ABI-093")]
    [Trait("Req", "ABI-095")]
    public void Thunk() => Snapshot.Verify("Thunk", Emit(ThunkSource));

    [Fact]
    [Trait("Req", "CMP-372")]
    public void Thunk_SubtractsTheFieldOffsetAndTailCalls()
    {
        var text = Emit(ThunkSource);
        Assert.Contains("define internal void @\"M.TFoo.$thunk.M.IQux.Baz\"(ptr %iref, i32 %p1) #0 {", text);
        Assert.Contains("%self = getelementptr inbounds i8, ptr %iref, i64 -16", text);
        Assert.Contains("musttail call void @\"M.TFoo.Baz\"(ptr %self, i32 %p1)", text);
        Assert.Contains("%r = musttail call i32 %fn(ptr %self)", text);                                        // virtual Get: VMT slot
        Assert.Contains("@\"M.TFoo.$imt.M.IQux\" = constant [4 x ptr] [ptr @\"M.TFoo.$thunk.M.IQux.Baz\", ptr @\"M.TFoo.$thunk.M.IQux.Get\", "
            + "ptr @\"M.TFoo.$thunk.M.IQux.Name\", ptr @\"M.TFoo.$thunk.M.IQux.Qux\"], align 8", text);
    }

    /// <summary>C-100 / CMP-373: <c>is</c>/<c>as</c> with an interface pass the <c>linkonce_odr</c> GUID constant to the RTL helpers.</summary>
    [Fact]
    [Trait("Req", "CMP-373")]
    [Trait("Req", "LANG-650")]
    [Trait("Req", "LANG-651")]
    public void InterfaceIsAs_PassTheGuidConstant()
    {
        var text = Emit("""
            unit M;
            interface
            type
              IBar = interface ['{01234567-89AB-CDEF-0123-456789ABCDEF}'] end;
              TFoo = class(IBar) end;
            function Test(O: TFoo; const I: IBar): Boolean;
            implementation
            function Test(O: TFoo; const I: IBar): Boolean;
            var B: IBar;
            begin
              B := O as IBar;
              B := I as IBar;
              Result := O is IBar;
            end;
            end.
            """);
        Assert.Contains("$\"M.IBar.$guid\" = comdat any", text);
        Assert.Contains("@\"M.IBar.$guid\" = linkonce_odr constant [16 x i8] [i8 103, i8 69, i8 35, i8 1, i8 -85, i8 -119, i8 -17, i8 -51, "
            + "i8 1, i8 35, i8 69, i8 103, i8 -119, i8 -85, i8 -51, i8 -17], comdat, align 4", text);
        Assert.Contains("invoke void @\"System._ObjToIntf\"(ptr %B.addr, ptr %", text);
        Assert.Contains(", ptr @\"M.IBar.$guid\")", text);
        Assert.Contains("invoke void @\"System._IntfCast\"(ptr %B.addr, ptr %", text);
        Assert.Matches(@"call i8 @""System._ObjSupports""\(ptr %\w+, ptr @""M.IBar.\$guid""\)", text);
    }

    /// <summary>C-209 (LANG-266): <c>nil</c> as a dynamic-array (and string) argument is a null pointer, by value and as <c>const</c>.</summary>
    [Fact]
    [Trait("Req", "LANG-266")]
    public void NilDynamicArrayArgument_IsANullPointer()
    {
        var text = Emit("""
            unit M;
            interface
            type TBytes = array of Byte;
            function Len(const B: TBytes): Integer;
            function Run: Integer;
            implementation
            function Len(const B: TBytes): Integer; begin Result := Length(B) end;
            function Value(B: TBytes): Integer; begin Result := Length(B) end;
            function Run: Integer; begin Result := Len(nil) + Value(nil) end;
            end.
            """);
        Assert.Contains("call i32 @\"M.Len\"(ptr null)", text);
        Assert.Contains("call i32 @\"M.Value\"(ptr null)", text);
    }

    internal const string RttiEnumSource = """
        unit M;
        interface
        type
          TColor = (Red, Green);
          TWarm = Green..Green;
          TSet = set of TColor;
        function Info: Pointer;
        implementation
        function Info: Pointer;
        begin
          Result := TypeInfo(TColor);
          Result := TypeInfo(TWarm);
          Result := TypeInfo(TSet);
          Result := TypeInfo(Integer);
        end;
        end.
        """;

    /// <summary>C-112 / CMP-405, ABI §10: enumeration (names table, BaseType), subrange, set and Integer blocks; TypeInfo(T) is their address.</summary>
    [Fact]
    [Trait("Req", "CMP-405")]
    [Trait("Req", "CMP-407")]
    public void RttiEnum() => Snapshot.Verify("RttiEnum", Emit(RttiEnumSource));

    internal const string RttiClassSource = """
        unit M;
        interface
        type
          {$M+}
          TA = class
          private
            FX: Integer;
            function GetY: Integer;
            procedure SetY(V: Integer); virtual;
          published
            procedure Run;
            property X: Integer read FX write FX;
            property Y: Integer read GetY write SetY;
          end;
          {$M-}
          TB = class(TA)
          end;
        implementation
        function TA.GetY: Integer; begin Result := FX end;
        procedure TA.SetY(V: Integer); begin FX := V end;
        procedure TA.Run; begin end;
        end.
        """;

    /// <summary>
    /// C-112 / CMP-406, CMP-408, ABI §10.1: the class RTTI blocks (VMT, ParentInfo, unit name, the three tables), referenced from
    /// the VMT's TypeInfo slot; a field getter is the offset, a static getter the code address, a virtual setter the VMT offset.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-406")]
    [Trait("Req", "CMP-408")]
    public void RttiClass() => Snapshot.Verify("RttiClass", Emit(RttiClassSource));

    private const string RttiScopeClass = """
          TS = class
          private
            FHidden: Integer;
          public
            procedure Visible;
            property Pub: Integer read FHidden;
          published
            FShown: TS;
            procedure Shown;
            property Pubd: Integer read FHidden;
          end;
        implementation
        procedure TS.Visible; begin end;
        procedure TS.Shown; begin end;
        end.
        """;

    /// <summary>C-169 / ABI-143 broad (default) scope: every field (with visibility 0), public and published methods and properties.</summary>
    [Fact]
    [Trait("Req", "ABI-143")]
    [Trait("Req", "CMP-408")]
    public void RttiClassBroad() => Snapshot.Verify("RttiClassBroad", Emit("unit M;\ninterface\ntype\n" + RttiScopeClass));

    /// <summary>C-169 / ABI-143 published scope (<c>{$RTTI PUBLISHED}</c> before the class): only the published members; the same entries.</summary>
    [Fact]
    [Trait("Req", "ABI-143")]
    [Trait("Req", "LANG-882")]
    public void RttiClassPublished() => Snapshot.Verify("RttiClassPublished", Emit("unit M;\ninterface\ntype\n{$RTTI PUBLISHED}\n" + RttiScopeClass));

    internal const string RttiStoredDefaultSource = """
        unit M;
        interface
        type
          TK = (K0, K1, K2);
          {$M+}
          TA = class
          private
            FA: Integer;
            FFlag: Boolean;
            FK: TK;
            function IsStatic: Boolean;
            function IsVirt: Boolean; virtual;
          published
            property A: Integer read FA write FA default 7;
            property B: Integer read FA stored False;
            property C: Integer read FA stored FFlag nodefault;
            property D: Integer read FA stored IsStatic;
            property E: Integer read FA stored IsVirt;
            property K: TK read FK write FK default K2;
          end;
          {$M-}
        implementation
        function TA.IsStatic: Boolean; begin Result := FFlag end;
        function TA.IsVirt: Boolean; begin Result := True end;
        end.
        """;

    /// <summary>C-217 / LANG-644, ABI §10.1: StoredKind 0 (Stored 0/1), 1 (field offset), 2 (code address), 3 (VMT offset); Default value or $8000000000000000.</summary>
    [Fact]
    [Trait("Req", "LANG-644")]
    public void RttiStoredDefault() => Snapshot.Verify("RttiStoredDefault", Emit(RttiStoredDefaultSource));

    /// <summary>
    /// C-173 / ABI-172, CMP-413: <c>_ReturnAddress</c> (with or without empty parentheses) is <c>call ptr
    /// @llvm.returnaddress(i32 0)</c>, the return address of the routine that contains it.
    /// </summary>
    [Fact]
    [Trait("Req", "ABI-172")]
    [Trait("Req", "CMP-413")]
    public void ReturnAddress() => Snapshot.Verify("ReturnAddress", Emit(ReturnAddressSource));

    internal const string ReturnAddressSource = """
        unit M;
        interface
        function Caller: Pointer;
        implementation
        function Caller: Pointer;
        begin
          Result := _ReturnAddress;
          if _ReturnAddress() = nil then
            Result := nil;
        end;
        end.
        """;

    /// <summary>
    /// C-174 / ABI-105, CMP-413: <c>_SetMXCSR(V)</c> stores <c>V</c> in a temporary and loads it with
    /// <c>llvm.x86.sse.ldmxcsr</c>; <c>_GetMXCSR</c> stores the register with <c>llvm.x86.sse.stmxcsr</c> into a temporary
    /// and reads it.
    /// </summary>
    [Fact]
    [Trait("Req", "ABI-105")]
    [Trait("Req", "CMP-413")]
    public void Mxcsr() => Snapshot.Verify("Mxcsr", Emit(MxcsrSource));

    internal const string MxcsrSource = """
        unit M;
        interface
        procedure Setup;
        function Current: Cardinal;
        implementation
        procedure Setup;
        begin
          _SetMXCSR($1F80);
          _SetMXCSR(_GetMXCSR or $0040);
        end;
        function Current: Cardinal;
        begin
          Result := _GetMXCSR;
        end;
        end.
        """;

    internal static readonly IReadOnlyDictionary<string, string> Sources = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["ReturnAddress"] = ReturnAddressSource,
        ["Mxcsr"] = MxcsrSource,
        ["RttiEnum"] = RttiEnumSource,
        ["RttiStoredDefault"] = RttiStoredDefaultSource,
        ["RttiClass"] = RttiClassSource,
        ["Thunk"] = ThunkSource,
        ["ThreadVar"] = ThreadVarSource,
        ["TryExcept"] = TryExceptSource,
        ["TryFinally"] = TryFinallySource,
        ["Vmt"] = VmtSource,
        ["ProcValue"] = ProcValueSource,
        ["Atomic"] = AtomicSource,
        ["ArrayConstructors"] = ArrayConstructorsSource,
        ["OpenArrays"] = OpenArraysSource,
        ["ReadText"] = ReadTextSource,
        ["ManagedRecords"] = ManagedRecordsSource,
        ["ByteStrings"] = ByteStringsSource,
        ["DynArrays"] = DynArraysSource,
        ["StringFunctions"] = StringFunctionsSource,
        ["Strings"] = StringsSource,
        ["StringOperators"] = StringOperatorsSource,
        ["RealIntrinsics"] = RealIntrinsicsSource,
        ["Reals"] = RealsSource,
        ["Typecasts"] = TypecastsSource,
        ["Pointers"] = PointersSource,
        ["AggregateAbi"] = AbiLoweringTests.Source,
        ["TypedConstants"] = TypedConstantsSource,
        ["Records"] = RecordsSource,
        ["StaticArrays"] = StaticArraysSource,
        ["BigSets"] = BigSetsSource,
        ["Enums"] = EnumsSource,
        ["Sets"] = SetsSource,
        ["ExternalDecl"] = ExternalDeclSource,
        ["Writeln"] = WritelnSource,
        ["Checks"] = ChecksSource,
        ["Add"] = AddSource,
        ["ShortCircuit"] = ShortCircuitSource,
        ["Division"] = DivisionSource,
        ["Conversions"] = ConversionsSource,
        ["For"] = ForSource,
        ["Case"] = CaseSource,
        ["Loops"] = LoopsSource,
        ["IfExit"] = IfExitSource,
        ["Calls"] = CallsSource,
        ["NestedFrame"] = NestedFrameSource,
    };

    /// <summary>C-199: every <c>linkonce_odr</c> global of every snapshot module has <c>, comdat</c> and its <c>$name = comdat any</c>.</summary>
    [Fact]
    [Trait("Req", "CMP-365")]
    public void LinkOnceOdrGlobals_HaveComdats()
    {
        var checkedCount = 0;
        foreach (var (name, source) in Sources)
        {
            var lines = Emit(source).ReplaceLineEndings("\n").Split('\n');
            foreach (var line in lines.Where(l => l.StartsWith('@') && l.Contains(" = linkonce_odr ", StringComparison.Ordinal)))
            {
                var global = line[1..line.IndexOf(" = ", StringComparison.Ordinal)];
                Assert.True(line.Contains(", comdat, align", StringComparison.Ordinal), $"{name}: {line}");
                Assert.Contains("$" + global + " = comdat any", lines);
                checkedCount++;
            }
        }

        Assert.True(checkedCount > 0, "no linkonce_odr global in the snapshot sources");
    }

    [Fact]
    [Trait("Req", "LANG-462")]
    [Trait("Req", "LANG-463")]
    [Trait("Req", "LANG-470")]
    [Trait("Req", "CMP-430")]
    [Trait("Req", "CMP-431")]
    [Trait("Req", "CMP-433")]
    public void ExternalDecl()
    {
        var text = Emit(ExternalDeclSource);

        Assert.Contains("declare dllimport i32 @GetCurrentProcessId()\n", text);
        Assert.Contains("declare dllimport void @Sleep(i32)\n", text);
        Assert.Contains("declare dllimport void @ExitProcess(i32)\n", text);
        Assert.Contains("declare i32 @Helper(i32)\n", text);
        Assert.Contains("declare i32 @other_helper(i32)\n", text);
        Assert.Contains("define i32 @impl_sym(i32 %x) #0 {", text);
        Assert.Contains("define i32 @\"M.Exported\"(i32 %x) #0 {", text);
        Assert.Contains("define i32 @\"M.Run\"() #0 {", text);
        Assert.Contains("  call void @Sleep(i32 10)\n", text);
        Assert.Contains("@_counter = global i32 7, align 4\n", text);
        Assert.Contains("@_fltused = global i32 39029, align 4\n", text);
        Assert.Contains("@\"M.Plain\" = internal global i32 zeroinitializer, align 4\n", text);
        Snapshot.Verify("ExternalDecl", text);
    }

    [Fact]
    [Trait("Req", "CMP-390")]
    [Trait("Req", "CMP-391")]
    [Trait("Req", "CMP-394")]
    [Trait("Req", "CMP-395")]
    public void TryExcept() => Snapshot.Verify("TryExcept", Emit(TryExceptSource));

    /// <summary>CMP-390/391/394/395 in <see cref="TryExceptSource"/>: the shapes the spec and the C-079/C-188 spike prescribe.</summary>
    [Fact]
    [Trait("Req", "CMP-391")]
    [Trait("Req", "CMP-394")]
    public void TryExcept_UsesTheCmp391Shape()
    {
        var text = Emit(TryExceptSource);

        Assert.Contains("define void @\"M.Fail\"(i32 %N) #0 {", text);                      // no EH scope: plain calls
        Assert.Contains("  call void @\"System._Raise\"(ptr %t", text);
        Assert.Contains(", ptr null) #1\n  unreachable\n", text);
        Assert.Contains("define i32 @\"M.Run\"(i32 %N) #0 personality ptr @__C_specific_handler {", text);
        Assert.Contains("declare i32 @__C_specific_handler(...)\n", text);
        Assert.Contains("  invoke void @\"M.Fail\"(i32 %t", text);
        Assert.Contains("  %cs.2 = catchswitch within none [label %ex.pad.2] unwind label %ex.dispatch.1\n", text);
        Assert.Contains("  %cs.1 = catchswitch within none [label %ex.pad.1] unwind to caller\n", text);
        Assert.Contains("  %cp.2 = catchpad within %cs.2 [ptr @\"System._SehFilter\"]\n  catchret from %cp.2 to label %ex.body.2\n", text);
        Assert.Contains("call ptr @\"System._BeginCatch\"()", text);
        Assert.Contains("call i8 @\"System._IsClass\"(ptr %t", text);                     // never raises: a call (CMP-394)
        Assert.Contains("  invoke void @\"System._ReRaise\"() #1\n", text);
        // C-188: the inner cleanup has an enclosing scope, so its _EndCatch is an invoke in the funclet; the outer one a call
        Assert.Contains("  %cl.2 = cleanuppad within none []\n  invoke void @\"System._EndCatch\"() [ \"funclet\"(token %cl.2) ]\n"
            + "          to label %cont.", text);
        Assert.Contains("  cleanupret from %cl.2 unwind label %ex.dispatch.1\n", text);
        Assert.Contains("  call void @\"System._EndCatch\"() [ \"funclet\"(token %cl.1) ]\n  cleanupret from %cl.1 unwind to caller\n", text);
    }

    /// <summary>
    /// C-193 / ABI-125: every routine of unit System whose name starts with <c>_</c> is defined with the <c>noinline</c> group
    /// <c>#4</c> (<c>_Raise</c> stays the debugger's raise hook at every optimisation level); other System routines keep theirs.
    /// </summary>
    [Fact]
    [Trait("Req", "ABI-125")]
    public void SystemHelpersAreNoInline()
    {
        var text = RtlSystemCompilesTests.EmitSystem();

        var helpers = text.Split('\n').Where(l => l.StartsWith("define ", StringComparison.Ordinal) && l.Contains(" @\"System._", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(helpers);
        Assert.All(helpers, l => Assert.EndsWith(" #4 {", l.Replace(" personality ptr @__C_specific_handler", "", StringComparison.Ordinal)));
        Assert.Contains(helpers, l => l.StartsWith("define void @\"System._Raise\"(ptr %Obj, ptr %Addr) #4 ", StringComparison.Ordinal));
        Assert.Contains("define void @\"System.TObject.Free\"(ptr %Self) #0 ", text);
        Assert.Contains("define i32 @memcmp(", text);
        Assert.DoesNotContain(text.Split('\n'), l => l.StartsWith("define i32 @memcmp(", StringComparison.Ordinal) && !l.Contains(" #2 ", StringComparison.Ordinal));
        Assert.Contains("attributes #4 = { noinline uwtable \"frame-pointer\"=\"all\" }\n", text);

        var unit = Emit("unit M; interface procedure _Own; implementation procedure _Own; begin end; end.");
        Assert.Contains("define void @\"M._Own\"() #0 {", unit);                                  // only unit System
        Assert.DoesNotContain("attributes #4", unit);
    }

    /// <summary>
    /// C-166 / ABI-125, CMP-302: a fixture unit System (no RTL file): its <c>_…</c> routines get the <c>noinline</c> group
    /// <c>#4</c>, the others the default group <c>#0</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "ABI-125")]
    [Trait("Req", "CMP-302")]
    public void SystemNoInline() => Snapshot.Verify("SystemNoInline", EmitSystemUnit(SystemNoInlineSource));

    internal const string SystemNoInlineSource = """
        unit System;
        interface
        var Input, Output: Text;
        procedure _Helper(Code: Integer);
        procedure _Raise(Obj: Pointer; Addr: Pointer);
        procedure Plain;
        implementation
        procedure _Helper(Code: Integer); begin end;
        procedure _Raise(Obj: Pointer; Addr: Pointer); begin _Helper(1) end;
        procedure Plain; begin _Helper(2) end;
        end.
        """;

    /// <summary>
    /// C-166 / ABI-132, CMP-410, CMP-411: in <c>{$I+}</c> state <c>_IOCheck</c> follows every text-I/O statement (Write, Read
    /// and a call of a System routine whose first parameter is <c>var … : Text</c>); in <c>{$I-}</c> state none does and
    /// <c>IOResult</c> is an ordinary call.
    /// </summary>
    [Fact]
    [Trait("Req", "ABI-132")]
    [Trait("Req", "CMP-410")]
    [Trait("Req", "CMP-411")]
    public void IOCheck() => Snapshot.Verify("IOCheck", EmitAgainst(IOCheckSystemSource, IOCheckSource));

    internal const string IOCheckSystemSource = """
        unit System;
        interface
        var Input, Output: Text;
        function IOResult: Integer;
        procedure Reset(var F: Text);
        implementation
        function IOResult: Integer; begin Result := 0 end;
        procedure Reset(var F: Text); begin end;
        end.
        """;

    internal const string IOCheckSource = """
        unit M;
        interface
        procedure Checked(var F: Text; var I: Integer);
        function Unchecked(var F: Text; var I: Integer): Integer;
        implementation
        {$I+}
        procedure Checked(var F: Text; var I: Integer);
        begin
          Reset(F);
          Read(F, I);
          Writeln(F, I);
        end;
        {$I-}
        function Unchecked(var F: Text; var I: Integer): Integer;
        begin
          Reset(F);
          Read(F, I);
          Writeln(F, I);
          Result := IOResult;
        end;
        end.
        """;

    /// <summary>Emits a stand-alone unit System (bound without another System).</summary>
    internal static string EmitSystemUnit(string source)
    {
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", source));
        Assert.Empty(tree.Diagnostics);
        var result = Binder.BindCompilationUnit(tree, new BuiltInTypes());
        Assert.Empty(Format(result.Diagnostics));
        var writer = new LlvmTextWriter();
        IrEmitter.EmitUnit(Lowerer.Lower(result), writer);
        return writer.GetText();
    }

    /// <summary>Emits unit <paramref name="source"/> bound against the System unit <paramref name="system"/>.</summary>
    internal static string EmitAgainst(string system, string source)
    {
        var types = new BuiltInTypes();
        var bound = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", system)), types);
        Assert.Empty(Format(bound.Diagnostics));
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\M.pas", source));
        Assert.Empty(tree.Diagnostics);
        var result = Binder.BindCompilationUnit(tree, types, bound);
        Assert.Empty(Format(result.Diagnostics));
        var writer = new LlvmTextWriter();
        IrEmitter.EmitUnit(Lowerer.Lower(result), writer);
        return writer.GetText();
    }

    /// <summary>
    /// C-093: a routine's managed cleanup scope (CMP-325 rule 2) is its outermost EH scope (CMP-393): raising calls are invokes
    /// unwinding to <c>cleanup.pad</c> (CMP-394), a try statement's dispatch and cleanup unwind there, and the funclet finalises
    /// the locals; a routine whose body has no raising call keeps a plain function.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-393")]
    [Trait("Req", "CMP-394")]
    [Trait("Req", "LANG-840")]
    public void ManagedCleanup_IsTheOutermostEhScope()
    {
        var text = Emit("""
            unit M;
            interface
            procedure P;
            function Q: Integer;
            implementation
            procedure P;
            var S: string;
            begin
              S := 'x';
              try
                Q;
              except
              end;
            end;
            function Q: Integer;
            var S: string;
            begin
              Result := Length(S);
            end;
            end.
            """);

        var p = text[text.IndexOf("define void @\"M.P\"", StringComparison.Ordinal)..];
        p = p[..p.IndexOf("\n}\n", StringComparison.Ordinal)];
        Assert.StartsWith("define void @\"M.P\"() #0 personality ptr @__C_specific_handler {", p);
        Assert.Contains("  invoke void @\"System._UStrAsg\"(ptr %S.addr, ptr getelementptr", p);
        Assert.Contains("          to label %cont.1 unwind label %cleanup.pad\n", p);
        Assert.Contains("  %cs.1 = catchswitch within none [label %ex.pad.1] unwind label %cleanup.pad\n", p);
        Assert.Contains("  invoke void @\"System._EndCatch\"() [ \"funclet\"(token %cl.1) ]\n          to label %cont.", p);
        Assert.Contains("  cleanupret from %cl.1 unwind label %cleanup.pad\n", p);
        Assert.Contains("cleanup:\n  call void @\"System._UStrClr\"(ptr %S.addr)\n  br label %exit\n", p);
        Assert.EndsWith("cleanup.pad:\n  %cl = cleanuppad within none []\n  call void @\"System._UStrClr\"(ptr %S.addr) [ \"funclet\"(token %cl) ]\n"
            + "  cleanupret from %cl unwind to caller", p);
        var q = text[text.IndexOf("define i32 @\"M.Q\"", StringComparison.Ordinal)..];
        q = q[..q.IndexOf("\n}\n", StringComparison.Ordinal)];
        Assert.StartsWith("define i32 @\"M.Q\"() #0 {", q);
        Assert.DoesNotContain("cleanuppad", q);
    }

    [Fact]
    [Trait("Req", "CMP-392")]
    [Trait("Req", "LANG-810")]
    public void TryFinally() => Snapshot.Verify("TryFinally", Emit(TryFinallySource));

    /// <summary>CMP-392 in <see cref="TryFinallySource"/>: outlined <c>$fin</c> routines on the frame, called on every way out.</summary>
    [Fact]
    [Trait("Req", "CMP-392")]
    [Trait("Req", "LANG-810")]
    public void TryFinally_OutlinesTheFinallyPartAndCallsItOnEveryWayOut()
    {
        var text = Emit(TryFinallySource);

        Assert.Contains("%\"M.Run.$frame\" = type { ptr, i32, i32 }\n", text);                  // link, Result, I
        Assert.Contains("define internal void @\"M.Run.$fin1\"(ptr %$link) #3 {", text);
        Assert.Contains("define internal void @\"M.Run.$fin2\"(ptr %$link) #3 personality ptr @__C_specific_handler {", text);
        Assert.Contains("attributes #3 = { noinline optnone uwtable \"frame-pointer\"=\"all\" }\n", text);
        Assert.Contains("  %cl.3 = cleanuppad within none []\n  call void @\"M.Run.$fin1\"(ptr %$frame) [ \"funclet\"(token %cl.3) ]\n"
            + "  cleanupret from %cl.3 unwind to caller\n", text);
        // normal completion, Continue, Break and Exit: four direct calls of $fin1, then the jump
        Assert.Equal(5, text.Split("call void @\"M.Run.$fin1\"(ptr %$frame)").Length - 1);
        Assert.Contains("  invoke void @\"M.Step\"(i32 0)\n          to label %cont.", text);
    }

    [Fact]
    [Trait("Req", "CMP-366")]
    [Trait("Req", "CMP-367")]
    [Trait("Req", "LANG-622")]
    public void Vmt() => Snapshot.Verify("Vmt", Emit(VmtSource));

    /// <summary>
    /// CMP-366 / ABI-073/074: slot order and contents — the abstract slot holds <c>System._AbstractError</c> in TA only, the
    /// overrides replace their slots, the <c>dynamic</c> method appends one; the interface classes' VMTs are external, TC's internal.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-366")]
    [Trait("Req", "LANG-622")]
    public void Vmt_SlotsFollowOverridesAndAbstractMethods()
    {
        var text = Emit(VmtSource);

        Assert.Contains("[2 x ptr] [ptr @\"M.TA.P\", ptr @\"System._AbstractError\"] }, align 8\n", text);
        Assert.Contains("[3 x ptr] [ptr @\"M.TB.P\", ptr @\"M.TB.Q\", ptr @\"M.TB.R\"] }, align 8\n", text);
        Assert.Contains("[3 x ptr] [ptr @\"M.TB.P\", ptr @\"M.TB.Q\", ptr @\"M.TC.R\"] }, align 8\n", text);
        Assert.Contains("@\"M.TA.$vmt\" = constant", text);
        Assert.Contains("@\"M.TC.$vmt\" = internal constant { ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [3 x ptr] } { ptr @\"M.TB.$vmt\", i64 16,", text);
        Assert.Contains("declare void @\"System._AbstractError\"() #1\n", text);
    }

    /// <summary>
    /// C-190 / CMP-362, 365: an anonymous structural type spelt the same in a class field, a record field and a global is
    /// one type text and gets one RTTI block per module, whichever declaration reaches it first.
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-362")]
    [InlineData("array of string")]
    [InlineData("array[0..1] of string")]
    [InlineData("array of array of string")]
    [InlineData("record S: string; end")]
    public void AnonymousTypeInfo_IsDefinedOncePerModule(string type)
    {
        var text = Emit($$"""
            unit M;
            interface
            implementation
            type
              TLeaf = class Tags: {{type}}; end;
              TRec = record Tags: {{type}}; end;
            var
              A: {{type}};
              B: TRec;
              C: TLeaf;
            procedure Use;
            var L: {{type}};
            begin
            end;
            end.
            """);

        var definitions = text.Split('\n').Where(l => l.StartsWith($"@\"M.$rtti.{type}\" = ", StringComparison.Ordinal)).ToList();
        Assert.Single(definitions);
    }

    [Fact]
    [Trait("Req", "CMP-414")]
    [Trait("Req", "ABI-180")]
    [Trait("Req", "ABI-181")]
    public void ThreadVar() => Snapshot.Verify("ThreadVar", Emit(ThreadVarSource));

    /// <summary>CMP-414 / ABI-180: the descriptor, its registration first in <c>$init</c>, the four-instruction access; no module global per threadvar.</summary>
    [Fact]
    [Trait("Req", "CMP-414")]
    [Trait("Req", "ABI-180")]
    [Trait("Req", "ABI-181")]
    public void ThreadVar_UsesTheCmp414Shape()
    {
        var text = Emit(ThreadVarSource);

        Assert.Contains("@\"M.$tls\" = global { i64, i64 } { i64 16, i64 -1 }, align 8\n", text);
        Assert.Contains("define void @\"M.$init\"() #0 {\nentry:\n  call void @\"System._TlsRegister\"(ptr @\"M.$tls\")\n", text);
        Assert.Contains("call ptr @\"System._TlsBase\"()\n", text);
        Assert.Matches(@"  %(t\d+) = call ptr @""System._TlsBase""\(\)\n  %(t\d+) = load i64, ptr getelementptr inbounds \(i8, ptr @""M.\$tls"", i64 8\), align 8\n"
            + @"  %(t\d+) = getelementptr inbounds i8, ptr %\1, i64 %\2\n  %t\d+ = getelementptr inbounds i8, ptr %\3, i64 8\n", text);
        Assert.DoesNotContain("@\"M.Depth\"", text);
        Assert.DoesNotContain("@\"M.Sum\"", text);
    }

    [Fact]
    [Trait("Req", "CMP-412")]
    [Trait("Req", "ABI-172")]
    public void Atomic() => Snapshot.Verify("Atomic", Emit(AtomicSource));

    /// <summary>CMP-412: the atomic instructions with <c>seq_cst</c> and natural alignment; no helper call, no other intrinsic.</summary>
    [Fact]
    [Trait("Req", "CMP-412")]
    [Trait("Req", "ABI-172")]
    public void Atomic_UsesAtomicInstructions()
    {
        var text = Emit(AtomicSource);

        Assert.Contains("  %t1 = atomicrmw add ptr %i, i32 1 seq_cst, align 4\n  %t2 = add i32 %t1, 1\n", text);
        Assert.Contains("  %t7 = atomicrmw sub ptr %q, i64 %t6 seq_cst, align 8\n  %t8 = sub i64 %t7, %t6\n", text);
        Assert.Contains("  %t10 = atomicrmw add ptr @\"M.G\", i32 5 seq_cst, align 4\n", text);
        Assert.Contains("  %t2 = atomicrmw xchg ptr %p, ptr %t1 seq_cst, align 8\n", text);
        Assert.Contains("  %t4 = cmpxchg ptr %p, ptr %t3, ptr null seq_cst seq_cst, align 8\n  %t5 = extractvalue { ptr, i1 } %t4, 0\n", text);
        Assert.Contains("  %t6 = atomicrmw add ptr %t, i64 8 seq_cst, align 8\n  %t7 = add i64 %t6, 8\n  %t8 = inttoptr i64 %t7 to ptr\n", text);
        Assert.Contains("  %t9 = atomicrmw xchg ptr %c, i32 7 seq_cst, align 4\n", text);
        Assert.Contains("  %t10 = cmpxchg ptr %c, i32 7, i32 1 seq_cst seq_cst, align 4\n", text);
        Assert.DoesNotContain("call", text);
    }

    [Fact]
    [Trait("Req", "CMP-412")]
    public void StringFunctions() => Snapshot.Verify("StringFunctions", Emit(StringFunctionsSource));

    /// <summary>C-062: the helpers are declared with the rtl-abi.md §9.3/§9.10 signatures; <c>UpCase</c> needs no call.</summary>
    [Fact]
    [Trait("Req", "CMP-412")]
    public void StringFunctions_DeclareTheAbiHelpers()
    {
        var text = Emit(StringFunctionsSource);

        Assert.Contains("declare void @\"System._UStrSetLen\"(ptr, i32)\n", text);
        Assert.Contains("declare void @\"System._UStrDelete\"(ptr, i32, i32)\n", text);
        Assert.Contains("declare void @\"System._UStrInsert\"(ptr, ptr, i32)\n", text);
        Assert.Contains("declare void @\"System._UStrCopy\"(ptr, ptr, i32, i32)\n", text);
        Assert.Contains("declare i32 @\"System._UStrPos\"(ptr, ptr, i32)\n", text);
        Assert.Contains("declare void @\"System._StrInt\"(ptr, i64, i32)\n", text);
        Assert.Contains("declare void @\"System._StrFloat\"(ptr, double, i32, i32)\n", text);
        Assert.Contains("declare i64 @\"System._ValInt\"(ptr, ptr)\n", text);
        Assert.Contains("declare double @\"System._ValFloat\"(ptr, ptr)\n", text);
        Assert.Contains(", i32 2147483647)\n", text);
        Assert.Contains("(ptr %s, double %t", text);
        Assert.DoesNotContain("UpCase", text);
    }

    [Fact]
    [Trait("Req", "CMP-410")]
    [Trait("Req", "CMP-320")]
    public void Writeln() => Snapshot.Verify("Writeln", Emit(WritelnSource));

    /// <summary>The CMP-410 example (<c>{$I-}</c>) and the CMP-320 <c>'Hello'</c> block verbatim; <c>{$I+}</c> adds <c>_IOCheck</c>.</summary>
    [Fact]
    [Trait("Req", "CMP-410")]
    [Trait("Req", "CMP-320")]
    public void Writeln_MatchesTheSpecExamples()
    {
        var text = Emit(WritelnSource);
        const string xEquals = "ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [3 x i16] }, ptr @.str.0, i32 0, i32 5)";

        Assert.Contains(
            "  call void @\"System._WriteUStr\"(ptr @\"System.Output\", " + xEquals + ", i32 0)\n" +
            "  %t1 = load i32, ptr %x.addr, align 4\n" +
            "  %t2 = sext i32 %t1 to i64\n" +
            "  call void @\"System._WriteInt\"(ptr @\"System.Output\", i64 %t2, i32 5)\n" +
            "  call void @\"System._WriteLn\"(ptr @\"System.Output\")\n" +
            "  call void @\"System._WriteEnd\"(ptr @\"System.Output\")\n", text);
        Assert.Contains(
            "@.str.1 = private unnamed_addr constant { i32, i32, i16, i16, i32, [6 x i16] } { i32 -1, i32 5, i16 1200, i16 2, i32 0, " +
            "[6 x i16] [i16 72, i16 101, i16 108, i16 108, i16 111, i16 0] }, align 16\n", text);
        Assert.Contains("@\"System.Output\" = external global [624 x i8], align 8\n", text);
        Assert.Contains("call void @\"System._WriteUStr\"(ptr @\"System.Output\", ptr null, i32 0)", text);
        Assert.Contains("\n@.str.2 = private unnamed_addr constant", text);  // 'x=', 'Hello', 'Grüße 😀': each emitted once
        Assert.DoesNotContain("@.str.3", text);
        var show = text[text.IndexOf("@\"M.Show\"", StringComparison.Ordinal)..text.IndexOf("@\"M.ToFile\"", StringComparison.Ordinal)];
        Assert.DoesNotContain("_IOCheck", show);
        Assert.Contains("  call void @\"System._WriteEnd\"(ptr %T)\n  call void @\"System._IOCheck\"()\n", text);
    }

    [Fact]
    [Trait("Req", "CMP-314")]
    [Trait("Req", "CMP-312")]
    public void Checks() => Snapshot.Verify("Checks", Emit(ChecksSource));

    /// <summary>CMP-312 <c>{$Q+}</c> pattern and CMP-314 <c>{$R+}</c> pattern; nothing is checked outside the switches.</summary>
    [Fact]
    [Trait("Req", "CMP-312")]
    [Trait("Req", "CMP-314")]
    public void Checks_FollowTheSwitches()
    {
        var text = Emit(ChecksSource);

        Assert.Contains("call { i32, i1 } @llvm.sadd.with.overflow.i32(i32 ", text);
        Assert.Contains("call { i32, i1 } @llvm.umul.with.overflow.i32(i32 ", text);
        Assert.Contains("call { i64, i1 } @llvm.ssub.with.overflow.i64(i64 0, i64 ", text);
        Assert.Contains("call void @\"System._OverflowError\"() #1\n  unreachable\n", text);
        Assert.Contains("call void @\"System._RangeError\"() #1\n  unreachable\n", text);
        var wrap = text[text.IndexOf("@\"M.Wrap\"", StringComparison.Ordinal)..];
        Assert.DoesNotContain("with.overflow", wrap);
        Assert.DoesNotContain("_RangeError", wrap[..wrap.IndexOf("\n}\n", StringComparison.Ordinal)]);
        Assert.DoesNotContain("Error", Emit(AddSource));
    }

    [Fact]
    [Trait("Req", "CMP-345")]
    public void NestedFrame() => Snapshot.Verify("NestedFrame", Emit(NestedFrameSource));

    [Fact]
    [Trait("Req", "CMP-310")]
    public void Calls() => Snapshot.Verify("Calls", Emit(CallsSource));

    /// <summary>CMP-345: frame type, static link as first parameter, captured variable reached through the link.</summary>
    [Fact]
    [Trait("Req", "CMP-345")]
    public void NestedRoutine_ReachesCapturedVariableThroughItsLink()
    {
        var text = Emit(NestedFrameSource);

        Assert.Contains("%\"M.Outer.$frame\" = type { ptr, i32 }\n", text);
        Assert.Contains("define internal void @\"M.Outer.Add\"(ptr %$link, i32 %x) #0 {", text);
        Assert.Contains("getelementptr inbounds %\"M.Outer.$frame\", ptr %$link, i32 0, i32 1\n", text);
        Assert.Contains("call void @\"M.Outer.Add\"(ptr %$frame, i32 2)", text);
    }

    [Fact]
    [Trait("Req", "LANG-210")]
    [Trait("Req", "LANG-212")]
    [Trait("Req", "CMP-314")]
    public void Enums() => Snapshot.Verify("Enums", Emit(EnumsSource));

    [Fact]
    [Trait("Req", "CMP-335")]
    [Trait("Req", "LANG-252")]
    public void Sets() => Snapshot.Verify("Sets", Emit(SetsSource));

    [Fact]
    [Trait("Req", "CMP-336")]
    [Trait("Req", "LANG-252")]
    public void BigSets() => Snapshot.Verify("BigSets", Emit(BigSetsSource));

    [Fact]
    [Trait("Req", "CMP-308")]
    [Trait("Req", "CMP-331")]
    public void StaticArrays() => Snapshot.Verify("StaticArrays", Emit(StaticArraysSource));

    [Fact]
    [Trait("Req", "CMP-306")]
    [Trait("Req", "CMP-307")]
    public void Records() => Snapshot.Verify("Records", Emit(RecordsSource));

    /// <summary>CMP-306 struct types, CMP-307 field GEPs, CMP-309 alignments of packed fields, CMP-326 copies.</summary>
    [Fact]
    [Trait("Req", "CMP-306")]
    [Trait("Req", "CMP-307")]
    [Trait("Req", "CMP-309")]
    [Trait("Req", "CMP-326")]
    public void Records_StructTypesFieldAddressesAndCopies()
    {
        var text = Emit(RecordsSource);

        Assert.Contains("%\"M.TRect\" = type { %\"M.TPoint\", %\"M.TPoint\", i16 }\n", text);
        Assert.Contains("%\"M.TPoint\" = type { i32, i32 }\n", text);
        Assert.Contains("%\"M.TPacked\" = type <{ i8, i32 }>\n", text);
        Assert.Contains("%\"M.TFour\" = type <{ i8, [3 x i8], i64 }>\n", text);
        Assert.Contains("%\"M.TLine\" = type { [2 x %\"M.TPoint\"], %\"M.TFour\" }\n", text);
        // f (ABI alignment 4, LLVM alignment 1) at offset 20 of the frame: packed with explicit padding (CMP-345 + CMP-306)
        Assert.Contains("%\"M.Outer.$frame\" = type <{ ptr, %\"M.TPoint\", i8, [3 x i8], %\"M.TFour\" }>\n", text);
        Assert.Contains("getelementptr inbounds %\"M.Outer.$frame\", ptr %$link, i32 0, i32 4\n", text);
        Assert.Contains("getelementptr inbounds %\"M.TPoint\", ptr %p, i32 0, i32 0\n", text);
        Assert.Contains("getelementptr inbounds %\"M.TFour\", ptr %f, i32 0, i32 2\n", text);
        Assert.Contains(", ptr align 4 %r, i64 20, i1 false)", text);
        Assert.Contains("@\"M.G\" = global %\"M.TRect\" zeroinitializer, align 4\n", text);
        var copy = text[text.IndexOf("define void @\"M.Copy\"", StringComparison.Ordinal)..text.IndexOf("define void @\"M.Line\"", StringComparison.Ordinal)];
        Assert.Contains("load i64, ptr %t", copy);                                        // pt := local.B (8 bytes)
        Assert.Matches(@"store i32 %t\d+, ptr %t\d+, align 1\n", copy);                  // q.I in a packed record
        Assert.Matches(@"store i64 7, ptr %t\d+, align 4\n", copy);                       // f.D at offset 4 under {$A4}
    }

    [Fact]
    [Trait("Req", "CMP-341")]
    [Trait("Req", "LANG-344")]
    public void TypedConstants() => Snapshot.Verify("TypedConstants", Emit(TypedConstantsSource));

    /// <summary>CMP-341 verbatim for the interface table; records with padding; routine-local constants; initialised variables.</summary>
    [Fact]
    [Trait("Req", "CMP-341")]
    [Trait("Req", "LANG-344")]
    [Trait("Req", "LANG-522")]
    public void TypedConstants_AreConstantGlobalsWithTheirValues()
    {
        var text = Emit(TypedConstantsSource);

        Assert.Contains("@\"M.Table\" = constant [3 x i32] [i32 1, i32 2, i32 3], align 4\n", text);
        Assert.Contains("@\"M.Origin\" = constant %\"M.TPoint\" { i32 5, i32 -6 }, align 4\n", text);
        Assert.Contains("@\"M.Grid\" = global [2 x [2 x i8]] [[2 x i8] [i8 1, i8 2], [2 x i8] [i8 3, i8 4]], align 1\n", text);
        Assert.Contains("@\"M.Four\" = internal constant %\"M.TFour\" <{ i8 1, [3 x i8] zeroinitializer, i64 2 }>, align 4\n", text);
        Assert.Contains("@\"M.Half\" = internal constant %\"M.TPoint\" { i32 9, i32 zeroinitializer }, align 4\n", text);
        Assert.Contains("@\"M.Flags\" = internal constant [2 x i8] [i8 0, i8 1], align 1\n", text);
        Assert.Contains("@\"M.Small\" = internal constant i8 -127, align 1\n", text);
        Assert.Contains("@\"M.Start\" = internal global %\"M.TPoint\" { i32 1, i32 2 }, align 4\n", text);
        Assert.Contains("@\"M.Pick.Local\" = internal constant [2 x i16] [i16 10, i16 20], align 2\n", text);
        Assert.Contains("getelementptr inbounds [3 x i32], ptr @\"M.Table\", i64 0, i64 ", text);
        Assert.Contains("load i64, ptr @\"M.Origin\", align 4\n", text);                  // Start := Origin (8 bytes)
    }

    /// <summary>CMP-308 element addresses; CMP-326 copies; CMP-331 checks only under {$R+} and only where the index can leave the bounds.</summary>
    [Fact]
    [Trait("Req", "CMP-308")]
    [Trait("Req", "CMP-326")]
    [Trait("Req", "CMP-331")]
    public void StaticArrays_ElementAddressesAndCopies()
    {
        var text = Emit(StaticArraysSource);

        Assert.Contains("getelementptr inbounds [4 x i32], ptr %local.addr, i64 0, i64 %t", text);
        Assert.Contains("getelementptr inbounds [3 x [10 x i8]], ptr @\"M.G\", i64 0, i64 2\n", text);
        Assert.Contains("call void @llvm.memcpy.p0.p0.i64(ptr align 4 %v, ptr align 4 %local.addr, i64 16, i1 false)", text);
        Assert.Contains("call void @llvm.memmove.p0.p0.i64(ptr align 1 %t", text);
        Assert.Contains("@\"M.G\" = global [3 x [10 x i8]] zeroinitializer, align 1\n", text);
        var copy2 = text[text.IndexOf("@\"M.Copy2\"", StringComparison.Ordinal)..text.IndexOf("@\"M.Grid\"", StringComparison.Ordinal)];
        Assert.Contains("load i32, ptr %q, align 2", copy2);
        var grid = text[text.IndexOf("define void @\"M.Grid\"", StringComparison.Ordinal)..];
        Assert.Equal(2, grid.Split("call void @\"System._RangeError\"").Length - 1);
    }

    /// <summary>CMP-336: helpers with a temporary destination, inline <c>in</c> guarded by <c>x &lt;u 256</c>, 32-byte copies.</summary>
    [Fact]
    [Trait("Req", "CMP-336")]
    public void BigSets_UseHelpersAndInlineBitTests()
    {
        var text = Emit(BigSetsSource);

        Assert.Contains("call void @\"System._SetUnion\"(ptr %$tmp", text);
        Assert.Contains("call void @\"System._SetInter\"(ptr %$tmp", text);
        Assert.Contains("call void @\"System._SetDiff\"(ptr %$tmp", text);
        Assert.Contains("call i8 @\"System._SetEq\"(ptr ", text);
        Assert.Contains("call i8 @\"System._SetLe\"(ptr ", text);
        Assert.Contains("call void @\"System._SetRange\"(ptr %$tmp", text);
        Assert.Contains("call void @llvm.memset.p0.i64(ptr align 1 %$tmp", text);
        Assert.Contains("call void @llvm.memcpy.p0.p0.i64(ptr align 1 %r, ptr align 1 ", text);
        Assert.Contains("icmp ult i32 ", text);
        Assert.Contains("lshr i32 ", text);
        Assert.Contains("getelementptr inbounds i8, ptr ", text);
        Assert.Contains("@\"M.Vowels\" = global [32 x i8] [i8 0, ", text);
        Assert.Contains("= private unnamed_addr constant [32 x i8] ", text);
    }

    [Fact]
    [Trait("Req", "LANG-480")]
    [Trait("Req", "LANG-484")]
    [Trait("Req", "CMP-378")]
    [Trait("Req", "CMP-433")]
    public void ProcValue() => Snapshot.Verify("ProcValue", Emit(ProcValueSource));

    /// <summary>CMP-378: a procedural value is the function address; a call loads it and calls through it with the signature's types.</summary>
    [Fact]
    [Trait("Req", "CMP-378")]
    [Trait("Req", "CMP-433")]
    public void ProcValue_StoresAddressesAndCallsThroughThem()
    {
        var text = Emit(ProcValueSource);

        Assert.Contains("  store ptr @\"M.Add\", ptr %p.addr, align 8\n", text);
        Assert.Contains("@\"M.Table\" = internal global [2 x ptr] zeroinitializer, align 8\n", text);
        Assert.Matches(@"  %t\d+ = load ptr, ptr %f\.addr, align 8\n", text);
        Assert.Matches(@"  %t\d+ = call i32 %t\d+\(i32 %t\d+, i32 %t\d+\)\n", text);
        Assert.Matches(@"  call void %t\d+\(i32 %t\d+\)\n", text);
        Assert.DoesNotContain("stdcall", text);
        Assert.DoesNotContain("x86_stdcallcc", text);
    }

    [Fact]
    [Trait("Req", "CMP-340")]
    [Trait("Req", "CMP-342")]
    public void Pointers() => Snapshot.Verify("Pointers", Emit(PointersSource));

    [Fact]
    [Trait("Req", "LANG-290")]
    [Trait("Req", "LANG-205")]
    [Trait("Req", "CMP-313")]
    [Trait("Req", "CMP-412")]
    public void Typecasts() => Snapshot.Verify("Typecasts", Emit(TypecastsSource));

    [Fact]
    [Trait("Req", "CMP-313")]
    [Trait("Req", "CMP-315")]
    [Trait("Req", "CMP-316")]
    public void Reals() => Snapshot.Verify("Reals", Emit(RealsSource));

    [Fact]
    [Trait("Req", "CMP-410")]
    [Trait("Req", "CMP-412")]
    public void RealIntrinsics() => Snapshot.Verify("RealIntrinsics", Emit(RealIntrinsicsSource));

    [Fact]
    [Trait("Req", "CMP-320")]
    [Trait("Req", "CMP-324")]
    [Trait("Req", "CMP-325")]
    public void Strings() => Snapshot.Verify("Strings", Emit(StringsSource));

    [Fact]
    [Trait("Req", "CMP-324")]
    [Trait("Req", "LANG-243")]
    [Trait("Req", "LANG-244")]
    public void StringOperators() => Snapshot.Verify("StringOperators", Emit(StringOperatorsSource));

    [Fact]
    [Trait("Req", "CMP-320")]
    [Trait("Req", "CMP-324")]
    [Trait("Req", "LANG-245")]
    [Trait("Req", "LANG-246")]
    public void ByteStrings() => Snapshot.Verify("ByteStrings", Emit(ByteStringsSource));

    [Fact]
    [Trait("Req", "CMP-330")]
    [Trait("Req", "CMP-331")]
    [Trait("Req", "CMP-405")]
    [Trait("Req", "ABI-044")]
    public void DynArrays() => Snapshot.Verify("DynArrays", Emit(DynArraysSource));

    [Fact]
    [Trait("Req", "CMP-325")]
    [Trait("Req", "ABI-110")]
    public void ManagedRecords() => Snapshot.Verify("ManagedRecords", Emit(ManagedRecordsSource));

    [Fact]
    [Trait("Req", "CMP-411")]
    [Trait("Req", "LANG-946")]
    public void ReadText() => Snapshot.Verify("ReadText", Emit(ReadTextSource));

    [Fact]
    [Trait("Req", "LANG-259")]
    [Trait("Req", "LANG-290")]
    [Trait("Req", "CMP-330")]
    public void ArrayConstructors() => Snapshot.Verify("ArrayConstructors", Emit(ArrayConstructorsSource));

    /// <summary>C-106: open arrays (ABI-080 pair, CMP-331 check against High + 1, LANG-432 prologue copy and its release).</summary>
    [Fact]
    [Trait("Req", "ABI-080")]
    [Trait("Req", "LANG-432")]
    public void OpenArrays() => Snapshot.Verify("OpenArrays", Emit(OpenArraysSource));

    /// <summary>ABI-080/082: an open array is two IR parameters, <c>ptr</c> then <c>i64</c> High; callers pass both (C-106).</summary>
    [Fact]
    [Trait("Req", "ABI-080")]
    public void OpenArrays_PassDataAndHigh()
    {
        var text = Emit(OpenArraysSource);

        Assert.Matches(@"define i32 @""M\.Sum""\(ptr %a, i64 %""?a\$high""?\)", text);
        Assert.Matches(@"invoke i32 @""M\.Sum""\(ptr %f\.addr, i64 2\)", text);                  // the static array f: High = 2
        Assert.Matches(@"invoke i32 @""M\.Sum""\(ptr %t\d+, i64 %t\d+\)", text);                 // d: High = Length − 1
        Assert.Matches(@"invoke i32 @""M\.Sum""\(ptr null, i64 -1\)", text);                   // []
        Assert.Contains("invoke void @\"M.Bump\"(ptr %$tmp2.addr, i64 0)", text);             // ['x'] in a temporary
        Assert.Contains("invoke ptr @\"System._GetMem\"(i64 ", text);                        // LANG-432: a heap copy beyond 4096 bytes
        Assert.Contains("@\"System._AddRefArray\"(ptr ", text);
        Assert.Contains("@\"System._FinalizeArray\"(ptr ", text);
    }

    /// <summary>C-066: the rtl-abi.md §9.9 input helpers as declared and called (CMP-411).</summary>
    [Fact]
    [Trait("Req", "CMP-411")]
    [Trait("Req", "ABI-132")]
    public void ReadText_UsesTheAbiHelpers()
    {
        var text = Emit(ReadTextSource);

        foreach (var line in (string[])[
                     "declare i64 @\"System._ReadInt\"(ptr)\n", "declare i64 @\"System._ReadUInt\"(ptr)\n",
                     "declare double @\"System._ReadFloat\"(ptr)\n", "declare i16 @\"System._ReadWChar\"(ptr)\n",
                     "declare void @\"System._ReadUStr\"(ptr, ptr)\n", "declare void @\"System._ReadLn\"(ptr)\n",
                     "declare void @\"System._ReadEnd\"(ptr)\n", "declare i8 @\"System._Eof\"(ptr)\n", "declare i8 @\"System._Eoln\"(ptr)\n",
                     "@\"System.Input\" = external global [624 x i8], align 8\n",
                     " = invoke i64 @\"System._ReadInt\"(ptr @\"System.Input\")\n",           // the routine has a string local (CMP-394)
                     " = invoke i8 @\"System._Eof\"(ptr @\"System.Input\")\n",
                     "  invoke void @\"System._RunError\"(i32 106) #1\n",
                     " = fptrunc double ",
                     "  invoke void @\"System._ReadUStr\"(ptr @\"System.Input\", ptr %s.addr)\n",
                 ])
        {
            Assert.Contains(line, text);
        }

        Assert.Equal(1, text.Split("invoke void @\"System._IOCheck\"()").Length - 1);            // {$I-} for Read(t, s)
        Assert.Equal(2, text.Split("invoke void @\"System._RunError\"(i32 106)").Length - 1);     // Integer and Word; UInt64 needs no check
    }

    /// <summary>C-065: the rtl-abi.md §9.5 helpers at the CMP-325 rule 1–3 and 6 points.</summary>
    [Fact]
    [Trait("Req", "CMP-325")]
    [Trait("Req", "CMP-340")]
    [Trait("Req", "ABI-080")]
    [Trait("Req", "ABI-110")]
    public void ManagedRecords_UseCopyRecordAddRefAndFinalize()
    {
        var text = Emit(ManagedRecordsSource);

        foreach (var line in (string[])[
                     "define void @\"M.Make\"(ptr %Result, ptr %n) #0 {\n",                    // ABI-081: a managed result is hidden
                     "define void @\"M.Take\"(ptr %p, i64 %s, ptr %o) #0 personality ptr @__C_specific_handler {\n",   // ABI-080: 24 bytes by address, 8 as i64
                     "  invoke void @\"System._AddRef\"(ptr %s.addr, ptr @\"M.TSmall.$rtti\")\n",     // the callee's own reference (CMP-394)
                     "  invoke void @\"System._Finalize\"(ptr %o, ptr @\"M.TPerson.$rtti\")\n",       // the out parameter
                     "  invoke void @\"System._CopyRecord\"(ptr %o, ptr %p, ptr @\"M.TPerson.$rtti\")\n",
                     "  invoke void @\"M.Make\"(ptr %a.addr, ptr ",                                    // rule 4: into a
                     "  invoke void @\"System._CopyRecord\"(ptr %copy.addr, ptr %names.addr, ptr @\"M.TNames.$rtti\")\n",
                     "  invoke void @\"System._CopyRecord\"(ptr %$tmp1.addr, ptr %a.addr, ptr @\"M.TPerson.$rtti\")\n", // the argument copy
                     "  call void @llvm.memset.p0.i64(ptr align 8 %a.addr, i8 0, i64 24, i1 false)\n",               // rule 1
                     "  call void @\"System._Finalize\"(ptr %$tmp1.addr, ptr @\"M.TPerson.$rtti\")\n",              // the caller finalises its copy
                     "  call void @\"System._Finalize\"(ptr %$tmp1.addr, ptr @\"M.TPerson.$rtti\") [ \"funclet\"(token %cl) ]\n",  // also on unwind (CMP-393)
                     "declare void @\"System._CopyRecord\"(ptr, ptr, ptr)\n",
                     "@\"M.TPerson.$rtti.fields\" = private unnamed_addr constant [2 x { ptr, i64 }] [{ ptr, i64 } { ptr @\"System.string.$rtti\", i64 0 }, { ptr, i64 } { ptr @\"M.$rtti.array of AnsiString\", i64 16 }], align 8\n",
                 ])
        {
            Assert.Contains(line, text);
        }

        var take = text[text.IndexOf("define void @\"M.Take\"", StringComparison.Ordinal)..];
        take = take[..take.IndexOf("\n}\n", StringComparison.Ordinal)];
        Assert.DoesNotContain("(ptr %p, ptr @\"M.TPerson.$rtti\")", take);                                // no reference counting of the caller's copy
        var use = text[text.IndexOf("define void @\"M.Use\"", StringComparison.Ordinal)..];
        use = use[..use.IndexOf("\n}\n", StringComparison.Ordinal)];
        Assert.Matches(@"invoke void @""System\._Finalize""\(ptr %t\d+, ptr @""M\.TPerson\.\$rtti""\)\n          to label %cont\.\d+ unwind label %cleanup\.pad\ncont\.\d+:\n  %t\d+ = load ptr, ptr %\$tmp2\.addr, align 8\n  call void @""System\._FreeMem""\(ptr %t\d+\)\n", use);   // Dispose; _FreeMem never raises (CMP-394)
    }

    /// <summary>C-061: the CMP-324 operator rows as emitted (helpers, the parts array, nil tests, unique before a character store).</summary>
    [Fact]
    [Trait("Req", "CMP-324")]
    [Trait("Req", "CMP-331")]
    [Trait("Req", "ABI-024")]
    public void StringOperators_UseTheCmp324Helpers()
    {
        var text = Emit(StringOperatorsSource);

        foreach (var line in (string[])[
                     "  invoke void @\"System._UStrCat\"(ptr %Result, ptr %",                // CMP-394: Concat has a temporary
                     " = alloca [3 x ptr], align 8\n",
                     ", i32 3)\n",
                     "  invoke void @\"System._UStrCatN\"(ptr %Result, ptr %$tmp",
                     " = call i8 @\"System._UStrEq\"(ptr %",
                     " = call i32 @\"System._UStrCmp\"(ptr %",
                     " = icmp slt i32 %",
                     " = icmp sge i32 %",
                     " = icmp eq ptr %",
                     " = icmp ne ptr %",
                     "  call void @\"System._UStrUnique\"(ptr %s)\n",
                     "declare i8 @\"System._UStrEq\"(ptr, ptr)\n",
                     "declare i32 @\"System._UStrCmp\"(ptr, ptr)\n",
                     "declare void @\"System._UStrCatN\"(ptr, ptr, i32)\n",
                     "declare void @\"System._UStrUnique\"(ptr)\n",
                 ])
        {
            Assert.Contains(line, text);
        }

        var poke = text[text.IndexOf("define void @\"M.Poke\"", StringComparison.Ordinal)..];
        poke = poke[..poke.IndexOf("\n}\n", StringComparison.Ordinal)];
        Assert.Equal(2, poke.Split("_UStrUnique").Length - 1);                                      // one per written character
        Assert.Equal(2, poke.Split(" = icmp ult i64 ").Length - 1);                                 // {$R+}: the read and the write are checked
    }

    /// <summary>C-060: the CMP-324 table rows and CMP-325 rules 1–4 as emitted.</summary>
    [Fact]
    [Trait("Req", "CMP-324")]
    [Trait("Req", "CMP-325")]
    [Trait("Req", "ABI-023")]
    [Trait("Req", "ABI-031")]
    [Trait("Req", "ABI-032")]
    public void Strings_UseTheAbiHelpersAndOneCleanupBlock()
    {
        var text = Emit(StringsSource);

        foreach (var line in (string[])[
                     "define void @\"M.Echo\"(ptr %Result, ptr %s) #0 personality ptr @__C_specific_handler {\n",   // CMP-390/393
                     "  call void @\"System._UStrAddRef\"(ptr %t1)\n",                     // rule 3, value parameter
                     "  store ptr null, ptr %t.addr, align 8\n",                            // rule 1
                     "  invoke void @\"System._UStrClr\"(ptr %o)\n",                        // the out parameter, ABI-030 (CMP-394)
                     "  invoke void @\"System._UStrAsg\"(ptr %v, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.0, i32 0, i32 5))\n",
                     "  invoke void @\"M.Echo\"(ptr %$tmp1.addr, ptr %",                    // o := Echo(t): o is no local
                     "  invoke void @\"M.Echo\"(ptr %t.addr, ptr %",                        // t := Echo(v): into t (rule 4)
                     "  invoke void @\"System._UStrFromWChar\"(ptr %t.addr, i16 %",
                     "  invoke void @\"System._UStrAsg\"(ptr @\"M.G\", ptr %",
                     " = getelementptr inbounds i8, ptr %", ", i64 -12\n",                  // ABI-023
                     " = getelementptr inbounds i16, ptr %",                                // ABI-024
                     " = icmp ult i64 %",                                                   // CMP-331
                     "cleanup:\n  call void @\"System._UStrClr\"(ptr %t.addr)\n  call void @\"System._UStrClr\"(ptr %$tmp1.addr)\n  call void @\"System._UStrClr\"(ptr %$tmp2.addr)\n  br label %exit\n",
                     "declare void @\"System._UStrAsg\"(ptr, ptr)\n",
                     "declare void @\"System._UStrFromWChar\"(ptr, i16)\n",
                 ])
        {
            Assert.Contains(line, text);
        }

        var use = text[text.IndexOf("define void @\"M.Use\"", StringComparison.Ordinal)..];
        use = use[..use.IndexOf("\n}\n", StringComparison.Ordinal)];
        // Exit (a direct branch arm) and the end of the body; the invokes unwind to cleanup.pad (CMP-393)
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(use, @"label %cleanup(?!\.pad)").Count);
        Assert.Contains("cleanup.pad:\n  %cl = cleanuppad within none []\n  call void @\"System._UStrClr\"(ptr %t.addr) [ \"funclet\"(token %cl) ]\n", use);
        Assert.DoesNotContain("select", use);                                                       // no speculative load from nil
    }

    /// <summary>C-059: the helper declarations of rtl-abi.md §9.9/§9.10 and the CMP-410 arguments of <c>_WriteFloat</c>.</summary>
    [Fact]
    [Trait("Req", "CMP-410")]
    [Trait("Req", "CMP-412")]
    public void RealIntrinsics_UseTheAbiHelpers()
    {
        var text = Emit(RealIntrinsicsSource);

        foreach (var line in (string[])["declare i64 @\"System._Trunc\"(double)\n", "declare i64 @\"System._Round\"(double)\n",
                     "declare double @\"System._Int\"(double)\n", "declare double @\"System._Frac\"(double)\n",
                     "declare double @llvm.sqrt.f64(double)\n", "declare void @\"System._WriteFloat\"(ptr, double, i32, i32)\n"])
        {
            Assert.Contains(line, text);
        }

        Assert.Contains(", i32 23, i32 -1)\n", text);                                                  // Write(d)
        Assert.Contains(" = fpext float ", text);
        Assert.Equal(2, text.Split("call i64 @\"System._").Length - 1);                                 // Trunc(2.5), Round(2.5) are folded
    }

    /// <summary>C-058: the CMP-313/315/316 instructions; no fast-math flags and no overflow intrinsics on reals under <c>{$Q+}</c>.</summary>
    [Fact]
    [Trait("Req", "CMP-313")]
    [Trait("Req", "CMP-315")]
    [Trait("Req", "CMP-316")]
    [Trait("Req", "LANG-232")]
    public void Reals_UseTheSpecInstructions()
    {
        var text = Emit(RealsSource);

        Assert.Contains("@\"M.G\" = global float 0x3FB99999A0000000, align 4\n", text);             // 0.1 rounded to Single
        foreach (var instruction in (string[])[" = fmul float ", " = fsub float ", " = fadd double ", " = fmul double ", " = fdiv double ",
                     " = fsub double ", " = fneg double ", " = sitofp i32 ", " = uitofp i32 ", " = uitofp i64 ", " = fpext float ",
                     " = fptrunc double ", " = fcmp oeq double ", " = fcmp une double ", " = fcmp olt double ",
                     " = fcmp ole double ", " = fcmp ogt double ", " = fcmp oge double "])
        {
            Assert.Contains(instruction, text);
        }

        Assert.Contains(", 0x4000000000000000\n", text);                                            // s * 2 stays Single (LANG-230)
        Assert.DoesNotContain("with.overflow", text);
        Assert.DoesNotContain(" fast ", text);
    }

    /// <summary>C-057: the casts' instructions and the <c>Assert</c> call (ABI-103: message or null, file name, line; noreturn).</summary>
    [Fact]
    [Trait("Req", "LANG-290")]
    [Trait("Req", "CMP-412")]
    public void Typecasts_ExtendViewAndAssert()
    {
        var text = Emit(TypecastsSource);

        Assert.Contains("  %t2 = sext i8 %t1 to i64\n  %t3 = inttoptr i64 %t2 to ptr\n", text);
        Assert.Contains(" = ptrtoint ptr ", text);
        Assert.Contains("store i32 7, ptr %t", text);
        Assert.Contains(", align 1\n", text);
        Assert.Contains("call void @\"System._Assert\"(ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i16] }, ptr @.str.0, i32 0, i32 5), ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.1, i32 0, i32 5), i32 27) #1\n", text);
        Assert.Contains("call void @\"System._Assert\"(ptr null, ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i16] }, ptr @.str.1, i32 0, i32 5), i32 28) #1\n", text);
        Assert.Equal(2, text.Split("call void @\"System._Assert\"").Length - 1);
        Assert.Contains("declare void @\"System._Assert\"(ptr, ptr, i32) #1\n", text);
    }

    /// <summary>CMP-340/342/412 forms; ABI-060 one-byte empty records; CMP-309 copy-in/copy-out for a packed var argument.</summary>
    [Fact]
    [Trait("Req", "CMP-340")]
    [Trait("Req", "CMP-341")]
    [Trait("Req", "CMP-342")]
    [Trait("Req", "CMP-412")]
    [Trait("Req", "ABI-060")]
    public void Pointers_AddressesArithmeticAndMemoryIntrinsics()
    {
        var text = Emit(PointersSource);

        Assert.Contains("store ptr @\"M.G\", ptr %p.addr, align 8\n", text);                        // p := @G
        Assert.Contains("call ptr @\"System._AllocZero\"(i64 16)\n", text);                          // New(n): SizeOf(TNode)
        Assert.Contains("call void @\"System._FreeMem\"(ptr ", text);
        Assert.Contains("call ptr @\"System._GetMem\"(i64 16)\n", text);
        Assert.Matches(@"call void @llvm\.memset\.p0\.i64\(ptr align 1 %t\d+, i8 0, i64 16, i1 false\)", text);
        Assert.Matches(@"call void @llvm\.memmove\.p0\.p0\.i64\(ptr align 1 %t\d+, ptr align 4 @""M\.G"", i64 4, i1 false\)", text);
        Assert.Matches(@"getelementptr inbounds i16, ptr %t\d+, i64 2\n", text);                    // c + 2
        Assert.Matches(@"getelementptr inbounds i32, ptr %t\d+, i64 3\n", text);                    // Inc(p, 3)
        Assert.Matches(@"ptrtoint ptr %t\d+ to i64\n", text);                                        // p - q
        Assert.Matches(@"sdiv i64 %t\d+, 4\n", text);
        Assert.Contains("store ptr @\"M.Bump\", ptr %u.addr, align 8\n", text);                    // @Routine
        Assert.Contains("@\"M.CG\" = constant ptr @\"M.G\", align 8\n", text);
        Assert.Contains("@\"M.Table\" = constant [2 x ptr] [ptr @\"M.G\", ptr null], align 8\n", text);
        Assert.Contains("@\"M.E\" = global [1 x i8] zeroinitializer, align 1\n", text);            // empty record: one byte
        Assert.Contains("%e2.addr = alloca [1 x i8], align 1\n", text);
        // Bump(K.I): K.I is copied into an aligned temporary and back (the callee assumes align 4)
        Assert.Matches(@"load i32, ptr %t\d+, align 1\n  store i32 %t\d+, ptr %\$tmp\d+\.addr, align 4\n  call void @""M\.Bump""\(ptr %\$tmp\d+\.addr\)\n", text);
    }

    [Fact]
    [Trait("Req", "CMP-321")]
    public void For() => Snapshot.Verify("For", Emit(ForSource));

    [Fact]
    [Trait("Req", "CMP-322")]
    public void Case() => Snapshot.Verify("Case", Emit(CaseSource));

    [Fact]
    [Trait("Req", "CMP-319")]
    public void Loops() => Snapshot.Verify("Loops", Emit(LoopsSource));

    [Fact]
    [Trait("Req", "CMP-318")]
    public void IfExit() => Snapshot.Verify("IfExit", Emit(IfExitSource));

    [Fact]
    [Trait("Req", "CMP-310")]
    public void EveryRoutine_HasExactlyOneRet()
    {
        foreach (var source in Sources.Values)
        {
            var text = Emit(source);
            Assert.Equal(text.Split("define ").Length - 1, text.Split("\n  ret ").Length - 1);
        }
    }

    [Fact]
    [Trait("Req", "CMP-310")]
    public void Add()
    {
        var text = Emit(AddSource);

        Assert.Contains(Cmp310Add, text);
        Snapshot.Verify("Add", text);
    }

    [Fact]
    [Trait("Req", "CMP-317")]
    public void ShortCircuit() => Snapshot.Verify("ShortCircuit", Emit(ShortCircuitSource));

    [Fact]
    [Trait("Req", "CMP-312")]
    public void Division() => Snapshot.Verify("Division", Emit(DivisionSource));

    [Fact]
    [Trait("Req", "CMP-313")]
    public void Conversions() => Snapshot.Verify("Conversions", Emit(ConversionsSource));

    [Fact]
    [Trait("Req", "CMP-311")]
    public void ImplementationOnlyRoutine_HasInternalLinkage()
    {
        var text = Emit(ShortCircuitSource);

        Assert.Contains("define i8 @\"M.Both\"(i8 %a, i8 %b) #0 {", text);
        Assert.Contains("define internal i8 @\"M.Mixed\"(i8 %a, i8 %b, i8 %c) #0 {", text);
    }

    [Fact]
    [Trait("Req", "CMP-303")]
    public void ParameterNamedLikeAGeneratedName_GetsASuffix()
    {
        var text = Emit("""
            unit M;
            interface
            function F(t1, exit: Integer): Integer;
            implementation
            function F(t1, exit: Integer): Integer;
            begin
              Result := t1 - exit;
            end;
            end.
            """);

        Assert.Contains("define i32 @\"M.F\"(i32 %t1.1, i32 %exit.1) #0 {", text);
        Assert.Contains("  %t1 = load i32, ptr %t1.1.addr, align 4\n", text);
    }

    internal static string Emit(string source)
    {
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\M.pas", source));
        Assert.Empty(tree.Diagnostics);
        var result = BindWithStubSystem(tree);
        Assert.Empty(Format(result.Diagnostics));
        var writer = new LlvmTextWriter();
        IrEmitter.EmitUnit(Lowerer.Lower(result), writer);
        return writer.GetText();
    }
}

/// <summary>The modules of <see cref="IrSnapshotTests"/> are accepted by the pinned clang; fails when the toolchain is missing.</summary>
[Trait("Phase", "2")]
[Trait("Category", "Toolchain")]
public sealed class IrSnapshotToolchainTests
{
    public static TheoryData<string, string> Modules()
    {
        var data = new TheoryData<string, string>();
        foreach (var optimisation in new[] { "-O0", "-O2" })
        {
            foreach (var name in IrSnapshotTests.Sources.Keys)
            {
                data.Add(name, optimisation);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Modules))]
    [Trait("Req", "CMP-125")]
    public void EmittedModule_CompilesWithPinnedClang(string name, string optimisation)
    {
        var (exitCode, output) = Clang.CompileToObject(IrSnapshotTests.Emit(IrSnapshotTests.Sources[name]), optimisation);

        Assert.True(exitCode == 0, "clang failed:\n" + output);
    }
}

/// <summary>Compares text with <c>Snapshots/&lt;name&gt;.ll</c> of the source tree; <c>UPDATE_SNAPSHOTS=1</c> rewrites the file.</summary>
internal static class Snapshot
{
    public static string Directory => Path.Combine(RepoPaths.Root, "compiler", "tests", "FreexComp.Compiler.Tests", "Snapshots");

    public static void Verify(string name, string actual)
    {
        var path = Path.Combine(Directory, name + ".ll");
        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"Snapshot {path} is missing (create it deliberately with UPDATE_SNAPSHOTS=1). Actual:\n{actual}");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }
}
