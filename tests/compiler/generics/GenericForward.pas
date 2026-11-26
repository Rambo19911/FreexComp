// covers: LANG-782, LANG-783
program GenericForward;

type
  TNode<T> = class;
  IVisitor<T> = interface;

  TList<T> = class
    Head: TNode<T>;
    procedure Push(const V: T);
    procedure Walk(const Visitor: IVisitor<T>);
    destructor Destroy; override;
  end;

  // a class between the forward and the completion: its field names the still incomplete instance
  THolder = class
    Last: TNode<string>;
  end;

  TNode<T> = class
    Value: T;
    Next: TNode<T>;
    Owner: TList<T>;
  end;

  IVisitor<T> = interface
    procedure Visit(const Item: T);
  end;

  TPrinter = class(TInterfacedObject, IVisitor<Integer>)
    procedure Visit(const Item: Integer);
  end;

  TJoiner = class(TInterfacedObject, IVisitor<string>)
    Text: string;
    procedure Visit(const Item: string);
  end;

procedure TList<T>.Push(const V: T);
var
  N: TNode<T>;
begin
  N := TNode<T>.Create;
  N.Value := V;
  N.Next := Head;
  N.Owner := Self;
  Head := N;
end;

procedure TList<T>.Walk(const Visitor: IVisitor<T>);
var
  N: TNode<T>;
begin
  N := Head;
  while N <> nil do
  begin
    Visitor.Visit(N.Value);
    N := N.Next;
  end;
end;

destructor TList<T>.Destroy;
var
  N: TNode<T>;
begin
  while Head <> nil do
  begin
    N := Head;
    Head := N.Next;
    N.Free;
  end;
  inherited;
end;

procedure TPrinter.Visit(const Item: Integer);
begin
  WriteLn('visit ', Item);
end;

procedure TJoiner.Visit(const Item: string);
begin
  Text := Text + Item + ';';
end;

var
  Numbers: TList<Integer>;
  Words: TList<string>;
  Holder: THolder;
  Joiner: TJoiner;
  Visitor: IVisitor<string>;
  Printer: IVisitor<Integer>;
  Live: Integer;
begin
  Live := _LiveAllocCount;
  Numbers := TList<Integer>.Create;
  Numbers.Push(1);
  Numbers.Push(2);
  Printer := TPrinter.Create;
  Numbers.Walk(Printer);
  Printer := nil;
  WriteLn('owner ', Numbers.Head.Owner = Numbers, ' ', Numbers.Head.Next.Owner = Numbers);
  WriteLn('tail ', Numbers.Head.Next.Next = nil);

  Words := TList<string>.Create;
  Words.Push('b');
  Words.Push('a');
  Joiner := TJoiner.Create;
  Visitor := Joiner;
  Words.Walk(Visitor);
  WriteLn('words ', Joiner.Text);
  Visitor := nil;

  Holder := THolder.Create;
  Holder.Last := Words.Head.Next;
  WriteLn('holder ', Holder.Last.Value, ' ', Holder.Last.Owner = Words);
  Holder.Free;
  Numbers.Free;
  Words.Free;
  WriteLn('leaks ', _LiveAllocCount - Live);
end.
