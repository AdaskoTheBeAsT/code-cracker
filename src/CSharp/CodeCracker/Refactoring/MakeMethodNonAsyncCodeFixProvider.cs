using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeCracker.CSharp.Design.InconsistentAccessibility;
using CodeCracker.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace CodeCracker.CSharp.Refactoring
{
    /// <summary>
    /// DEPRECATED: This code fix provider is deprecated and will be removed in a future version.
    /// 
    /// Reason for deprecation:
    /// - The C# compiler already issues warning CS1998 for async methods without await operators
    /// - Modern IDEs (Visual Studio, Rider, VS Code) provide built-in quick-fixes for CS1998
    /// - The pattern of using async without await is increasingly rare in modern C# codebases
    /// - Best practice is to not use the async modifier if there are no await expressions
    /// 
    /// Recommended alternatives:
    /// - Use the built-in IDE quick-fixes for CS1998 warnings
    /// - Manually remove the async keyword and return Task/Task&lt;T&gt; directly
    /// - For simple cases, use Task.FromResult() or return the Task directly (task eliding)
    /// 
    /// Example modern patterns:
    /// <code>
    /// // Instead of: async Task&lt;int&gt; GetValue() { return 42; }
    /// // Use: Task&lt;int&gt; GetValue() => Task.FromResult(42);
    /// 
    /// // Instead of: async Task&lt;Data&gt; GetData() { return await repository.GetAsync(); }
    /// // Use: Task&lt;Data&gt; GetData() => repository.GetAsync();  // Task eliding
    /// </code>
    /// </summary>
    [Obsolete("This code fix provider is deprecated. Modern IDEs provide built-in quick-fixes for CS1998. " +
              "Use IDE quick-fixes or manually remove async keyword when there are no await expressions. " +
              "This will be removed in a future version.", false)]
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakeMethodNonAsyncCodeFixProvider)), Shared]
    class MakeMethodNonAsyncCodeFixProvider : CodeFixProvider
    {
        internal const string AsyncMethodLacksAwaitCompilerWarningNumber = "CS1998";

        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(AsyncMethodLacksAwaitCompilerWarningNumber);

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();
            context.RegisterCodeFix(
                CodeAction.Create(
                    Resources.MakeMethodNonAsyncCodeFixProvider_Title,
                    ct => MakeMethodNonAsyncAsync(context.Document, diagnostic, ct)),
                diagnostic);
            return Task.FromResult(0);
        }

        private async static Task<Document> MakeMethodNonAsyncAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var methodDeclaration = node.FirstAncestorOrSelfOfType<MethodDeclarationSyntax>();
            var rewriter = new ReturnTaskFromResultRewriter();
            var asyncKeyword = methodDeclaration.Modifiers.First(t => t.IsKind(SyntaxKind.AsyncKeyword));
            var newMethodDeclaration =
                methodDeclaration
                    .WithModifiers(methodDeclaration.Modifiers.Remove(asyncKeyword))
                    .WithBody((BlockSyntax) rewriter.VisitBlock(methodDeclaration.Body));
            var newRoot = root.ReplaceNode(methodDeclaration, newMethodDeclaration);
            return document.WithSyntaxRoot(newRoot);
        }

        private class ReturnTaskFromResultRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
            {

                var newNode = node.WithExpression(
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParseName("System.Threading.Tasks.Task")
                                .WithAdditionalAnnotations(Simplifier.Annotation),
                            SyntaxFactory.IdentifierName("FromResult")),
                        SyntaxFactory.ArgumentList().AddArguments(SyntaxFactory.Argument(node.Expression))));
                return base.VisitReturnStatement(newNode);
            }
        }
    }
}
