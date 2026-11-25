// covers: RTL-1112, RTL-1113, RTL-1114
// phase: 8
program Win_UiaLayouts;
{ The x64 layouts of the two records of the UI Automation bindings (UiaRect, UiaPoint) and the values of the
  constants the unit UIAutomation declares, plus the safe-array type flag of ActiveX. The expected numbers are those
  of the reference structure definitions and of the identifier, enumeration and error-code pages; a constant whose
  page gives no number (the root object id, the runtime-id prefix, the object and child ids) is checked against
  the value of the definition source named in the unit header. Calls no function. }
uses Windows, ActiveX, UIAutomation, TestKit;

function Offset(Base, Field: Pointer): Integer;
begin
  Result := Integer(NativeUInt(Field) - NativeUInt(Base));
end;

procedure TestLayouts;
var
  R: TUiaRect;
  P: TUiaPoint;
begin
  Section('record layouts (RTL-1114)');
  CheckEqI(32, SizeOf(TUiaRect), 'SizeOf(TUiaRect)');
  CheckEqI(0, Offset(@R, @R.left), 'left');
  CheckEqI(8, Offset(@R, @R.top), 'top');
  CheckEqI(16, Offset(@R, @R.width), 'width');
  CheckEqI(24, Offset(@R, @R.height), 'height');
  CheckEqI(SizeOf(TUiaRect), SizeOf(UiaRect), 'UiaRect');
  CheckEqI(16, SizeOf(TUiaPoint), 'SizeOf(TUiaPoint)');
  CheckEqI(0, Offset(@P, @P.x), 'x');
  CheckEqI(8, Offset(@P, @P.y), 'y');
  CheckEqI(SizeOf(TUiaPoint), SizeOf(UiaPoint), 'UiaPoint');
end;

procedure TestEnumerations;
begin
  Section('enumerations (RTL-1112)');
  CheckEqI($1, ProviderOptions_ClientSideProvider, 'ProviderOptions_ClientSideProvider');
  CheckEqI($2, ProviderOptions_ServerSideProvider, 'ProviderOptions_ServerSideProvider');
  CheckEqI($4, ProviderOptions_NonClientAreaProvider, 'ProviderOptions_NonClientAreaProvider');
  CheckEqI($8, ProviderOptions_OverrideProvider, 'ProviderOptions_OverrideProvider');
  CheckEqI($20, ProviderOptions_UseComThreading, 'ProviderOptions_UseComThreading');
  CheckEqI(0, NavigateDirection_Parent, 'NavigateDirection_Parent');
  CheckEqI(1, NavigateDirection_NextSibling, 'NavigateDirection_NextSibling');
  CheckEqI(2, NavigateDirection_PreviousSibling, 'NavigateDirection_PreviousSibling');
  CheckEqI(3, NavigateDirection_FirstChild, 'NavigateDirection_FirstChild');
  CheckEqI(4, NavigateDirection_LastChild, 'NavigateDirection_LastChild');
  CheckEqI(0, ToggleState_Off, 'ToggleState_Off');
  CheckEqI(1, ToggleState_On, 'ToggleState_On');
  CheckEqI(2, ToggleState_Indeterminate, 'ToggleState_Indeterminate');
  CheckEqI(0, RowOrColumnMajor_RowMajor, 'RowOrColumnMajor_RowMajor');
  CheckEqI(1, RowOrColumnMajor_ColumnMajor, 'RowOrColumnMajor_ColumnMajor');
  CheckEqI(2, RowOrColumnMajor_Indeterminate, 'RowOrColumnMajor_Indeterminate');
  CheckEqI(0, StructureChangeType_ChildAdded, 'StructureChangeType_ChildAdded');
  CheckEqI(1, StructureChangeType_ChildRemoved, 'StructureChangeType_ChildRemoved');
  CheckEqI(2, StructureChangeType_ChildrenInvalidated, 'StructureChangeType_ChildrenInvalidated');
  CheckEqI(3, StructureChangeType_ChildrenBulkAdded, 'StructureChangeType_ChildrenBulkAdded');
  CheckEqI(4, StructureChangeType_ChildrenBulkRemoved, 'StructureChangeType_ChildrenBulkRemoved');
  CheckEqI(5, StructureChangeType_ChildrenReordered, 'StructureChangeType_ChildrenReordered');
end;

procedure TestPropertyIds;
begin
  Section('property identifiers (RTL-1112)');
  CheckEqI(30005, UIA_NamePropertyId, 'UIA_NamePropertyId');
  CheckEqI(30013, UIA_HelpTextPropertyId, 'UIA_HelpTextPropertyId');
  CheckEqI(30011, UIA_AutomationIdPropertyId, 'UIA_AutomationIdPropertyId');
  CheckEqI(30012, UIA_ClassNamePropertyId, 'UIA_ClassNamePropertyId');
  CheckEqI(30024, UIA_FrameworkIdPropertyId, 'UIA_FrameworkIdPropertyId');
  CheckEqI(30003, UIA_ControlTypePropertyId, 'UIA_ControlTypePropertyId');
  CheckEqI(30010, UIA_IsEnabledPropertyId, 'UIA_IsEnabledPropertyId');
  CheckEqI(30022, UIA_IsOffscreenPropertyId, 'UIA_IsOffscreenPropertyId');
  CheckEqI(30009, UIA_IsKeyboardFocusablePropertyId, 'UIA_IsKeyboardFocusablePropertyId');
  CheckEqI(30008, UIA_HasKeyboardFocusPropertyId, 'UIA_HasKeyboardFocusPropertyId');
  CheckEqI(30016, UIA_IsControlElementPropertyId, 'UIA_IsControlElementPropertyId');
  CheckEqI(30017, UIA_IsContentElementPropertyId, 'UIA_IsContentElementPropertyId');
  CheckEqI(30004, UIA_LocalizedControlTypePropertyId, 'UIA_LocalizedControlTypePropertyId');
  CheckEqI(30086, UIA_ToggleToggleStatePropertyId, 'UIA_ToggleToggleStatePropertyId');
  CheckEqI(30045, UIA_ValueValuePropertyId, 'UIA_ValueValuePropertyId');
end;

procedure TestPatternIds;
begin
  Section('control pattern identifiers (RTL-1112)');
  CheckEqI(10000, UIA_InvokePatternId, 'UIA_InvokePatternId');
  CheckEqI(10001, UIA_SelectionPatternId, 'UIA_SelectionPatternId');
  CheckEqI(10002, UIA_ValuePatternId, 'UIA_ValuePatternId');
  CheckEqI(10003, UIA_RangeValuePatternId, 'UIA_RangeValuePatternId');
  CheckEqI(10005, UIA_ExpandCollapsePatternId, 'UIA_ExpandCollapsePatternId');
  CheckEqI(10006, UIA_GridPatternId, 'UIA_GridPatternId');
  CheckEqI(10007, UIA_GridItemPatternId, 'UIA_GridItemPatternId');
  CheckEqI(10010, UIA_SelectionItemPatternId, 'UIA_SelectionItemPatternId');
  CheckEqI(10012, UIA_TablePatternId, 'UIA_TablePatternId');
  CheckEqI(10013, UIA_TableItemPatternId, 'UIA_TableItemPatternId');
  CheckEqI(10014, UIA_TextPatternId, 'UIA_TextPatternId');
  CheckEqI(10015, UIA_TogglePatternId, 'UIA_TogglePatternId');
end;

procedure TestControlTypeIds;
begin
  Section('control type identifiers (RTL-1112)');
  CheckEqI(50000, UIA_ButtonControlTypeId, 'UIA_ButtonControlTypeId');
  CheckEqI(50002, UIA_CheckBoxControlTypeId, 'UIA_CheckBoxControlTypeId');
  CheckEqI(50003, UIA_ComboBoxControlTypeId, 'UIA_ComboBoxControlTypeId');
  CheckEqI(50004, UIA_EditControlTypeId, 'UIA_EditControlTypeId');
  CheckEqI(50006, UIA_ImageControlTypeId, 'UIA_ImageControlTypeId');
  CheckEqI(50007, UIA_ListItemControlTypeId, 'UIA_ListItemControlTypeId');
  CheckEqI(50008, UIA_ListControlTypeId, 'UIA_ListControlTypeId');
  CheckEqI(50011, UIA_MenuItemControlTypeId, 'UIA_MenuItemControlTypeId');
  CheckEqI(50012, UIA_ProgressBarControlTypeId, 'UIA_ProgressBarControlTypeId');
  CheckEqI(50013, UIA_RadioButtonControlTypeId, 'UIA_RadioButtonControlTypeId');
  CheckEqI(50014, UIA_ScrollBarControlTypeId, 'UIA_ScrollBarControlTypeId');
  CheckEqI(50015, UIA_SliderControlTypeId, 'UIA_SliderControlTypeId');
  CheckEqI(50017, UIA_StatusBarControlTypeId, 'UIA_StatusBarControlTypeId');
  CheckEqI(50018, UIA_TabControlTypeId, 'UIA_TabControlTypeId');
  CheckEqI(50019, UIA_TabItemControlTypeId, 'UIA_TabItemControlTypeId');
  CheckEqI(50020, UIA_TextControlTypeId, 'UIA_TextControlTypeId');
  CheckEqI(50021, UIA_ToolBarControlTypeId, 'UIA_ToolBarControlTypeId');
  CheckEqI(50023, UIA_TreeControlTypeId, 'UIA_TreeControlTypeId');
  CheckEqI(50024, UIA_TreeItemControlTypeId, 'UIA_TreeItemControlTypeId');
  CheckEqI(50025, UIA_CustomControlTypeId, 'UIA_CustomControlTypeId');
  CheckEqI(50026, UIA_GroupControlTypeId, 'UIA_GroupControlTypeId');
  CheckEqI(50027, UIA_ThumbControlTypeId, 'UIA_ThumbControlTypeId');
  CheckEqI(50028, UIA_DataGridControlTypeId, 'UIA_DataGridControlTypeId');
  CheckEqI(50029, UIA_DataItemControlTypeId, 'UIA_DataItemControlTypeId');
  CheckEqI(50032, UIA_WindowControlTypeId, 'UIA_WindowControlTypeId');
  CheckEqI(50033, UIA_PaneControlTypeId, 'UIA_PaneControlTypeId');
  CheckEqI(50035, UIA_HeaderItemControlTypeId, 'UIA_HeaderItemControlTypeId');
  CheckEqI(50036, UIA_TableControlTypeId, 'UIA_TableControlTypeId');
  CheckEqI(50038, UIA_SeparatorControlTypeId, 'UIA_SeparatorControlTypeId');
end;

procedure TestEventIdsAndCodes;
begin
  Section('event identifiers, result codes, other constants (RTL-1112, RTL-1113)');
  CheckEqI(20002, UIA_StructureChangedEventId, 'UIA_StructureChangedEventId');
  CheckEqI(20004, UIA_AutomationPropertyChangedEventId, 'UIA_AutomationPropertyChangedEventId');
  CheckEqI(20005, UIA_AutomationFocusChangedEventId, 'UIA_AutomationFocusChangedEventId');
  CheckEqI(20009, UIA_Invoke_InvokedEventId, 'UIA_Invoke_InvokedEventId');
  CheckEqI(20012, UIA_SelectionItem_ElementSelectedEventId, 'UIA_SelectionItem_ElementSelectedEventId');
  CheckEqI(Integer($80040201), UIA_E_ELEMENTNOTAVAILABLE, 'UIA_E_ELEMENTNOTAVAILABLE');
  CheckEqI(Integer($80040200), UIA_E_ELEMENTNOTENABLED, 'UIA_E_ELEMENTNOTENABLED');
  CheckEqI(Integer($80131509), UIA_E_INVALIDOPERATION, 'UIA_E_INVALIDOPERATION');
  CheckEqI(-25, UiaRootObjectId, 'UiaRootObjectId');
  CheckEqI(3, UiaAppendRuntimeId, 'UiaAppendRuntimeId');
  CheckEqI(-4, OBJID_CLIENT, 'OBJID_CLIENT');
  CheckEqI(0, CHILDID_SELF, 'CHILDID_SELF');
  CheckEqI($2000, VT_ARRAY, 'VT_ARRAY');
end;

begin
  TestLayouts;
  TestEnumerations;
  TestPropertyIds;
  TestPatternIds;
  TestControlTypeIds;
  TestEventIdsAndCodes;
  Done;
end.
