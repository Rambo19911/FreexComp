unit Geometry;
interface
type
  TCircle = class
    Radius: Double;
  end;
const
  Pi2 = 6.28318;
function CircleArea(R: Double): Double;
implementation
function Helper(R: Double): Double; begin Result := R * R end;
function CircleArea(R: Double): Double; begin Result := Helper(R) * Pi2 / 2 end;
end.
