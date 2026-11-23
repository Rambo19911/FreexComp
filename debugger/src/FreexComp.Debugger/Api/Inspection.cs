namespace FreexComp.Debugger;

/// <summary>A thread of the debuggee.</summary>
public sealed record ThreadInfo(int Id, string Name, bool IsCurrent);

/// <summary>One stack frame. <paramref name="Id"/> is valid for the current stop epoch only; index 0 is the innermost frame.</summary>
public sealed record StackFrameInfo(
    int Id,
    int Index,
    string Name,
    SourceLocation? Location,
    bool IsUserCode,
    ulong InstructionPointer,
    string Module);

/// <summary>A page of stack frames and the total number of frames.</summary>
public sealed record StackTraceResult(IReadOnlyList<StackFrameInfo> Frames, int TotalFrames);

/// <summary>Kind of a variable scope.</summary>
public enum ScopeKind { Arguments, Locals, Globals }

/// <summary>A variable scope of a frame.</summary>
public sealed record VariableScope(ScopeKind Kind, string Name, VariableHandle Handle);

/// <summary>Reference to an expandable value; opaque to the IDE and valid for one stop epoch.</summary>
public readonly record struct VariableHandle(int Epoch, int Id);

/// <summary>Kind of a rendered value.</summary>
public enum ValueKind
{
    Integer, Float, Boolean, Char, Enum, Set, String, StaticArray, DynamicArray, Record,
    ClassInstance, ClassReference, Pointer, Interface, Procedural, Raw, Error
}

/// <summary>A rendered value node.</summary>
public sealed record VariableInfo(
    string Name,
    string Value,
    string TypeName,
    ValueKind Kind,
    VariableHandle? Children,
    int ChildCount,
    bool CanModify,
    string? EvaluateName,
    ulong? Address);

/// <summary>Where an expression is evaluated from.</summary>
public enum EvaluateContext { Watch, Hover, Console }

/// <summary>Result of an expression evaluation.</summary>
public sealed record EvaluateResult(VariableInfo Value);
