using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace CodeCracker.CSharp.Design
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class UseInvokeMethodToFireEventAnalyzer : DiagnosticAnalyzer
    {
        internal const string Title = "Check for null before calling a delegate";
        internal const string MessageFormat = "Verify if delegate '{0}' is null before invoking it.";
        internal const string Category = SupportedCategories.Design;
        const string Description = "In C#6 a delegate can be invoked using the null-propagating operator (?.) and it's"
            + " invoke method to avoid throwing a NullReference exception when there is no method attached to the delegate. "
            + "Or you can check for null before calling the delegate.";

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId.UseInvokeMethodToFireEvent.ToDiagnosticId(),
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.UseInvokeMethodToFireEvent));

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context) =>
            context.RegisterSyntaxNodeAction(Analyzer, SyntaxKind.InvocationExpression);

        private static void Analyzer(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            var invocation = (InvocationExpressionSyntax)context.Node;
            var identifier = invocation.Expression as IdentifierNameSyntax;
            if (identifier == null) return;
            var typeInfo = context.SemanticModel.GetTypeInfo(identifier, context.CancellationToken);

            if (typeInfo.ConvertedType?.BaseType == null) return;
            if (typeInfo.ConvertedType.BaseType.Name != typeof(MulticastDelegate).Name) return;

            var symbol = context.SemanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is ILocalSymbol) return;

            var invokedMethodSymbol = (typeInfo.ConvertedType as INamedTypeSymbol)?.DelegateInvokeMethod;
            if (invokedMethodSymbol == null) return;

            if (HasCheckForNullThatReturns(invocation, context.SemanticModel, symbol)) return;
            if (IsInsideANullCheck(invocation, context.SemanticModel, symbol)) return;
            if (IsPartOfATernaryThatChecksForNull(invocation, context.SemanticModel, symbol)) return;
            if (IsPartOfALogicalOrThatChecksForNull(invocation, context.SemanticModel, symbol)) return;
            if (IsPartOfALogicalAndThatChecksForNotNull(invocation, context.SemanticModel, symbol)) return;
            if (HasArgumentNullExceptionThrowIfNullBeforeInvocation(invocation, context.SemanticModel, symbol)) return;
            if (symbol.IsReadOnlyAndInitializedForCertain(context)) return;

            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), identifier.Identifier.Text));
        }

        private static bool HasCheckForNullThatReturns(InvocationExpressionSyntax invocation, SemanticModel semanticModel, ISymbol symbol)
        {
            var method = invocation.FirstAncestorOfKind(SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration) as BaseMethodDeclarationSyntax;
            if (method != null && method.Body != null)
            {
                var ifs = method.Body.Statements.OfKind(SyntaxKind.IfStatement);
                foreach (IfStatementSyntax @if in ifs)
                {
                    if (!@if.Condition?.IsKind(SyntaxKind.EqualsExpression) ?? true) continue;
                    var equals = (BinaryExpressionSyntax)@if.Condition;
                    if (equals.Left == null || equals.Right == null) continue;
                    if (@if.GetLocation().SourceSpan.Start > invocation.GetLocation().SourceSpan.Start) return false;
                    ISymbol identifierSymbol;
                    if (equals.Right.IsKind(SyntaxKind.NullLiteralExpression) && equals.Left.IsKind(SyntaxKind.IdentifierName))
                        identifierSymbol = semanticModel.GetSymbolInfo(equals.Left).Symbol;
                    else if (equals.Left.IsKind(SyntaxKind.NullLiteralExpression) && equals.Right.IsKind(SyntaxKind.IdentifierName))
                        identifierSymbol = semanticModel.GetSymbolInfo(equals.Right).Symbol;
                    else continue;
                    if (!symbol.Equals(identifierSymbol)) continue;
                    if (@if.Statement == null) continue;
                    if (@if.Statement.IsKind(SyntaxKind.Block))
                    {
                        var ifBlock = (BlockSyntax)@if.Statement;
                        if (ifBlock.Statements.OfKind(SyntaxKind.ThrowStatement, SyntaxKind.ReturnStatement).Any()) return true;
                    }
                    else
                    {
                        if (@if.Statement.IsAnyKind(SyntaxKind.ThrowStatement, SyntaxKind.ReturnStatement)) return true;
                    }
                }
            }
            return false;
        }

        private static bool IsPartOfATernaryThatChecksForNull(InvocationExpressionSyntax invocation, SemanticModel semanticModel, ISymbol symbol) =>
            IsConditionThatChecksForNotEqualsNull(invocation.FirstAncestorOfType<ConditionalExpressionSyntax>()?.Condition, semanticModel, symbol);

        private static bool IsPartOfALogicalOrThatChecksForNull(InvocationExpressionSyntax invocation, SemanticModel semanticModel, ISymbol symbol) =>
            IsConditionThatChecksForEqualsNull(invocation.FirstAncestorOfKind<BinaryExpressionSyntax>(SyntaxKind.LogicalOrExpression)?.Left, semanticModel, symbol);

        private static bool IsPartOfALogicalAndThatChecksForNotNull(InvocationExpressionSyntax invocation, SemanticModel semanticModel, ISymbol symbol) =>
            IsConditionThatChecksForNotEqualsNull(invocation.FirstAncestorOfKind<BinaryExpressionSyntax>(SyntaxKind.LogicalAndExpression)?.Left, semanticModel, symbol);

        private static bool IsInsideANullCheck(InvocationExpressionSyntax invocation, SemanticModel semanticModel, ISymbol symbol) =>
            invocation.Ancestors().OfType<IfStatementSyntax>().Any(@if => IsConditionThatChecksForNotEqualsNull(@if.Condition, semanticModel, symbol));

        private static bool IsConditionThatChecksForNotEqualsNull(ExpressionSyntax condition, SemanticModel semanticModel, ISymbol symbol) =>
            IsConditionThatChecksForNull(condition, semanticModel, symbol, SyntaxKind.NotEqualsExpression);

        private static bool IsConditionThatChecksForEqualsNull(ExpressionSyntax condition, SemanticModel semanticModel, ISymbol symbol) =>
            IsConditionThatChecksForNull(condition, semanticModel, symbol, SyntaxKind.EqualsExpression);

        private static bool IsConditionThatChecksForNull(ExpressionSyntax condition, SemanticModel semanticModel, ISymbol symbol, SyntaxKind binarySyntaxKind)
        {
            if (condition == null) return false;
            if (!condition.IsKind(binarySyntaxKind)) return false;
            var equals = (BinaryExpressionSyntax)condition;
            if (equals.Left == null || equals.Right == null) return false;
            ISymbol identifierSymbol;
            if (equals.Right.IsKind(SyntaxKind.NullLiteralExpression) && equals.Left.IsKind(SyntaxKind.IdentifierName))
                identifierSymbol = semanticModel.GetSymbolInfo(equals.Left).Symbol;
            else if (equals.Left.IsKind(SyntaxKind.NullLiteralExpression) && equals.Right.IsKind(SyntaxKind.IdentifierName))
                identifierSymbol = semanticModel.GetSymbolInfo(equals.Right).Symbol;
            else return false;
            if (symbol.Equals(identifierSymbol)) return true;
            return false;
        }

        private static bool HasArgumentNullExceptionThrowIfNullBeforeInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            ISymbol symbol)
        {
            // Looks for a preceding call that guards the same symbol with:
            //  System.ArgumentNullException.ThrowIfNull(symbol);
            //  ArgumentNullException.ThrowIfNull(symbol);                (with using System;)
            //  ThrowIfNull(symbol);                                     (with using static System.ArgumentNullException;)
            //
            // This implementation is intentionally simple and resilient to:
            //  - Missing ThrowIfNull symbol (older target frameworks)
            //  - Extra nesting / formatting
            //  - Minimal test snippets (no wrapping class/namespace)

            var block = invocation.FirstAncestorOfType<BlockSyntax>();
            if (block == null) return false;

            var invocationStart = invocation.SpanStart;

            // Detect a static using for ArgumentNullException (textual or semantic)
            var root = block.SyntaxTree.GetRoot();
            bool hasUsingStaticArgNull =
                root.DescendantNodes()
                    .OfType<UsingDirectiveSyntax>()
                    .Any(u =>
                        u.StaticKeyword.Kind() == SyntaxKind.StaticKeyword &&
                        (u.Name.ToString() == "System.ArgumentNullException"
                         || u.Name.ToString() == "ArgumentNullException"
                         || (semanticModel.GetSymbolInfo(u.Name).Symbol is INamedTypeSymbol ts
                             && ts.Name == nameof(ArgumentNullException)
                             && ts.ContainingNamespace?.ToDisplayString() == "System")));

            // Fallback textual scan (covers cases where symbol resolution fails entirely)
            if (!hasUsingStaticArgNull)
            {
                var fullText = root.GetText().ToString();
                if (fullText.IndexOf("using static System.ArgumentNullException", StringComparison.Ordinal) >= 0)
                    hasUsingStaticArgNull = true;
            }

            foreach (var priorInvocation in block.DescendantNodes()
                         .OfType<InvocationExpressionSyntax>()
                         .Where(i => i.SpanStart < invocationStart))
            {
                if (!IsThrowIfNullGuard(priorInvocation)) continue;

                var args = priorInvocation.ArgumentList?.Arguments;
                if (args == null || args.Value.Count == 0) continue;

                var guarded = semanticModel.GetSymbolInfo(args.Value[0].Expression).Symbol;
                if (guarded != null && SymbolEqualityComparer.Default.Equals(guarded, symbol))
                    return true;
            }

            return false;

            bool IsThrowIfNullGuard(InvocationExpressionSyntax candidate)
            {
                // Get invoked simple name
                var simpleName = candidate.Expression switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                    _ => null
                };
                if (simpleName != "ThrowIfNull") return false;

                // Qualification rules
                switch (candidate.Expression)
                {
                    case IdentifierNameSyntax:
                        // Must have using static
                        return hasUsingStaticArgNull;

                    case MemberAccessExpressionSyntax ma:
                        // Collapse left side text (handles fully-qualified chain)
                        var left = ma.Expression.ToString();
                        if (left == "System.ArgumentNullException" || left == "ArgumentNullException")
                            return true;

                        // Semantic fallback (in case of partial qualification or alias)
                        if (semanticModel.GetSymbolInfo(ma.Expression).Symbol is INamedTypeSymbol leftType &&
                            leftType.Name == nameof(ArgumentNullException) &&
                            leftType.ContainingNamespace?.ToDisplayString() == "System")
                            return true;
                        return false;

                    default:
                        return false;
                }
            }
        }
    }
}