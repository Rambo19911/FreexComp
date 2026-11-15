// covers: ABI-103, ABI-102, RTL-138
// opc-run: variants 1 2 3 4
program Sys_AbstractError;
{ _AbstractError, the code of abstract VMT slots (ABI §9.1). Variant 1: no AbstractErrorProc, so runtime error 211
  (message on stderr, exit code 211). Variant 2: the hook is called first; it returns, so runtime error 211 follows.
  Variants 1 and 2 call _AbstractError directly; variants 3 and 4 do the same through a call of an abstract virtual
  method (RTL-138), without and with the hook. The variant is ParamStr(1). }

type
  TShape = class
    procedure Draw; virtual; abstract;
  end;
  TShapeClass = class of TShape;

var
  ShapeClass: TShapeClass;
  Shape: TShape;

procedure Hook;
begin
  Writeln('hook called');
end;

begin
  Writeln('assigned before ', Assigned(AbstractErrorProc));
  if (ParamStr(1) = '2') or (ParamStr(1) = '4') then
    AbstractErrorProc := Hook;
  if (ParamStr(1) = '3') or (ParamStr(1) = '4') then
  begin
    ShapeClass := TShape;
    Shape := ShapeClass.Create;
    Shape.Draw;
  end
  else
    _AbstractError;
  Writeln('not reached');
end.
