{ context: routine-directive }
unit RoutineDirectiveMethod;
interface
type
  TWorker = class
    procedure Work; {|}
  end;
implementation
procedure TWorker.Work; begin end;
end.
{#has virtual, override, overload, abstract, reintroduce}
{#not begin, TWorker}
