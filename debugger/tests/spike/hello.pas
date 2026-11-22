program hello;                 {  1 }
                               {  2 }
function Twice(X: Integer): Integer;   {  3 }
begin                          {  4 }
  Result := X * 2;             {  5 }
end;                           {  6 }
                               {  7 }
var                            {  8 }
  A, B: Integer;               {  9 }
begin                          { 10 }
  A := 21;                     { 11 }
  B := Twice(A);               { 12 }
  WriteLn(B);                  { 13 }
end.                           { 14 }
