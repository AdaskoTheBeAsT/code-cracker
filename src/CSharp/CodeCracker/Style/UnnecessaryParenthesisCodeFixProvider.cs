using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCracker.CSharp.Style
{
    /// <summary>
    /// Provides fixes to remove unnecessary empty constructor parentheses from object initializers.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnnecessaryParenthesisCodeFixProvider)), Shared]
    public class UnnecessaryParenthesisCodeFixProvider : CodeFixProvider
    {
        /// <inheritdoc/>
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(DiagnosticId.UnnecessaryParenthesis.ToDiagnosticId());

        /// <inheritdoc/>
        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        /// <inheritdoc/>
        public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();
            context.RegisterCodeFix(CodeAction.Create("Remove unnecessary parenthesis", ct => RemoveParenthesisAsync(context.Document, diagnostic, ct), nameof(UnnecessaryParenthesisCodeFixProvider)), diagnostic);
            return Task.FromResult(0);
        }
        private static async Task<Document> RemoveParenthesisAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var argumentList = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<ArgumentListSyntax>().First();
            var newRoot = root.RemoveNode(argumentList, SyntaxRemoveOptions.KeepTrailingTrivia);
            var newDocument = document.WithSyntaxRoot(newRoot);
            return newDocument;
        }
    }
}