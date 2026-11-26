// covers: RTL-860
// phase: 8
program Win_CommCtrlDecl;
{ ImageList_GetBkColor (CommCtrl, task R-253) against the system: the program imports it from comctl32 (the link
  fails without the .def entry), the loaded comctl32 exports it, and it reads back each colour ImageList_SetBkColor
  sets; SetBkColor returns the colour GetBkColor read before. The colour of a new list is not printed (the reference
  page names none). Needs no desktop. }
uses Windows, CommCtrl, TestKit;

const
  Teal = $00998833;   // COLORREF order: $00BBGGRR

var
  IL: HIMAGELIST;
  Before: COLORREF;

begin
  Section('import');
  Check(GetProcAddress(GetModuleHandleW('comctl32.dll'), 'ImageList_GetBkColor') <> nil,
    'the loaded comctl32 exports ImageList_GetBkColor');

  Section('ImageList_GetBkColor');
  IL := ImageList_Create(4, 4, ILC_COLOR32 or ILC_MASK, 1, 1);
  Check(IL <> 0, 'a masked 32-bit 4 x 4 image list');
  Before := ImageList_GetBkColor(IL);
  CheckEqI(Before, ImageList_SetBkColor(IL, Teal), 'SetBkColor returns the colour GetBkColor read');
  CheckEqI(Teal, ImageList_GetBkColor(IL), 'GetBkColor reads the colour that was set');
  CheckEqI(Teal, ImageList_SetBkColor(IL, CLR_NONE), 'SetBkColor returns the colour set before');
  CheckEqI(CLR_NONE, ImageList_GetBkColor(IL), 'GetBkColor reads CLR_NONE');

  Section('cleanup');
  Check(ImageList_Destroy(IL), 'ImageList_Destroy');
  Done;
end.
