// phase: 8
// covers: RTL-980 RTL-981 RTL-983 RTL-984 RTL-986
program SU_OSVersion;
{ os-version.md: TOSVersion agrees with the Win32*Version variables (one RtlGetVersion call fills both), describes a
  Windows x64 system, and its Check / CheckBuild comparisons hold against the system's own numbers; a second thread
  reads the same values. Only relations are printed, never a number of the machine. }
uses
  Windows, SysUtils, TestKit;

var
  TMajor, TMinor, TBuild, TSPMajor, TSPMinor: Integer;
  TArch: TOSVersion.TArchitecture;
  TPlat: TOSVersion.TPlatform;
  TCheck, TCheckBuild, TCheckNext: Boolean;
  TName, TText: string;

function Reader(Parameter: Pointer): Integer;
begin
  TMajor := TOSVersion.Major;
  TMinor := TOSVersion.Minor;
  TBuild := TOSVersion.Build;
  TSPMajor := TOSVersion.ServicePackMajor;
  TSPMinor := TOSVersion.ServicePackMinor;
  TArch := TOSVersion.Architecture;
  TPlat := TOSVersion.Platform;
  TCheck := TOSVersion.Check(TOSVersion.Major, TOSVersion.Minor);
  TCheckBuild := TOSVersion.CheckBuild(TOSVersion.Build);
  TCheckNext := TOSVersion.CheckBuild(TOSVersion.Build + 1);
  TName := TOSVersion.Name;
  TText := TOSVersion.ToString;
  Result := 0;
end;

procedure TestNumbers;
begin
  Section('numbers');
  CheckEqI(Win32MajorVersion, TOSVersion.Major, 'Major = Win32MajorVersion');
  CheckEqI(Win32MinorVersion, TOSVersion.Minor, 'Minor = Win32MinorVersion');
  CheckEqI(Win32BuildNumber, TOSVersion.Build, 'Build = Win32BuildNumber');
  Check(TOSVersion.Major >= 10, 'Major >= 10');
  Check(TOSVersion.Build > 0, 'Build > 0');
  Check(TOSVersion.ServicePackMajor >= 0, 'ServicePackMajor >= 0');
  Check(TOSVersion.ServicePackMinor >= 0, 'ServicePackMinor >= 0');
end;

procedure TestSystem;
begin
  Section('system');
  Check(TOSVersion.Platform = pfWindows, 'Platform = pfWindows');
  Check(TOSVersion.Architecture = arIntelX64, 'Architecture = arIntelX64');
end;

procedure TestCheck;
var
  Ma, Mi, Sp, B: Integer;
begin
  Section('Check');
  Ma := TOSVersion.Major;
  Mi := TOSVersion.Minor;
  Sp := TOSVersion.ServicePackMajor;
  B := TOSVersion.Build;
  CheckEqB(True, TOSVersion.Check(Ma), 'Check(Major)');
  CheckEqB(True, TOSVersion.Check(Ma - 1), 'Check(Major - 1)');
  CheckEqB(False, TOSVersion.Check(Ma + 1), 'Check(Major + 1)');
  CheckEqB(True, TOSVersion.Check(Ma, Mi), 'Check(Major, Minor)');
  CheckEqB(False, TOSVersion.Check(Ma, Mi + 1), 'Check(Major, Minor + 1)');
  CheckEqB(True, TOSVersion.Check(Ma - 1, Mi + 1), 'Check(Major - 1, Minor + 1)');
  CheckEqB(False, TOSVersion.Check(Ma + 1, 0), 'Check(Major + 1, 0)');
  CheckEqB(True, TOSVersion.Check(Ma, Mi, Sp), 'Check(Major, Minor, ServicePackMajor)');
  CheckEqB(False, TOSVersion.Check(Ma, Mi, Sp + 1), 'Check(Major, Minor, ServicePackMajor + 1)');
  CheckEqB(True, TOSVersion.Check(Ma - 1, Mi + 1, Sp + 1), 'Check(Major - 1, Minor + 1, ServicePackMajor + 1)');
  CheckEqB(False, TOSVersion.Check(Ma, Mi + 1, 0), 'Check(Major, Minor + 1, 0)');
  Section('CheckBuild');
  CheckEqB(True, TOSVersion.CheckBuild(17763), 'CheckBuild(17763)');
  CheckEqB(True, TOSVersion.CheckBuild(B), 'CheckBuild(Build)');
  CheckEqB(False, TOSVersion.CheckBuild(B + 1), 'CheckBuild(Build + 1)');
  CheckEqB(True, TOSVersion.CheckBuild(0), 'CheckBuild(0)');
end;

procedure TestThread;
var
  H: THandle;
  Id: Cardinal;
begin
  Section('second thread');
  H := BeginThread(nil, 0, Reader, nil, 0, Id);
  Check(H <> 0, 'thread started');
  WaitForSingleObject(H, INFINITE);
  CloseHandle(H);
  CheckEqI(TOSVersion.Major, TMajor, 'Major');
  CheckEqI(TOSVersion.Minor, TMinor, 'Minor');
  CheckEqI(TOSVersion.Build, TBuild, 'Build');
  CheckEqI(TOSVersion.ServicePackMajor, TSPMajor, 'ServicePackMajor');
  CheckEqI(TOSVersion.ServicePackMinor, TSPMinor, 'ServicePackMinor');
  Check(TArch = TOSVersion.Architecture, 'Architecture');
  Check(TPlat = TOSVersion.Platform, 'Platform');
  CheckEqB(True, TCheck, 'Check(Major, Minor)');
  CheckEqB(True, TCheckBuild, 'CheckBuild(Build)');
  CheckEqB(False, TCheckNext, 'CheckBuild(Build + 1)');
  Check(TName = TOSVersion.Name, 'Name');
  Check(TText = TOSVersion.ToString, 'ToString');
end;

begin
  TestNumbers;
  TestSystem;
  TestCheck;
  TestThread;
  Done;
end.
