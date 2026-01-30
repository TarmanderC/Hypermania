using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Hypermania.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoFloatDoubleInGameSimAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "HM0001";
    public const string MathfDiagnosticId = "HM0002";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Disallow float/double in Game.Sim",
        messageFormat: "Type '{0}' is not allowed in namespace Game.Sim",
        category: "Determinism",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor MathfRule = new(
        id: MathfDiagnosticId,
        title: "Disallow Mathf in Game.Sim",
        messageFormat: "Use of '{0}' is not allowed in namespace Game.Sim",
        category: "Determinism",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule, MathfRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterSymbolAction(AnalyzeParameter, SymbolKind.Parameter);

        context.RegisterSyntaxNodeAction(
            AnalyzeNumericLiteral,
            SyntaxKind.NumericLiteralExpression
        );
        context.RegisterSyntaxNodeAction(AnalyzeCastExpression, SyntaxKind.CastExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeLocalDeclaration,
            SyntaxKind.LocalDeclarationStatement
        );

        context.RegisterOperationAction(AnalyzeBinaryOperation, OperationKind.Binary);
        context.RegisterOperationAction(
            AnalyzeCompoundAssignmentOperation,
            OperationKind.CompoundAssignment
        );
        context.RegisterOperationAction(AnalyzeInvocationOperation, OperationKind.Invocation);
    }

    private static bool IsInTargetNamespace(ISymbol symbol)
    {
        var ns = symbol.ContainingNamespace?.ToDisplayString() ?? "";
        return ns == "Game.Sim" || ns.StartsWith("Game.Sim.");
    }

    private static bool IsInTargetNamespace(OperationAnalysisContext context, SyntaxNode node)
    {
        var sym = context.ContainingSymbol;
        if (sym is not null)
            return IsInTargetNamespace(sym);

        var model = context.Operation.SemanticModel;
        if (model is null)
            return false;

        var enclosing = model.GetEnclosingSymbol(node.SpanStart, context.CancellationToken);
        return enclosing is not null && IsInTargetNamespace(enclosing);
    }

    private static bool IsBanned(ITypeSymbol? type) =>
        type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double;

    private static bool IsSfloat(ITypeSymbol? type) =>
        type?.ToDisplayString() == "Utils.SoftFloat.sfloat";

    private static bool IsImmediatelyCastedToSfloat(
        SyntaxNode node,
        SyntaxNodeAnalysisContext context
    )
    {
        SyntaxNode current = node;

        while (current.Parent is ParenthesizedExpressionSyntax)
            current = current.Parent;

        if (current.Parent is not CastExpressionSyntax cast)
            return false;

        var castType = context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type;
        return IsSfloat(castType);
    }

    private static void Report(SymbolAnalysisContext context, Location location, ITypeSymbol type)
    {
        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                location,
                type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            )
        );
    }

    private static void Report(
        SyntaxNodeAnalysisContext context,
        Location location,
        ITypeSymbol type
    )
    {
        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                location,
                type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            )
        );
    }

    private static void Report(
        OperationAnalysisContext context,
        Location location,
        ITypeSymbol type
    )
    {
        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                location,
                type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            )
        );
    }

    private static void ReportMathf(OperationAnalysisContext context, Location location)
    {
        context.ReportDiagnostic(Diagnostic.Create(MathfRule, location, "UnityEngine.Mathf"));
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (!IsInTargetNamespace(field))
            return;
        if (IsBanned(field.Type))
            Report(context, field.Locations[0], field.Type);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var prop = (IPropertySymbol)context.Symbol;
        if (!IsInTargetNamespace(prop))
            return;
        if (IsBanned(prop.Type))
            Report(context, prop.Locations[0], prop.Type);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!IsInTargetNamespace(method))
            return;

        if (IsBanned(method.ReturnType))
            Report(context, method.Locations[0], method.ReturnType);

        foreach (var p in method.Parameters)
        {
            if (IsBanned(p.Type))
                Report(
                    context,
                    p.Locations.Length > 0 ? p.Locations[0] : method.Locations[0],
                    p.Type
                );
        }
    }

    private static void AnalyzeParameter(SymbolAnalysisContext context)
    {
        var p = (IParameterSymbol)context.Symbol;
        if (!IsInTargetNamespace(p))
            return;
        if (IsBanned(p.Type))
            Report(context, p.Locations[0], p.Type);
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
    {
        var symbol = context.ContainingSymbol;
        if (symbol is null || !IsInTargetNamespace(symbol))
            return;

        var localDecl = (LocalDeclarationStatementSyntax)context.Node;

        foreach (var v in localDecl.Declaration.Variables)
        {
            var localSymbol =
                context.SemanticModel.GetDeclaredSymbol(v, context.CancellationToken)
                as ILocalSymbol;
            if (localSymbol is null)
                continue;

            var type = localSymbol.Type;
            if (IsBanned(type))
                Report(context, v.Identifier.GetLocation(), type);
        }
    }

    private static void AnalyzeNumericLiteral(SyntaxNodeAnalysisContext context)
    {
        var symbol = context.ContainingSymbol;
        if (symbol is null || !IsInTargetNamespace(symbol))
            return;

        var literal = (LiteralExpressionSyntax)context.Node;

        var type = context.SemanticModel.GetTypeInfo(literal, context.CancellationToken).Type;
        if (!IsBanned(type))
            return;

        // Keep this exemption only for literals (per your earlier behavior).
        if (IsImmediatelyCastedToSfloat(literal, context))
            return;

        Report(context, literal.GetLocation(), type!);
    }

    private static void AnalyzeCastExpression(SyntaxNodeAnalysisContext context)
    {
        var symbol = context.ContainingSymbol;
        if (symbol is null || !IsInTargetNamespace(symbol))
            return;

        var cast = (CastExpressionSyntax)context.Node;

        var type = context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type;
        if (!IsBanned(type))
            return;

        Report(context, cast.Type.GetLocation(), type!);
    }

    // Single rule: ban any binary op where result/operands are float/double.
    private static void AnalyzeBinaryOperation(OperationAnalysisContext context)
    {
        var bin = (IBinaryOperation)context.Operation;

        if (!IsInTargetNamespace(context, bin.Syntax))
            return;

        if (
            IsBanned(bin.Type)
            || IsBanned(bin.LeftOperand?.Type)
            || IsBanned(bin.RightOperand?.Type)
        )
        {
            var t = bin.Type ?? bin.LeftOperand?.Type ?? bin.RightOperand?.Type;
            if (t is null)
                return;

            Report(context, bin.Syntax.GetLocation(), t);
        }
    }

    private static void AnalyzeCompoundAssignmentOperation(OperationAnalysisContext context)
    {
        var op = (ICompoundAssignmentOperation)context.Operation;

        if (!IsInTargetNamespace(context, op.Syntax))
            return;

        if (IsBanned(op.Type) || IsBanned(op.Target?.Type) || IsBanned(op.Value?.Type))
        {
            var t = op.Type ?? op.Target?.Type ?? op.Value?.Type;
            if (t is null)
                return;

            Report(context, op.Syntax.GetLocation(), t);
        }
    }

    private static void AnalyzeInvocationOperation(OperationAnalysisContext context)
    {
        var inv = (IInvocationOperation)context.Operation;

        if (!IsInTargetNamespace(context, inv.Syntax))
            return;

        var containingType = inv.TargetMethod.ContainingType;
        if (containingType is null)
            return;

        if (
            containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == "global::UnityEngine.Mathf"
        )
        {
            ReportMathf(context, inv.Syntax.GetLocation());
        }
    }
}
