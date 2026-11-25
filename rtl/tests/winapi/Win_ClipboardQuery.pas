// phase: 8
// covers: RTL-1080, RTL-1081
program Win_ClipboardQuery;
{ Windows unit, user32 part: IsClipboardFormatAvailable and the standard clipboard format numbers CF_* (task
  R-241). The function needs no open clipboard. What the clipboard holds is machine state, so the result for
  CF_UNICODETEXT is not checked: the call only has to link and return. No program can have put data on the
  clipboard under a format number that is neither a standard format, nor in the private or GDI object range
  ($0200...$03FF), nor in the range of registered formats ($C000...$FFFF): the result for such a number is False. }
uses Windows, TestKit;

const
  UnregisteredFormat = $7FFF;

var
  HasText: BOOL;

begin
  Section('CF_* values');
  CheckEqI(1, CF_TEXT, 'CF_TEXT');
  CheckEqI(2, CF_BITMAP, 'CF_BITMAP');
  CheckEqI(8, CF_DIB, 'CF_DIB');
  CheckEqI(13, CF_UNICODETEXT, 'CF_UNICODETEXT');
  CheckEqI(15, CF_HDROP, 'CF_HDROP');

  Section('IsClipboardFormatAvailable');
  HasText := IsClipboardFormatAvailable(CF_UNICODETEXT);
  Check(HasText or not HasText, 'the call for CF_UNICODETEXT returns');
  Check(not IsClipboardFormatAvailable(UnregisteredFormat), 'an unregistered format number is not available');
  Done;
end.
