// covers: LANG-651
program IntfAsFails;

{ 'as' to an interface the object does not support: runtime error 228 (without SysUtils). }

type
  IFoo = interface
    ['{0A0B0C0D-1111-2222-3333-444455556666}']
    procedure Foo;
  end;

  IBar = interface
    ['{0A0B0C0D-7777-8888-9999-AAAABBBBCCCC}']
    procedure Bar;
  end;

  TFoo = class(TInterfacedObject, IFoo)
    procedure Foo;
  end;

procedure TFoo.Foo;
begin
  Writeln('foo');
end;

var
  F: IFoo;
  B: IBar;
begin
  F := TFoo.Create;
  F.Foo;
  Writeln('before');
  B := F as IBar;
  Writeln('not reached');
end.
