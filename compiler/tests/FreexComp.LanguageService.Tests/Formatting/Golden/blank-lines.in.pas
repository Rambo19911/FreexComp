unit BlankLines;


interface



procedure A;
procedure B;
implementation
procedure A;
begin

  Writeln('a');


  Writeln('b');

end;
procedure B;
begin
end;



// comment about C

procedure C;
begin
end;
initialization
A;
finalization
B;
end.
