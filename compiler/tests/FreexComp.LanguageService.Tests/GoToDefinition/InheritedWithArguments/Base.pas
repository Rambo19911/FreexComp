unit Base;
interface
type
  TBase = class
    procedure Put(I: Integer); overload; virtual;
    procedure Put(const S: string); overload; virtual;
  end;
implementation
procedure TBase.Put(I: Integer);
begin
  Writeln(I);
end;
procedure TBase.{#}Put(const S: string);
begin
  Writeln(S);
end;
end.
