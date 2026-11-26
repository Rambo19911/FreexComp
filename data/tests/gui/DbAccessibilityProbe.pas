program DbAccessibilityProbe;
{ A-083: the program the UI Automation client test DbAccessibilityClientTests.Uia_DbGridCellsAndNavigatorButtons
  reads. One form 'DB Accessibility Probe' with a data grid (component name Grid; persistent columns titled 'Item'
  and 'Label' over the fields id and name of five records 1 Alpha ... 5 Epsilon, all shown) and a navigator (Nav)
  on the same data source. Protocol (each answer is one stdout line, flushed):
    'ready <form handle in hex>' once the form is shown;
    on WM_APP + 1: 'state <RecNo> <SelectedIndex of the grid> <dataset state>';
    on WM_APP + 2: the form becomes the active window of the probe's thread (no foreground change) and the grid
      takes the focus, then 'focused grid <True|False>' (whether the grid's window has the focus);
    on WM_APP + 3 (wParam = a virtual key): WM_KEYDOWN with that key is sent to the grid's window, then the state
      line of WM_APP + 1;
    WM_CLOSE ends the program (exit code 0). Nothing is read from or written to files. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, DB, DBCtrls, DBGrids, TestDataSet;

const
  WM_PROBE_STATE = WM_APP + 1;
  WM_PROBE_FOCUS = WM_APP + 2;
  WM_PROBE_KEY = WM_APP + 3;

type
  TProbeForm = class(TForm)
  private
    procedure SayState;
  protected
    procedure WndProc(var Message: TMessage); override;
  end;

var
  Probe: TProbeForm;
  DS: TTestDataSet;
  Source: TDataSource;
  Grid: TDBGrid;
  Nav: TDBNavigator;

procedure Say(const S: string);
begin
  WriteLn(S);
  Flush(Output);
end;

procedure TProbeForm.SayState;
begin
  Say('state ' + IntToStr(DS.RecNo) + ' ' + IntToStr(Grid.SelectedIndex) + ' ' + StateName(DS.State));
end;

procedure TProbeForm.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    WM_PROBE_STATE:
      SayState;
    WM_PROBE_FOCUS:
      begin
        SetActiveWindow(Handle);
        Grid.SetFocus;
        if GetFocus = Grid.Handle then
          Say('focused grid True')
        else
          Say('focused grid False');
      end;
    WM_PROBE_KEY:
      begin
        SendMessage(Grid.Handle, WM_KEYDOWN, Message.WParam, 0);
        SayState;
      end;
    WM_CLOSE:
      Application.Terminate;
  else
    inherited WndProc(Message);
  end;
end;

procedure Build;
const
  Names: array[1..5] of string = ('Alpha', 'Beta', 'Gamma', 'Delta', 'Epsilon');
var
  I: Integer;
  C: TColumn;
begin
  DS := TTestDataSet.Create(nil);
  DS.AddDef('id', ftInteger);
  DS.AddDef('name', ftString, 10);
  for I := 1 to 5 do
    DS.AddRow([IntValue(I), StringValue(Names[I])]);
  Source := TDataSource.Create(nil);
  Source.DataSet := DS;
  Probe := TProbeForm.CreateNew(nil);
  Probe.Name := 'ProbeForm';
  Probe.Caption := 'DB Accessibility Probe';
  Probe.SetBounds(60, 60, 640, 480);
  Grid := TDBGrid.Create(Probe);
  Grid.Name := 'Grid';
  Grid.Parent := Probe;
  Grid.SetBounds(8, 8, 600, 340);
  C := Grid.Columns.Add;
  C.FieldName := 'id';
  C.Title := 'Item';
  C := Grid.Columns.Add;
  C.FieldName := 'name';
  C.Title := 'Label';
  Grid.DataSource := Source;
  Nav := TDBNavigator.Create(Probe);
  Nav.Name := 'Nav';
  Nav.Parent := Probe;
  Nav.SetBounds(8, 360, 400, 30);
  Nav.DataSource := Source;
  DS.Open;
  Probe.Show;
end;

begin
  Application.Initialize;
  Build;
  Say('ready ' + IntToHex(NativeUInt(Probe.Handle), 1));
  while not Application.Terminated do
    Application.HandleMessage;
  Probe.Free;
  Source.Free;
  DS.Free;
end.
