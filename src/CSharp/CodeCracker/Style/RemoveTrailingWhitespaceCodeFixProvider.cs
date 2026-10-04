using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCracker.CSharp.Style
{
    /// <summary>
    /// Provides fixes to remove trailing whitespace.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveTrailingWhitespaceCodeFixProvider)), Shared]
    public class RemoveTrailingWhitespaceCodeFixProvider : CodeFixProvider
    {
        /// <inheritdoc/>
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId.RemoveTrailingWhitespace.ToDiagnosticId());

        /// <inheritdoc/>
        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        /// <inheritdoc/>
        public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();
            context.RegisterCodeFix(CodeAction.Create("Remove trailing whitespace", ct => RemoveTrailingWhiteSpaceAsync(context.Document, diagnostic, ct), nameof(RemoveTrailingWhitespaceCodeFixProvider)), diagnostic);
            return Task.FromResult(0);
        }

        private static async Task<Document> RemoveTrailingWhiteSpaceAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var trivia = root.FindTrivia(diagnostic.Location.SourceSpan.End - 1);
            SyntaxNode newRoot;
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                newRoot = root.ReplaceTrivia(trivia, new SyntaxTrivia[] { });
            }
            else if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia, SyntaxKind.MultiLineDocumentationCommentTrivia, SyntaxKind.PragmaWarningDirectiveTrivia))
            {
                var commentText = trivia.ToFullString();
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
                var commentLines = commentText.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
#pragma warning restore RS1035 // Do not use APIs banned for analyzers
                var newComment = "";
                var builder = new System.Text.StringBuilder();
                builder.Append(newComment);
                for (int i = 0; i < commentLines.Length; i++)
                {
                    var commentLine = commentLines[i];
                    builder.Append(Regex.Replace(commentLine, @"\s+$", ""));
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
                    if (i < commentLines.Length - 1) builder.Append(Environment.NewLine);
#pragma warning restore RS1035 // Do not use APIs banned for analyzers
                }
                newComment = builder.ToString();
                newRoot = root.ReplaceTrivia(trivia, SyntaxFactory.SyntaxTrivia(SyntaxKind.DocumentationCommentExteriorTrivia, newComment));
            }
            else
            {
                var triviaNoTrailingWhiteSpace = Regex.Replace(trivia.ToFullString(), @"\s+$", "");
                newRoot = root.ReplaceTrivia(trivia, SyntaxFactory.ParseTrailingTrivia(triviaNoTrailingWhiteSpace));
            }
            return document.WithSyntaxRoot(newRoot);
        }
    }
}