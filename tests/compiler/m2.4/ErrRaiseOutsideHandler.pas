// covers: LANG-805, LANG-807, LANG-822
program ErrRaiseOutsideHandler;

var
  I: Integer;
begin
  raise;
  raise I;
  try
  except
    on Integer do ;
  end;
end.