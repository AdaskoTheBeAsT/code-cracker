using CodeCracker.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace CodeCracker.CSharp.Style
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class StringFormatAnalyzer : DiagnosticAnalyzer
    {
        internal const string Category = SupportedCategories.Style;
        internal static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.StringFormatAnalyzer_Title), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.StringFormatAnalyzer_MessageFormat), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.StringFormatAnalyzer_Description), Resources.ResourceManager, typeof(Resources));

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId.StringFormat.ToDiagnosticId(),
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.StringFormat));

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context) => context.RegisterSyntaxNodeAction(AnalyzeFormatInvocation, SyntaxKind.InvocationExpression);

        private static void AnalyzeFormatInvocation(SyntaxNodeAnalysisContext context) =>
            AnalyzeFormatInvocation(context, "Format", "string.Format(string, ", "string.Format(string, params object[])", Rule);

        public static void AnalyzeFormatInvocation(SyntaxNodeAnalysisContext context, string methodName, string methodOverloadSignature, string methodWithArraySignature, DiagnosticDescriptor rule)
        {
            if (context.IsGenerated()) return;

            var invocationExpression = (InvocationExpressionSyntax)context.Node;
            var memberExpression = invocationExpression.Expression as MemberAccessExpressionSyntax;
            if (memberExpression?.Name?.ToString() != methodName) return;

            var memberSymbol = context.SemanticModel.GetSymbolInfo(memberExpression).Symbol;
            if (memberSymbol == null) return;

            // Keep original string-signature guard (backwards compatibility), but fall back to robust symbol checks if it fails.
            bool looksLikeOriginalTarget = memberSymbol.ToString().StartsWith(methodOverloadSignature, StringComparison.Ordinal);
            var methodSymbol = memberSymbol as IMethodSymbol;
            if (!looksLikeOriginalTarget)
            {
                if (methodSymbol == null) return;
                // Must be string.Format(...)
                if (methodSymbol.Name != methodName) return;
                if (methodSymbol.ContainingType?.SpecialType != SpecialType.System_String) return;
                if (methodSymbol.Parameters.Length == 0 || methodSymbol.Parameters[0].Type.SpecialType != SpecialType.System_String) return;
            }

            var argumentList = invocationExpression.ArgumentList as ArgumentListSyntax;
            if (argumentList?.Arguments.Count < 2) return;

            // First argument must be a string literal
            if (!argumentList.Arguments[0].Expression.IsKind(SyntaxKind.StringLiteralExpression)) return;

            // Robust skip logic for params object[] single array argument
            // (Original code relied on exact ToString() equality with methodWithArraySignature and failed across frameworks.)
            if (methodSymbol != null)
            {
                // Case: string.Format(string, params object[]) and call is: string.Format("...", argsArray);
                if (argumentList.Arguments.Count == 2 &&
                    methodSymbol.Parameters.Length >= 2 &&
                    methodSymbol.Parameters.Last().IsParams &&
                    context.SemanticModel.GetTypeInfo(argumentList.Arguments[1].Expression).Type is IArrayTypeSymbol)
                {
                    return; // ignore – test expectation: no diagnostic
                }

                // Case: string.Format(IFormatProvider, string, params object[]) and call is: string.Format(provider, "...", argsArray);
                if (argumentList.Arguments.Count == 3 &&
                    methodSymbol.Parameters.Length >= 3 &&
                    methodSymbol.Parameters.Last().IsParams &&
                    methodSymbol.Parameters[1].Type.SpecialType == SpecialType.System_String &&
                    context.SemanticModel.GetTypeInfo(argumentList.Arguments[2].Expression).Type is IArrayTypeSymbol)
                {
                    return; // ignore array-packed args for provider overload
                }
            }
            else
            {
                // Fallback to old behavior if methodSymbol not available and original signature string matched
                if (memberSymbol.ToString() == methodWithArraySignature &&
                    argumentList.Arguments.Skip(1).Any(a => context.SemanticModel.GetTypeInfo(a.Expression).Type?.TypeKind == TypeKind.Array))
                    return;
            }

            var formatLiteral = (LiteralExpressionSyntax)argumentList.Arguments[0].Expression;
            var constValue = context.SemanticModel.GetConstantValue(formatLiteral);
            if (!constValue.HasValue || constValue.Value is not string format) return;

            // Build placeholder array with correct arg count (excluding format literal)
            var dummyArgs = Enumerable.Range(1, argumentList.Arguments.Count - 1).Select(_ => new object()).ToArray();
            try
            {
                string.Format(format, dummyArgs);
            }
            catch (FormatException)
            {
                // Invalid format string -> do not suggest interpolation
                return;
            }

            var diag = Diagnostic.Create(rule, invocationExpression.GetLocation());
            context.ReportDiagnostic(diag);
        }
    }
}
