// covers: CMP-1323, CMP-1324, CMP-1325
program ResRcData;

{ Card F09-017, acceptance 3: the program names a resource script; the script is compiled and linked, and the program
  reads the entries back from its own image. }

{$R *.rc}

uses
  Classes, Windows;

procedure Dump(const Title: string; S: TResourceStream);
var
  B: Byte;
  I: Integer;
begin
  Write(Title, ': size ', S.Size, ', bytes');
  for I := 1 to S.Size do
  begin
    S.Read(B, 1);
    Write(' ', B);
  end;
  Writeln;
end;

var
  S: TResourceStream;
begin
  S := TResourceStream.Create(HInstance, 'PAYLOAD', RT_RCDATA);
  try
    Dump('PAYLOAD', S);
  finally
    S.Free;
  end;

  S := TResourceStream.CreateFromID(HInstance, 7, RT_RCDATA);
  try
    Dump('7', S);
  finally
    S.Free;
  end;

  try
    TResourceStream.Create(HInstance, 'MISSING', RT_RCDATA).Free;
    Writeln('MISSING: found');
  except
    on E: EResNotFound do
      Writeln('MISSING: ', E.ClassName);
  end;
end.
