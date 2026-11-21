// covers: UIF-331
program LayoutHost;
{ U-051, layout.md section 15: the run-time side of the shared layout corpus. Usage: LayoutHost <form> [<steps>].
  Dump mode (no window handles, PixelsPerInch 96, no load scaling) is switched on before any form exists; the .form
  is read with ReadComponentFromText into a plain TForm; the steps (default: a single dump) run in file order:
    client <width> <height>                       the root form's client size
    set <ComponentName> <Property> <value>       explicit property assignment through RTTI
    dump                                          a dump of the root form (layout.md UIF-320)
    scale <M> <D>                                 Root.ScaleBy(M, D); the dump's dpi stays 96
  One dump is printed as it is; several are printed as a JSON array of the dumps, each indented by two spaces.
  Any error goes to stderr with exit code 1. This program is run by the test LayoutCases_Runtime, not as a golden
  program of group A. }
uses
  SysUtils, Classes, TypInfo, Controls, Forms, StdCtrls, ExtCtrls, ComCtrls;

var
  Root: TForm;
  Dumps: TStringList;

function ReadText(const FileName: string): string;
var
  L: TStringList;
begin
  L := TStringList.Create;
  try
    L.LoadFromFile(FileName);
    Result := L.Text;
  finally
    L.Free;
  end;
end;

procedure Fail(const Msg: string);
begin
  WriteLn(ErrOutput, 'LayoutHost: ', Msg);
  Halt(1);
end;

function IntArg(const S, Line: string): Integer;
begin
  if not TryStrToInt(S, Result) then
    Fail('not an integer: ''' + S + ''' in step ''' + Line + '''');
end;

procedure SetProperty(const CompName, PropName, Value, Line: string);
var
  C: TComponent;
  P: PPropInfo;
begin
  if SameText(CompName, Root.Name) then
    C := Root
  else
    C := Root.FindComponent(CompName);
  if C = nil then
    Fail('no component ''' + CompName + ''' in step ''' + Line + '''');
  P := GetPropInfo(C, PropName);
  if P = nil then
    Fail('no published property ''' + PropName + ''' in step ''' + Line + '''');
  case P^.PropType^.Kind of
    tkInteger, tkInt64:
      SetOrdProp(C, P, IntArg(Value, Line));
    tkEnumeration:
      SetEnumProp(C, P, Value);
  else
    Fail('unsupported property kind in step ''' + Line + '''');
  end;
end;

procedure RunStep(const Line: string);
var
  W: TStringList;
  Cmd: string;
begin
  W := TStringList.Create;
  try
    W.Delimiter := ' ';
    W.StrictDelimiter := True;
    W.DelimitedText := Trim(Line);
    if (W.Count = 0) or (Trim(Line) = '') then
      Exit;
    Cmd := LowerCase(W[0]);
    if (Cmd = 'dump') and (W.Count = 1) then
      Dumps.Add(LayoutDumpText(Root))
    else if (Cmd = 'client') and (W.Count = 3) then
    begin
      Root.ClientWidth := IntArg(W[1], Line);
      Root.ClientHeight := IntArg(W[2], Line);
    end
    else if (Cmd = 'scale') and (W.Count = 3) then
      Root.ScaleBy(IntArg(W[1], Line), IntArg(W[2], Line))
    else if (Cmd = 'set') and (W.Count = 4) then
      SetProperty(W[1], W[2], W[3], Line)
    else
      Fail('bad step ''' + Line + '''');
  finally
    W.Free;
  end;
end;

procedure Main;
var
  Steps: TStringList;
  I, J: Integer;
  Lines: TStringList;
  S: string;
begin
  if (ParamCount < 1) or (ParamCount > 2) then
    Fail('usage: LayoutHost <form> [<steps>]');
  LayoutDumpMode := True;                                    { UIF-311, UIF-313: before any form exists }
  Application.Initialize;
  Root := TForm.CreateNew(nil);
  ReadComponentFromText(ReadText(ParamStr(1)), Root, ExtractFileName(ParamStr(1)));
  Dumps := TStringList.Create;
  Steps := TStringList.Create;
  try
    if ParamCount = 2 then
      Steps.LoadFromFile(ParamStr(2))
    else
      Steps.Add('dump');
    for I := 0 to Steps.Count - 1 do
      RunStep(Steps[I]);
    if Dumps.Count = 1 then
      Write(Dumps[0])
    else
    begin
      S := '[' + #10;
      Lines := TStringList.Create;
      try
        for I := 0 to Dumps.Count - 1 do
        begin
          Lines.Text := Dumps[I];
          for J := 0 to Lines.Count - 1 do
          begin
            S := S + '  ' + Lines[J];
            if (J = Lines.Count - 1) and (I < Dumps.Count - 1) then
              S := S + ',';
            S := S + #10;
          end;
        end;
      finally
        Lines.Free;
      end;
      Write(S + ']' + #10);
    end;
  finally
    Steps.Free;
    Dumps.Free;
  end;
end;

begin
  try
    Main;
  except
    on E: Exception do
      Fail(E.ClassName + ': ' + E.Message);
  end;
end.
