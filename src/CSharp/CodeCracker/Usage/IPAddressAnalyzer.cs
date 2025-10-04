using System;
using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using CodeCracker.CSharp.Usage.MethodAnalyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeCracker.CSharp.Usage
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class IPAddressAnalyzer : DiagnosticAnalyzer
    {
        internal const string Title = "Your IP Address syntax is incorrect.";
        internal const string MessageFormat = "{0}";
        internal const string Category = SupportedCategories.Usage;

        private const string Description =
            "An error was found parsing the IP Address string.";

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId.IPAddress.ToDiagnosticId(),
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.IPAddress));

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context) => context.RegisterSyntaxNodeAction(Analyzer, SyntaxKind.InvocationExpression);

        private static void Analyzer(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;

            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation.ArgumentList?.Arguments.Count != 1) return;

            // Try semantic binding first
            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation.Expression);
            var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

            var isIPAddressParse = methodSymbol != null
                && methodSymbol.Name == "Parse"
                && methodSymbol.ContainingType != null
                && methodSymbol.ContainingType.ToDisplayString() == "System.Net.IPAddress"
                && methodSymbol.Parameters.Length == 1
                && methodSymbol.Parameters[0].Type.SpecialType == SpecialType.System_String;

            // If semantic model failed to bind (methodSymbol == null) we do a syntactic fallback so tests still work
            if (!isIPAddressParse && methodSymbol == null)
                isIPAddressParse = IsLikelyIPAddressParseSyntax(invocation.Expression);

            if (!isIPAddressParse) return;

            var argLiteral = invocation.ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax;
            if (argLiteral == null || !argLiteral.IsKind(SyntaxKind.StringLiteralExpression)) return;

            var ipText = argLiteral.Token.ValueText;

            // Valid address? then no diagnostic
            if (IPAddress.TryParse(ipText, out _)) return;

            // Need the actual framework error message (tests compare it)
            string message;
            try
            {
                _ = IPAddress.Parse(ipText); // should throw
                return;                       // defensive
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }

            // Report at the literal so column matches expected test (quote position)
            context.ReportDiagnostic(Diagnostic.Create(Rule, argLiteral.GetLocation(), message));
        }

        private static bool IsLikelyIPAddressParseSyntax(ExpressionSyntax expr)
        {
            // Match:
            //   System.Net.IPAddress.Parse("x")
            //   IPAddress.Parse("x")
            // Using only syntax when semantic binding failed (likely missing reference in test harness)
            if (expr is MemberAccessExpressionSyntax ma)
            {
                if (ma.Name.Identifier.Text != "Parse") return false;

                // Fully qualified chain
                var leftText = ma.Expression.ToString();
                if (leftText == "System.Net.IPAddress" || leftText == "IPAddress")
                    return true;

                // Handle possible global:: prefix
                if (leftText == "global::System.Net.IPAddress")
                    return true;
            }
            return false;
        }
    }
}
