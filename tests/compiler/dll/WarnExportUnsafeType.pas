// covers: LANG-551
// opc: --emit obj
library WarnExportUnsafeType;
// exporting a routine whose parameter or result type is not boundary-safe is allowed and warned once
// per export item (first offending type); {$WARN EXPORT_UNSAFE_TYPE OFF} silences it; the library still builds

type
  TVec3 = record X, Y, Z: Integer; end;
  TNamed = record Id: Integer; Name: string; end;
  TInts = array of Integer;

function Safe(A: Integer; const V: TVec3; P: PChar): TVec3; stdcall;
begin
  Result := V;
  Result.X := A;
end;

function Greeting(const Who: string): string;
begin
  Result := 'Hello, ' + Who;
end;

procedure Fill(var Target: TNamed; const Values: TInts);
begin
  Target.Id := Length(Values);
end;

function Count(const Values: array of Integer): Integer;
begin
  Result := Length(Values);
end;

exports
  Safe,
  Greeting,
  Greeting name 'greeting_again',
  Fill,
  Count;

{$WARN EXPORT_UNSAFE_TYPE OFF}
exports
  Greeting name 'quiet_greeting';
{$WARN EXPORT_UNSAFE_TYPE ON}

begin
end.
