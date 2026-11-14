unit Gfx;

{$L defs\gdi32.def}

interface

function StockBrush: NativeUInt;

implementation

function GetStockObject(Index: Integer): NativeUInt; stdcall; external 'gdi32.dll';

function StockBrush: NativeUInt;
begin
  Result := GetStockObject(0);                         { WHITE_BRUSH }
end;

end.