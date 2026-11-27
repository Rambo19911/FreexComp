{ context: declaration-start }
program DeclarationStartClassBody;
type
  TForm = class
    Caption: string;
    {|}
  end;
begin
end.
{#has private, protected, public, published, strict, property, constructor, destructor, procedure, function, const, var}
{#not Caption, Writeln, begin}
