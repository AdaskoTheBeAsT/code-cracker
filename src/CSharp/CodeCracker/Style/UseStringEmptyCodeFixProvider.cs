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
    /// Provides fixes to replace empty string literals with string.Empty.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseStringEmptyCodeFixProvider)), Shared]
    public class UseStringEmptyCodeFixProvider : CodeFixProvider
    {
        /// <summary>
        /// The code action title for replacing an empty string literal with string.Empty.
        /// </summary>
        public const string MessageFormat = "Use 'string.Empty'";

        /// <inheritdoc/>
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId.UseStringEmpty.ToDiagnosticId());

        /// <inheritdoc/>
        public sealed override FixAllProvider GetFixAllProvider() => UseStringEmptyCodeFixAllProvider.Instance;

        /// <inheritdoc/>
        public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();
            context.RegisterCodeFix(CodeAction.Create(MessageFormat, c => UseStringEmptyAsync(context.Document, diagnostic, c), nameof(UseStringEmptyCodeFixProvider)), diagnostic);
            return Task.FromResult(0);
        }

        private async static Task<Document> UseStringEmptyAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var literal = root.FindNode(diagnostic.Location.SourceSpan).DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().First();
            var newRoot = root.ReplaceNode(literal, SyntaxFactory.ParseExpression("string.Empty").WithLeadingTrivia(literal.GetLeadingTrivia()).WithTrailingTrivia(literal.GetTrailingTrivia()));
            var newDocument = document.WithSyntaxRoot(newRoot);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}