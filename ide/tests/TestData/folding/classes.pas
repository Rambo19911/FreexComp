unit Classes;

interface

type
  TFwd = class;
  TMeta = class of TFwd;
  EShort = class(Exception);
  TFoo = class(TObject)
  private
    FX: Integer;
  public
    procedure Run;
    property X: Integer read FX;
  end;

  TRec = packed record
    A: Integer;
    case Integer of
      0: (B: Byte);
      1: (C: Char);
  end;

  IBar = interface
    ['{00000000-0000-0000-0000-000000000000}']
    procedure Go;
  end;

  TOld = object
    V: Integer;
  end;

  TProc = procedure of object;
  TList<T> = class
  end;

implementation

procedure TFoo.Run;
begin
end;

end.
