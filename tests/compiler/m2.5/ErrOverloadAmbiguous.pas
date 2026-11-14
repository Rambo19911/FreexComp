// covers: LANG-440, LANG-442
program ErrOverloadAmbiguous;

procedure Mix(A: Integer; B: Double); overload;
begin
end;

procedure Mix(A: Double; B: Integer); overload;
begin
end;

procedure Narrow(X: Byte); overload;
begin
end;

procedure Narrow(X: Word); overload;
begin
end;

procedure Plain(X: Integer);
begin
end;

procedure Plain(const X: string); overload;
begin
end;

var
  I: Integer;
begin
  I := 1;
  Mix(1, 1);
  Narrow(I);
  Narrow(True);
end.
