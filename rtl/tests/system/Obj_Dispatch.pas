// phase: 4
// covers: RTL-153, ABI-073
program Obj_Dispatch;
{ TObject.Dispatch / DefaultHandler with untyped var parameters and _Dispatch (RTL-153, ABI 7.2/9.6): the id is the
  first 4 bytes of the message; each MsgTable (VMT+40, sorted by id) is binary-searched, the class's first, then each
  ancestor's; a found handler gets Self and the message address; otherwise the virtual DefaultHandler runs.
  The MsgTables are built here by hand in copies of real VMTs (an object's VMT pointer is switched to the copy), so the
  lookup is tested independently of the compiler's message methods (compiler goldens m2.5/Message*). }
{$POINTERMATH ON}
uses TestKit;

type
  TMsg = record
    Id: Cardinal;
    High: Cardinal;          { not part of the id }
    Result: Integer;
  end;
  PMsg = ^TMsg;

  TBase = class
    procedure DefaultHandler(var Message); override;
  end;

  TDerived = class(TBase)
  end;

  TEntry = record
    MsgId: Int64;
    Code: Pointer;
  end;

  TTable3 = record
    Count: NativeInt;
    E: array[0..2] of TEntry;
  end;

  TTable2 = record
    Count: NativeInt;
    E: array[0..1] of TEntry;
  end;

  { the 64-byte VMT header and the ten TObject slots (ABI-073) }
  TVmtCopy = array[0..17] of Pointer;

var
  Log: string;
  SeenSelf, SeenMsg: Pointer;
  FakeDerived, FakeBase: TVmtCopy;
  TableDerived: TTable3;
  TableBase: TTable2;

procedure TBase.DefaultHandler(var Message);
begin
  Log := Log + 'default ' + IntStr(TMsg(Message).Id) + ';';
  TMsg(Message).Result := -1;
end;

procedure Note(const Name: string; Self: Pointer; Msg: PMsg);
begin
  Log := Log + Name + ';';
  SeenSelf := Self;
  SeenMsg := Msg;
  Msg^.Result := Msg^.Id * 10;
end;

procedure D3(Self: Pointer; Msg: PMsg);
begin
  Note('D3', Self, Msg);
end;

procedure D5(Self: Pointer; Msg: PMsg);
begin
  Note('D5', Self, Msg);
end;

procedure D9(Self: Pointer; Msg: PMsg);
begin
  Note('D9', Self, Msg);
end;

procedure B5(Self: Pointer; Msg: PMsg);
begin
  Note('B5', Self, Msg);
end;

procedure B7(Self: Pointer; Msg: PMsg);
begin
  Note('B7', Self, Msg);
end;

procedure SetEntry(var E: TEntry; Id: Int64; Code: Pointer);
begin
  E.MsgId := Id;
  E.Code := Code;
end;

{ FakeDerived: TDerived's VMT with MsgTable 3, 5, 9 and parent FakeBase; FakeBase: TBase's VMT with MsgTable 5, 7 }
procedure BuildVmts;
begin
  Move(PByte(Pointer(TDerived))^, FakeDerived, SizeOf(TVmtCopy));
  Move(PByte(Pointer(TBase))^, FakeBase, SizeOf(TVmtCopy));
  TableDerived.Count := 3;
  SetEntry(TableDerived.E[0], 3, @D3);
  SetEntry(TableDerived.E[1], 5, @D5);
  SetEntry(TableDerived.E[2], 9, @D9);
  TableBase.Count := 2;
  SetEntry(TableBase.E[0], 5, @B5);
  SetEntry(TableBase.E[1], 7, @B7);
  FakeDerived[5] := @TableDerived;
  FakeDerived[0] := @FakeBase;
  FakeBase[5] := @TableBase;
end;

function Send(O: TObject; Id: Cardinal): Integer;
var
  M: TMsg;
begin
  M.Id := Id;
  M.High := $FFFFFFFF;
  M.Result := 0;
  SeenMsg := nil;
  O.Dispatch(M);
  Result := M.Result;
  if SeenMsg <> nil then
    Check(SeenMsg = @M, 'handler gets the message address (' + IntStr(Id) + ')');
end;

procedure TestTables;
var
  O: TDerived;
  Real: Pointer;
begin
  Section('MsgTable lookup');
  BuildVmts;
  O := TDerived.Create;
  Real := PPointer(O)^;
  PPointer(O)^ := @FakeDerived;
  Log := '';
  CheckEqI(30, Send(O, 3), 'first entry of the class');
  CheckEqI(50, Send(O, 5), 'middle entry: the class wins over the ancestor');
  CheckEqI(90, Send(O, 9), 'last entry of the class');
  CheckEqI(70, Send(O, 7), 'ancestor entry');
  Check(SeenSelf = Pointer(O), 'handler gets Self');
  CheckEqI(-1, Send(O, 4), 'unknown id between entries: DefaultHandler');
  CheckEqI(-1, Send(O, 1), 'below every entry');
  CheckEqI(-1, Send(O, 65535), 'above every entry');
  CheckEqS('D3;D5;D9;B7;default 4;default 1;default 65535;', Log, 'order of calls');
  PPointer(O)^ := Real;
  Log := '';
  CheckEqI(-1, Send(O, 3), 'real VMT without MsgTables: DefaultHandler');
  CheckEqS('default 3;', Log, 'override of DefaultHandler reached through Dispatch');
  O.Free;
end;

procedure TestTObject;
var
  O: TObject;
  B: TBase;
  M: TMsg;
begin
  Section('TObject');
  O := TObject.Create;
  M.Id := 3;
  M.Result := 12;
  O.Dispatch(M);
  CheckEqI(12, M.Result, 'TObject.DefaultHandler does nothing');
  O.DefaultHandler(M);
  CheckEqI(12, M.Result, 'called directly');
  O.Free;
  Log := '';
  M.Id := 77;
  B := TBase.Create;
  _Dispatch(Pointer(B), M);
  CheckEqS('default 77;', Log, '_Dispatch falls back to the virtual DefaultHandler');
  B.Free;
end;

begin
  TestTables;
  TestTObject;
  Done;
end.
