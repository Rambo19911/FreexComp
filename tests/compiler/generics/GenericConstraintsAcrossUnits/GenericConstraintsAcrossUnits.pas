// covers: CMP-1479, LANG-983
program GenericConstraintsAcrossUnits;

uses
  UShapes, UShapeKinds;

procedure ShowAndFree(Item: TBase);
begin
  WriteLn(Item.Name);
  Item.Free;
end;

procedure Run;
var
  C: TCircle;
  G: IGreeter;
  H: THello;
  Tag: TLoudTag;
  Plain: TTools.TTag;
  A, B: TPowerTools;
begin
  // TFactory<T: class, constructor> of UShapes with the classes of UShapeKinds
  ShowAndFree(TFactory<TBase>.Make);
  ShowAndFree(TFactory<TCircle>.Make);
  ShowAndFree(TFactory<TSquare>.Make);
  ShowAndFree(TFactory<TLabelled>.Make);

  // TNamer<T: TBase>
  C := TCircle.Create;
  WriteLn(TNamer<TCircle>.NameOf(C), ' ', TNamer<TBase>.NameOf(C));
  C.Free;

  // Greet<T: IGreeter>: an interface reference and an object, written and inferred
  G := THello.Create;
  WriteLn(TTools.Greet<IGreeter>(G), ' ', TTools.Greet(G));
  H := THello.Create;
  WriteLn(TTools.Greet<THello>(H), ' ', TTools.Greet(H));
  H.Free;

  // Fresh<T: TBase, constructor>
  WriteLn(TTools.Fresh<TSquare>, ', ', TTools.Fresh<TLabelled>, ', ', TTools.Fresh<TCircle>);

  // a constraint that names a nested type of the declaring class, and one that names the declaring class
  Tag := TLoudTag.Create;
  Plain := TTools.TTag.Create;
  WriteLn(TTools.Tagged<TTools.TTag>(Tag, Plain), ' ', TTools.Tagged(Tag, Tag));
  Plain.Free;
  Tag.Free;
  A := TPowerTools.Create;
  B := TPowerTools.Create;
  WriteLn(A.Same<TPowerTools>(A), ' ', A.Same(B));
  B.Free;
  A.Free;
end;

begin
  Run;
end.
