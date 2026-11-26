// covers: LANG-784, LANG-785
program GenericTypeOperands;

uses
  SysUtils;

type
  IBox<T> = interface
    ['{6A1F0C2E-3B4D-4E5F-8A9B-0C1D2E3F4A5B}']
    function Get: T;
  end;

  TBox<T> = class(TInterfacedObject, IBox<T>)
    V: T;
    function Get: T;
  end;

  TPair<A, B> = record
    Lo: A;
    Hi: B;
  end;

  EGen<T> = class(Exception)
  end;

function TBox<T>.Get: T;
begin
  Result := V;
end;

procedure Fail(Which: Integer);
begin
  case Which of
    0: raise EGen<Integer>.Create('int');
    1: raise EGen<string>.Create('str');
  else
    raise Exception.Create('plain');
  end;
end;

procedure Handle(Which: Integer);
begin
  try
    Fail(Which);
  except
    on E: EGen<Integer> do
      WriteLn('EGen<Integer> ', E.Message);
    on GenericTypeOperands.EGen<string> do
      WriteLn('EGen<string>');
    on E: Exception do
      WriteLn('Exception ', E.Message, ' ', E is EGen<Integer>);
  end;
end;

var
  Box: TBox<Integer>;
  Keep: IBox<Integer>;
  O: TObject;
  Other: TObject;
  I: IInterface;
  P: Pointer;
  W: Word;
  Pair: TPair<Byte, Byte>;
  G: TGUID;
begin
  Box := TBox<Integer>.Create;
  Box.V := 42;
  Keep := Box;
  O := Box;
  Other := TObject.Create;

  // is / as with class and interface instances
  WriteLn('is ', O is TBox<Integer>, ' ', O is GenericTypeOperands.TBox<Integer>, ' ', Other is TBox<Integer>, ' ', O is TBox<string>);
  WriteLn('nil is ', TObject(nil) is TBox<Integer>);
  WriteLn('as ', (O as TBox<Integer>).V, ' ', (O as GenericTypeOperands.TBox<Integer>).Get);
  WriteLn('is intf ', O is IBox<Integer>, ' ', O is GenericTypeOperands.IBox<Integer>, ' ', Other is IBox<Integer>);
  I := Keep;
  WriteLn('as intf ', (I as IBox<Integer>).Get, ' ', (O as GenericTypeOperands.IBox<Integer>).Get);

  // typecasts: class (with a suffix), interface from a pointer, value cast of a Word
  WriteLn('class cast ', TBox<Integer>(O).V, ' ', GenericTypeOperands.TBox<Integer>(O).Get);
  TBox<Integer>(O).V := 7;
  WriteLn('after store ', Box.V);
  P := Pointer(Keep);
  WriteLn('intf cast ', IBox<Integer>(P).Get, ' ', GenericTypeOperands.IBox<Integer>(P).Get);
  W := $0201;
  Pair := TPair<Byte, Byte>(W);
  WriteLn('value cast ', Pair.Lo, ' ', Pair.Hi);
  W := $0403;
  Pair := GenericTypeOperands.TPair<Byte, Byte>(W);
  WriteLn('value cast ', Pair.Lo, ' ', Pair.Hi);

  // handler types: the first matching handler wins
  Handle(0);
  Handle(1);
  Handle(2);

  // interface instances as TGUID values
  WriteLn('supports ', Supports(O, IBox<Integer>), ' ', Supports(O, GenericTypeOperands.IBox<Integer>), ' ', Supports(Other, IBox<Integer>));
  G := IBox<Integer>;
  WriteLn(GUIDToString(G));
  G := GenericTypeOperands.IBox<string>;
  WriteLn(GUIDToString(G));

  Other.Free;
  O := nil;
  Box := nil;
  Keep := nil;
  I := nil;
end.
