// covers: LANG-731
program ErrOperatorDecl;

type
  TVec = record
    X: Integer;
    class operator Add(A, B: Integer): Integer;
    class operator Negative(const A, B: TVec): TVec;
  end;

begin
end.
