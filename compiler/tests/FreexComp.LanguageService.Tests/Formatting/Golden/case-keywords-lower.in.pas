Program CaseKeywords;

Type
  TShape = Class
  Private
    FSize: Integer;
  Public
    Constructor Create; Virtual;
    Function Area: Integer; Virtual; Abstract;
    Property Size: Integer Read FSize Write FSize;
  End;

Constructor TShape.Create;
Begin
  FSize := 1;
End;

Var
  I: Integer;
  S: String;
Begin
  S := 'Begin End';    { Begin And End in a comment }
  For I := 1 To 3 Do
    If (I Mod 2 = 0) And Not (I = 4) Then
      Writeln(S)
    Else
      Writeln(I Div 1, ' ', NIL = NIL);
End.
