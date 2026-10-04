using CodeCracker.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCracker.CSharp.Style
{
    /// <summary>
    /// Provides fixes to replace eligible string.Format calls with interpolated strings.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StringFormatCodeFixProvider)), Shared]
    public class StringFormatCodeFixProvider : CodeFixProvider
    {
        /// <inheritdoc/>
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(DiagnosticId.StringFormat.ToDiagnosticId());

        /// <inheritdoc/>
        public sealed override FixAllProvider GetFixAllProvider() => StringFormatFixAllProvider.Instance;

        /// <inheritdoc/>
        public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();
            context.RegisterCodeFix(CodeAction.Create(Resources.StringFormatCodeFixProvider_Title, c => MakeStringInterpolationAsync(context.Document, diagnostic, c), nameof(StringFormatCodeFixProvider)), diagnostic);
            return Task.FromResult(0);
        }

        private async static Task<Document> MakeStringInterpolationAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var invocationExpression = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().First();
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var newRoot = CreateNewStringInterpolation(root, invocationExpression);
            var newDocument = document.WithSyntaxRoot(newRoot);
            return newDocument;
        }

        /// <summary>
        /// Replaces a composite-format invocation in a syntax root with an interpolated string.
        /// </summary>
        /// <param name="root">The syntax root containing the target declaration or expression.</param>
        /// <param name="invocationExpression">The composite-format invocation with a literal format string and individually supplied arguments.</param>
        /// <returns>The syntax root with the invocation replaced.</returns>
        public static SyntaxNode CreateNewStringInterpolation(SyntaxNode root, InvocationExpressionSyntax invocationExpression)
        {
            var newStringInterpolation = CreateNewStringInterpolation(invocationExpression);
            var newRoot = root.ReplaceNode(invocationExpression, newStringInterpolation);
            return newRoot;
        }

        /// <summary>
        /// Builds an interpolated string from a composite-format invocation with a literal format and individually supplied arguments.
        /// </summary>
        /// <param name="invocationExpression">The composite-format invocation with a literal format string and individually supplied arguments.</param>
        /// <returns>The interpolated string expression.</returns>
        public static InterpolatedStringExpressionSyntax CreateNewStringInterpolation(InvocationExpressionSyntax invocationExpression)
        {
            var argumentList = invocationExpression.ArgumentList;
            var arguments = argumentList.Arguments;
            var formatLiteral = (LiteralExpressionSyntax)arguments[0].Expression;
            var analyzingInterpolation = (InterpolatedStringExpressionSyntax)SyntaxFactory.ParseExpression($"${formatLiteral.Token.Text}");
            var interpolationArgs = arguments.Skip(1).ToArray();
            var expressionsToReplace = new Dictionary<ExpressionSyntax, ExpressionSyntax>();
            foreach (var interpolation in analyzingInterpolation.Contents.OfType<InterpolationSyntax>())
            {
                var index = (int)((LiteralExpressionSyntax)interpolation.Expression).Token.Value;
                var expression = interpolationArgs[index].Expression;
                expression = expression.WithoutAllTrivia();
                var conditional = expression as ConditionalExpressionSyntax;
                if (conditional != null) expression = SyntaxFactory.ParenthesizedExpression(expression);
                expressionsToReplace.Add(interpolation.Expression, expression);
            }
            var newStringInterpolation = analyzingInterpolation.ReplaceNodes(expressionsToReplace.Keys, (o, _) => expressionsToReplace[o]);
            return newStringInterpolation;
        }
    }
}