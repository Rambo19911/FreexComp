unit Routines;

interface

procedure A;
function B(X: Integer): Integer;

implementation

procedure A;
begin
  Writeln('a');
end;

function B(X: Integer): Integer;
var
  P: procedure;

  function Inner: Integer;
  begin
    Result := 1;
  end;

begin
  P := procedure
    begin
      Writeln;
    end;
  Result := X + Inner;
end;

procedure Forwarded; forward;

procedure Ext; external 'k.dll';

class procedure TFoo.Bar;
begin
end;

procedure Asm1; assembler;
asm
  ret
end;

end.
