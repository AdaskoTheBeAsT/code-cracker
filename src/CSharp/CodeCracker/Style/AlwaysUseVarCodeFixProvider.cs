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

namespace CodeCracker.CSharp.Style
{

    /// <summary>
    /// Provides fixes to replace eligible explicit variable types with var while preserving declaration modifiers.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AlwaysUseVarCodeFixProvider)), Shared]
    public class AlwaysUseVarCodeFixProvider : CodeFixProvider
    {
        /// <inheritdoc/>
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(DiagnosticId.AlwaysUseVar.ToDiagnosticId(), DiagnosticId.AlwaysUseVarOnPrimitives.ToDiagnosticId());

        /// <inheritdoc/>
        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        /// <inheritdoc/>
        public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();
            context.RegisterCodeFix(CodeAction.Create("Use 'var'", c => UseVarAsync(context.Document, diagnostic, c), nameof(AlwaysUseVarCodeFixProvider)), diagnostic);
            return Task.FromResult(0);
        }

        private async static Task<Document> UseVarAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var node = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);
            var declarationExpression = node.FirstAncestorOrSelf<DeclarationExpressionSyntax>();
            if (declarationExpression != null &&
                !(declarationExpression.Parent is ArgumentSyntax argument && argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)))
            {
                var assignment = declarationExpression.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
                var forEach = declarationExpression.FirstAncestorOrSelf<ForEachVariableStatementSyntax>();
                var original = assignment?.Left ?? forEach?.Variable;
                var replacement = AlwaysUseVarSemanticHelper.CreateInferredDeconstruction(original);
                if (replacement == null) return document;
                return document.WithSyntaxRoot(root.ReplaceNode(original,
                    replacement.WithAdditionalAnnotations(Formatter.Annotation)));
            }
            var localDeclaration = node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
            if (localDeclaration == null || !localDeclaration.Declaration.Type.Span.Contains(diagnosticSpan))
            {
                return document.WithSyntaxRoot(root.ReplaceNode(node,
                    SyntaxFactory.IdentifierName("var").WithTriviaFrom(node).WithAdditionalAnnotations(Formatter.Annotation)));
            }
            var variableDeclaration = localDeclaration.Declaration;
            var numVariables = variableDeclaration.Variables.Count;

            var newVariableDeclarations = new VariableDeclarationSyntax[numVariables];

            var originalTypeSyntax = variableDeclaration.Type;
            var typeToReplace = AlwaysUseVarAnalyzer.GetTypeToReplace(originalTypeSyntax);
            var varSyntax = originalTypeSyntax.ReplaceNode(typeToReplace,
                SyntaxFactory.IdentifierName("var"));

            //Create a new var declaration for each variable.
            for (var i = 0; i < numVariables; i++)
            {
                var originalVariable = variableDeclaration.Variables[i];
                var newLeadingTrivia = originalVariable.GetLeadingTrivia();

                //Get the trivia from the separator as well
                if (i != 0)
                {
                    newLeadingTrivia = newLeadingTrivia.InsertRange(0,
                        variableDeclaration.Variables.GetSeparator(i-1).GetAllTrivia());
                }

                newVariableDeclarations[i] = SyntaxFactory.VariableDeclaration(varSyntax)
                    .AddVariables(originalVariable.WithLeadingTrivia(newLeadingTrivia));
            }

            //ensure trivia for leading type node is preserved
            newVariableDeclarations[0] = newVariableDeclarations[0]
                .WithType(originalTypeSyntax.ReplaceNode(typeToReplace,
                    SyntaxFactory.IdentifierName("var").WithSameTriviaAs(typeToReplace)));

            var newLocalDeclarationStatements = newVariableDeclarations
                .Select(declaration => SyntaxFactory.LocalDeclarationStatement(declaration)
                    .WithAwaitKeyword(localDeclaration.AwaitKeyword)
                    .WithUsingKeyword(localDeclaration.UsingKeyword)
                    .WithModifiers(localDeclaration.Modifiers)
                    .WithAttributeLists(localDeclaration.AttributeLists)).ToList();


            //Preserve the trivia for the entire statement at the start and at the end
            newLocalDeclarationStatements[0] =
                newLocalDeclarationStatements[0].WithLeadingTrivia(localDeclaration.GetLeadingTrivia());
            var lastIndex = numVariables -1;
            newLocalDeclarationStatements[lastIndex] =
                newLocalDeclarationStatements[lastIndex].WithTrailingTrivia(localDeclaration.GetTrailingTrivia());

            var newRoot = newLocalDeclarationStatements.Count == 1
                ? root.ReplaceNode(localDeclaration, newLocalDeclarationStatements[0])
                : localDeclaration.Parent is GlobalStatementSyntax globalStatement
                    ? root.ReplaceNode(globalStatement, newLocalDeclarationStatements.Select(globalStatement.WithStatement))
                    : root.ReplaceNode(localDeclaration, newLocalDeclarationStatements);
            var newDocument = document.WithSyntaxRoot(newRoot);
            return newDocument;
        }
    }
}