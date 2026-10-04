using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CodeCracker.CSharp.Style
{
    internal static class AlwaysUseVarSemanticHelper
    {
        internal static bool SameLocal(ILocalSymbol original, ILocalSymbol inferred) =>
            original != null && inferred != null && inferred.Type.TypeKind != TypeKind.Error &&
            SymbolEqualityComparer.IncludeNullability.Equals(original.Type, inferred.Type) &&
            original.RefKind == inferred.RefKind && original.ScopedKind == inferred.ScopedKind;

        internal static bool SameLocals(SemanticModel originalModel, IEnumerable<SyntaxNode> originalNodes,
            SemanticModel inferredModel, IEnumerable<SyntaxNode> inferredNodes, CancellationToken cancellationToken)
        {
            var originals = originalNodes.ToArray();
            var inferred = inferredNodes.ToArray();
            if (originals.Length == 0 || originals.Length != inferred.Length) return false;
            for (var i = 0; i < originals.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SameLocal(originalModel.GetDeclaredSymbol(originals[i], cancellationToken) as ILocalSymbol,
                    inferredModel.GetDeclaredSymbol(inferred[i], cancellationToken) as ILocalSymbol)) return false;
            }
            return true;
        }

        internal static bool SameBindings(SemanticModel originalModel, SyntaxNode original,
            SemanticModel inferredModel, SyntaxNode inferred, CancellationToken cancellationToken)
        {
            if (original is LiteralExpressionSyntax) return true;
            var originalCalls = BindingNodes(original).ToArray();
            var inferredCalls = BindingNodes(inferred).ToArray();
            if (originalCalls.Length != inferredCalls.Length) return false;
            for (var i = 0; i < originalCalls.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var originalSymbol = originalModel.GetSymbolInfo(originalCalls[i], cancellationToken).Symbol;
                var inferredSymbol = inferredModel.GetSymbolInfo(inferredCalls[i], cancellationToken).Symbol;
                if (originalSymbol == null && inferredSymbol == null &&
                    originalCalls[i] is ElementAccessExpressionSyntax originalElement &&
                    inferredCalls[i] is ElementAccessExpressionSyntax inferredElement &&
                    originalModel.GetTypeInfo(originalElement.Expression, cancellationToken).Type is IArrayTypeSymbol originalArray &&
                    inferredModel.GetTypeInfo(inferredElement.Expression, cancellationToken).Type is IArrayTypeSymbol inferredArray &&
                    SymbolEqualityComparer.IncludeNullability.Equals(originalArray, inferredArray)) continue;
                if (originalSymbol == null || inferredSymbol == null ||
                    !SymbolEqualityComparer.IncludeNullability.Equals(originalSymbol, inferredSymbol)) return false;
            }
            return true;
        }

        private static IEnumerable<SyntaxNode> BindingNodes(SyntaxNode node) =>
            node.DescendantNodesAndSelf().Where(n => n is InvocationExpressionSyntax ||
                n is BaseObjectCreationExpressionSyntax || n is ElementAccessExpressionSyntax);

        internal static bool SameDeconstruction(DeconstructionInfo original, DeconstructionInfo inferred)
        {
            if (!SymbolEqualityComparer.IncludeNullability.Equals(original.Method, inferred.Method) ||
                original.Nested.Length != inferred.Nested.Length) return false;
            if (original.Conversion.HasValue != inferred.Conversion.HasValue) return false;
            if (original.Conversion.HasValue && !original.Conversion.Value.Equals(inferred.Conversion.Value)) return false;
            for (var i = 0; i < original.Nested.Length; i++)
                if (!SameDeconstruction(original.Nested[i], inferred.Nested[i])) return false;
            return true;
        }

        internal static bool SameEnumeration(ForEachStatementInfo original, ForEachStatementInfo inferred) =>
            SymbolEqualityComparer.IncludeNullability.Equals(original.ElementType, inferred.ElementType) &&
            SymbolEqualityComparer.Default.Equals(original.GetEnumeratorMethod, inferred.GetEnumeratorMethod) &&
            SymbolEqualityComparer.Default.Equals(original.MoveNextMethod, inferred.MoveNextMethod) &&
            SymbolEqualityComparer.Default.Equals(original.CurrentProperty, inferred.CurrentProperty) &&
            SymbolEqualityComparer.Default.Equals(original.DisposeMethod, inferred.DisposeMethod) &&
            original.ElementConversion.Equals(inferred.ElementConversion) &&
            original.CurrentConversion.Equals(inferred.CurrentConversion) &&
            original.IsAsynchronous == inferred.IsAsynchronous;

        internal static DeclarationExpressionSyntax CreateInferredDeconstruction(ExpressionSyntax expression)
        {
            var designation = CreateDesignation(expression);
            return designation == null ? null :
                SyntaxFactory.DeclarationExpression(SyntaxFactory.IdentifierName("var"), designation)
                    .WithTriviaFrom(expression);
        }

        private static VariableDesignationSyntax CreateDesignation(ExpressionSyntax expression)
        {
            if (expression is DeclarationExpressionSyntax declaration &&
                (declaration.Designation is SingleVariableDesignationSyntax ||
                 declaration.Designation is DiscardDesignationSyntax))
            {
                // Comments attached to an explicit element type must not disappear.
                var comments = declaration.Type.DescendantTrivia()
                    .Where(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia));
                return declaration.Designation.WithLeadingTrivia(
                    declaration.GetLeadingTrivia().AddRange(comments).AddRange(declaration.Designation.GetLeadingTrivia()));
            }
            if (!(expression is TupleExpressionSyntax tuple)) return null;
            var designations = new List<VariableDesignationSyntax>();
            foreach (var argument in tuple.Arguments)
            {
                var designation = CreateDesignation(argument.Expression);
                if (designation == null) return null;
                designations.Add(designation);
            }
            return SyntaxFactory.ParenthesizedVariableDesignation(tuple.OpenParenToken,
                SyntaxFactory.SeparatedList(designations, tuple.Arguments.GetSeparators()), tuple.CloseParenToken);
        }
    }
}
