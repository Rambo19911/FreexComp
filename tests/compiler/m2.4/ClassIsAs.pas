// covers: LANG-650, LANG-651, CMP-367
program ClassIsAs;

{ 'is' on instances of the class, of descendants, of an unrelated sibling and on nil; 'is' on class references (nil
  included); 'as' downcasting (the result typed as the target: calls a method only the target has), upcasting, and
  nil staying nil; 'is' as an if condition. }

type
  TA = class
    function Name: string; virtual;
  end;

  TB = class(TA)
    function Name: string; override;
    procedure OnlyB;
  end;

  TC = class(TB)
    function Name: string; override;
  end;

  TD = class(TA)
  end;

  TAClass = class of TA;

function TA.Name: string;
begin
  Result := 'A';
end;

function TB.Name: string;
begin
  Result := 'B';
end;

procedure TB.OnlyB;
begin
  Writeln('only B: ', Name);
end;

function TC.Name: string;
begin
  Result := 'C';
end;

var
  A: TA;
  B: TB;
  R: TAClass;
  Items: array[0..3] of TA;
  I: Integer;
begin
  Items[0] := TA.Create;
  Items[1] := TB.Create;
  Items[2] := TC.Create;
  Items[3] := nil;
  for I := 0 to 3 do
    Writeln(I, ': ', Items[I] is TA, ' ', Items[I] is TB, ' ', Items[I] is TC, ' ', Items[I] is TD);

  B := Items[2] as TB;
  Writeln(B.Name, ' ', (Items[1] as TB).Name);
  B := Items[1] as TB;
  B.OnlyB;
  A := Items[3] as TB;
  Writeln(A = nil);
  B := TC.Create;
  A := B as TA;
  Writeln(A.Name, ' ', A is TC);

  R := TC;
  Writeln(R is TB, ' ', R is TD);
  R := TA;
  Writeln(R is TB);
  R := nil;
  Writeln(R is TA);
  if Items[1] is TB then
    Writeln('branch');

  for I := 0 to 2 do
    Items[I].Free;
  B.Free;
end.
