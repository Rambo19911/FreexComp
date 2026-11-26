unit UQUser;

interface

uses
  UQGen;

function UserBoxClass: TClass;
function UserInnerClass: TClass;
function UserCellClass: TClass;
function UserEnumeratorClass: TClass;
function UserBoxInfo: Pointer;
function UserCellInfo: Pointer;
function UserEnumeratorInfo: Pointer;
function UserSum: Integer;
function UserCount(L: UQGen.TList<Integer>): Integer;

implementation

function UserBoxClass: TClass;
begin
  Result := UQGen.TBox<Integer>;
end;

function UserInnerClass: TClass;
begin
  Result := UQGen.TOuter.TInner;
end;

function UserCellClass: TClass;
begin
  Result := UQGen.TOuter.TCell<string>;
end;

function UserEnumeratorClass: TClass;
begin
  Result := UQGen.TList<Integer>.TEnumerator;
end;

function UserBoxInfo: Pointer;
begin
  Result := TypeInfo(UQGen.TBox<Integer>);
end;

function UserCellInfo: Pointer;
begin
  Result := TypeInfo(UQGen.TOuter.TCell<string>);
end;

function UserEnumeratorInfo: Pointer;
begin
  Result := TypeInfo(UQGen.TList<Integer>.TEnumerator);
end;

function UserSum: Integer;
var
  L: UQGen.TList<Integer>;
  E: UQGen.TList<Integer>.TEnumerator;
  B: UQGen.TBox<Integer>;
begin
  L := UQGen.TList<Integer>.Create;
  L.Add(4);
  L.Add(5);
  Result := 0;
  E := L.GetEnumerator;
  while E.MoveNext do
    Inc(Result, E.Current);
  E.Free;
  L.Free;
  B := UQGen.TBox<Integer>.Create(100);
  Inc(Result, B.Value);
  B.Free;
end;

function UserCount(L: UQGen.TList<Integer>): Integer;
var
  N: Integer;
begin
  Result := 0;
  for N in L do
    Inc(Result);
end;

end.
