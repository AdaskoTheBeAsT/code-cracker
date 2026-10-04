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
    /// Provides fixes to remove empty object initializers.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EmptyObjectInitializerCodeFixProvider)), Shared]
    public class EmptyObjectInitializerCodeFixProvider : CodeFixProvider
    {
        /// <inheritdoc/>
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(DiagnosticId.EmptyObjectInitializer.ToDiagnosticId());

        /// <inheritdoc/>
        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        /// <inheritdoc/>
        public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();
            context.RegisterCodeFix(CodeAction.Create("Remove empty object initializer", ct => RemoveAsync(context.Document, diagnostic, ct), nameof(EmptyObjectInitializerCodeFixProvider )), diagnostic);
            return Task.FromResult(0);
        }

        /// <summary>
        /// Removes the empty object initializer identified by the diagnostic.
        /// </summary>
        /// <param name="document">The document containing the target syntax.</param>
        /// <param name="diagnostic">The diagnostic identifying the target declaration or expression.</param>
        /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
        /// <returns>A task returning the document without the empty initializer.</returns>
        public static async Task<Document> RemoveAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var oldDeclaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<ObjectCreationExpressionSyntax>().First();
            var newDeclaration = oldDeclaration.WithInitializer(null).WithoutTrailingTrivia();
            if (newDeclaration.ArgumentList == null)
                newDeclaration = newDeclaration.WithoutTrailingTrivia().WithArgumentList(SyntaxFactory.ArgumentList());
            root = root.ReplaceNode(oldDeclaration, newDeclaration);
            var newDocument = document.WithSyntaxRoot(root);
            return newDocument;
        }
    }
}