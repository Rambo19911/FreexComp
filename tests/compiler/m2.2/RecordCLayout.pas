// covers: LANG-263, ABI-060, ABI-064
program RecordCLayout;
// Records mirroring Win32 structures have the Win64 C layout: SizeOf and field offsets (measured through addresses)
// equal the hand-computed C values; packed = #pragma pack(1), {$ALIGN 4} = #pragma pack(4).
type
  TPoint = record
    X, Y: Integer;
  end;

  TMsg = record
    hwnd: Pointer;
    message: Cardinal;
    wParam: NativeUInt;
    lParam: NativeInt;
    time: Cardinal;
    pt: TPoint;
    lPrivate: Cardinal;
  end;

  TLogFont = record
    lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight: Integer;
    lfItalic, lfUnderline, lfStrikeOut, lfCharSet: Byte;
    lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily: Byte;
    lfFaceName: array[0..31] of WideChar;
  end;

  TInner = record
    A: AnsiChar;
    B: Integer;
  end;

  TNested = record
    C: AnsiChar;
    I: TInner;
    D: AnsiChar;
  end;

  TPacked = packed record
    C: AnsiChar;
    I: Integer;
    S: SmallInt;
    D: Double;
  end;

{$ALIGN 4}
  TAlign4 = record
    C: AnsiChar;
    X: Double;
    S: SmallInt;
  end;
{$ALIGN 8}

var
  M: TMsg;
  L: TLogFont;
  N: TNested;
  P: TPacked;
  A: TAlign4;

function Off(Field, Base: Pointer): Integer;
begin
  Result := NativeInt(Field) - NativeInt(Base);
end;

begin
  Writeln('TPoint ', SizeOf(TPoint));
  Writeln('TMsg ', SizeOf(TMsg), ': ', Off(@M.message, @M), ' ', Off(@M.wParam, @M), ' ', Off(@M.lParam, @M), ' ',
    Off(@M.time, @M), ' ', Off(@M.pt, @M), ' ', Off(@M.lPrivate, @M));
  Writeln('TLogFont ', SizeOf(TLogFont), ': ', Off(@L.lfItalic, @L), ' ', Off(@L.lfPitchAndFamily, @L), ' ',
    Off(@L.lfFaceName, @L));
  Writeln('TNested ', SizeOf(TNested), ': ', Off(@N.I, @N), ' ', Off(@N.I.B, @N), ' ', Off(@N.D, @N));
  Writeln('TPacked ', SizeOf(TPacked), ': ', Off(@P.I, @P), ' ', Off(@P.S, @P), ' ', Off(@P.D, @P));
  Writeln('TAlign4 ', SizeOf(TAlign4), ': ', Off(@A.X, @A), ' ', Off(@A.S, @A));
end.
