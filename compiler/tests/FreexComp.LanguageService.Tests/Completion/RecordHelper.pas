{ context: record-helper }
program RecordHelper;
type
  TVector = record
    X, Y: Double;
  end;
  TVectorHelper = record helper for TVector
    function Length: Double;
    procedure Scale(Factor: Double);
  end;
function TVectorHelper.Length: Double; begin Result := Sqrt(X * X + Y * Y) end;
procedure TVectorHelper.Scale(Factor: Double); begin X := X * Factor end;
var
  V: TVector;
begin
  V.{|}
end.
{#has X, Y, Length, Scale}
{#kind Length=Method, X=Field}
