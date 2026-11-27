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
    ulong? Address)
{
    /// <summary>
    /// The text of the value for a text viewer (<see cref="IDebugSession.GetTextDetailsAsync"/>,
    /// <see cref="IDebugSession.ReadTextAsync"/>): non-null exactly for strings, character arrays and computed strings
    /// (not for a <c>nil</c> pointer or an error). Valid for the stop epoch only, like every handle.
    /// </summary>
    public VariableHandle? Text { get; init; }
}

/// <summary>Where an expression is evaluated from.</summary>
public enum EvaluateContext { Watch, Hover, Console }

/// <summary>Result of an expression evaluation.</summary>
public sealed record EvaluateResult(VariableInfo Value);

/// <summary>The kind of a text behind <see cref="VariableInfo.Text"/>.</summary>
public enum TextKind { Unicode, Ansi, Short, PChar, PAnsiChar, CharArray, Computed }

/// <summary>
/// What is known about a text: kind; length in elements (-1: a zero-terminated text without a terminator within the
/// scan limit); reference count of a heap block (-1: a constant); code page; encoding as shown to the user; element size
/// in bytes; address of the first element; declared maximum length of a short string.
/// </summary>
public sealed record TextDetails(TextKind Kind, long Length, int? RefCount, int? CodePage, string Encoding,
    int ElementSize, ulong? PayloadAddress, int? MaxLength);

/// <summary>
/// One chunk of a text: the decoded elements <paramref name="Start"/> … <paramref name="Next"/> − 1. The next chunk
/// starts at <paramref name="Next"/>; <paramref name="AtEnd"/> when the end of the text was reached;
/// <paramref name="Truncated"/> when only a readable prefix of the chunk could be read.
/// </summary>
public sealed record TextChunk(long Start, long Next, string Text, bool AtEnd, bool Truncated);
