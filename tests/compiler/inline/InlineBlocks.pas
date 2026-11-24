// covers: LANG-560, LANG-561
program InlineBlocks;

{ Inline declarations in every kind of statement list: mid-block, nested blocks, repeat, the try/finally/except lists,
  a case else list and loop bodies. Storage is per activation; a sibling block may reuse a name with another type. }

uses SysUtils;

procedure Blocks(N: Integer);
begin
  Writeln('start ', N);
  var A, B: Integer;
  A := N;
  B := N * 2;
  var Sum := A + B;
  Writeln('sum ', Sum);
  begin
    var Inner := Sum + 1;
    begin
      var Deep := Inner * 2;
      Writeln('nested ', Inner, ' ', Deep);
    end;
    var Deep := 'after';
    Writeln(Deep);
  end;
  begin
    var Inner := 'sibling';
    Writeln(Inner);
  end;
  repeat
    var Step := A + 1;
    A := Step;
    Write(' ', Step);
  until A >= N + 3;
  Writeln;
  try
    var T := Sum * 2;
    Writeln('try ', T);
    try
      var U := T + 1;
      Writeln('inner try ', U);
      raise Exception.Create('oops');
    finally
      var F := 'finally';
      var G := Length(F);
      Writeln(F, ' ', G);
      begin
        var Nested := F + ' block';
        Writeln(Nested);
      end;
      try
        Writeln('nested try ', G);
      finally
        Writeln(F, ' again');
      end;
    end;
  except
    var X := 'except';
    Writeln(X, ' ', Sum);
  end;
  try
    raise Exception.Create('typed');
  except
    on E: Exception do
    begin
      var M := E.Message + '!';
      Writeln(M);
    end;
    else
      var Z := 0;
      Writeln(Z);
  end;
  try
    raise TObject.Create;
  except
    on E: EAbort do
      Writeln('abort');
    else
      var Z := 'else list';
      Writeln(Z);
  end;
  case N of
    1: Writeln('one');
  else
    var C := N * 100;
    var D: string;
    D := 'case else';
    Writeln(D, ' ', C);
  end;
  for var I := 1 to 3 do
  begin
    var Sq := I * I;
    var Txt: string;
    Txt := Txt + '#';
    Write(' ', Sq, Txt);
  end;
  Writeln;
  var W := 0;
  while W < 2 do
  begin
    var Twice := W * 2;
    Inc(W);
    Write(' ', Twice);
  end;
  Writeln;
end;

function Depth(N: Integer): Integer;
begin
  var Mine := N;
  if N > 0 then
  begin
    var Below := Depth(N - 1);
    Mine := Mine + Below;
  end;
  Result := Mine;
end;

begin
  Blocks(2);
  Writeln(Depth(4));
  var Main := 'main';
  begin
    var Local := Main + ' block';
    Writeln(Local);
  end;
  Writeln(Main);
end.
