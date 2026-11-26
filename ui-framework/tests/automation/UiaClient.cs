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
}

/// <summary>IUIAutomationElement, slots 3 to 40.</summary>
[ComImport]
[Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    void Slot3_SetFocus();
    void Slot4_GetRuntimeId();

    IUIAutomationElement? FindFirst(TreeScope scope, IUIAutomationCondition condition);    // slot 5

    void Slot6_FindAll();
    void Slot7_FindFirstBuildCache();
    void Slot8_FindAllBuildCache();
    void Slot9_BuildUpdatedCache();
    void Slot10_GetCurrentPropertyValue();
    void Slot11_GetCurrentPropertyValueEx();
    void Slot12_GetCachedPropertyValue();
    void Slot13_GetCachedPropertyValueEx();
    void Slot14_GetCurrentPatternAs();
    void Slot15_GetCachedPatternAs();
    void Slot16_GetCurrentPattern();
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
    public const int AutomationIdProperty = 30011;

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
    public const int Window = 50032;
    public const int Pane = 50033;

    /// <summary>The programmatic name of a control-type id the tests know (for messages).</summary>
    public static string Name(int controlType) => controlType switch
    {
        Button => "Button", CheckBox => "CheckBox", ComboBox => "ComboBox", Edit => "Edit", Image => "Image",
        List => "List", ProgressBar => "ProgressBar", RadioButton => "RadioButton", ScrollBar => "ScrollBar",
        Slider => "Slider", StatusBar => "StatusBar", Tab => "Tab", Text => "Text", ToolBar => "ToolBar",
        Tree => "Tree", Group => "Group", Thumb => "Thumb", DataGrid => "DataGrid", Window => "Window",
        Pane => "Pane", _ => controlType.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
