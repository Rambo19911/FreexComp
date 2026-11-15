// covers: RTL-133, RTL-138, RTL-212
program SU_HookLifetime;
{ system.md section 11.3: the initialization of SysUtils sets ErrorProc, ExceptProc, ExceptObjProc, AssertErrorProc
  and AbstractErrorProc; its finalization restores nil. SUHookProbe prints the hooks before SysUtils is initialised
  and after it is finalised, the main block while SysUtils is live. With SysUtils, calling an abstract method
  raises EAbstractError 'Abstract method called' (through AbstractErrorProc). }
uses SUHookProbe, SysUtils;

type
  TShape = class
    procedure Draw; virtual; abstract;
  end;
  TShapeClass = class of TShape;

var
  ShapeClass: TShapeClass;
  Shape: TShape;

begin
  PrintHooks('main block');
  ShapeClass := TShape;
  Shape := ShapeClass.Create;
  try
    Shape.Draw;
    Writeln('not reached');
  except
    on E: Exception do
      Writeln('abstract call: ', E.ClassName, ': ', E.Message);
  end;
  Shape.Free;
end.
