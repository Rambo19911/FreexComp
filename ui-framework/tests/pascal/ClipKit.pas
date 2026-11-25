// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
unit ClipKit;
{ U-152: clipboard operations of an edit control for programs that assert on them (shared unit, not a program).
  The clipboard belongs to the whole window station: a program that watches it (a clipboard history, a remote
  session) opens it for a moment after every change, and while it is open the cut, copy and paste of an edit
  control do nothing at all. A program that copies and then pastes at once therefore sometimes pastes nothing.
  The functions here repeat one operation until its effect is seen, with a time limit (a wait on a condition, no
  fixed delay). An operation that did nothing left the control and the clipboard as they were, so repeating it
  is the same as doing it once. }

interface

uses
  Windows, Forms, StdCtrls, ActnList;

const
  ClipWaitMs = 10000;

{ A.Execute (a cut or a paste on C) until C.Text = Want; the result of the last Execute }
function ExecuteUntilText(A: TCustomAction; C: TCustomEdit; const Want: string): Boolean;
{ A.Execute (a copy) until the clipboard text is Want; the result of the last Execute }
function ExecuteUntilCopied(A: TCustomAction; const Want: string): Boolean;
{ the clipboard text, read by pasting into an edit of this unit until it is Want or the time is over }
function ClipboardText(const Want: string): string;
{ one such paste: the clipboard text, or '' while the clipboard is open elsewhere }
function ClipboardTextNow: string;
{ for a loop of the program around an operation of its own: True when the time since Start (GetTickCount) is
  over, else a short pause }
function ClipWaitOver(Start: DWORD): Boolean;

implementation

var
  ProbeForm: TForm;                                          { never shown }
  Probe: TEdit;

function ClipWaitOver(Start: DWORD): Boolean;
begin
  Result := GetTickCount - Start > ClipWaitMs;
  if not Result then
    Sleep(1);
end;

function ClipboardTextNow: string;
begin
  if Probe = nil then
  begin
    ProbeForm := TForm.CreateNew(nil);
    Probe := TEdit.Create(ProbeForm);
    Probe.Parent := ProbeForm;
    Probe.HandleNeeded;
  end;
  Probe.Text := '';
  Probe.PasteFromClipboard;
  Result := Probe.Text;
end;

function ExecuteUntilText(A: TCustomAction; C: TCustomEdit; const Want: string): Boolean;
var
  Start: DWORD;
begin
  Start := GetTickCount;
  repeat
    Result := A.Execute;
  until (C.Text = Want) or ClipWaitOver(Start);
end;

function ExecuteUntilCopied(A: TCustomAction; const Want: string): Boolean;
var
  Start: DWORD;
begin
  Start := GetTickCount;
  repeat
    Result := A.Execute;
  until (ClipboardTextNow = Want) or ClipWaitOver(Start);
end;

function ClipboardText(const Want: string): string;
var
  Start: DWORD;
begin
  Start := GetTickCount;
  repeat
    Result := ClipboardTextNow;
  until (Result = Want) or ClipWaitOver(Start);
end;

initialization

finalization
  ProbeForm.Free;

end.
