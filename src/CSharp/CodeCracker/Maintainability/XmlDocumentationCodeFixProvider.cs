using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCracker.CSharp.Maintainability
{
    /// <summary>
    /// Provides shared helpers for reconciling method parameters with XML parameter documentation.
    /// </summary>
    public abstract class XmlDocumentationCodeFixProvider : CodeFixProvider
    {
        /// <summary>
        /// Reconciles XML parameter entries with the method's parameters using the derived provider's repair strategy.
        /// </summary>
        /// <param name="method">The method whose parameter documentation is being reconciled.</param>
        /// <param name="root">The syntax root containing the target declaration or expression.</param>
        /// <returns>The syntax root with the method's parameter documentation repaired.</returns>
        public abstract SyntaxNode FixParameters(MethodDeclarationSyntax method, SyntaxNode root);

        /// <summary>
        /// Locates the diagnosed method and applies the provider's XML parameter documentation repair.
        /// </summary>
        /// <param name="document">The document containing the target syntax.</param>
        /// <param name="diagnostic">The diagnostic identifying the target declaration or expression.</param>
        /// <param name="c">The token used to cancel the asynchronous operation.</param>
        /// <returns>A task returning the document with repaired parameter documentation.</returns>
        protected async Task<Document> FixParametersAsync(Document document, Diagnostic diagnostic, CancellationToken c)
        {
            var root = await document.GetSyntaxRootAsync(c).ConfigureAwait(false);
            var documentationNode = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().First();
            var newRoot = FixParameters(documentationNode, root);
            var newDocument = document.WithSyntaxRoot(newRoot);
            return newDocument;
        }

        /// <summary>
        /// Pairs method parameters with XML parameter entries by name, including names present on only one side.
        /// </summary>
        /// <param name="method">The method whose parameter documentation is being reconciled.</param>
        /// <param name="documentationNode">The method's XML documentation comment.</param>
        /// <returns>Pairs containing a parameter and its XML element and name attribute; either side is null when absent.</returns>
        protected static IEnumerable<Tuple<ParameterSyntax, Tuple<XmlElementSyntax, XmlNameAttributeSyntax>>> GetMethodParametersWithDocParameters(MethodDeclarationSyntax method, DocumentationCommentTriviaSyntax documentationNode)
        {
            var methodParameters = method.ParameterList.Parameters;

            var xElementsWitAttrs = documentationNode.Content.OfType<XmlElementSyntax>()
                                    .Where(xEle => xEle.StartTag.Name.LocalName.ValueText == "param")
                                    .SelectMany(xEle => xEle.StartTag.Attributes, (xEle, attr) => new Tuple<XmlElementSyntax, XmlNameAttributeSyntax>(xEle, (XmlNameAttributeSyntax)attr));

            var keys = methodParameters.Select(parameter => parameter.Identifier.ValueText)
                .Union(xElementsWitAttrs.Select(x => x.Item2.Identifier.Identifier.ValueText))
                .ToImmutableList();

            return (from key in keys
                    let Parameter = methodParameters.FirstOrDefault(p => p.Identifier.ValueText == key)
                    let DocParameter = xElementsWitAttrs.FirstOrDefault(p => p.Item2.Identifier.Identifier.ValueText == key)
                    select new Tuple<ParameterSyntax, Tuple<XmlElementSyntax, XmlNameAttributeSyntax>>(Parameter, DocParameter));
        }
    }
}
