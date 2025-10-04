using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;

namespace CodeCracker.CSharp.Usage
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class RedundantFieldAssignmentAnalyzer : DiagnosticAnalyzer
    {
        internal const string Title = "Redundant field assignment";
        internal const string MessageFormat = "Field {0} is assigning to default value {1}. Remove the assignment.";
        internal const string Category = SupportedCategories.Usage;
        const string Description = "It's recommend not to assign the default value to a field as a performance optimization.";

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId.RedundantFieldAssignment.ToDiagnosticId(),
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            customTags: WellKnownDiagnosticTags.Unnecessary,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.RedundantFieldAssignment));

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context) => context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);

        private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            var fieldDeclaration = context.Node as FieldDeclarationSyntax;
            var variable = fieldDeclaration?.Declaration.Variables.LastOrDefault();
            if (variable?.Initializer == null) return;
            if (fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword)) return;
            var initializerValue = variable.Initializer.Value;
            if (initializerValue.IsKind(SyntaxKind.DefaultExpression))
            {
                ReportDiagnostic(context, variable, initializerValue);
                return;
            }
            var semanticModel = context.SemanticModel;
            var fieldSymbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
            if (fieldSymbol == null) return;
            if (!IsAssigningToDefault(fieldSymbol.Type, initializerValue, semanticModel)) return;
            ReportDiagnostic(context, variable, initializerValue);
        }

        private static void ReportDiagnostic(SyntaxNodeAnalysisContext context, VariableDeclaratorSyntax variable, ExpressionSyntax initializerValue)
        {
            var diag = Diagnostic.Create(Rule, variable.GetLocation(), variable.Identifier.ValueText, initializerValue.ToString());
            context.ReportDiagnostic(diag);
        }

        private static bool IsAssigningToDefault(ITypeSymbol fieldType, ExpressionSyntax initializerValue, SemanticModel semanticModel)
        {
            if (fieldType.IsReferenceType)
            {
                if (!initializerValue.IsKind(SyntaxKind.NullLiteralExpression)) return false;
            }
            else
            {
                if (!IsValueTypeAssigningToDefault(fieldType, initializerValue, semanticModel)) return false;
            }
            return true;
        }

        private static bool IsValueTypeAssigningToDefault(ITypeSymbol fieldType, ExpressionSyntax initializerValue, SemanticModel semanticModel)
        {
            static bool IsFullyQualified(ITypeSymbol t, string fq) =>
                t != null &&
                t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                 .TrimStart("global::".ToCharArray()) == fq;

            // Booleans
            if (fieldType.SpecialType == SpecialType.System_Boolean)
            {
                if (initializerValue is LiteralExpressionSyntax litBool &&
                    litBool.Token.Value is bool b &&
                    b == false) return true;
                return false;
            }

            // Numeric primitives
            if (fieldType.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 or
                SpecialType.System_Decimal or
                SpecialType.System_Single or
                SpecialType.System_Double)
            {
                if (initializerValue.ToString() == "0") return true;
                if (initializerValue is LiteralExpressionSyntax litNum)
                {
                    try
                    {
                        var val = Convert.ToDouble(litNum.Token.Value);
                        return Math.Abs(val) == 0d;
                    }
                    catch { return false; }
                }
                return false;
            }

            // IntPtr / UIntPtr
            if (fieldType.SpecialType == SpecialType.System_IntPtr || IsFullyQualified(fieldType, "System.IntPtr"))
                return IsIntPtrLikeZero(initializerValue, semanticModel, expectUInt: false);

            if (fieldType.SpecialType == SpecialType.System_UIntPtr || IsFullyQualified(fieldType, "System.UIntPtr"))
                return IsIntPtrLikeZero(initializerValue, semanticModel, expectUInt: true);

            // DateTime
            if (fieldType.SpecialType == SpecialType.System_DateTime || IsFullyQualified(fieldType, "System.DateTime"))
            {
                if (initializerValue is not MemberAccessExpressionSyntax ma) return false;
                if (semanticModel.GetSymbolInfo(ma).Symbol is IFieldSymbol fs)
                    return fs.Name == "MinValue" &&
                        fs.ContainingType != null &&
                        (fs.ContainingType.SpecialType == SpecialType.System_DateTime ||
                         IsFullyQualified(fs.ContainingType, "System.DateTime"));

                var text = ma.ToString();
                return text == "DateTime.MinValue" || text == "System.DateTime.MinValue";
            }

            // Enums
            if (fieldType.TypeKind == TypeKind.Enum)
            {
                if (initializerValue.ToString() == "0") return true;
                if (initializerValue is LiteralExpressionSyntax litEnum)
                {
                    try
                    {
                        var val = Convert.ToDouble(litEnum.Token.Value);
                        return Math.Abs(val) == 0d;
                    }
                    catch { return false; }
                }
                return false;
            }

            return false;

            static bool IsIntPtrLikeZero(ExpressionSyntax expr, SemanticModel sm, bool expectUInt)
            {
                if (expr is not MemberAccessExpressionSyntax ma) return false;

                // Prefer symbol if available
                if (sm.GetSymbolInfo(ma).Symbol is IFieldSymbol fs)
                {
                    // Instead of relying on ToString() (which can yield "nint.Zero"/"nuint.Zero"),
                    // match by field name plus containing type special type or metadata name.
                    if (fs.Name == "Zero" && fs.ContainingType != null)
                    {
                        var ct = fs.ContainingType;
                        var isIntPtr =
                            ct.SpecialType == SpecialType.System_IntPtr ||
                            ct.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                              .TrimStart("global::".ToCharArray()) == "System.IntPtr" ||
                            ct.Name == "IntPtr" || ct.Name == "nint";

                        var isUIntPtr =
                            ct.SpecialType == SpecialType.System_UIntPtr ||
                            ct.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                              .TrimStart("global::".ToCharArray()) == "System.UIntPtr" ||
                            ct.Name == "UIntPtr" || ct.Name == "nuint";

                        if (expectUInt) return isUIntPtr;
                        return isIntPtr;
                    }
                    return false;
                }

                // Fallback textual
                var txt = ma.ToString();
                return expectUInt
                    ? txt is "UIntPtr.Zero" or "System.UIntPtr.Zero" or "nuint.Zero"
                    : txt is "IntPtr.Zero" or "System.IntPtr.Zero" or "nint.Zero";
            }
        }
    }
}