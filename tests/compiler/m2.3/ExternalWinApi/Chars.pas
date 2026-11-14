unit Chars;

interface

function IsAlpha(C: WideChar): LongBool; stdcall; external 'user32.dll' name 'IsCharAlphaW';
function IsUpper(C: WideChar): LongBool; stdcall; external 'user32' name 'IsCharUpperW';

implementation

end.