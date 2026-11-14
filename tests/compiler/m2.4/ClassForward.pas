// covers: LANG-601, LANG-600, LANG-615
program ClassForward;

type
  TDepartment = class;

  TPerson = class
    Name: string;
    Dept: TDepartment;
    Next: TPerson;
    procedure Show;
  end;

  TDepartment = class
    Title: string;
    Head: TPerson;
    procedure Show;
  end;

procedure TPerson.Show;
begin
  Writeln(Name, ' works in ', Dept.Title);
end;

procedure TDepartment.Show;
var
  P: TPerson;
begin
  Writeln(Title, ':');
  P := Head;
  while P <> nil do
  begin
    Write('  ');
    P.Show;
    P := P.Next;
  end;
end;

var
  D: TDepartment;
  A, B: TPerson;
begin
  D := TDepartment.Create;
  D.Title := 'Research';
  A := TPerson.Create;
  A.Name := 'Ann';
  A.Dept := D;
  B := TPerson.Create;
  B.Name := 'Bob';
  B.Dept := D;
  A.Next := B;
  D.Head := A;
  D.Show;
  Writeln(B.Next = nil, ' ', A.Dept = B.Dept);
  B.Free;
  A.Free;
  D.Free;
end.
