using CodeCracker.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace CodeCracker.CSharp.Refactoring
{
    /// <summary>
    /// Reports PropertyChangedEventArgs allocations that can be cached.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PropertyChangedEventArgsUnnecessaryAllocationAnalyzer : DiagnosticAnalyzer
    {
        private const string PropertyChangedEventArgsClassName = "PropertyChangedEventArgs";

        internal const string Category = SupportedCategories.Refactoring;

        private static readonly IdentifierExtractor ExtractIdentifier = new IdentifierExtractor();
        private static readonly IsArgumentALiteralOrNameof IsAnyArgumentLiteralOrNameof = new IsArgumentALiteralOrNameof();

        internal static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.PropertyChangedEventArgsUnnecessaryAllocation_Title), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.PropertyChangedEventArgsUnnecessaryAllocation_Description), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.PropertyChangedEventArgsUnnecessaryAllocation_MessageFormat), Resources.ResourceManager, typeof(Resources));

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId.PropertyChangedEventArgsUnnecessaryAllocation.ToDiagnosticId(),
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.PropertyChangedEventArgsUnnecessaryAllocation));

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(LanguageVersion.CSharp6, PropertyChangedCreation, SyntaxKind.ObjectCreationExpression);
        }

        private static void PropertyChangedCreation(SyntaxNodeAnalysisContext context)
        {
            var propertyChangedEventArgsCreationExpr = (ObjectCreationExpressionSyntax)context.Node;
            var identifier = propertyChangedEventArgsCreationExpr.Type.Accept(ExtractIdentifier);
            if (ShouldReportDiagnostic(propertyChangedEventArgsCreationExpr, identifier.ValueText))
            {
                var data = new PropertyChangedEventArgsAnalyzerData(propertyChangedEventArgsCreationExpr);

                context.ReportDiagnostic(Diagnostic.Create(Rule, propertyChangedEventArgsCreationExpr.GetLocation(), data.ToDiagnosticProperties()));
            }
        }

        private static bool ShouldReportDiagnostic(ObjectCreationExpressionSyntax propertyChangedExpr, string identifierName) =>
                IsPropertyChangedEventArgs(identifierName)
                && propertyChangedExpr.ArgumentList.Accept(IsAnyArgumentLiteralOrNameof)
                && !IsAlreadyStatic(propertyChangedExpr);

        private static bool IsPropertyChangedEventArgs(string s) => string.Equals(PropertyChangedEventArgsClassName, s, StringComparison.Ordinal);

        private static bool IsAlreadyStatic(ObjectCreationExpressionSyntax objectCreationExpr)
        {
            var result = false;
            var memberForObjectCreationExpr = objectCreationExpr.FirstAncestorOrSelfThatIsAMember();
            if (memberForObjectCreationExpr == null)
            {
                return result;
            }

            switch (memberForObjectCreationExpr.Kind())
            {
                case SyntaxKind.ConstructorDeclaration:
                    var constructorDeclaration = (ConstructorDeclarationSyntax)memberForObjectCreationExpr;
                    result = ContainsStaticModifier(constructorDeclaration.Modifiers);
                    break;
                case SyntaxKind.FieldDeclaration:
                    var fieldDeclaration = (FieldDeclarationSyntax)memberForObjectCreationExpr;
                    result = ContainsStaticModifier(fieldDeclaration.Modifiers);
                    break;
                default:
                    break;
            }
            return result;
        }

        private static bool ContainsStaticModifier(SyntaxTokenList modifiers) => modifiers.Any(StaticKeyword);

        private class IdentifierExtractor : CSharpSyntaxVisitor<SyntaxToken>
        {
            public override SyntaxToken VisitIdentifierName(IdentifierNameSyntax node) => node.Identifier;
            public override SyntaxToken VisitQualifiedName(QualifiedNameSyntax node) => VisitIdentifierName((IdentifierNameSyntax)node.Right);
        }

        private class IsArgumentALiteralOrNameof : CSharpSyntaxVisitor<bool>
        {
            public override bool VisitArgumentList(ArgumentListSyntax node) => node.Arguments.Any(arg => arg.Accept(this));
            public override bool VisitArgument(ArgumentSyntax node) => node.Expression.Accept(this);
            public override bool VisitLiteralExpression(LiteralExpressionSyntax node) => true;

            public override bool VisitIdentifierName(IdentifierNameSyntax node)
                => string.Equals("nameof", node.Identifier.ValueText, StringComparison.Ordinal);

            public override bool VisitInvocationExpression(InvocationExpressionSyntax node) => node.Expression.Accept(this);
        }
    }

    /// <summary>
    /// Stores event argument creation details used to generate a reusable static field.
    /// </summary>
    public class PropertyChangedEventArgsAnalyzerData
    {
        private const string ArgumentKeyName = "Argument";
        private const string IsNullKeyName = "IsNull";
        private const string IsNameofKeyName = "NameOf";
        private const string TypeKeyName = "Type";
        private const string SuffixAllProperties = "AllProperties";

        /// <summary>
        /// The event argument type name as written in the creation expression.
        /// </summary>
        public readonly string FullTypeName;
        /// <summary>
        /// The property-name argument text or the identifier used in a nameof expression.
        /// </summary>
        public readonly string ArgumentName;
        /// <summary>
        /// Indicates whether the creation expression uses a null property-name argument.
        /// </summary>
        public readonly bool ArgumentIsNullLiteral;
        /// <summary>
        /// Indicates whether the creation expression uses a nameof property-name argument.
        /// </summary>
        public readonly bool ArgumentIsNameofExpression;
        /// <summary>
        /// The proposed identifier for the cached event argument field, populated when diagnostic properties are restored.
        /// </summary>
        public readonly string StaticFieldIdentifierNameProposition;

        private PropertyChangedEventArgsAnalyzerData(string fullTypeName, string argumentName, string isNullLiteral, string isNameof)
        {
            FullTypeName = fullTypeName;
            ArgumentName = argumentName ?? string.Empty;
            ArgumentIsNullLiteral = bool.Parse(isNullLiteral);
            ArgumentIsNameofExpression = bool.Parse(isNameof);

            StaticFieldIdentifierNameProposition = $"PropertyChangedEventArgsFor{SuffixForStaticInstance()}";
        }

        /// <summary>
        /// Extracts the event argument type and property-name argument from an object creation expression.
        /// </summary>
        /// <param name="propertyChangedInstanceCreationExpr">The PropertyChangedEventArgs object creation expression to inspect.</param>
        /// <exception cref="ArgumentNullException">The creation expression is null.</exception>
        public PropertyChangedEventArgsAnalyzerData(ObjectCreationExpressionSyntax propertyChangedInstanceCreationExpr)
        {
            if (propertyChangedInstanceCreationExpr == null)
                throw new ArgumentNullException(nameof(propertyChangedInstanceCreationExpr));
            var analyzer = new PropertyChangedCreationSyntaxAnalyzer();
            propertyChangedInstanceCreationExpr.ArgumentList.Accept(analyzer);
            FullTypeName = propertyChangedInstanceCreationExpr.Type.ToString();
            ArgumentName = analyzer.IdentifierName;
            ArgumentIsNullLiteral = analyzer.NullLiteralExpressionFound;
            ArgumentIsNameofExpression = analyzer.NameofExpressionFound;
        }

        /// <summary>
        /// Chooses a static field identifier that does not collide with existing name hints.
        /// </summary>
        /// <param name="nameHints">The existing identifiers to avoid when choosing a field name.</param>
        /// <returns>The proposed field identifier, with a numeric suffix if needed.</returns>
        public string StaticFieldIdentifierName(IEnumerable<string> nameHints) => nameHints.Contains(StaticFieldIdentifierNameProposition) ?
                                                                                    CreateNewIdentifierName(StaticFieldIdentifierNameProposition, 1, nameHints) : StaticFieldIdentifierNameProposition;

        /// <summary>
        /// Creates a private static readonly field that caches the represented event arguments.
        /// </summary>
        /// <param name="nameHints">The existing identifiers to avoid when choosing a field name.</param>
        /// <returns>The initialized field declaration using an available identifier.</returns>
        public MemberDeclarationSyntax PropertyChangedEventArgsStaticField(IEnumerable<string> nameHints)
        {
            return FieldDeclaration(List<AttributeListSyntax>(),
                TokenList(Token(PrivateKeyword), Token(StaticKeyword), Token(ReadOnlyKeyword)),
                VariableDeclaration(FieldType(FullTypeName), VariableName(StaticFieldIdentifierName(nameHints))));
        }

        /// <summary>
        /// Serializes the event argument creation details into diagnostic properties.
        /// </summary>
        /// <returns>The immutable diagnostic property dictionary.</returns>
        public ImmutableDictionary<string, string> ToDiagnosticProperties()
        {
            var dict = ImmutableDictionary.CreateBuilder<string, string>();

            dict.Add(ArgumentKeyName, ArgumentName);
            dict.Add(IsNullKeyName, ArgumentIsNullLiteral.ToString());
            dict.Add(IsNameofKeyName, ArgumentIsNameofExpression.ToString());
            dict.Add(TypeKeyName, FullTypeName);

            return dict.ToImmutable();
        }

        /// <summary>
        /// Restores event argument creation details from diagnostic properties.
        /// </summary>
        /// <param name="properties">The diagnostic property dictionary previously produced by this data object.</param>
        /// <returns>The data object reconstructed from the property dictionary.</returns>
        public static PropertyChangedEventArgsAnalyzerData FromDiagnosticProperties(ImmutableDictionary<string, string> properties)
        {
            return new PropertyChangedEventArgsAnalyzerData(
                properties[TypeKeyName], properties[ArgumentKeyName], properties[IsNullKeyName], properties[IsNameofKeyName]);
        }

        private EqualsValueClauseSyntax PropertyChangedEventArgsInstance() =>
            EqualsValueClause(Token(EqualsToken),
                ObjectCreationExpression(ParseTypeName(FullTypeName),
                    ArgumentList(
                        SingletonSeparatedList(
                            PropertyChangedEventArgsCtorArgument())), default(InitializerExpressionSyntax)));

        private ArgumentSyntax PropertyChangedEventArgsCtorArgument() =>
            Argument(ArgumentIsNameofExpression
                                ? ParseExpression($"nameof({ArgumentName})")
                                : ArgumentIsNullLiteral ? LiteralExpression(NullLiteralExpression) : StringLiteral(ArgumentName));

        private string SuffixForStaticInstance()
        {
            return ArgumentIsNullLiteral || ArgumentNameIsStar() ? SuffixAllProperties : MakeValidIdentifier(ArgumentName);
        }

        private SeparatedSyntaxList<VariableDeclaratorSyntax> VariableName(string fieldName) =>
            SeparatedList(new[]
            {
                VariableDeclarator(fieldName)
                    .WithInitializer(PropertyChangedEventArgsInstance())
            });

        private bool ArgumentNameIsStar() => string.Equals(ArgumentName, "*", StringComparison.OrdinalIgnoreCase);

        private static string MakeValidIdentifier(string s) => IsValidIdentifier(s) ? s : SanitizeIdentifierName(s);

        private static string SanitizeIdentifierName(string s) => s.ToCharArray()
                                                                        .Aggregate(new StringBuilder(),
                                                                                (sanitized, nextChar) => IsValidIdentifier($"{sanitized.ToString()}{nextChar}") ? sanitized.Append(nextChar) : sanitized)
                                                                        .ToString();

        private static LiteralExpressionSyntax StringLiteral(string s) => LiteralExpression(StringLiteralExpression, Literal(s));

        private static IdentifierNameSyntax FieldType(string type) => IdentifierName(type);

        private static string CreateNewIdentifierName(string oldName, int extension, IEnumerable<string> nameHints)
        {
            if (oldName == null)
            {
                oldName = string.Empty;
            }

            // Find the start index of the trailing numeric suffix (if any)
            var i = oldName.Length - 1;
            while (i >= 0 && char.IsDigit(oldName[i]))
            {
                i--;
            }

            // Split into base name and numeric suffix (in correct order)
            var baseName = oldName.Substring(0, i + 1);
            var digits = oldName.Substring(i + 1);

            // Parse the existing numeric suffix (default to 0 if none/invalid)
            int current = 0;
            if (!string.IsNullOrEmpty(digits))
            {
                int.TryParse(digits, out current);
            }

            // Propose next name and retry if it collides
            var candidate = $"{baseName}{current + extension}";
            return nameHints.Contains(candidate)
                ? CreateNewIdentifierName(candidate, 1, nameHints) // increment by 1 on each retry
                : candidate;
        }

        private class PropertyChangedCreationSyntaxAnalyzer : CSharpSyntaxWalker
        {
            public bool NullLiteralExpressionFound { get; private set; }
            public bool NameofExpressionFound { get; private set; }
            public string IdentifierName { get; private set; }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                NameofExpressionFound = false;
                NullLiteralExpressionFound = node.IsKind(NullLiteralExpression);
                IdentifierName = node.Token.ValueText;
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                NameofExpressionFound = true;
                base.VisitInvocationExpression(node);
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node) => IdentifierName = node.Identifier.ValueText;
        }
    }
}