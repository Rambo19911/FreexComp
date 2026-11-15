// phase: 4
// covers: RTL-500, RTL-505
program Cls_ReaderPropValueError;
{ TReader.PropValueError (classes.md section 10.7, task R-203): the read proc of a defined property rejects a value
  it has read and raises F0103 "Invalid value for property <path>" at the position where the value starts - for
  DefineProperty after or before consuming the value, for a value spanning lines, and for DefineBinaryProperty,
  whose stream proc reaches the reader kept by DefineProperties. OnError may handle it: the property line is
  skipped and reading continues. }
uses
  SysUtils, Classes, TestKit;

type
  TChecked = class(TComponent)
  private
    FReader: TReader;
  public
    Small, After: Integer;
    Lines: string;
  protected
    procedure DefineProperties(Filer: TFiler); override;
    procedure ReadSmall(Reader: TReader);
    procedure ReadNotString(Reader: TReader);
    procedure ReadLines(Reader: TReader);
    procedure ReadAfter(Reader: TReader);
    procedure ReadKind(Stream: TStream);
  end;

  THandler = class
    Messages: string;
    procedure Skip(Reader: TReader; const Message: string; var Handled: Boolean);
  end;

procedure TChecked.DefineProperties(Filer: TFiler);
begin
  inherited DefineProperties(Filer);
  if Filer is TReader then
    FReader := TReader(Filer);
  Filer.DefineProperty('Small', ReadSmall, nil, True);
  Filer.DefineProperty('NotString', ReadNotString, nil, True);
  Filer.DefineProperty('Lines', ReadLines, nil, True);
  Filer.DefineProperty('After', ReadAfter, nil, True);
  Filer.DefineBinaryProperty('Kind', ReadKind, nil, True);
end;

{ consumes the value first, then rejects it }
procedure TChecked.ReadSmall(Reader: TReader);
var
  V: Integer;
begin
  V := Reader.ReadInteger;
  if V > 100 then
    Reader.PropValueError;
  Small := V;
end;

{ rejects before consuming anything }
procedure TChecked.ReadNotString(Reader: TReader);
begin
  if Reader.NextValue = vaString then
    Reader.PropValueError;
  Reader.SkipValue;
end;

{ a list: the error is reported at "(", not at the element being read }
procedure TChecked.ReadLines(Reader: TReader);
var
  S: string;
begin
  Reader.ReadListBegin;
  while not Reader.EndOfList do
  begin
    S := Reader.ReadString;
    if S = 'bad' then
      Reader.PropValueError;
    Lines := Lines + '[' + S + ']';
  end;
  Reader.ReadListEnd;
end;

procedure TChecked.ReadAfter(Reader: TReader);
begin
  After := Reader.ReadInteger;
end;

{ the first byte names the content kind; only 1 is known }
procedure TChecked.ReadKind(Stream: TStream);
var
  B: Byte;
begin
  B := 0;
  Stream.Read(B, 1);
  if B <> 1 then
    FReader.PropValueError;
end;

procedure THandler.Skip(Reader: TReader; const Message: string; var Handled: Boolean);
begin
  Messages := Messages + Message + ';';
  Handled := True;
end;

function Err(const Body: string): string;
var
  C: TChecked;
begin
  Result := '';
  C := TChecked.Create(nil);
  try
    ReadComponentFromText('object C: TChecked'#10 + Body + 'end'#10, C, 'C.form');
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
  C.Free;
end;

var
  C: TChecked;
  H: THandler;
  S: TStringStream;
  R: TReader;
begin
  Section('accepted values');
  C := TChecked.Create(nil);
  ReadComponentFromText('object C: TChecked'#10'  Small = 7'#10'  Lines = ('#10'    ''a'''#10'    ''b'''#10'  )'#10 +
    '  NotString = 3'#10'  Kind = {'#10'    01FF'#10'  }'#10'end'#10, C, 'C.form');
  CheckEqI(7, C.Small, 'Small read');
  CheckEqS('[a][b]', C.Lines, 'Lines read');
  C.Free;

  Section('F0103 at the value position');
  CheckEqS('EReadError: C.form(2,11): error F0103: Invalid value for property Small', Err('  Small = 500'#10),
    'after the value was consumed');
  CheckEqS('EReadError: C.form(3,17): error F0103: Invalid value for property NotString',
    Err('  Small = 1'#10'  NotString =   ''s'''#10), 'before the value is consumed');
  CheckEqS('EReadError: C.form(2,11): error F0103: Invalid value for property Lines',
    Err('  Lines = ('#10'    ''ok'''#10'    ''bad'''#10'  )'#10), 'a list value: its opening parenthesis');
  CheckEqS('EReadError: C.form(2,10): error F0103: Invalid value for property Kind',
    Err('  Kind = {'#10'    02'#10'  }'#10), 'a binary value: its opening brace');

  Section('OnError skips the property line');
  H := THandler.Create;
  C := TChecked.Create(nil);
  S := TStringStream.Create('object C: TChecked'#10'  Small = 500'#10'  Kind = {'#10'    03'#10'  }'#10 +
    '  After = 5'#10'end'#10);
  R := TReader.Create(S, 4096);
  R.SourceName := 'C.form';
  R.OnError := H.Skip;
  R.ReadRootComponent(C);
  CheckEqS('C.form(2,11): error F0103: Invalid value for property Small;' +
    'C.form(3,10): error F0103: Invalid value for property Kind;', H.Messages, 'both errors reported to OnError');
  CheckEqI(0, C.Small, 'the rejected value is not stored');
  CheckEqI(5, C.After, 'reading continues after the skipped lines');
  R.Free;
  S.Free;
  C.Free;
  H.Free;
  Done;
end.
