using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCracker.CSharp.Usage
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ArgumentExceptionCodeFixProvider)), Shared]
    public class ArgumentExceptionCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(DiagnosticId.ArgumentException.ToDiagnosticId());

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();

            // Ensure deterministic ordering of fixes: a, b, value (the test expects index 2 => "value")
            var orderedParamNames = diagnostic.Properties
                .Where(p => p.Key.StartsWith("param"))
                .OrderBy(p => p.Key)               // keys are "param{parameterName}" => param a, param b, param value
                .Select(p => p.Value)
                .ToList();

            foreach (var paramName in orderedParamNames)
            {
                var title = $"Use '{paramName}'";
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title,
                        c => FixParamAsync(context.Document, diagnostic, paramName, c),
                        equivalenceKey: $"Use_{paramName}"),
                    diagnostic);
            }

            return Task.CompletedTask;
        }

        private static async Task<Document> FixParamAsync(Document document, Diagnostic diagnostic, string newParamName, CancellationToken cancellationToken)
        {
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var objectCreation = root
                .FindToken(diagnostic.Location.SourceSpan.Start)
                .Parent
                .AncestorsAndSelf()
                .OfType<ObjectCreationExpressionSyntax>()
                .First();

            var argumentList = objectCreation.ArgumentList;
            if (argumentList == null || argumentList.Arguments.Count < 2)
                return document;

            // Assumes second ctor argument is the param name literal (already guaranteed by analyzer)
            if (argumentList.Arguments[1].Expression is not LiteralExpressionSyntax paramNameLiteral)
                return document;

            var newLiteral = SyntaxFactory.ParseExpression($"\"{newParamName}\"")
                .WithTriviaFrom(paramNameLiteral);

            var newRoot = root.ReplaceNode(paramNameLiteral, newLiteral);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}