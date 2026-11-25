// covers: UIF-1325, UIF-1326, UIF-1327, UIF-1328, UIF-1329, UIF-1330
// needs: desktop
// phase: 8
// opc-run: tempdir
program Ctl_RichEditStreams;
{ U-145, rich-edit.md section 5: PlainText decides the format of Lines.LoadFromStream / LoadFromFile /
  SaveToStream / SaveToFile and changes no content (UIF-1325); an RTF save starts with the RTF signature, is
  written at the stream's position and leaves content, selection and Modified alone (UIF-1327); a bold red run
  saved from one control and loaded into another keeps its attributes, the load replaces the content, puts the
  caret at 0, clears Modified and the undo buffer and fires OnChange once (UIF-1326, card acceptance 2); a stream
  without the RTF signature is loaded as text; the text path with and without TEncoding, the TEncoding overloads
  being text also for a rich control (UIF-1328); an exception raised by the stream arrives as the same object and a
  failed load leaves the control empty (UIF-1329); RTF loading needs a window, text loading does not (UIF-1330). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ComCtrls, RichEdit, UIConsts,
  TestKit;

{$POINTERMATH ON}

type
  EFailingStream = class(Exception);

  { a memory stream that raises once FailAfter bytes were read, or on every write }
  TFailingStream = class(TMemoryStream)
  public
    FailAfter: Integer;
    FailWrites: Boolean;
    Raised: Pointer;
    function Read(var Buffer; Count: Integer): Integer; override;
    function Write(const Buffer; Count: Integer): Integer; override;
  end;

  TLog = class
  public
    Text: string;
    procedure Changed(Sender: TObject);
  end;

const
  CRLF = #13#10;

var
  Log: TLog;

function TFailingStream.Read(var Buffer; Count: Integer): Integer;
var
  E: EFailingStream;
begin
  if (FailAfter >= 0) and (Position >= FailAfter) then
  begin
    E := EFailingStream.Create('read failed');
    Raised := Pointer(E);
    raise E;
  end;
  if (FailAfter >= 0) and (Position + Count > FailAfter) then
    Count := Integer(FailAfter - Position);
  Result := inherited Read(Buffer, Count);
end;

function TFailingStream.Write(const Buffer; Count: Integer): Integer;
var
  E: EFailingStream;
begin
  if FailWrites then
  begin
    E := EFailingStream.Create('write failed');
    Raised := Pointer(E);
    raise E;
  end;
  Result := inherited Write(Buffer, Count);
end;

procedure TLog.Changed(Sender: TObject);
begin
  Text := Text + 'change;';
end;

function Shown(const S: string): string;
begin
  Result := StringReplace(S, CRLF, '|', [rfReplaceAll]);
end;

procedure NativeSelect(H: HWND; First, Last: Integer);
var
  R: TCharRange;
begin
  R.cpMin := First;
  R.cpMax := Last;
  SendMessageW(H, EM_EXSETSEL, 0, LPARAM(@R));
end;

{ the first Count bytes of a stream from Start, one character per byte }
function Head(M: TMemoryStream; Start, Count: Integer): string;
var
  P: PByte;
  I: Integer;
begin
  Result := '';
  P := PByte(M.Memory);
  for I := Start to Start + Count - 1 do
    if I < M.Size then
      Result := Result + Char(P[I]);
end;

procedure PutBytes(M: TStream; const S: string);
var
  B: TBytes;
begin
  B := TEncoding.ASCII.GetBytes(S);
  if Length(B) > 0 then
    M.WriteBuffer(B[0], Length(B));
end;

function StyleStr(S: TFontStyles): string;
begin
  Result := '';
  if fsBold in S then
    Result := Result + 'B';
  if fsItalic in S then
    Result := Result + 'I';
  if Result = '' then
    Result := '-';
end;

{ style and colour of First .. Last }
function Run(R: TRichEdit; First, Last: Integer): string;
begin
  NativeSelect(R.Handle, First, Last);
  Result := StyleStr(R.SelAttributes.Style) + ' ' + ColorToString(R.SelAttributes.Color);
end;

procedure Test;
var
  F: TForm;
  A, B, P: TRichEdit;
  M: TMemoryStream;
  FS: TFailingStream;
  Dir, FileName, Cls, Msg: string;
  Caught: Pointer;
begin
  Application.Initialize;
  Dir := IncludeTrailingPathDelimiter(ParamStr(1));
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  M := TMemoryStream.Create;
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    A := TRichEdit.Create(F);
    A.Parent := F;
    A.HandleNeeded;
    B := TRichEdit.Create(F);
    B.Parent := F;
    B.HandleNeeded;
    A.Text := 'plain bold red tail' + CRLF + 'second';
    NativeSelect(A.Handle, 6, 14);
    A.SelAttributes.Style := [fsBold];
    A.SelAttributes.Color := clRed;

    Section('RTF save (UIF-1327)');
    CheckEqB(False, A.PlainText, 'PlainText False: RTF');
    A.Modified := True;
    NativeSelect(A.Handle, 3, 8);
    PutBytes(M, '##');
    A.Lines.SaveToStream(M);
    CheckEqS('##{\rtf', Head(M, 0, 7), 'the output starts with the RTF signature at the stream''s position');
    Check(M.Position = M.Size, 'the stream stands after the written bytes');
    CheckEqS('plain bold red tail|second', Shown(A.Text), 'content unchanged');
    CheckEqS('3+5', Format('%d+%d', [A.SelStart, A.SelLength]), 'selection unchanged');
    CheckEqB(True, A.Modified, 'Modified unchanged');

    Section('RTF load into a second control (UIF-1326)');
    B.Text := 'old content';
    B.SelStart := 4;
    B.SelText := 'X';
    B.Modified := True;
    CheckEqB(True, B.CanUndo, 'the second control has something to undo');
    B.OnChange := Log.Changed;
    Log.Text := '';
    M.Position := 2;
    B.Lines.LoadFromStream(M);
    CheckEqS('change;', Log.Text, 'OnChange fires once');
    CheckEqS('plain bold red tail|second', Shown(B.Text), 'the whole content is replaced');
    CheckEqS('0+0', Format('%d+%d', [B.SelStart, B.SelLength]), 'the caret is at position 0');
    CheckEqB(False, B.Modified, 'Modified is False');
    CheckEqB(False, B.CanUndo, 'the undo buffer is empty');
    CheckEqS('B clRed', Run(B, 6, 14), 'the run is bold and red (card acceptance 2)');
    CheckEqS('- clWindowText', Run(B, 0, 6), 'the text before it is plain');
    CheckEqS('- clWindowText', Run(B, 14, 19), 'the text after it is plain');
    CheckEqI(2, B.Lines.Count, 'two paragraphs');
    B.OnChange := nil;

    Section('files (UIF-1325)');
    FileName := Dir + 'rich.rtf';
    A.Lines.SaveToFile(FileName);
    M.Clear;
    M.LoadFromFile(FileName);
    CheckEqS('{\rtf', Head(M, 0, 5), 'SaveToFile writes RTF');
    B.Text := '';
    B.Lines.LoadFromFile(FileName);
    CheckEqS('plain bold red tail|second', Shown(B.Text), 'LoadFromFile reads it');
    CheckEqS('B clRed', Run(B, 6, 14), 'with the formatting');

    Section('text fallback (UIF-1326)');
    M.Clear;
    PutBytes(M, 'just text' + CRLF + 'line two');
    M.Position := 0;
    B.Lines.LoadFromStream(M);
    CheckEqS('just text|line two', Shown(B.Text), 'a stream without the RTF signature is loaded as text');
    CheckEqS('- clWindowText', Run(B, 0, 9), 'in the default format');
    M.Clear;
    PutBytes(M, '{\rt');
    M.Position := 0;
    B.Lines.LoadFromStream(M);
    CheckEqS('{\rt', B.Text, 'a stream shorter than the signature is text');
    M.Clear;
    M.Position := 0;
    B.Lines.LoadFromStream(M);
    CheckEqS('', B.Text, 'an empty stream gives an empty control');

    Section('PlainText (UIF-1325, UIF-1328)');
    A.PlainText := True;
    CheckEqS('plain bold red tail|second', Shown(A.Text), 'PlainText changes no content');
    CheckEqS('B clRed', Run(A, 6, 14), 'and no formatting');
    M.Clear;
    A.Lines.SaveToStream(M);
    CheckEqS('plain bold red tail|second|', Shown(Head(M, 0, 100)), 'PlainText: the save is the text');
    B.PlainText := True;
    M.Position := 0;
    B.Lines.LoadFromStream(M);
    CheckEqS('plain bold red tail|second', Shown(B.Text), 'PlainText: the load reads text');
    CheckEqS('- clWindowText', Run(B, 6, 14), 'without formatting');
    M.Clear;
    PutBytes(M, '{\rtf1 not parsed}');
    M.Position := 0;
    B.Lines.LoadFromStream(M);
    CheckEqS('{\rtf1 not parsed}', B.Text, 'PlainText: RTF source is shown as it is');
    FileName := Dir + 'plain.txt';
    A.Lines.SaveToFile(FileName);
    B.Lines.LoadFromFile(FileName);
    CheckEqS('plain bold red tail|second', Shown(B.Text), 'PlainText: file round trip');
    B.PlainText := False;
    B.Lines.LoadFromFile(FileName);
    CheckEqS('plain bold red tail|second', Shown(B.Text), 'a text file given to a rich control shows its text');

    Section('TEncoding overloads (UIF-1328)');
    M.Clear;
    A.Lines.SaveToStream(M, TEncoding.Unicode);
    CheckEqI(Length('plain bold red tail' + CRLF + 'second' + CRLF) * 2, M.Size, 'UTF-16: two bytes per character');
    M.Position := 0;
    B.Lines.LoadFromStream(M, TEncoding.Unicode);
    CheckEqS('plain bold red tail|second', Shown(B.Text), 'UTF-16 round trip');
    A.PlainText := False;
    M.Clear;
    A.Lines.SaveToStream(M, TEncoding.UTF8);
    CheckEqS('plain', Head(M, 0, 5), 'a rich control: the TEncoding overload still saves text');
    M.Clear;
    PutBytes(M, '{\rtf1 as text}');
    M.Position := 0;
    B.Lines.LoadFromStream(M, TEncoding.UTF8);
    CheckEqS('{\rtf1 as text}', B.Text, 'a rich control: the TEncoding overload still loads text');
    FileName := Dir + 'utf8.txt';
    A.Lines.SaveToFile(FileName, TEncoding.UTF8);
    B.Lines.LoadFromFile(FileName, TEncoding.UTF8);
    CheckEqS('plain bold red tail|second', Shown(B.Text), 'file round trip with an encoding');

    Section('a failing stream (UIF-1329)');
    FS := TFailingStream.Create;
    try
      FS.FailAfter := -1;
      A.Lines.SaveToStream(FS);
      Check(FS.Size > 40, 'an RTF document to fail on');
      FS.Position := 0;
      FS.FailAfter := 20;
      B.Text := 'will be lost';
      Caught := nil;
      Cls := '';
      try
        B.Lines.LoadFromStream(FS);
      except
        on E: Exception do
        begin
          Caught := Pointer(E);
          Cls := E.ClassName + ': ' + E.Message;
        end;
      end;
      CheckEqS('EFailingStream: read failed', Cls, 'the exception of Read arrives');
      Check((Caught <> nil) and (Caught = FS.Raised), 'it is the same exception object');
      CheckEqS('', B.Text, 'after the failed load the control is empty');
      B.Text := 'usable';
      CheckEqS('usable', B.Text, 'and still usable');
      FS.FailAfter := 0;
      FS.Position := 0;
      Cls := '';
      try
        B.Lines.LoadFromStream(FS);
      except
        on E: Exception do
          Cls := E.ClassName + ': ' + E.Message;
      end;
      CheckEqS('EFailingStream: read failed', Cls, 'a stream that fails on its first read');
      CheckEqS('', B.Text, 'the control is empty');
      FS.FailAfter := -1;
      FS.FailWrites := True;
      FS.Clear;
      Caught := nil;
      Cls := '';
      NativeSelect(A.Handle, 2, 4);
      try
        A.Lines.SaveToStream(FS);
      except
        on E: Exception do
        begin
          Caught := Pointer(E);
          Cls := E.ClassName + ': ' + E.Message;
        end;
      end;
      CheckEqS('EFailingStream: write failed', Cls, 'the exception of Write arrives');
      Check((Caught <> nil) and (Caught = FS.Raised), 'it is the same exception object');
      CheckEqS('plain bold red tail|second', Shown(A.Text), 'a failed save leaves the content');
      CheckEqS('2+2', Format('%d+%d', [A.SelStart, A.SelLength]), 'and the selection');
      FS.FailWrites := False;
      FS.Clear;
      A.Lines.SaveToStream(FS);
      CheckEqS('{\rtf', Head(FS, 0, 5), 'the control saves again afterwards');
    finally
      FS.Free;
    end;

    Section('an error of the engine (UIF-1329)');
    M.Clear;
    PutBytes(M, '{\rtf1 a document that never closes its group');
    M.Position := 0;
    B.Text := 'will be lost';
    Cls := '';
    Msg := '';
    try
      B.Lines.LoadFromStream(M);
    except
      on E: Exception do
      begin
        Cls := E.ClassName;
        Msg := E.Message;
      end;
    end;
    Expect('EStreamError', SRichEditLoadFail);
    CheckRaises('the engine reports an error without an exception: EStreamError', Cls, Msg);
    CheckEqS('', B.Text, 'after the failed load the control is empty');

    Section('without a parent (UIF-1330)');
    P := TRichEdit.Create(nil);
    try
      P.Name := 'Orphan';
      M.Clear;
      A.Lines.SaveToStream(M);
      M.Position := 0;
      Cls := '';
      Msg := '';
      try
        P.Lines.LoadFromStream(M);
      except
        on E: Exception do
        begin
          Cls := E.ClassName;
          Msg := E.Message;
        end;
      end;
      Expect('EInvalidOperation', 'Control "Orphan" has no parent window');
      CheckRaises('RTF loading needs a window', Cls, Msg);
      Check(not P.HandleAllocated, 'no handle');
      P.PlainText := True;
      M.Clear;
      PutBytes(M, 'text' + CRLF + 'only');
      M.Position := 0;
      P.Lines.LoadFromStream(M);
      CheckEqS('text|only', Shown(P.Text), 'text loading works without a handle');
      CheckEqI(2, P.Lines.Count, 'into the internal list');
      M.Clear;
      P.Lines.SaveToStream(M);
      CheckEqS('text|only|', Shown(Head(M, 0, 100)), 'text saving works without a handle');
    finally
      P.Free;
    end;
  finally
    M.Free;
    F.Free;
    Log.Free;
  end;
end;

begin
  Test;
  Done;
end.
