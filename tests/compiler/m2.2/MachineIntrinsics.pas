// covers: ABI-172, ABI-105
program MachineIntrinsics;
// _ReturnAddress is the (non-nil) return address of the routine containing it; _GetMXCSR/_SetMXCSR read and write the
// SSE control/status register: at start-up its control bits are $1F80 (all exceptions masked, round to nearest), and a
// value set is read back (the status flags, bits 0..5, are masked off because any floating-point operation may set them).
function Here: Pointer;
begin
  Result := _ReturnAddress;
end;

var
  Saved: Cardinal;
begin
  Writeln(Here <> nil);
  Writeln(_ReturnAddress() <> nil);
  Saved := _GetMXCSR;
  Writeln(Saved and $FFC0);
  _SetMXCSR($7F80);
  Writeln(_GetMXCSR and $FFC0);
  _SetMXCSR(Saved);
  Writeln((_GetMXCSR and $FFC0) = (Saved and $FFC0));
end.
