using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RepoIndexer;

internal sealed class DeclarationRecordingWalker : CSharpSyntaxWalker
{
    private readonly List<string> _declarations = new();
    private readonly StringBuilder _builder = new();
    private readonly Stack<string> _namespaceStack = new();
    private readonly Stack<string> _typeStack = new();

    public IReadOnlyList<string> Declarations => _declarations;

    private void PushType(SyntaxToken identifier, TypeParameterListSyntax? typeParameterList)
    {
        PushType(identifier, typeParameterList, null);
    }

    private void PushType(SyntaxToken identifier, TypeParameterListSyntax? typeParameterList, ParameterListSyntax? parameterList)
    {
        var typeName = identifier.ValueText + GetTypeParameters(typeParameterList);
        _typeStack.Push(typeName);

        BeginApi("T");
        WriteNamespaceAndTypeName();
        if (parameterList != null)
            WriteParameterList(parameterList);
        EndApi();
    }

    private void PopType()
    {
        _typeStack.Pop();
    }

    private string GetTypeParameters(TypeParameterListSyntax? typeParameterList)
    {
        if (typeParameterList == null)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("<");

        foreach (var parameter in typeParameterList.Parameters)
        {
            if (sb.Length > 1)
                sb.Append(", ");

            sb.Append(parameter.Identifier.ValueText);               
        }

        sb.Append(">");

        return sb.ToString();
    }

    private void WriteNamespaceAndTypeName()
    {
        var isFirst = true;

        foreach (var namespaceName in _namespaceStack.Reverse())
        {
            if (isFirst)
                isFirst = false;
            else
                _builder.Append(".");

            _builder.Append(namespaceName);
        }

        foreach (var typeName in _typeStack.Reverse())
        {
            if (isFirst)
                isFirst = false;
            else
                _builder.Append(".");

            _builder.Append(typeName);
        }
    }

    private void BeginApi(string kind)
    {
        _builder.Append(kind);
        _builder.Append(":");
    }

    private void EndApi()
    {
        var declaration = _builder.ToString();
        _declarations.Add(declaration);
        _builder.Clear();
    }


    private void WriteMethod(string name, ParameterListSyntax parameterList)
    {
        WriteMethod(name, null, parameterList);
    }

    private void WriteMethod(string name, TypeParameterListSyntax? typeParameterList, ParameterListSyntax parameterList)
    {
        Write(name);
        if (typeParameterList != null)
            Write(GetTypeParameters(typeParameterList));
        WriteParameterList(parameterList);
    }

    private void WriteType(TypeSyntax? type, SyntaxTokenList modifiers)
    {
        for (var i = 0; i < modifiers.Count; i++)
        {
            if (i > 0)
                Write(" ");

            Write(modifiers[i].ValueText);
        }

        if (modifiers.Any())
            Write(" ");

        if (type == null)
        {
            Write("?");
        }
        else
        {
            WriteType(type);
        }
    }

    private void WriteType(TypeSyntax type)
    {
        Write(type.ToString());
    }

    private void WriteParameterList(ParameterListSyntax parameterList)
    {
        Write(parameterList.OpenParenToken.ValueText);
        WriteParameters(parameterList.Parameters);
        Write(parameterList.CloseParenToken.ValueText);
    }

    private void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        var isFirst = true;

        foreach (var parameter in parameters)
        {
            if (isFirst)
                isFirst = false;
            else
                Write(",");

            WriteType(parameter.Type, parameter.Modifiers);
        }
    }

    private void Write(string text)
    {
        _builder.Append(text);
    }

    // Let's ignore namespace for indexing purposes. Historically, we haven't
    // been consistent with adding namespaces. Many of the code blocks only contain
    // types.
    //
    // public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    // {
    //     Debug.Assert(_typeStack.Count == 0);
    //
    //     var name = node.Name.ToString().Trim();
    //     _namespaceStack.Push(name);
    //
    //     BeginApi("N");
    //     WriteNamespaceAndTypeName();
    //     EndApi();
    //
    //     base.VisitNamespaceDeclaration(node);
    //
    //     _namespaceStack.Pop();
    // }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        PushType(node.Identifier, node.TypeParameterList);
        base.VisitClassDeclaration(node);
        PopType();
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        PushType(node.Identifier, node.TypeParameterList);
        base.VisitStructDeclaration(node);
        PopType();
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        PushType(node.Identifier, null);
        base.VisitEnumDeclaration(node);
        PopType();
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        PushType(node.Identifier, node.TypeParameterList);
        base.VisitInterfaceDeclaration(node);
        PopType();
    }

    public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
    {
        PushType(node.Identifier, node.TypeParameterList, node.ParameterList);
        base.VisitDelegateDeclaration(node);
        PopType();
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        BeginApi("M");
        WriteNamespaceAndTypeName();
        Write(".");
        WriteMethod(node.Identifier.ValueText, node.ParameterList);
        EndApi();

        base.VisitConstructorDeclaration(node);
    }

    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        BeginApi("M");
        WriteNamespaceAndTypeName();
        Write(".");
        WriteMethod("~"  + node.Identifier.ValueText, node.ParameterList);
        EndApi();
        base.VisitDestructorDeclaration(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        BeginApi("M");
        WriteNamespaceAndTypeName();
        Write(".");
        WriteMethod(node.Identifier.ValueText, node.TypeParameterList, node.ParameterList);
        EndApi();
        base.VisitMethodDeclaration(node);
    }

    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        BeginApi("M");
        WriteNamespaceAndTypeName();
        Write(".");
        WriteMethod(node.ImplicitOrExplicitKeyword.ValueText, node.ParameterList);
        EndApi();
        base.VisitConversionOperatorDeclaration(node);
    }

    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        BeginApi("M");
        WriteNamespaceAndTypeName();
        Write(".");
        WriteMethod(node.OperatorToken.ValueText, node.ParameterList);
        EndApi();

        base.VisitOperatorDeclaration(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        BeginApi("P");
        WriteNamespaceAndTypeName();
        Write(".");
        Write(node.Identifier.ValueText);
        EndApi();
        base.VisitPropertyDeclaration(node);
    }

    public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        BeginApi("P");
        WriteNamespaceAndTypeName();
        Write("[");
        WriteParameters(node.ParameterList.Parameters);
        Write("]");
        EndApi();
        base.VisitIndexerDeclaration(node);
    }

    public override void VisitEventDeclaration(EventDeclarationSyntax node)
    {
        BeginApi("E");
        WriteNamespaceAndTypeName();
        Write(".");
        Write(node.Identifier.ValueText);
        EndApi();
        base.VisitEventDeclaration(node);
    }

    public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
    {
        BeginApi("F");
        WriteNamespaceAndTypeName();
        Write(".");
        Write(node.Identifier.ValueText);
        EndApi();
        base.VisitEnumMemberDeclaration(node);
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        BeginApi("F");
        WriteNamespaceAndTypeName();
        Write(".");
        Write(node.Declaration.Variables.Single().Identifier.ValueText);
        EndApi();
        base.VisitFieldDeclaration(node);
    }
}
