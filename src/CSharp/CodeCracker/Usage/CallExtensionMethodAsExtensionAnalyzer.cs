using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeCracker.CSharp.Usage
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CallExtensionMethodAsExtensionAnalyzer : DiagnosticAnalyzer
    {
        internal const string Title = "Call Extension Method As Extension";
        internal const string MessageFormat = "Do not call '{0}' method of class '{1}' as a static method";
        internal const string Category = SupportedCategories.Usage;

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId.CallExtensionMethodAsExtension.ToDiagnosticId(),
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.CallExtensionMethodAsExtension));

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            if (context.Node is not InvocationExpressionSyntax invocation) return;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

            var args = invocation.ArgumentList?.Arguments;
            if (args is null || args.Value.Count == 0) return;

            // Try direct symbol
            var nameInfo = context.SemanticModel.GetSymbolInfo(memberAccess.Name);
            var methodSymbol = nameInfo.Symbol as IMethodSymbol;

            // Fallback: resolve from container (helps before generic inference completes)
            if (methodSymbol == null)
            {
                if (context.SemanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is INamedTypeSymbol container)
                {
                    var name = memberAccess.Name.Identifier.Text;
                    var firstArgType = context.SemanticModel.GetTypeInfo(args.Value[0].Expression).ConvertedType;
                    var candidates = container
                        .GetMembers(name)
                        .OfType<IMethodSymbol>()
                        .Where(m => m.IsExtensionMethod && m.Parameters.Length == args.Value.Count)
                        .ToList();

                    foreach (var cand in candidates)
                    {
                        var thisParamType = cand.Parameters[0].Type;
                        if (firstArgType != null && ArgumentMatchesParameter(firstArgType, thisParamType, context.SemanticModel))
                        {
                            methodSymbol = cand;
                            break;
                        }
                    }
                }
            }

            if (methodSymbol == null) return;

            // Normalize to original (if somehow reduced)
            if (methodSymbol.ReducedFrom is IMethodSymbol original)
                methodSymbol = original;

            if (!methodSymbol.IsExtensionMethod) return;

            // Static form must have left side bound to the extension container type
            var leftSymbol = context.SemanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
            if (leftSymbol is not INamedTypeSymbol) return;

            // Static form passes 'this' explicitly: argument count == parameter count
            if (args.Value.Count != methodSymbol.Parameters.Length) return;

            // Skip dynamic
            if (args.Value.Any(a =>
            {
                var ti = context.SemanticModel.GetTypeInfo(a.Expression);
                return ti.Type?.TypeKind == TypeKind.Dynamic || ti.ConvertedType?.TypeKind == TypeKind.Dynamic;
            })) return;

            // Fast path: exact container + first arg compatible with 'this'
            if (SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType, leftSymbol) &&
                ArgumentMatchesParameter(
                    context.SemanticModel.GetTypeInfo(args.Value[0].Expression).ConvertedType,
                    methodSymbol.Parameters[0].Type,
                    context.SemanticModel))
            {
                Report(context, memberAccess, methodSymbol);
                return;
            }

            // Speculative safety: ensure rewriting to extension form still binds to same original method
            if (SpeculativeBindsSame(context.SemanticModel, invocation, memberAccess, methodSymbol))
            {
                Report(context, memberAccess, methodSymbol);
                return;
            }

            // Heuristic fallback: all param/arg pairs compatible & no competing ambiguous overload
            if (HeuristicEquivalent(context.SemanticModel, invocation, methodSymbol))
            {
                Report(context, memberAccess, methodSymbol);
            }
        }

        private static bool SpeculativeBindsSame(
            SemanticModel model,
            InvocationExpressionSyntax invocation,
            MemberAccessExpressionSyntax memberAccess,
            IMethodSymbol original)
        {
            var args = invocation.ArgumentList.Arguments;
            if (args.Count == 0) return false;

            var receiver = args[0].Expression;
            var remaining = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(args.Skip(1)));

            var extensionForm = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    ParenthesizeIfNeeded(receiver),
                    memberAccess.Name),
                remaining);

            var positions = new[]
            {
                invocation.FullSpan.Start,
                invocation.SpanStart,
                memberAccess.SpanStart
            };

            foreach (var pos in positions)
            {
                var spec = model.GetSpeculativeSymbolInfo(pos, extensionForm, SpeculativeBindingOption.BindAsExpression);
                if (spec.Symbol is IMethodSymbol reduced && reduced.ReducedFrom is IMethodSymbol orig &&
                    (SymbolEqualityComparer.Default.Equals(orig, original) ||
                     SymbolEqualityComparer.Default.Equals(orig.OriginalDefinition, original.OriginalDefinition)))
                    return true;
            }
            return false;
        }

        private static bool HeuristicEquivalent(
            SemanticModel model,
            InvocationExpressionSyntax invocation,
            IMethodSymbol method)
        {
            var args = invocation.ArgumentList.Arguments;
            if (args.Count != method.Parameters.Length) return false;

            // All arguments compatible
            for (int i = 0; i < args.Count; i++)
            {
                var argType = model.GetTypeInfo(args[i].Expression).ConvertedType;
                if (!ArgumentMatchesParameter(argType, method.Parameters[i].Type, model))
                    return false;
            }

            // No competing extension overload that also matches (avoid ambiguity)
            var container = method.ContainingType;
            if (container != null)
            {
                var competing = container
                    .GetMembers(method.Name)
                    .OfType<IMethodSymbol>()
                    .Where(m => m.IsExtensionMethod &&
                                !SymbolEqualityComparer.Default.Equals(m, method) &&
                                m.Parameters.Length == method.Parameters.Length)
                    .Any(m =>
                    {
                        for (int i = 0; i < m.Parameters.Length; i++)
                        {
                            var argType = model.GetTypeInfo(args[i].Expression).ConvertedType;
                            if (!ArgumentMatchesParameter(argType, m.Parameters[i].Type, model))
                                return false;
                        }
                        return true;
                    });

                if (competing) return false;
            }

            return true;
        }

        /// <summary>
        /// Central compatibility: identity, implicit conversion, array↔IEnumerable&lt;T&gt; (with uninferred T support),
        /// type parameters, error symbols, interface implementation of IEnumerable&lt;T&gt;.
        /// </summary>
        private static bool ArgumentMatchesParameter(ITypeSymbol? argType, ITypeSymbol paramType, SemanticModel model)
        {
            if (argType == null || paramType == null) return false;
            if (argType.Kind == SymbolKind.ErrorType || paramType.Kind == SymbolKind.ErrorType) return true;
            if (paramType is ITypeParameterSymbol) return true;
            if (SymbolEqualityComparer.Default.Equals(argType, paramType)) return true;

            // Direct array -> IEnumerable<T>
            if (argType is IArrayTypeSymbol array && paramType is INamedTypeSymbol pn &&
                pn.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                var elem = array.ElementType;
                var targ = pn.TypeArguments[0];
                if (targ is ITypeParameterSymbol ||
                    SymbolEqualityComparer.Default.Equals(elem, targ) ||
                    model.Compilation.ClassifyConversion(elem, targ).IsImplicit ||
                    model.Compilation.ClassifyConversion(targ, elem).IsImplicit)
                    return true;
            }

            // IEnumerable<T> arg -> array<T> param
            if (paramType is IArrayTypeSymbol paramArray && argType is INamedTypeSymbol an &&
                an.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                var elem = paramArray.ElementType;
                var targ = an.TypeArguments[0];
                if (targ is ITypeParameterSymbol ||
                    SymbolEqualityComparer.Default.Equals(elem, targ) ||
                    model.Compilation.ClassifyConversion(targ, elem).IsImplicit ||
                    model.Compilation.ClassifyConversion(elem, targ).IsImplicit)
                    return true;
            }

            // Normal implicit conversion
            if (model.Compilation.ClassifyConversion(argType, paramType).IsImplicit) return true;

            // Param is IEnumerable<T>, arg implements it
            if (paramType is INamedTypeSymbol pNamed &&
                pNamed.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T &&
                argType.AllInterfaces.Any(i =>
                    i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T &&
                    SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], pNamed.TypeArguments[0])))
                return true;

            return false;
        }

        private static void Report(
            SyntaxNodeAnalysisContext context,
            MemberAccessExpressionSyntax memberAccess,
            IMethodSymbol methodSymbol)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    memberAccess.GetLocation(),
                    methodSymbol.Name,
                    methodSymbol.ContainingType.Name));
        }

        private static ExpressionSyntax ParenthesizeIfNeeded(ExpressionSyntax expr) =>
            expr is IdentifierNameSyntax or MemberAccessExpressionSyntax or LiteralExpressionSyntax
                ? expr
                : SyntaxFactory.ParenthesizedExpression(expr);
    }
}