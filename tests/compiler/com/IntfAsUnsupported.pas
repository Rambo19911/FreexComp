// covers: LANG-683
program IntfAsUnsupported;

{ Card acceptance 4: "as" to an interface the stream of the operating system does not implement raises EIntfCastError;
  the reference count is the same afterwards. }

uses
  SysUtils;

type
  IStream = interface(IUnknown)
    ['{0000000C-0000-0000-C000-000000000046}']
  end;

  IUnrelated = interface(IUnknown)
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8B70}']
    procedure Nothing;
  end;

function SHCreateMemStream(Init: Pointer; Size: Cardinal): Pointer; stdcall; external 'shlwapi.dll';

{ The count while S is held: one more reference is taken and dropped, and _Release returns what remains. }
function RefCount(const S: IStream): Integer;
begin
  S._AddRef;
  Result := S._Release;
end;

procedure Run;
var
  P: Pointer;
  Stream: IStream;
  Other: IUnrelated;
  Before: Integer;
begin
  P := SHCreateMemStream(nil, 0);
  Stream := IStream(P);
  Before := RefCount(Stream);
  WriteLn('count ', Before);
  WriteLn('Supports ', Supports(Stream, IUnrelated));
  try
    Other := Stream as IUnrelated;
    WriteLn('not reached');
  except
    on E: EIntfCastError do
      WriteLn(E.ClassName, ': ', E.Message);
  end;
  WriteLn('Other is nil ', Other = nil);
  WriteLn('count unchanged ', RefCount(Stream) = Before);
  Stream := nil;
  WriteLn('final release ', IStream(P)._Release);
end;

begin
  Run;
  WriteLn('done');
end.
