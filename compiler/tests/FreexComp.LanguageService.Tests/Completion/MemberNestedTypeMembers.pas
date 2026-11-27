{ context: member-nested-type }
program MemberNestedTypeMembers;
type
  TTree = class
  public
    type
      TNode = class
        Value: Integer;
        class function Leaf: TNode;
      end;
  end;
class function TTree.TNode.Leaf: TNode; begin Result := nil end;
var
  N: TObject;
begin
  N := TTree.TNode.{|}
end.
{#has Leaf, Create}
{#not Value}
