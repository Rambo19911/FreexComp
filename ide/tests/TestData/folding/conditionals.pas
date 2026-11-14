program Conditionals;

{$IFDEF DEBUG}
const Mode = 'debug';
{$ELSE}
const Mode = 'release';
{$ENDIF}

{$IFNDEF X}{$ENDIF}

{$IF Defined(A)}
{$IFDEF B}
x
{$ENDIF}
{$IFEND}

{$IFDEF Y}
{$ENDIF}

begin
end.
