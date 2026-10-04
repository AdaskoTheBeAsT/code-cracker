using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;

namespace CodeCracker.CSharp.Performance
{
    /// <summary>
    /// Reports nonabstract attribute classes that are not sealed.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SealedAttributeAnalyzer : DiagnosticAnalyzer
    {
        internal const string Title = "Unsealed Attribute";
        internal const string MessageFormat = "Mark '{0}' as sealed.";
        internal const string Category = SupportedCategories.Performance;
        const string Description = "Framework methods that retrieve attributes by default search the whole "
            + "inheritence hierarchy of the attribute class. "
            + "Marking the type as sealed eliminate this search and can improve performance";

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId.SealedAttribute.ToDiagnosticId(),
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.SealedAttribute));

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context) => context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);

        private static void Analyze(SymbolAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            var type = (INamedTypeSymbol)context.Symbol;

            if (type.TypeKind != TypeKind.Class) return;

            if (!IsAttribute(type)) return;

            if (type.IsAbstract || type.IsSealed) return;

            context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name));
        }

        /// <summary>
        /// Checks whether a type has a base type named Attribute.
        /// </summary>
        /// <param name="symbol">The type symbol to inspect.</param>
        /// <returns>True if an Attribute-named base type is found; otherwise, false.</returns>
        public static bool IsAttribute(ITypeSymbol symbol)
        {
            var @base = symbol.BaseType;
            var attributeName = typeof(Attribute).Name;

            while (@base != null)
            {
                if (@base.Name == attributeName)
                    return true;

                @base = @base.BaseType;
            }

            return false;
        }
    }
}