{ BroadTablesKit - the body of classes/TI_BroadTables and classes/TI_BroadTablesPublished (RTL-405): the same classes
  and checks compiled with the default broad RTTI scope and with --rtti published. The raw tables print every entry
  (they differ between the scopes); everything specified as published must give identical results. }
unit BroadTablesKit;

interface

procedure Run;

implementation

{$POINTERMATH ON}

uses
  TypInfo, TestKit;

type
  TItem = class
  end;

  {$M+}
  TProbe = class
  private
    FSecret: Integer;
    FCount: Integer;
  protected
    FGuarded: Integer;
  public
    FOpen: Integer;
    procedure Shown;
    property Count: Integer read FCount write FCount;
  published
    FItem: TItem;
    procedure Clicked(Sender: TObject);
    property Size: Integer read FSecret write FSecret;
  end;
  {$M-}

procedure TProbe.Shown; begin end;
procedure TProbe.Clicked(Sender: TObject); begin end;

const
  VisNames: array[TMemberVisibility] of string = ('mvPrivate', 'mvProtected', 'mvPublic', 'mvPublished');

procedure Run;
var
  TI: PTypeInfo;
  F: PFieldInfo;
  M: PMethodInfo;
  P: PPropInfo;
  List: PPropList;
  Count, I: Integer;
  Probe: TProbe;
begin
  TI := TypeInfo(TProbe);
  Section('raw tables (scope dependent)');
  F := GetFieldTable(TI, Count);
  for I := 0 to Count - 1 do
    Writeln('field ', F[I].Name, ' ', VisNames[F[I].Visibility], ' offset ', F[I].Offset, ' type ', F[I].FieldType^.Name);
  M := GetMethodTable(TI, Count);
  for I := 0 to Count - 1 do
    Writeln('method ', M[I].Name, ' ', VisNames[M[I].Visibility], ' kind ', M[I].Kind);
  P := GetDeclaredPropTable(TI, Count);
  for I := 0 to Count - 1 do
    Writeln('property ', P[I].Name, ' ', VisNames[P[I].Visibility]);

  Section('published results (identical in both scopes)');
  Probe := TProbe.Create;
  CheckEqI(1, GetPropList(TI, List), 'one published property');
  CheckEqS('Size', List^[0]^.Name, 'its name');
  FreeMem(List);
  Check(GetPropInfo(TI, 'Count') = nil, 'public property not found');
  Check(TProbe.MethodAddress('Clicked') <> nil, 'published method found');
  Check(TProbe.MethodAddress('Shown') = nil, 'public method not found');
  Check(Probe.FieldAddress('FItem') = @Probe.FItem, 'published field found');
  Check(Probe.FieldAddress('FSecret') = nil, 'private field not found');
  Check(Probe.FieldAddress('FOpen') = nil, 'public field not found');
  Probe.FGuarded := 0;
  Probe.FOpen := 0;
  Probe.Free;
  Done;
end;

end.
