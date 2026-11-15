// covers: UIF-002
program UiConsts_Compile;
{ U-002: UIConsts compiles and holds the message texts of the catalogues in core.md section 14, graphics.md
  section 8, application-forms.md section 10 and controls-standard.md section 30, in that order; one
  formatted text shows the constants are usable with Format. }
uses SysUtils, UIConsts;

begin
  Writeln(SParentRequired);
  Writeln(SControlParentSelf);
  Writeln(SCannotFocus);
  Writeln(SWindowCreate);
  Writeln(SWindowClass);
  Writeln(SInvalidParentClass);
  Writeln(SValueOutOfRange);
  Writeln(SOutOfResources);
  Writeln(SNoCanvasHandle);
  Writeln(SInvalidPixelFormat);
  Writeln(SInvalidBitmapSize);
  Writeln(SUnsupportedBitmap);
  Writeln(SInvalidBitmap);
  Writeln(SUnknownExtension);
  Writeln(SIconStreaming);
  Writeln(SInvalidImageListIndex);
  Writeln(SVisibleModal);
  Writeln(SDisabledModal);
  Writeln(SMDINotSupported);
  Writeln(SUnsupportedButtons);
  Writeln(SLayoutDumpError);
  Writeln(SMsgDlgWarning);
  Writeln(SMsgDlgError);
  Writeln(SMsgDlgInformation);
  Writeln(SMsgDlgConfirm);
  Writeln(SOKButton);
  Writeln(SCancelButton);
  Writeln(SScrollBarRange);
  Writeln(SProgressRange);
  Writeln(STrackBarRange);
  Writeln(SMultiLineTabs);
  Writeln(SFixedColTooBig);
  Writeln(SFixedRowTooBig);
  Writeln(SGridIndexOutOfRange);
  Writeln(SGridRowsFixed);
  Writeln(SCommonDialogError);
  Writeln(Format(SCommonDialogError, [$2A]));
end.