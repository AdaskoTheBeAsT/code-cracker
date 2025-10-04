using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace CodeCracker.CSharp.Usage
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class StringFormatArgsAnalyzer : DiagnosticAnalyzer
    {
        internal const string Title = "Incorrect String.Format usage";
        internal const string IncorrectNumberOfArgsMessage = "The number of arguments in String.Format is incorrect.";
        internal const string InvalidArgsReferenceMessage = "Invalid argument reference in String.Format.";
        internal const string Category = SupportedCategories.Usage;
        const string Description = "The format argument in String.Format determines the number of other arguments that need to be "
            + "passed into the method based on the number of curly braces {} used. The incorrect number of arguments are being passed.";

        internal static readonly DiagnosticDescriptor ExtraArgs = new DiagnosticDescriptor(
            DiagnosticId.StringFormatArgs_ExtraArgs.ToDiagnosticId(),
            Title,
            IncorrectNumberOfArgsMessage,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.StringFormatArgs_ExtraArgs));

        internal static readonly DiagnosticDescriptor InvalidArgs = new DiagnosticDescriptor(
            DiagnosticId.StringFormatArgs_InvalidArgs.ToDiagnosticId(),
            Title,
            InvalidArgsReferenceMessage,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.StringFormatArgs_InvalidArgs));

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(ExtraArgs, InvalidArgs);

        public override void Initialize(AnalysisContext context) =>
            context.RegisterSyntaxNodeAction(Analyzer, SyntaxKind.InvocationExpression);

        private static void Analyzer(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;

            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
            if (memberAccess.Name?.Identifier.Text != "Format") return;

            var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) return;
            if (methodSymbol.Name != "Format") return;
            if (methodSymbol.ContainingType?.SpecialType != SpecialType.System_String) return;
            if (methodSymbol.Parameters.Length == 0) return;
            if (methodSymbol.Parameters[0].Type.SpecialType != SpecialType.System_String) return;

            if (invocation.ArgumentList is not ArgumentListSyntax argList) return;
            var arguments = argList.Arguments;
            if (arguments.Count == 0) return;

            // We only handle when the first argument is a literal string – skip otherwise (interpolated, const, etc.)
            if (!arguments[0].Expression.IsKind(SyntaxKind.StringLiteralExpression)) return;

            // Handle the params object[] case where user passes a single array variable or creation expression:
            // string.Format("This {0} is {1}", args);
            // Current old logic counted 'args' as just 1 argument and then invalidated {1}.
            // If the overload is params object[] AND there are exactly 2 arguments AND the second argument is an array, we skip diagnostics.
            if (methodSymbol.Parameters.Last().IsParams
                && arguments.Count == 2
                && context.SemanticModel.GetTypeInfo(arguments[1].Expression).Type is IArrayTypeSymbol)
            {
                // This is the failing test scenario; do not analyze further.
                return;
            }

            // Parse placeholders from format string
            var formatLiteral = (LiteralExpressionSyntax)arguments[0].Expression;
            // Use interpolated parse trick (same as original code)
            var parsedInterpolated = (InterpolatedStringExpressionSyntax)SyntaxFactory.ParseExpression($"${formatLiteral.Token.Text}");

            var allInterpolations = parsedInterpolated.Contents
                .Where(c => c.IsKind(SyntaxKind.Interpolation))
                .Cast<InterpolationSyntax>()
                .ToList();

            var distinctPlaceholders = allInterpolations
                .Select(i => i.Expression.ToString())
                .Distinct()
                .ToList();

            // Number of supplied value arguments (excluding format string)
            var suppliedArgCount = arguments.Count - 1;

            // If we have params object[] expanded normally (multiple arguments) this is fine;
            // If user passed an array explicitly we already returned above.
            if (distinctPlaceholders.Count < suppliedArgCount)
            {
                context.ReportDiagnostic(Diagnostic.Create(ExtraArgs, invocation.GetLocation()));
                return;
            }

            // Validate each placeholder is a valid integer index referencing supplied arguments.
            foreach (var placeholder in distinctPlaceholders)
            {
                if (!int.TryParse(placeholder, out var index) ||
                    index < 0 ||
                    index >= suppliedArgCount)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidArgs, invocation.GetLocation()));
                    return;
                }
            }
        }
    }
}
