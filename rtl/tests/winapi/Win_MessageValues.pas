// covers: RTL-850
program Win_MessageValues;
{ Messages unit (winapi.md section 6): a sample of the message constants of every family RTL-850 names, with the
  values of the Win32 reference, including the cue-banner and minimum-visible messages of the edit and combo box
  that the standard controls use (ECM_FIRST + 1, CBM_FIRST + 1, CBM_FIRST + 3). Each value is also checked against
  its family base where the reference defines the message that way. }
uses Windows, Messages, TestKit;

begin
  Section('window messages');
  CheckEqI($0000, WM_NULL, 'WM_NULL');
  CheckEqI($0001, WM_CREATE, 'WM_CREATE');
  CheckEqI($0002, WM_DESTROY, 'WM_DESTROY');
  CheckEqI($0005, WM_SIZE, 'WM_SIZE');
  CheckEqI($000F, WM_PAINT, 'WM_PAINT');
  CheckEqI($0010, WM_CLOSE, 'WM_CLOSE');
  CheckEqI($0100, WM_KEYDOWN, 'WM_KEYDOWN');
  CheckEqI($0111, WM_COMMAND, 'WM_COMMAND');
  CheckEqI($0200, WM_MOUSEMOVE, 'WM_MOUSEMOVE');
  CheckEqI($02E0, WM_DPICHANGED, 'WM_DPICHANGED');
  CheckEqI($0400, WM_USER, 'WM_USER');
  CheckEqI($8000, WM_APP, 'WM_APP');

  Section('control messages and notifications');
  CheckEqI($00B1, EM_SETSEL, 'EM_SETSEL');
  CheckEqI($0300, EN_CHANGE, 'EN_CHANGE');
  CheckEqI($00F5, BM_CLICK, 'BM_CLICK');
  CheckEqI(0, BN_CLICKED, 'BN_CLICKED');
  CheckEqI(1, BST_CHECKED, 'BST_CHECKED');
  CheckEqI($0180, LB_ADDSTRING, 'LB_ADDSTRING');
  CheckEqI(1, LBN_SELCHANGE, 'LBN_SELCHANGE');
  CheckEqI($0143, CB_ADDSTRING, 'CB_ADDSTRING');
  CheckEqI(1, CBN_SELCHANGE, 'CBN_SELCHANGE');
  CheckEqI($0172, STM_SETIMAGE, 'STM_SETIMAGE');
  CheckEqI($00E9, SBM_SETSCROLLINFO, 'SBM_SETSCROLLINFO');

  Section('cue banner and minimum visible items');
  CheckEqI($1501, EM_SETCUEBANNER, 'EM_SETCUEBANNER');
  CheckEqI($1502, EM_GETCUEBANNER, 'EM_GETCUEBANNER');
  CheckEqI($1701, CB_SETMINVISIBLE, 'CB_SETMINVISIBLE');
  CheckEqI($1702, CB_GETMINVISIBLE, 'CB_GETMINVISIBLE');
  CheckEqI($1703, CB_SETCUEBANNER, 'CB_SETCUEBANNER');
  CheckEqI($1704, CB_GETCUEBANNER, 'CB_GETCUEBANNER');
  CheckEqI(ECM_FIRST + 1, EM_SETCUEBANNER, 'EM_SETCUEBANNER = ECM_FIRST + 1');
  CheckEqI(CBM_FIRST + 3, CB_SETCUEBANNER, 'CB_SETCUEBANNER = CBM_FIRST + 3');
  Done;
end.
