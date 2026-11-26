// covers: CMP-1306
program ErrRcSyntaxLine;

{ C-348: a syntax error of the resource script is reported at the line of the statement that holds it (the compiler finds the
  statement by running the resource tool on prefixes of the script's statements). }

{$R *.rc}

begin
  Writeln('not reached');
end.
