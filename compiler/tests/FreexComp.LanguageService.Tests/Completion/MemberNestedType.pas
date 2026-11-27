{ context: member-nested-type }
program MemberNestedType;
type
  TTree = class
  public
    type
      TNode = class
        Value: Integer;
      end;
      TKind = (kLeaf, kBranch);
  end;
var
  N: TObject;
begin
  N := TTree.{|}
end.
{#has TNode, TKind, Create}
{#kind TNode=Class, TKind=Enum}
