{ context: member-property }
program MemberFunctionResult;
type
  TBox = class
  private
    FLabel: string;
  public
    property Caption: string read FLabel;
  end;
function MakeBox: TBox;
begin
  Result := TBox.Create;
end;
begin
  Writeln(MakeBox.{|});
end.
{#has Caption, Free}
{#kind Caption=Property}
