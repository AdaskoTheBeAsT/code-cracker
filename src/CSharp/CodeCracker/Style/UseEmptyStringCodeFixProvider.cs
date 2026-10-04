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

namespace CodeCracker.CSharp.Style
{
    /// <summary>
    /// Provides fixes to replace string.Empty references with empty string literals.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseEmptyStringCodeFixProvider)), Shared]
    public class UseEmptyStringCodeFixProvider : CodeFixProvider
    {
        private const string EmptyString = "\"\"";
        /// <summary>
        /// The code action title for replacing string.Empty with an empty string literal.
        /// </summary>
        public const string MessageFormat = "Use " + EmptyString;

        /// <inheritdoc/>
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId.UseEmptyString.ToDiagnosticId());

        /// <inheritdoc/>
        public sealed override FixAllProvider GetFixAllProvider() => UseEmptyStringCodeFixAllProvider.Instance;

        /// <inheritdoc/>
        public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();
            context.RegisterCodeFix(CodeAction.Create(MessageFormat, c => UseEmptyStringAsync(context.Document, diagnostic, c), nameof(UseEmptyStringCodeFixProvider)), diagnostic);
            return Task.FromResult(0);
        }

        private async static Task<Document> UseEmptyStringAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var literal = root.FindNode(diagnostic.Location.SourceSpan).DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().First();
            var newRoot = root.ReplaceNode(literal, SyntaxFactory.ParseExpression(EmptyString).WithLeadingTrivia(literal.GetLeadingTrivia()).WithTrailingTrivia(literal.GetTrailingTrivia()));
            var newDocument = document.WithSyntaxRoot(newRoot);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}