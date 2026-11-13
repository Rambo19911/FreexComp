namespace FreexComp.Compiler.Syntax;

// grammar.md §3. C-013 needs type identifiers and string types; the other type syntaxes follow with C-014.

/// <summary><c>typeId</c>: a (possibly qualified) type name.</summary>
public sealed class NamedTypeSyntax : TypeSyntax
{
    internal NamedTypeSyntax(NameSyntax name)
        : base(SyntaxKind.NamedType)
    {
        Name = Add(name);
    }

    public NameSyntax Name { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitNamedType(this);
}

/// <summary><c>string [ "[" constExpr "]" ]</c>; with a length it is a ShortString of that maximum length.</summary>
public sealed class StringTypeSyntax : TypeSyntax
{
    internal StringTypeSyntax(SyntaxToken stringKeyword, SyntaxToken? openBracketToken, ExpressionSyntax? length,
                              SyntaxToken? closeBracketToken)
        : base(SyntaxKind.StringType)
    {
        StringKeyword = Add(stringKeyword);
        OpenBracketToken = Add(openBracketToken);
        Length = AddOptional(length);
        CloseBracketToken = Add(closeBracketToken);
    }

    public SyntaxToken StringKeyword { get; }

    public SyntaxToken? OpenBracketToken { get; }

    public ExpressionSyntax? Length { get; }

    public SyntaxToken? CloseBracketToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitStringType(this);
}

/// <summary><c>( enumElement { , enumElement } )</c></summary>
public sealed class EnumTypeSyntax : TypeSyntax
{
    internal EnumTypeSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<EnumMemberSyntax> members, SyntaxToken closeParenToken)
        : base(SyntaxKind.EnumType)
    {
        OpenParenToken = Add(openParenToken);
        Members = Add(members);
        CloseParenToken = Add(closeParenToken);
    }

    public SyntaxToken OpenParenToken { get; }

    public SeparatedSyntaxList<EnumMemberSyntax> Members { get; }

    public SyntaxToken CloseParenToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitEnumType(this);
}

/// <summary><c>ident [ = constExpr ]</c></summary>
public sealed class EnumMemberSyntax : SyntaxNode
{
    internal EnumMemberSyntax(SyntaxToken identifier, SyntaxToken? equalsToken, ExpressionSyntax? value)
        : base(SyntaxKind.EnumMember)
    {
        Identifier = Add(identifier);
        EqualsToken = Add(equalsToken);
        Value = AddOptional(value);
    }

    public SyntaxToken Identifier { get; }

    public SyntaxToken? EqualsToken { get; }

    public ExpressionSyntax? Value { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitEnumMember(this);
}

/// <summary><c>constExpr .. constExpr</c></summary>
public sealed class SubrangeTypeSyntax : TypeSyntax
{
    internal SubrangeTypeSyntax(ExpressionSyntax low, SyntaxToken dotDotToken, ExpressionSyntax high)
        : base(SyntaxKind.SubrangeType)
    {
        Low = Add(low);
        DotDotToken = Add(dotDotToken);
        High = Add(high);
    }

    public ExpressionSyntax Low { get; }

    public SyntaxToken DotDotToken { get; }

    public ExpressionSyntax High { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitSubrangeType(this);
}

/// <summary><c>^ typeId</c></summary>
public sealed class PointerTypeSyntax : TypeSyntax
{
    internal PointerTypeSyntax(SyntaxToken caretToken, TypeSyntax pointedType)
        : base(SyntaxKind.PointerType)
    {
        CaretToken = Add(caretToken);
        PointedType = Add(pointedType);
    }

    public SyntaxToken CaretToken { get; }

    /// <summary>A <see cref="NamedTypeSyntax"/> (a missing one after an error).</summary>
    public TypeSyntax PointedType { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitPointerType(this);
}

/// <summary><c>[ packed ] array [ "[" indexType { , indexType } "]" ] of ( type | const )</c></summary>
public sealed class ArrayTypeSyntax : TypeSyntax
{
    internal ArrayTypeSyntax(SyntaxToken? packedKeyword, SyntaxToken arrayKeyword, SyntaxToken? openBracketToken,
                             SeparatedSyntaxList<TypeSyntax> indexTypes, SyntaxToken? closeBracketToken, SyntaxToken ofKeyword,
                             TypeSyntax? elementType, SyntaxToken? constKeyword)
        : base(SyntaxKind.ArrayType)
    {
        PackedKeyword = Add(packedKeyword);
        ArrayKeyword = Add(arrayKeyword);
        OpenBracketToken = Add(openBracketToken);
        IndexTypes = Add(indexTypes);
        CloseBracketToken = Add(closeBracketToken);
        OfKeyword = Add(ofKeyword);
        ElementType = AddOptional(elementType);
        ConstKeyword = Add(constKeyword);
    }

    public SyntaxToken? PackedKeyword { get; }

    public SyntaxToken ArrayKeyword { get; }

    public SyntaxToken? OpenBracketToken { get; }

    /// <summary>Empty for a dynamic or open array.</summary>
    public SeparatedSyntaxList<TypeSyntax> IndexTypes { get; }

    public SyntaxToken? CloseBracketToken { get; }

    public SyntaxToken OfKeyword { get; }

    /// <summary>Null for <c>array of const</c>.</summary>
    public TypeSyntax? ElementType { get; }

    /// <summary>The <c>const</c> of <c>array of const</c>.</summary>
    public SyntaxToken? ConstKeyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitArrayType(this);
}

/// <summary><c>[ packed ] set of ordinalType</c></summary>
public sealed class SetTypeSyntax : TypeSyntax
{
    internal SetTypeSyntax(SyntaxToken? packedKeyword, SyntaxToken setKeyword, SyntaxToken ofKeyword, TypeSyntax elementType)
        : base(SyntaxKind.SetType)
    {
        PackedKeyword = Add(packedKeyword);
        SetKeyword = Add(setKeyword);
        OfKeyword = Add(ofKeyword);
        ElementType = Add(elementType);
    }

    public SyntaxToken? PackedKeyword { get; }

    public SyntaxToken SetKeyword { get; }

    public SyntaxToken OfKeyword { get; }

    public TypeSyntax ElementType { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitSetType(this);
}

/// <summary><c>[ packed ] file [ of type ]</c> (parsed; E2900 from the binder).</summary>
public sealed class FileTypeSyntax : TypeSyntax
{
    internal FileTypeSyntax(SyntaxToken? packedKeyword, SyntaxToken fileKeyword, SyntaxToken? ofKeyword, TypeSyntax? elementType)
        : base(SyntaxKind.FileType)
    {
        PackedKeyword = Add(packedKeyword);
        FileKeyword = Add(fileKeyword);
        OfKeyword = Add(ofKeyword);
        ElementType = AddOptional(elementType);
    }

    public SyntaxToken? PackedKeyword { get; }

    public SyntaxToken FileKeyword { get; }

    public SyntaxToken? OfKeyword { get; }

    public TypeSyntax? ElementType { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitFileType(this);
}

/// <summary><c>[ packed ] record { recordMember } [ variantPart ] end</c></summary>
public sealed class RecordTypeSyntax : TypeSyntax
{
    internal RecordTypeSyntax(SyntaxToken? packedKeyword, SyntaxToken recordKeyword, SyntaxList<SyntaxNode> members,
                              VariantPartSyntax? variantPart, SyntaxToken endKeyword)
        : base(SyntaxKind.RecordType)
    {
        PackedKeyword = Add(packedKeyword);
        RecordKeyword = Add(recordKeyword);
        Members = Add(members);
        VariantPart = AddOptional(variantPart);
        EndKeyword = Add(endKeyword);
    }

    public SyntaxToken? PackedKeyword { get; }

    public SyntaxToken RecordKeyword { get; }

    /// <summary>As <see cref="ClassTypeSyntax.Members"/>: default-section members, then visibility sections.</summary>
    public SyntaxList<SyntaxNode> Members { get; }

    public VariantPartSyntax? VariantPart { get; }

    public SyntaxToken EndKeyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitRecordType(this);
}

/// <summary><c>identList : type [ hints ] ;</c> — the <c>;</c> may be omitted before <c>end</c>, <c>)</c> or <c>case</c>.</summary>
public sealed class FieldDeclarationSyntax : MemberDeclarationSyntax
{
    internal FieldDeclarationSyntax(SeparatedSyntaxList<IdentifierNameSyntax> identifiers, SyntaxToken colonToken, TypeSyntax type,
                                    SyntaxList<HintDirectiveSyntax> hintDirectives, SyntaxToken? semicolonToken)
        : base(SyntaxKind.FieldDeclaration)
    {
        Identifiers = Add(identifiers);
        ColonToken = Add(colonToken);
        Type = Add(type);
        HintDirectives = Add(hintDirectives);
        SemicolonToken = Add(semicolonToken);
    }

    public SeparatedSyntaxList<IdentifierNameSyntax> Identifiers { get; }

    public SyntaxToken ColonToken { get; }

    public TypeSyntax Type { get; }

    public SyntaxList<HintDirectiveSyntax> HintDirectives { get; }

    public SyntaxToken? SemicolonToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitFieldDeclaration(this);
}

/// <summary><c>case [ ident : ] typeId of variant { ; variant } [ ; ]</c></summary>
public sealed class VariantPartSyntax : SyntaxNode
{
    internal VariantPartSyntax(SyntaxToken caseKeyword, SyntaxToken? tagIdentifier, SyntaxToken? colonToken, TypeSyntax tagType,
                               SyntaxToken ofKeyword, SeparatedSyntaxList<VariantSyntax> variants)
        : base(SyntaxKind.VariantPart)
    {
        CaseKeyword = Add(caseKeyword);
        TagIdentifier = Add(tagIdentifier);
        ColonToken = Add(colonToken);
        TagType = Add(tagType);
        OfKeyword = Add(ofKeyword);
        Variants = Add(variants);
    }

    public SyntaxToken CaseKeyword { get; }

    /// <summary>The tag field name, if the tag is stored.</summary>
    public SyntaxToken? TagIdentifier { get; }

    public SyntaxToken? ColonToken { get; }

    public TypeSyntax TagType { get; }

    public SyntaxToken OfKeyword { get; }

    /// <summary>Separated by <c>;</c>; a trailing <c>;</c> is kept.</summary>
    public SeparatedSyntaxList<VariantSyntax> Variants { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitVariantPart(this);
}

/// <summary><c>constExpr { , constExpr } : ( { fieldDecl } [ variantPart ] )</c></summary>
public sealed class VariantSyntax : SyntaxNode
{
    internal VariantSyntax(SeparatedSyntaxList<ExpressionSyntax> labels, SyntaxToken colonToken, SyntaxToken openParenToken,
                           SyntaxList<FieldDeclarationSyntax> fields, VariantPartSyntax? variantPart, SyntaxToken closeParenToken)
        : base(SyntaxKind.Variant)
    {
        Labels = Add(labels);
        ColonToken = Add(colonToken);
        OpenParenToken = Add(openParenToken);
        Fields = Add(fields);
        VariantPart = AddOptional(variantPart);
        CloseParenToken = Add(closeParenToken);
    }

    public SeparatedSyntaxList<ExpressionSyntax> Labels { get; }

    public SyntaxToken ColonToken { get; }

    public SyntaxToken OpenParenToken { get; }

    public SyntaxList<FieldDeclarationSyntax> Fields { get; }

    public VariantPartSyntax? VariantPart { get; }

    public SyntaxToken CloseParenToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitVariant(this);
}

/// <summary>
/// <c>( procedure [ formalParams ] | function [ formalParams ] : resultType ) [ of object ] { [ ; ] callConv }</c>, or
/// <c>reference to ( procedure … | function … )</c>.
/// </summary>
public sealed class ProceduralTypeSyntax : TypeSyntax
{
    internal ProceduralTypeSyntax(SyntaxToken? referenceKeyword, SyntaxToken? toKeyword, SyntaxToken keyword,
                                  ParameterListSyntax? parameterList, SyntaxToken? colonToken, TypeSyntax? resultType,
                                  SyntaxToken? ofKeyword, SyntaxToken? objectKeyword,
                                  SyntaxList<CallingConventionSyntax> callingConventions)
        : base(SyntaxKind.ProceduralType)
    {
        ReferenceKeyword = Add(referenceKeyword);
        ToKeyword = Add(toKeyword);
        Keyword = Add(keyword);
        ParameterList = AddOptional(parameterList);
        ColonToken = Add(colonToken);
        ResultType = AddOptional(resultType);
        OfKeyword = Add(ofKeyword);
        ObjectKeyword = Add(objectKeyword);
        CallingConventions = Add(callingConventions);
    }

    /// <summary>Contextual <c>reference</c> of <c>reference to</c>.</summary>
    public SyntaxToken? ReferenceKeyword { get; }

    public SyntaxToken? ToKeyword { get; }

    /// <summary><c>procedure</c> or <c>function</c>.</summary>
    public SyntaxToken Keyword { get; }

    public ParameterListSyntax? ParameterList { get; }

    public SyntaxToken? ColonToken { get; }

    public TypeSyntax? ResultType { get; }

    public SyntaxToken? OfKeyword { get; }

    public SyntaxToken? ObjectKeyword { get; }

    public SyntaxList<CallingConventionSyntax> CallingConventions { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitProceduralType(this);
}

/// <summary><c>[ ; ] callConv</c> after a procedural type.</summary>
public sealed class CallingConventionSyntax : SyntaxNode
{
    internal CallingConventionSyntax(SyntaxToken? semicolonToken, SyntaxToken keyword)
        : base(SyntaxKind.CallingConvention)
    {
        SemicolonToken = Add(semicolonToken);
        Keyword = Add(keyword);
    }

    public SyntaxToken? SemicolonToken { get; }

    /// <summary>An identifier whose <see cref="SyntaxToken.ContextualKind"/> is the convention keyword.</summary>
    public SyntaxToken Keyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitCallingConvention(this);
}

/// <summary><c>class of typeId</c></summary>
public sealed class ClassRefTypeSyntax : TypeSyntax
{
    internal ClassRefTypeSyntax(SyntaxToken classKeyword, SyntaxToken ofKeyword, TypeSyntax classType)
        : base(SyntaxKind.ClassRefType)
    {
        ClassKeyword = Add(classKeyword);
        OfKeyword = Add(ofKeyword);
        ClassType = Add(classType);
    }

    public SyntaxToken ClassKeyword { get; }

    public SyntaxToken OfKeyword { get; }

    public TypeSyntax ClassType { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitClassRefType(this);
}
