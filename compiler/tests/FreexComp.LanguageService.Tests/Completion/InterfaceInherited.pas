{ context: interface }
program InterfaceInherited;
type
  IReader = interface
    function Read: string;
  end;
  IReadWriter = interface(IReader)
    procedure Write(const S: string);
  end;
var
  RW: IReadWriter;
begin
  RW.{|}
end.
{#has Read, Write, QueryInterface}
