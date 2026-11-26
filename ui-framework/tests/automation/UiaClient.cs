using System.Runtime.InteropServices;

namespace FreexComp.UiFramework.Automation.Tests;

// Test-only interop slice of the operating system's UI Automation COM client (decision Q-P8A-16): the coclass
// CUIAutomation and the parts of IUIAutomation and IUIAutomationElement the client tests call, current values only,
// no cache requests. Interface GUIDs, CLSID and the vtable order are those of the published interface definitions
// (UIAutomationClient.idl on the reference pages, read back from the type library embedded in the system's
// UIAutomationCore.dll); property and control-type ids from the reference pages "Automation Element Property IDs" and
// "Control Type Identifiers" (the values the rtl binding unit UIAutomation declares, RTL-1115). Each interface declares
// every slot up to the last one used, in order; a slot the tests never call is declared as `void SlotN()` (its
// signature is irrelevant, only its position counts). Methods keep the default HRESULT translation: a failure throws
// COMException (for instance UIA_E_ELEMENTNOTAVAILABLE on an element whose provider is gone).
// U-156 added the event-handler registration of IUIAutomation (slots 32 to 41), GetCurrentPropertyValue and
// GetCurrentPattern of IUIAutomationElement, the Invoke and Toggle pattern interfaces and the three event-handler
// interfaces the client implements (classic COM interop: a managed class implementing a [ComImport] interface is
// handed to the system as a COM callable wrapper); GUIDs and slots read back the same way, pattern, event and
// property ids from the reference pages "Control Pattern Identifiers", "Event Identifiers" and "Property IDs".

/// <summary>CLSID_CUIAutomation.</summary>
[ComImport]
[Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E")]
internal class CUIAutomation
{
}

/// <summary>IUIAutomationCondition: no methods of its own.</summary>
[ComImport]
[Guid("352FFBA8-0973-437C-A61F-F64CAFD81DF9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationCondition
{
}

/// <summary>IUIAutomation, slots 3 (after IUnknown) to 23.</summary>
[ComImport]
[Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation
{
    void Slot3_CompareElements();
    void Slot4_CompareRuntimeIds();
    void Slot5_GetRootElement();

    IUIAutomationElement ElementFromHandle(IntPtr hwnd);                                    // slot 6

    void Slot7_ElementFromPoint();
    void Slot8_GetFocusedElement();
    void Slot9_GetRootElementBuildCache();
    void Slot10_ElementFromHandleBuildCache();
    void Slot11_ElementFromPointBuildCache();
    void Slot12_GetFocusedElementBuildCache();
    void Slot13_CreateTreeWalker();
    void Slot14_get_ControlViewWalker();
    void Slot15_get_ContentViewWalker();
    void Slot16_get_RawViewWalker();
    void Slot17_get_RawViewCondition();
    void Slot18_get_ControlViewCondition();
    void Slot19_get_ContentViewCondition();
    void Slot20_CreateCacheRequest();
    void Slot21_CreateTrueCondition();
    void Slot22_CreateFalseCondition();

    IUIAutomationCondition CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value); // slot 23

    void Slot24_CreatePropertyConditionEx();
    void Slot25_CreateAndCondition();
    void Slot26_CreateAndConditionFromArray();
    void Slot27_CreateAndConditionFromNativeArray();
    void Slot28_CreateOrCondition();
    void Slot29_CreateOrConditionFromArray();
    void Slot30_CreateOrConditionFromNativeArray();
    void Slot31_CreateNotCondition();

    void AddAutomationEventHandler(int eventId, IUIAutomationElement element, TreeScope scope, IntPtr cacheRequest,
        IUIAutomationEventHandler handler);                                                // slot 32

    void Slot33_RemoveAutomationEventHandler();

    void AddPropertyChangedEventHandlerNativeArray(IUIAutomationElement element, TreeScope scope, IntPtr cacheRequest,
        IUIAutomationPropertyChangedEventHandler handler, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] int[] propertyArray,
        int propertyCount);                                                                 // slot 34

    void Slot35_AddPropertyChangedEventHandler();
    void Slot36_RemovePropertyChangedEventHandler();

    void AddStructureChangedEventHandler(IUIAutomationElement element, TreeScope scope, IntPtr cacheRequest,
        IUIAutomationStructureChangedEventHandler handler);                                 // slot 37

    void Slot38_RemoveStructureChangedEventHandler();
    void Slot39_AddFocusChangedEventHandler();
    void Slot40_RemoveFocusChangedEventHandler();

    void RemoveAllEventHandlers();                                                          // slot 41
}

/// <summary>IUIAutomationElement, slots 3 to 40.</summary>
[ComImport]
[Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    void Slot3_SetFocus();

    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
    int[] GetRuntimeId();                                                                   // slot 4

    IUIAutomationElement? FindFirst(TreeScope scope, IUIAutomationCondition condition);    // slot 5

    void Slot6_FindAll();
    void Slot7_FindFirstBuildCache();
    void Slot8_FindAllBuildCache();
    void Slot9_BuildUpdatedCache();
    [return: MarshalAs(UnmanagedType.Struct)]
    object? GetCurrentPropertyValue(int propertyId);                                        // slot 10

    void Slot11_GetCurrentPropertyValueEx();
    void Slot12_GetCachedPropertyValue();
    void Slot13_GetCachedPropertyValueEx();
    void Slot14_GetCurrentPatternAs();
    void Slot15_GetCachedPatternAs();
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object? GetCurrentPattern(int patternId);                                               // slot 16

    void Slot17_GetCachedPattern();
    void Slot18_GetCachedParent();
    void Slot19_GetCachedChildren();
    void Slot20_get_CurrentProcessId();

    int CurrentControlType { get; }                                                         // slot 21

    void Slot22_get_CurrentLocalizedControlType();

    string CurrentName { [return: MarshalAs(UnmanagedType.BStr)] get; }                    // slot 23

    void Slot24_get_CurrentAcceleratorKey();
    void Slot25_get_CurrentAccessKey();
    void Slot26_get_CurrentHasKeyboardFocus();
    void Slot27_get_CurrentIsKeyboardFocusable();
    void Slot28_get_CurrentIsEnabled();

    string CurrentAutomationId { [return: MarshalAs(UnmanagedType.BStr)] get; }            // slot 29

    void Slot30_get_CurrentClassName();

    string CurrentHelpText { [return: MarshalAs(UnmanagedType.BStr)] get; }               // slot 31

    void Slot32_get_CurrentCulture();
    void Slot33_get_CurrentIsControlElement();

    int CurrentIsContentElement { get; }                                                    // slot 34 (BOOL)

    void Slot35_get_CurrentIsPassword();
    void Slot36_get_CurrentNativeWindowHandle();
    void Slot37_get_CurrentItemType();
    void Slot38_get_CurrentIsOffscreen();
    void Slot39_get_CurrentOrientation();

    string CurrentFrameworkId { [return: MarshalAs(UnmanagedType.BStr)] get; }            // slot 40
}

/// <summary>IUIAutomationInvokePattern, slot 3.</summary>
[ComImport]
[Guid("FB377FBE-8EA6-46D5-9C73-6499642D3059")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationInvokePattern
{
    void Invoke();                                                                          // slot 3
}

/// <summary>IUIAutomationTogglePattern, slots 3 and 4.</summary>
[ComImport]
[Guid("94CF8058-9B8D-4AB9-8BFD-4CD0A33C8C70")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTogglePattern
{
    void Toggle();                                                                          // slot 3

    int CurrentToggleState { get; }                                                         // slot 4 (ToggleState)
}

/// <summary>IUIAutomationEventHandler (implemented by the client), slot 3.</summary>
[ComImport]
[Guid("146C3C17-F12E-4E22-8C27-F894B9B79C69")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationEventHandler
{
    void HandleAutomationEvent(IUIAutomationElement sender, int eventId);                   // slot 3
}

/// <summary>IUIAutomationPropertyChangedEventHandler (implemented by the client), slot 3.</summary>
[ComImport]
[Guid("40CD37D4-C756-4B0C-8C6F-BDDFEEB13B50")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationPropertyChangedEventHandler
{
    void HandlePropertyChangedEvent(IUIAutomationElement sender, int propertyId,
        [MarshalAs(UnmanagedType.Struct)] object? newValue);                                // slot 3
}

/// <summary>IUIAutomationStructureChangedEventHandler (implemented by the client), slot 3.</summary>
[ComImport]
[Guid("E81D1B4E-11C5-42F8-9754-E7036C79F054")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationStructureChangedEventHandler
{
    void HandleStructureChangedEvent(IUIAutomationElement sender, int changeType,
        [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[]? runtimeId); // slot 3
}

/// <summary>TreeScope.</summary>
[Flags]
internal enum TreeScope
{
    Element = 1,
    Children = 2,
    Descendants = 4,
}

/// <summary>The ids the tests use (reference pages; UIA_*PropertyId, UIA_*ControlTypeId).</summary>
internal static class UiaIds
{
    public const int NameProperty = 30005;
    public const int IsEnabledProperty = 30010;
    public const int AutomationIdProperty = 30011;
    public const int FrameworkIdProperty = 30024;
    public const int ValueValueProperty = 30045;
    public const int RangeValueValueProperty = 30047;
    public const int ExpandCollapseStateProperty = 30070;
    public const int SelectionItemIsSelectedProperty = 30079;
    public const int ToggleStateProperty = 30086;

    public const int InvokePattern = 10000;
    public const int SelectionPattern = 10001;
    public const int ValuePattern = 10002;
    public const int RangeValuePattern = 10003;
    public const int ExpandCollapsePattern = 10005;
    public const int SelectionItemPattern = 10010;
    public const int TextPattern = 10014;
    public const int TogglePattern = 10015;

    public const int StructureChangedEvent = 20002;
    public const int InvokeInvokedEvent = 20009;

    public const int ChildrenInvalidated = 2;                                               // StructureChangeType
    public const int ToggleOff = 0;                                                         // ToggleState
    public const int ToggleOn = 1;

    public const int Button = 50000;
    public const int CheckBox = 50002;
    public const int ComboBox = 50003;
    public const int Edit = 50004;
    public const int Image = 50006;
    public const int List = 50008;
    public const int ProgressBar = 50012;
    public const int RadioButton = 50013;
    public const int ScrollBar = 50014;
    public const int Slider = 50015;
    public const int StatusBar = 50017;
    public const int Tab = 50018;
    public const int Text = 50020;
    public const int ToolBar = 50021;
    public const int Tree = 50023;
    public const int Group = 50026;
    public const int Thumb = 50027;
    public const int DataGrid = 50028;
    public const int Document = 50030;
    public const int Window = 50032;
    public const int Pane = 50033;

    /// <summary>The programmatic name of a control-type id the tests know (for messages).</summary>
    public static string Name(int controlType) => controlType switch
    {
        Button => "Button", CheckBox => "CheckBox", ComboBox => "ComboBox", Edit => "Edit", Image => "Image",
        List => "List", ProgressBar => "ProgressBar", RadioButton => "RadioButton", ScrollBar => "ScrollBar",
        Slider => "Slider", StatusBar => "StatusBar", Tab => "Tab", Text => "Text", ToolBar => "ToolBar",
        Tree => "Tree", Group => "Group", Thumb => "Thumb", DataGrid => "DataGrid", Document => "Document", Window => "Window",
        Pane => "Pane", _ => controlType.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
