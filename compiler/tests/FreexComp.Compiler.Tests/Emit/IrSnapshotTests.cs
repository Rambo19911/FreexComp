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

    internal static readonly IReadOnlyDictionary<string, string> Sources = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
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
    [Trait("Req", "CMP-340")]
    [Trait("Req", "CMP-342")]
    public void Pointers() => Snapshot.Verify("Pointers", Emit(PointersSource));

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
        var result = Binder.BindCompilationUnit(tree, new BuiltInTypes());
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
