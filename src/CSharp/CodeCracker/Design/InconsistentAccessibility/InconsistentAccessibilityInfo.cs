using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeCracker.CSharp.Design.InconsistentAccessibility
{
    /// <summary>
    /// Describes a type accessibility change and the corresponding code action.
    /// </summary>
    public class InconsistentAccessibilityInfo
    {
        /// <summary>
        /// Gets or sets the description displayed for the accessibility-changing code action.
        /// </summary>
        public string CodeActionMessage { get; set; }
        /// <summary>
        /// Gets or sets the referenced type syntax whose declaration needs different accessibility.
        /// </summary>
        public TypeSyntax TypeToChangeAccessibility { get; set; }
        /// <summary>
        /// Gets or sets the accessibility modifier tokens to apply to the type declaration.
        /// </summary>
        public SyntaxTokenList NewAccessibilityModifiers { get; set; }

        /// <summary>
        /// Checks whether an inaccessible type was located for the code action.
        /// </summary>
        /// <returns>True if a type to change has been recorded; otherwise, false.</returns>
        public bool TypeToChangeFound()
        {
            return TypeToChangeAccessibility != null;
        }
    }
}
