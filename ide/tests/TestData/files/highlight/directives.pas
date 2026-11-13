{$DEFINE DEBUG}
{$IFDEF DEBUG}
X := 1;
{$ELSE}
X := 2;
{$ENDIF}
{$I inc.inc}
