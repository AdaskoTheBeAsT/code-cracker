using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCracker.CSharp.Usage
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CallExtensionMethodAsExtensionCodeFixProvider)), Shared]
    public class CallExtensionMethodAsExtensionCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId.CallExtensionMethodAsExtension.ToDiagnosticId());

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Use extension method syntax",
                    ct => ApplyFixAsync(context.Document, diagnostic, ct),
                    nameof(CallExtensionMethodAsExtensionCodeFixProvider)),
                diagnostic);
            return Task.CompletedTask;
        }

        private static async Task<Document> ApplyFixAsync(Document document, Diagnostic diagnostic, CancellationToken ct)
        {
            var root = (CompilationUnitSyntax)await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var token = root.FindToken(diagnostic.Location.SourceSpan.Start);

            // Climb to the invocation
            var invocation = token.Parent
                .AncestorsAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault();
            if (invocation == null) return document;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return document; // safety

            var args = invocation.ArgumentList?.Arguments;
            if (args == null || args.Value.Count == 0)
                return document; // nothing to rewrite

            // First argument becomes receiver
            var receiver = args.Value[0].Expression;
            var remainingArgs = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(args.Value.Skip(1)));

            var newInvocation = invocation
                .WithExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        ParenthesizeIfNeeded(receiver),
                        memberAccess.Name))
                .WithArgumentList(remainingArgs)
                .WithLeadingTrivia(invocation.GetLeadingTrivia())
                .WithTrailingTrivia(invocation.GetTrailingTrivia())
                .WithAdditionalAnnotations(Formatter.Annotation);

            var newRoot = root.ReplaceNode(invocation, newInvocation);

            // Ensure namespace is imported if static call used fully qualified System.Linq.Enumerable.*
            newRoot = AddUsingIfMissing(newRoot, memberAccess, document, ct);

            return document.WithSyntaxRoot(newRoot);
        }

        private static CompilationUnitSyntax AddUsingIfMissing(
            CompilationUnitSyntax root,
            MemberAccessExpressionSyntax memberAccess,
            Document document,
            CancellationToken ct)
        {
            // If expression is something like System.Linq.Enumerable we may need 'using System.Linq;'
            var fullExprText = memberAccess.Expression.ToString();

            // Heuristic: if fully-qualified starts with System.Linq.Enumerable OR ends with .Enumerable
            // and we do not already have using System.Linq; add it.
            if (fullExprText.Contains("System.Linq.Enumerable"))
            {
                var already = root.Usings.Any(u => u.Name.ToString() == "System.Linq");
                if (!already)
                {
                    root = root.AddUsings(
                        SyntaxFactory.UsingDirective(
                            SyntaxFactory.ParseName("System.Linq"))
                        .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed));
                }
            }
            return root;
        }

        private static ExpressionSyntax ParenthesizeIfNeeded(ExpressionSyntax expr) =>
            expr is IdentifierNameSyntax or MemberAccessExpressionSyntax or LiteralExpressionSyntax
                ? expr
                : SyntaxFactory.ParenthesizedExpression(expr);

    }
}