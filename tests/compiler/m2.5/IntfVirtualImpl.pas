// covers: LANG-661, CMP-372
program IntfVirtualImpl;

{ An interface method implemented by a virtual (and by an abstract) method: the thunk dispatches through the VMT, so
  the overrides of descendant classes run although only the base class lists the interface. }

type
  ISpeaker = interface
    ['{7E7E0002-0000-0000-0000-000000000001}']
    function Speak: string;
    procedure Hello;
  end;

  TBase = class(TInterfacedObject, ISpeaker)
    function Speak: string; virtual;
    procedure Hello; virtual; abstract;
  end;

  TDerived = class(TBase)
    function Speak: string; override;
    procedure Hello; override;
  end;

  TOther = class(TBase)
    procedure Hello; override;
  end;

function TBase.Speak: string;
begin
  Result := 'base';
end;

function TDerived.Speak: string;
begin
  Result := 'derived';
end;

procedure TDerived.Hello;
begin
  Writeln('hello from ', Speak);
end;

procedure TOther.Hello;
begin
  Writeln('other says ', Speak);
end;

var
  S: ISpeaker;
begin
  S := TDerived.Create;
  Writeln(S.Speak);
  S.Hello;
  S := TOther.Create;
  Writeln(S.Speak);
  S.Hello;
  S := nil;
end.
