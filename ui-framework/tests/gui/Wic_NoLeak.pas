// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1359
program Wic_NoLeak;
{ U-150, wic-image.md UIF-1359: loads a valid image 1 000 times and then a damaged one 1 000 times, each into a
  new TWICImage that is freed again, after a warm-up of 10 loads of each kind. Prints the GDI object count
  (GetGuiResources), the kernel handle count (GetProcessHandleCount) and the count of live allocations of the
  process after the warm-up and at the end, the number of loads of each kind that behaved as expected, and the
  elapsed time of the 2 000 loads in milliseconds. The first parameter is the folder of the fixture images.
  WicImageLeakTests compiles and runs this program, waits for its end and compares the counts. }
uses
  Windows, SysUtils, Classes, Graphics;

type
  { GetProcessHandleCount of kernel32, which the Windows unit does not declare: bound at run time }
  THandleCountFunc = function(hProcess: THandle; var pdwHandleCount: DWORD): BOOL; stdcall;

var
  Dir: string;
  Loaded, Refused: Integer;

procedure LoadGood;
var
  Image: TWICImage;
begin
  Image := TWICImage.Create;
  try
    Image.LoadFromFile(Dir + 'alpha.png');
    if (Image.Width = 16) and (Image.Height = 12) and Image.HasAlpha then
      Inc(Loaded);
  finally
    Image.Free;
  end;
end;

procedure LoadDamaged;
var
  Image: TWICImage;
begin
  Image := TWICImage.Create;
  try
    try
      Image.LoadFromFile(Dir + 'broken.png');
    except
      on E: EInvalidGraphic do
        if Image.Empty then
          Inc(Refused);
    end;
  finally
    Image.Free;
  end;
end;

procedure PrintCounts(const Name: string);
var
  Handles: DWORD;
  Count: THandleCountFunc;
begin
  Handles := 0;
  Count := THandleCountFunc(GetProcAddress(GetModuleHandleW('kernel32.dll'), 'GetProcessHandleCount'));
  if not Assigned(Count) or not Count(GetCurrentProcess, Handles) then
  begin
    Writeln('GetProcessHandleCount failed');
    Halt(2);
  end;
  Writeln(Name, '.gdi=', GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS));
  Writeln(Name, '.handles=', Handles);
  Writeln(Name, '.allocs=', _LiveAllocCount);
end;

var
  I: Integer;
  Start: UInt64;
begin
  Dir := IncludeTrailingPathDelimiter(ParamStr(1));
  for I := 1 to 10 do
    LoadGood;
  for I := 1 to 10 do
    LoadDamaged;
  Loaded := 0;
  Refused := 0;
  PrintCounts('warm');
  Start := GetTickCount64;
  for I := 1 to 1000 do
    LoadGood;
  for I := 1 to 1000 do
    LoadDamaged;
  Start := GetTickCount64 - Start;
  PrintCounts('end');
  Writeln('loaded=', Loaded);
  Writeln('refused=', Refused);
  Writeln('ms=', Start);
end.
