// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1341, UIF-1343
// phase: 8
program Gfx_WicImageErrors;
{ U-148, wic-image.md section 2: a load that fails changes nothing. A cut image, text and a stream without bytes
  raise EInvalidGraphic with the fixed text; the image keeps its pixels, its stored bytes and its properties and
  fires no OnChange. The COM bracket of an operation leaves the thread as it found it: on a thread that never
  initialised COM a following CoInitializeEx with the multithreaded model returns S_OK (after a load and after a
  failed load), and loading works on a thread that uses the multithreaded model, whose own initialisation is
  still the only one afterwards. Needs no desktop. }
uses
  Windows, ActiveX, SysUtils, Classes, Graphics, TestKit, WicTestKit;

const
  Damaged = 'The image is damaged or its format is not supported';

type
  { loads on its own thread; the results are checked by the main thread after WaitFor }
  TLoadThread = class(TThread)
  public
    Multithreaded: Boolean;
    FirstInit, AfterLoad, AfterFailure: HResult;
    Width: Integer;
    FailureClass, FailureMessage: string;
  protected
    procedure Execute; override;
  end;

procedure TLoadThread.Execute;
var
  Image: TWICImage;
begin
  Image := TWICImage.Create;
  try
    FirstInit := S_OK;
    if Multithreaded then
      FirstInit := CoInitializeEx(nil, COINIT_MULTITHREADED);
    Image.LoadFromFile(Fixture('alpha.png'));
    Width := Image.Width;
    if Multithreaded then
      CoUninitialize;
    { the bracket left nothing behind: this is the first initialisation of the thread again }
    AfterLoad := CoInitializeEx(nil, COINIT_MULTITHREADED);
    if Succeeded(AfterLoad) then
      CoUninitialize;
    try
      Image.LoadFromFile(Fixture('broken.png'));
    except
      on E: Exception do
      begin
        FailureClass := E.ClassName;
        FailureMessage := E.Message;
      end;
    end;
    AfterFailure := CoInitializeEx(nil, COINIT_MULTITHREADED);
    if Succeeded(AfterFailure) then
      CoUninitialize;
  finally
    Image.Free;
  end;
end;

procedure CheckUnchanged(Image: TWICImage; Counter: TChangeCounter; Original: TMemoryStream; const Name: string);
var
  Saved: TMemoryStream;
begin
  CheckEqI(0, Counter.Count, Name + ': no OnChange');
  CheckEqB(False, Image.Empty, Name + ': not empty');
  CheckEqI(16, Image.Width, Name + ': Width');
  CheckEqI(12, Image.Height, Name + ': Height');
  Check(Image.ImageFormat = wifPng, Name + ': ImageFormat');
  CheckEqB(True, Image.HasAlpha, Name + ': HasAlpha');
  CheckEqB(False, Image.Modified, Name + ': Modified');
  CheckBlocks(Image, $FFFF0000, $FF00FF00, $80000080, $00000000, 0, Name);
  Saved := TMemoryStream.Create;
  try
    Image.SaveToStream(Saved);
    Check(SameBytes(Original, Saved), Name + ': the stored bytes are those of the first load');
  finally
    Saved.Free;
  end;
end;

procedure CheckFailures;
var
  Image, Blank: TWICImage;
  Counter: TChangeCounter;
  Original, M: TMemoryStream;

  procedure FailFile(const Name: string);
  begin
    Expect('EInvalidGraphic', Damaged);
    try
      Image.LoadFromFile(Fixture(Name));
      Check(False, Name + ' from the file raises');
    except
      on E: Exception do
        CheckRaises(Name + ' from the file raises', E.ClassName, E.Message);
    end;
    CheckUnchanged(Image, Counter, Original, Name + ' from the file');
    M := FileBytes(Fixture(Name));
    try
      try
        Image.LoadFromStream(M);
        Check(False, Name + ' from a stream raises');
      except
        on E: Exception do
          CheckRaises(Name + ' from a stream raises', E.ClassName, E.Message);
      end;
    finally
      M.Free;
    end;
    CheckUnchanged(Image, Counter, Original, Name + ' from a stream');
  end;

begin
  Section('a failed load changes nothing (UIF-1343)');
  Image := TWICImage.Create;
  Blank := TWICImage.Create;
  Counter := TChangeCounter.Create;
  Original := FileBytes(Fixture('alpha.png'));
  try
    Image.LoadFromFile(Fixture('alpha.png'));
    Image.OnChange := Counter.Changed;
    FailFile('broken.png');
    FailFile('not-an-image.png');

    Expect('EInvalidGraphic', Damaged);
    M := TMemoryStream.Create;
    try
      try
        Image.LoadFromStream(M);
        Check(False, 'an empty stream raises');
      except
        on E: Exception do
          CheckRaises('an empty stream raises', E.ClassName, E.Message);
      end;
      CheckUnchanged(Image, Counter, Original, 'an empty stream');
      M.WriteBuffer(Original.Memory^, Original.Size);
      try
        Image.LoadFromStream(M);
        Check(False, 'a stream at its end raises');
      except
        on E: Exception do
          CheckRaises('a stream at its end raises', E.ClassName, E.Message);
      end;
      CheckUnchanged(Image, Counter, Original, 'a stream at its end');
    finally
      M.Free;
    end;

    Blank.OnChange := Counter.Changed;
    try
      Blank.LoadFromFile(Fixture('broken.png'));
      Check(False, 'an empty image: the failed load raises');
    except
      on E: Exception do
        CheckRaises('an empty image: the failed load raises', E.ClassName, E.Message);
    end;
    CheckEqB(True, Blank.Empty, 'an empty image stays empty');
    Check(Blank.ImageFormat = wifUnknown, 'an empty image keeps wifUnknown');
    CheckEqI(0, Counter.Count, 'an empty image: no OnChange');

    Image.LoadFromFile(Fixture('plain.bmp'));
    CheckEqI(1, Counter.Count, 'a good load after the failures fires OnChange');
    Check(Image.ImageFormat = wifBmp, 'and replaces the image');
  finally
    Original.Free;
    Counter.Free;
    Blank.Free;
    Image.Free;
  end;
end;

procedure CheckMainThread;
var
  Image: TWICImage;
  HR: HResult;
begin
  Section('the bracket on the main thread, which did not initialise COM (UIF-1341)');
  { CheckFailures ran loads and failed loads on this thread before }
  HR := CoInitializeEx(nil, COINIT_MULTITHREADED);
  CheckEqI(S_OK, HR, 'after loads and failed loads the thread is still not initialised');
  Image := TWICImage.Create;
  try
    Image.LoadFromFile(Fixture('blocks.gif'));
    CheckEqI(16, Image.Width, 'loading works while the thread uses the multithreaded model');
  finally
    Image.Free;
  end;
  if Succeeded(HR) then
    CoUninitialize;
  HR := CoInitializeEx(nil, COINIT_MULTITHREADED);
  CheckEqI(S_OK, HR, 'the thread''s own initialisation was the only one');
  if Succeeded(HR) then
    CoUninitialize;
end;

procedure CheckThread(Multithreaded: Boolean; const Name: string);
var
  T: TLoadThread;
begin
  Section(Name + ' (UIF-1341)');
  T := TLoadThread.Create(True);
  try
    T.Multithreaded := Multithreaded;
    T.Start;
    T.WaitFor;
    Check(T.FatalException = nil, 'the thread ended without an exception');
    CheckEqI(S_OK, T.FirstInit, 'the thread''s own initialisation');
    CheckEqI(16, T.Width, 'the image loads');
    CheckEqI(S_OK, T.AfterLoad, 'after the load the thread is not initialised');
    Expect('EInvalidGraphic', Damaged);
    CheckRaises('the damaged image raises', T.FailureClass, T.FailureMessage);
    CheckEqI(S_OK, T.AfterFailure, 'after the failed load the thread is not initialised');
  finally
    T.Free;
  end;
end;

begin
  CheckFailures;
  CheckMainThread;
  CheckThread(False, 'a thread that did not initialise COM');
  CheckThread(True, 'a thread that uses the multithreaded model');
  Done;
end.
