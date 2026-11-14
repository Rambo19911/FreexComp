// covers: LANG-433, ABI-080
program ArrayOfConst;

type
  TFoo = class
  end;

procedure Show(const Args: array of const);
var
  I: Integer;
begin
  Write(Length(Args), ':');
  for I := 0 to High(Args) do
    case Args[I].VType of
      vtInteger: Write(' int=', Args[I].VInteger);
      vtBoolean: Write(' bool=', Args[I].VBoolean);
      vtChar: Write(' achar=', Args[I].VChar);
      vtWideChar: Write(' char=', Args[I].VWideChar);
      vtExtended: Write(' real=', Args[I].VExtended^:0:3);
      vtInt64: Write(' int64=', Args[I].VInt64^);
      vtUnicodeString: Write(' ustr=', string(PChar(Args[I].VUnicodeString)));
      vtAnsiString: Write(' astr=', string(PAnsiChar(Args[I].VAnsiString)));
      vtString: Write(' sstr=', Args[I].VString^);
      vtPointer: Write(' ptr=', Args[I].VPointer = nil);
      vtPChar: Write(' pachar=', Args[I].VPChar[0]);
      vtPWideChar: Write(' pchar=', Args[I].VPWideChar[1]);
      vtObject: Write(' obj=', Args[I].VObject is TFoo);
      vtClass: Write(' class=', Args[I].VClass = TFoo);
    else
      Write(' other=', Args[I].VType);
    end;
  Writeln;
end;

procedure Twice(const Args: array of const);
begin
  Show(Args);
  Show(Slice(Args, 1));
end;

function Concat3(const A, B, C: string): string;
begin
  Result := A + B + C;
end;

var
  S: string;
  A: AnsiString;
  Short: ShortString;
  D: Double;
  B: Byte;
  Big: Int64;
  C: Cardinal;
  O: TFoo;
  P: Pointer;
  PA: PAnsiChar;
  PW: PChar;
begin
  S := 'uni';
  A := 'ansi';
  Short := 'short';
  D := 1.25;
  B := 200;
  Big := 5000000000;
  C := 7;
  O := TFoo.Create;
  P := @B;
  PA := 'pa';
  PW := 'pw';
  Show([]);
  Show([42, B, C, -3, True, 'c', AnsiChar('d')]);
  Show([D, 2.5, Big, 5000000000, S, 'lit', Concat3('x', 'y', 'z'), A, Short]);
  Show([nil, P, PA, PW, O, TFoo]);
  Twice([1, 'two']);
  Big := 7;
  Show([Big, Int64(8), UInt64(9), Big + 1, High(Cardinal), C]);
  O.Free;
end.
