// covers: CMP-1381, ABI-150
program ErrFormClassInTwoUnits;

{ UA and UB each embed a form of class TForm1. The form resources of a program are keyed by the class name, so one of the
  two forms would load in place of the other: E3107 at the later unit's directive, nothing is linked. }

uses UA, UB;

begin
end.
