{ UIConsts - the message texts of the UI framework (README section 3): the catalogues of core.md section 14,
  graphics.md section 8, application-forms.md section 10 and controls-standard.md section 30. Strings only, no
  code. SSortedListError is RTLConsts' own text and is not repeated here. }
unit UIConsts;

interface

resourcestring
  { ---------------------------------------------------------------- core (core.md section 14) }
  SParentRequired = 'Control "%s" has no parent window';
  SControlParentSelf = 'A control cannot have itself as its parent';
  SCannotFocus = 'Cannot focus a disabled or invisible window';
  SWindowCreate = 'Error creating window: %s';
  SWindowClass = 'Error registering window class: %s';
  SInvalidParentClass = 'Control "%s" cannot be placed on a %s';
  SValueOutOfRange = 'Value out of range (%d)';

  { ---------------------------------------------------------------- graphics (graphics.md section 8) }
  SOutOfResources = 'Out of system resources';
  SNoCanvasHandle = 'Canvas does not allow drawing';
  SInvalidPixelFormat = 'Unsupported pixel format';
  SInvalidBitmapSize = 'Invalid bitmap size';
  SUnsupportedBitmap = 'Unsupported bitmap format';
  SInvalidBitmap = 'Bitmap image is not valid';
  SUnknownExtension = 'Unknown picture file extension (.%s)';
  SIconStreaming = 'Icon streaming is not supported';
  SInvalidImageListIndex = 'Invalid ImageList Index';

  { ---------------------------------------------------------------- forms (application-forms.md section 10) }
  SVisibleModal = 'Cannot make a visible window modal';
  SDisabledModal = 'Cannot make a disabled window modal';
  SMDINotSupported = 'MDI forms are not supported';
  SUnsupportedButtons = 'Unsupported button combination';
  SLayoutDumpError = 'Cannot write layout dump: %s';
  SMsgDlgWarning = 'Warning';
  SMsgDlgError = 'Error';
  SMsgDlgInformation = 'Information';
  SMsgDlgConfirm = 'Confirm';
  SOKButton = 'OK';
  SCancelButton = 'Cancel';

  { ---------------------------------------------------------------- controls (controls-standard.md section 30) }
  SScrollBarRange = 'Scrollbar property out of range';
  SProgressRange = 'Progress bar range is invalid';
  STrackBarRange = 'Track bar range is invalid';
  SMultiLineTabs = 'Multi-line tabs are not supported';
  SFixedColTooBig = 'Fixed column count must be less than column count';
  SFixedRowTooBig = 'Fixed row count must be less than row count';
  SGridIndexOutOfRange = 'Grid index out of range';
  SGridRowsFixed = 'Cannot insert or delete rows';
  SCommonDialogError = 'Common dialog error 0x%.4x';
  SMenuInsert = 'Menu item "%s" cannot be inserted here';

implementation

end.
