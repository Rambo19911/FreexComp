// covers: LANG-860, LANG-861, LANG-862, LANG-863, LANG-864, LANG-870
// opc: -D FROMCLI -D Second
program ConditionalCompilation;
const
  Level = 3;
  Name = 'abc';
begin
{$IFDEF FROMCLI}
  Writeln('FROMCLI defined');
{$ELSE}
  Writeln('FROMCLI missing');
{$ENDIF}
{$IFDEF second}
  Writeln('symbols are case-insensitive');
{$ENDIF}
{$IFNDEF NOTDEFINED}
  Writeln('IFNDEF of an unknown symbol');
{$ENDIF}
{$DEFINE LOCAL}
{$IFDEF LOCAL}
  Writeln('DEFINE works');
{$ENDIF}
{$UNDEF LOCAL}
{$IFDEF LOCAL}
  Writeln('never: UNDEF');
{$ELSE}
  Writeln('UNDEF works');
{$ENDIF}
{$IFDEF MSWINDOWS}
  Writeln('MSWINDOWS');
{$ENDIF}
{$IF Defined(WIN64) and Defined(CPUX64)}
  Writeln('WIN64 and CPUX64');
{$IFEND}
{$IF Level > 5}
  Writeln('never: level high');
{$ELSEIF Level = 3}
  Writeln('level is three');
{$ELSE}
  Writeln('never: level other');
{$ENDIF}
{$IF Declared(Name) and not Declared(Undeclared)}
  Writeln('Declared works');
{$ENDIF}
{$IF SizeOf(Integer) = 4}
  Writeln('SizeOf in IF');
{$ENDIF}
{$IFDEF FROMCLI}
  {$IFDEF NOTDEFINED}
  Writeln('never: nested');
  {$ELSE}
  Writeln('nested else');
  {$ENDIF}
{$ELSE}
  {$IFDEF ANYTHING}
  Writeln('never: inactive nest');
  {$ENDIF}
{$ENDIF}
{$IFDEF NOTDEFINED}
  this text is not tokenised: 'unterminated string
  { unterminated comment
  }
  Writeln('never');
{$ENDIF}
{$IFOPT R-}
  Writeln('range checks off');
{$ENDIF}
{$R+}
{$IFOPT R+}
  Writeln('range checks on');
{$ENDIF}
  Writeln('end');
end.
