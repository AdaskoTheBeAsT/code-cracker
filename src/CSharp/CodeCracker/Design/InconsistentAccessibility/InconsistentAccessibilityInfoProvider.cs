using Microsoft.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCracker.CSharp.Design.InconsistentAccessibility
{
    /// <summary>
    /// Defines how to locate a type whose accessibility must change to resolve a compiler diagnostic.
    /// </summary>
    public interface InconsistentAccessibilityInfoProvider
    {
        /// <summary>
        /// Locates the type and accessibility modifiers needed to resolve an inconsistent-accessibility diagnostic.
        /// </summary>
        /// <param name="document">The document containing the target syntax.</param>
        /// <param name="diagnostic">The diagnostic identifying the target declaration or expression.</param>
        /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
        /// <returns>A task returning the proposed accessibility change; the type is null when no matching declaration is found.</returns>
        Task<InconsistentAccessibilityInfo> GetInconsistentAccessibilityInfoAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken);
    }
}
