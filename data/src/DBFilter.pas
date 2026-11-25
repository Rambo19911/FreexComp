// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// DBFilter - the filter-expression parser and evaluator behind TDataSet.Filter.
//
//   filter     = orExpr ;
//   orExpr     = andExpr { "OR" andExpr } ;
//   andExpr    = notExpr { "AND" notExpr } ;
//   notExpr    = [ "NOT" ] primary ;
//   primary    = "(" filter ")" | comparison ;
//   comparison = fieldRef compOp operand | fieldRef "IS" [ "NOT" ] "NULL" ;
//   compOp     = "=" | "<>" | "<" | "<=" | ">" | ">=" ;
//   operand    = fieldRef | number | string | "TRUE" | "FALSE" | "NULL" ;
//   fieldRef   = identifier | "[" any chars except "]" "]" ;
//   number     = [ "-" ] digits [ "." digits ] ;
//   string     = "'" { char | "''" } "'" ;
//
// Keywords are case-insensitive. An expression is parsed when it is created (syntax errors), bound to the
// fields of a dataset before it is used (unknown fields, operands of the wrong type) and then evaluated
// against the values the fields read. Every error is EDatabaseError 'Filter error at position <n>: <message>'
// with the 1-based position of the offending token.
unit DBFilter;

interface

uses
  SysUtils, DB;

resourcestring
  SFltBadChar = 'Unexpected character ''%s''';
  SFltNoQuote = 'Unterminated string';
  SFltNoBracket = 'Missing '']''';
  SFltNoParen = 'Missing '')''';
  SFltFieldExpected = 'Field name expected';
  SFltOperatorExpected = 'Comparison operator expected';
  SFltOperandExpected = 'Operand expected';
  SFltNullExpected = 'NULL expected';
  SFltEndExpected = 'End of filter expected';
  SFltBadValue = '''%s'' is not a valid value for field ''%s''';
  SFltBadOperand = 'Operand does not match the type of field ''%s''';
  SFltNoCompare = 'Field ''%s'' cannot be compared';

type
  { the nodes of a parsed expression (private to this unit) }
  TFilterNodeList = array of TObject;

  TFilterExpr = class
  private
    FText: string;
    FRoot: TObject;
    FNodes: TFilterNodeList;
    FBound: Boolean;
  public
    { Parses the expression; raises the filter error of a syntax error. }
    constructor Create(const AText: string);
    destructor Destroy; override;
    { Resolves the field references against the fields of the dataset and converts the literals to the types
      of their fields; raises the filter error. Does nothing when the expression is bound. }
    procedure Bind(DataSet: TDataSet);
    { Forgets the fields (they may be destroyed when the dataset closes). }
    procedure Unbind;
    { Whether the row the fields read now passes; the expression must be bound. }
    function Matches(Options: TFilterOptions): Boolean;
    property Text: string read FText;
    property Bound: Boolean read FBound;
  end;

implementation

uses
  Character;

type
  TTokenKind = (tkEnd, tkName, tkNumber, tkString, tkOperator, tkOpen, tkClose);

  { Text: the name, the digits of the number, the value of the string or the operator }
  TToken = record
    Kind: TTokenKind;
    Text: string;
    Position: Integer;
    Quoted: Boolean;
  end;

  TTokens = array of TToken;

  TCompareOp = (coEqual, coNotEqual, coLess, coLessEqual, coGreater, coGreaterEqual);
  TNodeKind = (nkOr, nkAnd, nkNot, nkCompare);
  TOperandKind = (okField, okNumber, okString, okTrue, okFalse, okNull);
  { how the values of a field compare }
  TValueClass = (vcString, vcNumber, vcBoolean, vcDateTime, vcBlob);

  TFilterNode = class
  public
    Kind: TNodeKind;
    Left, Right: TFilterNode;
    { nkCompare }
    FieldName: string;
    FieldPosition: Integer;
    Op: TCompareOp;
    OperandKind: TOperandKind;
    OperandText: string;
    OperandPosition: Integer;
    { set by Bind: the fields and the number a literal stands for }
    Field, OtherField: TField;
    Number: Double;
  end;

  TFilterParser = class
  public
    Nodes: TFilterNodeList;
    Tokens: TTokens;
    Index: Integer;
    function NewNode(Kind: TNodeKind; Left, Right: TFilterNode): TFilterNode;
    function IsKeyword(const Word: string): Boolean;
    function IsAnyKeyword: Boolean;
    function ParseOr: TFilterNode;
    function ParseAnd: TFilterNode;
    function ParseNot: TFilterNode;
    function ParsePrimary: TFilterNode;
    function ParseComparison: TFilterNode;
  end;

procedure FilterError(Position: Integer; const Msg: string);
begin
  DatabaseErrorFmt(SDatFilter, [Position, Msg]);
end;

function IsDigit(C: Char): Boolean;
begin
  Result := (C >= '0') and (C <= '9');
end;

function IsNameStart(C: Char): Boolean;
begin
  Result := ((C >= 'A') and (C <= 'Z')) or ((C >= 'a') and (C <= 'z')) or (C = '_') or (C > #127);
end;

{ ---- lexer ---- }

procedure AddToken(var Tokens: TTokens; Kind: TTokenKind; const TokenText: string; Position: Integer; Quoted: Boolean);
begin
  SetLength(Tokens, Length(Tokens) + 1);
  Tokens[High(Tokens)].Kind := Kind;
  Tokens[High(Tokens)].Text := TokenText;
  Tokens[High(Tokens)].Position := Position;
  Tokens[High(Tokens)].Quoted := Quoted;
end;

function Tokenize(const Text: string): TTokens;
var
  I, N, Start: Integer;
  C: Char;
  S: string;
  Closed: Boolean;
begin
  SetLength(Result, 0);
  N := Length(Text);
  I := 1;
  while I <= N do
  begin
    C := Text[I];
    Start := I;
    if C <= ' ' then
      I := I + 1
    else if IsNameStart(C) then
    begin
      while (I <= N) and (IsNameStart(Text[I]) or IsDigit(Text[I])) do
        I := I + 1;
      AddToken(Result, tkName, Copy(Text, Start, I - Start), Start, False);
    end
    else if C = '[' then
    begin
      I := I + 1;
      while (I <= N) and (Text[I] <> ']') do
        I := I + 1;
      if I > N then
        FilterError(Start, SFltNoBracket);
      AddToken(Result, tkName, Copy(Text, Start + 1, I - Start - 1), Start, True);
      I := I + 1;
    end
    else if IsDigit(C) or ((C = '-') and (I < N) and IsDigit(Text[I + 1])) then
    begin
      I := I + 1;
      while (I <= N) and IsDigit(Text[I]) do
        I := I + 1;
      if (I < N) and (Text[I] = '.') and IsDigit(Text[I + 1]) then
      begin
        I := I + 1;
        while (I <= N) and IsDigit(Text[I]) do
          I := I + 1;
      end;
      AddToken(Result, tkNumber, Copy(Text, Start, I - Start), Start, False);
    end
    else if C = '''' then
    begin
      I := I + 1;
      S := '';
      Closed := False;
      while (I <= N) and not Closed do
        if Text[I] <> '''' then
        begin
          S := S + Text[I];
          I := I + 1;
        end
        else if (I < N) and (Text[I + 1] = '''') then
        begin
          S := S + '''';
          I := I + 2;
        end
        else
        begin
          Closed := True;
          I := I + 1;
        end;
      if not Closed then
        FilterError(Start, SFltNoQuote);
      AddToken(Result, tkString, S, Start, False);
    end
    else if C = '(' then
    begin
      AddToken(Result, tkOpen, '(', Start, False);
      I := I + 1;
    end
    else if C = ')' then
    begin
      AddToken(Result, tkClose, ')', Start, False);
      I := I + 1;
    end
    else if C = '=' then
    begin
      AddToken(Result, tkOperator, '=', Start, False);
      I := I + 1;
    end
    else if (C = '<') or (C = '>') then
    begin
      S := C;
      I := I + 1;
      if (I <= N) and ((Text[I] = '=') or ((C = '<') and (Text[I] = '>'))) then
      begin
        S := S + Text[I];
        I := I + 1;
      end;
      AddToken(Result, tkOperator, S, Start, False);
    end
    else
    begin
      S := C;
      FilterError(Start, Format(SFltBadChar, [S]));
    end;
  end;
  AddToken(Result, tkEnd, '', N + 1, False);
end;

{ The value of a number token; independent of the format settings. }
function NumberValue(const S: string): Double;
var
  I: Integer;
  Scale: Double;
begin
  Result := 0;
  Scale := 0;
  for I := 1 to Length(S) do
    if S[I] = '.' then
      Scale := 1
    else if IsDigit(S[I]) then
    begin
      Result := Result * 10 + (Ord(S[I]) - Ord('0'));
      Scale := Scale * 10;
    end;
  if Scale > 1 then
    Result := Result / Scale;
  if (S <> '') and (S[1] = '-') then
    Result := -Result;
end;

{ ---- parser ---- }

function TFilterParser.NewNode(Kind: TNodeKind; Left, Right: TFilterNode): TFilterNode;
begin
  Result := TFilterNode.Create;
  SetLength(Nodes, Length(Nodes) + 1);
  Nodes[High(Nodes)] := Result;
  Result.Kind := Kind;
  Result.Left := Left;
  Result.Right := Right;
end;

function TFilterParser.IsKeyword(const Word: string): Boolean;
begin
  Result := (Tokens[Index].Kind = tkName) and not Tokens[Index].Quoted and SameText(Tokens[Index].Text, Word);
end;

function TFilterParser.IsAnyKeyword: Boolean;
begin
  Result := IsKeyword('AND') or IsKeyword('OR') or IsKeyword('NOT') or IsKeyword('IS') or IsKeyword('NULL') or
    IsKeyword('TRUE') or IsKeyword('FALSE');
end;

function TFilterParser.ParseOr: TFilterNode;
begin
  Result := ParseAnd;
  while IsKeyword('OR') do
  begin
    Index := Index + 1;
    Result := NewNode(nkOr, Result, ParseAnd);
  end;
end;

function TFilterParser.ParseAnd: TFilterNode;
begin
  Result := ParseNot;
  while IsKeyword('AND') do
  begin
    Index := Index + 1;
    Result := NewNode(nkAnd, Result, ParseNot);
  end;
end;

function TFilterParser.ParseNot: TFilterNode;
begin
  if IsKeyword('NOT') then
  begin
    Index := Index + 1;
    Result := NewNode(nkNot, ParsePrimary, nil);
  end
  else
    Result := ParsePrimary;
end;

function TFilterParser.ParsePrimary: TFilterNode;
begin
  if Tokens[Index].Kind = tkOpen then
  begin
    Index := Index + 1;
    Result := ParseOr;
    if Tokens[Index].Kind <> tkClose then
      FilterError(Tokens[Index].Position, SFltNoParen);
    Index := Index + 1;
  end
  else
    Result := ParseComparison;
end;

function TFilterParser.ParseComparison: TFilterNode;
var
  Op: string;
begin
  if (Tokens[Index].Kind <> tkName) or IsAnyKeyword then
    FilterError(Tokens[Index].Position, SFltFieldExpected);
  Result := NewNode(nkCompare, nil, nil);
  Result.FieldName := Tokens[Index].Text;
  Result.FieldPosition := Tokens[Index].Position;
  Index := Index + 1;
  if IsKeyword('IS') then
  begin
    { IS NULL and IS NOT NULL are the comparisons = NULL and <> NULL }
    Index := Index + 1;
    Result.Op := coEqual;
    if IsKeyword('NOT') then
    begin
      Result.Op := coNotEqual;
      Index := Index + 1;
    end;
    if not IsKeyword('NULL') then
      FilterError(Tokens[Index].Position, SFltNullExpected);
    Result.OperandKind := okNull;
  end
  else
  begin
    if Tokens[Index].Kind <> tkOperator then
      FilterError(Tokens[Index].Position, SFltOperatorExpected);
    Op := Tokens[Index].Text;
    if Op = '=' then
      Result.Op := coEqual
    else if Op = '<>' then
      Result.Op := coNotEqual
    else if Op = '<' then
      Result.Op := coLess
    else if Op = '<=' then
      Result.Op := coLessEqual
    else if Op = '>' then
      Result.Op := coGreater
    else
      Result.Op := coGreaterEqual;
    Index := Index + 1;
    if Tokens[Index].Kind = tkNumber then
      Result.OperandKind := okNumber
    else if Tokens[Index].Kind = tkString then
      Result.OperandKind := okString
    else if Tokens[Index].Kind <> tkName then
      FilterError(Tokens[Index].Position, SFltOperandExpected)
    else if IsKeyword('TRUE') then
      Result.OperandKind := okTrue
    else if IsKeyword('FALSE') then
      Result.OperandKind := okFalse
    else if IsKeyword('NULL') then
      Result.OperandKind := okNull
    else if IsAnyKeyword then
      FilterError(Tokens[Index].Position, SFltOperandExpected)
    else
      Result.OperandKind := okField;
  end;
  Result.OperandText := Tokens[Index].Text;
  Result.OperandPosition := Tokens[Index].Position;
  Index := Index + 1;
end;

{ ---- binding and evaluation ---- }

function ValueClassOf(Field: TField): TValueClass;
begin
  case Field.DataType of
    ftString, ftMemo:
      Result := vcString;
    ftBoolean:
      Result := vcBoolean;
    ftDate, ftTime, ftDateTime:
      Result := vcDateTime;
    ftBlob:
      Result := vcBlob;
  else
    Result := vcNumber;
  end;
end;

procedure BindComparison(Node: TFilterNode; DataSet: TDataSet);
var
  FieldClass: TValueClass;
  Moment: TDateTime;
begin
  Node.Field := DataSet.FindField(Node.FieldName);
  if Node.Field = nil then
    FilterError(Node.FieldPosition, Format(SDatFieldNotFound, [Node.FieldName]));
  FieldClass := ValueClassOf(Node.Field);
  if Node.OperandKind = okNull then
    Exit;
  if Node.OperandKind = okField then
  begin
    Node.OtherField := DataSet.FindField(Node.OperandText);
    if Node.OtherField = nil then
      FilterError(Node.OperandPosition, Format(SDatFieldNotFound, [Node.OperandText]));
  end;
  if FieldClass = vcBlob then
    FilterError(Node.FieldPosition, Format(SFltNoCompare, [Node.FieldName]));
  case Node.OperandKind of
    okField:
      if ValueClassOf(Node.OtherField) = vcBlob then
        FilterError(Node.OperandPosition, Format(SFltNoCompare, [Node.OperandText]));
    okNumber:
      if FieldClass = vcNumber then
        Node.Number := NumberValue(Node.OperandText)
      else
        FilterError(Node.OperandPosition, Format(SFltBadOperand, [Node.FieldName]));
    okTrue, okFalse:
      if FieldClass = vcBoolean then
        Node.Number := Ord(Node.OperandKind = okTrue)
      else
        FilterError(Node.OperandPosition, Format(SFltBadOperand, [Node.FieldName]));
    okString:
      case FieldClass of
        vcNumber:
          if not TryStrToFloat(Node.OperandText, Node.Number) then
            FilterError(Node.OperandPosition, Format(SFltBadValue, [Node.OperandText, Node.FieldName]));
        vcBoolean:
          if SameText(Node.OperandText, 'True') or (Node.OperandText = '1') then
            Node.Number := 1
          else if SameText(Node.OperandText, 'False') or (Node.OperandText = '0') then
            Node.Number := 0
          else
            FilterError(Node.OperandPosition, Format(SFltBadValue, [Node.OperandText, Node.FieldName]));
        vcDateTime:
          begin
            if not TryStrToDateTime(Node.OperandText, Moment) then
              FilterError(Node.OperandPosition, Format(SFltBadValue, [Node.OperandText, Node.FieldName]));
            Node.Number := Moment;
          end;
      end;
  end;
end;

{ -1, 0 or 1 by UTF-16 code unit }
function CompareUnits(const A, B: string; IgnoreCase: Boolean): Integer;
var
  X, Y: string;
  I: Integer;
begin
  if IgnoreCase then
  begin
    X := ToUpper(A);
    Y := ToUpper(B);
  end
  else
  begin
    X := A;
    Y := B;
  end;
  Result := 0;
  I := 1;
  while (Result = 0) and (I <= Length(X)) and (I <= Length(Y)) do
  begin
    if X[I] < Y[I] then
      Result := -1
    else if X[I] > Y[I] then
      Result := 1;
    I := I + 1;
  end;
  if Result = 0 then
    if Length(X) < Length(Y) then
      Result := -1
    else if Length(X) > Length(Y) then
      Result := 1;
end;

{ The value of a non-null field as a number; False when a text is not a number. }
function NumberOf(Field: TField; out Value: Double): Boolean;
begin
  Result := True;
  case ValueClassOf(Field) of
    vcString:
      Result := TryStrToFloat(Field.AsString, Value);
    vcBoolean:
      Value := Ord(Field.AsBoolean);
  else
    Value := Field.AsFloat;
  end;
end;

function CompareNode(Node: TFilterNode; Options: TFilterOptions): Boolean;
var
  Order: Integer;
  X, Y: Double;
  Literal: string;
begin
  Result := False;
  if Node.OperandKind = okNull then
  begin
    if Node.Op = coEqual then
      Result := Node.Field.IsNull
    else if Node.Op = coNotEqual then
      Result := not Node.Field.IsNull;
    Exit;
  end;
  { a comparison with a null value is False }
  if Node.Field.IsNull then
    Exit;
  if (Node.OperandKind = okField) and Node.OtherField.IsNull then
    Exit;
  if (Node.OperandKind = okField) and (ValueClassOf(Node.Field) = vcString) and
    (ValueClassOf(Node.OtherField) = vcString) then
    Order := CompareUnits(Node.Field.AsString, Node.OtherField.AsString, foCaseInsensitive in Options)
  else if (Node.OperandKind = okString) and (ValueClassOf(Node.Field) = vcString) then
  begin
    Literal := Node.OperandText;
    if (Node.Op in [coEqual, coNotEqual]) and not (foNoPartialCompare in Options) and (Literal <> '') and
      (Literal[Length(Literal)] = '*') then
    begin
      { partial compare: the literal without its '*' is a prefix }
      Literal := Copy(Literal, 1, Length(Literal) - 1);
      Order := CompareUnits(Copy(Node.Field.AsString, 1, Length(Literal)), Literal, foCaseInsensitive in Options);
    end
    else
      Order := CompareUnits(Node.Field.AsString, Literal, foCaseInsensitive in Options);
  end
  else
  begin
    if not NumberOf(Node.Field, X) then
      Exit;
    if Node.OperandKind = okField then
    begin
      if not NumberOf(Node.OtherField, Y) then
        Exit;
    end
    else
      Y := Node.Number;
    if X < Y then
      Order := -1
    else if X > Y then
      Order := 1
    else
      Order := 0;
  end;
  case Node.Op of
    coEqual: Result := Order = 0;
    coNotEqual: Result := Order <> 0;
    coLess: Result := Order < 0;
    coLessEqual: Result := Order <= 0;
    coGreater: Result := Order > 0;
    coGreaterEqual: Result := Order >= 0;
  end;
end;

function EvalNode(Node: TFilterNode; Options: TFilterOptions): Boolean;
begin
  case Node.Kind of
    nkOr: Result := EvalNode(Node.Left, Options) or EvalNode(Node.Right, Options);
    nkAnd: Result := EvalNode(Node.Left, Options) and EvalNode(Node.Right, Options);
    nkNot: Result := not EvalNode(Node.Left, Options);
  else
    Result := CompareNode(Node, Options);
  end;
end;

{ TFilterExpr }

constructor TFilterExpr.Create(const AText: string);
var
  Parser: TFilterParser;
  I: Integer;
begin
  inherited Create;
  FText := AText;
  if Trim(AText) = '' then
    Exit;
  Parser := TFilterParser.Create;
  try
    try
      Parser.Tokens := Tokenize(AText);
      FRoot := Parser.ParseOr;
      if Parser.Tokens[Parser.Index].Kind <> tkEnd then
        FilterError(Parser.Tokens[Parser.Index].Position, SFltEndExpected);
      FNodes := Parser.Nodes;
    except
      for I := 0 to High(Parser.Nodes) do
        Parser.Nodes[I].Free;
      FRoot := nil;
      raise;
    end;
  finally
    Parser.Free;
  end;
end;

destructor TFilterExpr.Destroy;
var
  I: Integer;
begin
  for I := 0 to High(FNodes) do
    FNodes[I].Free;
  SetLength(FNodes, 0);
  inherited Destroy;
end;

procedure TFilterExpr.Bind(DataSet: TDataSet);
var
  I: Integer;
begin
  if FBound then
    Exit;
  for I := 0 to High(FNodes) do
    if TFilterNode(FNodes[I]).Kind = nkCompare then
      BindComparison(TFilterNode(FNodes[I]), DataSet);
  FBound := True;
end;

procedure TFilterExpr.Unbind;
var
  I: Integer;
begin
  FBound := False;
  for I := 0 to High(FNodes) do
  begin
    TFilterNode(FNodes[I]).Field := nil;
    TFilterNode(FNodes[I]).OtherField := nil;
  end;
end;

function TFilterExpr.Matches(Options: TFilterOptions): Boolean;
begin
  Result := (FRoot = nil) or EvalNode(TFilterNode(FRoot), Options);
end;

end.
