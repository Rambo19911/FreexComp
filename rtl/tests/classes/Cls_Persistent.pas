// phase: 4
// covers: RTL-410, RTL-411
program Cls_Persistent;
{ TPersistent (classes.md section 3): Assign(nil) raises EConvertError "Cannot assign nil to a <class>"; Assign calls
  Source.AssignTo(Self) and the base AssignTo raises "Cannot assign a <source> to a <dest>"; a descendant handling a
  source in Assign and another in AssignTo; GetNamePath is the class name without owner and owner path + '.' + class
  name with one. TPersistent is compiled in the M+ state, so a descendant's default-visibility property is published.
  The basic exception classes descend as section 2 lists. }
uses
  SysUtils, TypInfo, Classes, TestKit;

type
  TPoint3 = class(TPersistent)
  private
    FX: Integer;
    FOwner: TPersistent;
  protected
    procedure AssignTo(Dest: TPersistent); override;
    function GetOwner: TPersistent; override;
  public
    procedure Assign(Source: TPersistent); override;
    property Owner: TPersistent read FOwner write FOwner;
  published
    property X: Integer read FX write FX;
  end;

  TOther = class(TPersistent)
  public
    Value: Integer;
  end;

  TImplicitBase = class(TPersistent)
  protected
    FY: Integer;
  end;

  TImplicit = class(TImplicitBase)
    property Y: Integer read FY write FY;
  end;

procedure TPoint3.AssignTo(Dest: TPersistent);
begin
  if Dest is TOther then
    TOther(Dest).Value := FX
  else
    inherited AssignTo(Dest);
end;

function TPoint3.GetOwner: TPersistent;
begin
  Result := FOwner;
end;

procedure TPoint3.Assign(Source: TPersistent);
begin
  if Source is TPoint3 then
    FX := TPoint3(Source).FX
  else
    inherited Assign(Source);
end;

function AssignError(Dest, Source: TPersistent): string;
begin
  Result := '';
  try
    Dest.Assign(Source);
  except
    on E: EConvertError do
      Result := E.Message;
  end;
end;

var
  A, B: TPoint3;
  O: TOther;
begin
  A := TPoint3.Create;
  B := TPoint3.Create;
  O := TOther.Create;

  Section('Assign');
  A.X := 7;
  B.Assign(A);
  CheckEqI(7, B.X, 'handled in Assign');
  O.Assign(A);
  CheckEqI(7, O.Value, 'handled in the source''s AssignTo');
  CheckEqS('Cannot assign nil to a TPoint3', AssignError(A, nil), 'nil source');
  CheckEqS('Cannot assign a TOther to a TPoint3', AssignError(A, O), 'base AssignTo');
  CheckEqS('Cannot assign a TImplicit to a TOther', AssignError(O, TImplicit.Create), 'unknown pair');

  Section('GetNamePath');
  CheckEqS('TPoint3', A.GetNamePath, 'no owner');
  B.Owner := A;
  CheckEqS('TPoint3.TPoint3', B.GetNamePath, 'with owner');
  CheckEqS('TOther', O.GetNamePath, 'other');

  Section('published by default (M+)');
  CheckEqB(True, IsPublishedProp(TImplicit, 'Y'), 'default section of a TPersistent descendant');
  CheckEqB(True, IsPublishedProp(TPoint3, 'X'), 'published section');
  CheckEqB(False, IsPublishedProp(TPoint3, 'Owner'), 'public section');

  Section('exception classes');
  CheckEqB(True, EStringListError.InheritsFrom(EListError), 'EStringListError');
  CheckEqB(True, EReadError.InheritsFrom(EFilerError), 'EReadError');
  CheckEqB(True, EFilerError.InheritsFrom(EStreamError), 'EFilerError');
  CheckEqB(True, EClassNotFound.InheritsFrom(EFilerError), 'EClassNotFound');
  CheckEqB(True, EFOpenError.InheritsFrom(EStreamError), 'EFOpenError');
  CheckEqB(True, EOutOfResources.InheritsFrom(EOutOfMemory), 'EOutOfResources');
  CheckEqB(True, EComponentError.InheritsFrom(Exception), 'EComponentError');
  O.Free;
  B.Free;
  A.Free;
  Done;
end.
