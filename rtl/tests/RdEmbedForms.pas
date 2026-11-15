{ RdEmbedForms - form classes with embedded .form files for reader/Rd_EmbeddedForm (RTL-482, RTL-483): a base form,
  a derived form with its own file (read after the base one), a form whose file has an error, and a class without a
  file. }
unit RdEmbedForms;

interface

uses
  Classes;

type
  TEmbedPart = class(TComponent)
  private
    FCaption: string;
  published
    property Caption: string read FCaption write FCaption;
  end;

  TEmbedBase = class(TComponent)
  private
    FCaption: string;
    FWidth: Integer;
  public
    Log: string;
  published
    A: TEmbedPart;
    procedure DoNothing(Sender: TObject);
    property Caption: string read FCaption write FCaption;
    property Width: Integer read FWidth write FWidth;
  end;

  TEmbedDerived = class(TEmbedBase)
  published
    B: TEmbedPart;
  end;

  TEmbedBad = class(TComponent)
  end;

  TEmbedNone = class(TComponent)
  end;

implementation

{$FORM RdEmbedBase.form}
{$FORM RdEmbedDerived.form}
{$FORM RdEmbedBad.form}

procedure TEmbedBase.DoNothing(Sender: TObject);
begin
end;

initialization
  RegisterClass(TEmbedPart);
end.
