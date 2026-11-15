// covers: UIF-1200, UIF-1201
// opc: -U ..\..\src
program UiHarness_SelfTest;
{ U-001: the harness compiles a program of this area with TestKit (-U rtl/tests) and the framework sources
  (-U ui-framework/src) on the unit path, and runs it with FXC_UI_NONINTERACTIVE=1 (UIF-013, UIF-1201). }
uses SysUtils, TestKit;

begin
  Section('harness');
  CheckEqS('1', GetEnvironmentVariable('FXC_UI_NONINTERACTIVE'), 'non-interactive mode is set');
  Done;
end.