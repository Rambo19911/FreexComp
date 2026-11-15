// covers: LANG-721, LANG-722, CMP-380
program AnonLifetime;

type
  TProc = reference to procedure;
  TStrFunc = reference to function: string;

  TNoisy = class(TInterfacedObject)
    destructor Destroy; override;
  end;

destructor TNoisy.Destroy;
begin
  Writeln('noisy freed');
  inherited;
end;

function MakeHolder: TProc;
var
  Keep: IInterface;
begin
  Keep := TNoisy.Create;
  Result := procedure begin if Keep <> nil then Writeln('holding') end;
  Writeln('maker exits');
end;

function MakeTagger(const Tag: string): TStrFunc;
var
  Text: string;
begin
  Text := '[' + Tag + ']';
  Result := function: string begin Result := Text + Text end;
end;

procedure Run;
var
  P: TProc;
  F: TStrFunc;
begin
  P := MakeHolder;
  P();
  P := nil;
  Writeln('after nil');
  F := MakeTagger('abc');
  Writeln(F());
  Writeln(F());
  F := MakeTagger('x');
  Writeln(F);
end;

var
  Before: Integer;
begin
  Before := _LiveAllocCount;
  Run;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.