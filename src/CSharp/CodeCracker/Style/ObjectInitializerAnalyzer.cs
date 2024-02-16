using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CodeCracker.CSharp.Style
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ObjectInitializerAnalyzer : DiagnosticAnalyzer
    {
        internal const string TitleLocalDeclaration = "Use object initializer";
        internal const string MessageFormat = "{0}";
        internal const string Category = SupportedCategories.Style;
        internal const string TitleAssignment = "Use object initializer";
        const string Description = "When possible an object initializer should be used to initialize the properties of an "
            + "object instead of multiple assignments.";

        internal static readonly DiagnosticDescriptor RuleAssignment = new DiagnosticDescriptor(
            DiagnosticId.ObjectInitializer_Assignment.ToDiagnosticId(),
            TitleLocalDeclaration,
            MessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.ObjectInitializer_Assignment));

        internal static readonly DiagnosticDescriptor RuleLocalDeclaration = new DiagnosticDescriptor(
            DiagnosticId.ObjectInitializer_LocalDeclaration.ToDiagnosticId(),
            TitleLocalDeclaration,
            MessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.ObjectInitializer_LocalDeclaration));

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(RuleLocalDeclaration, RuleAssignment);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
            context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.ExpressionStatement);
        }

        private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated())
            {
                return;
            }

            var semanticModel = context.SemanticModel;

            if (!(context.Node is ExpressionStatementSyntax expressionStatement))
            {
                return;
            }

            if (expressionStatement.Expression.IsNotKind(SyntaxKind.SimpleAssignmentExpression))
            {
                return;
            }

            if (!(expressionStatement.Expression is AssignmentExpressionSyntax assignmentExpression))
            {
                return;
            }

            if (assignmentExpression.Right.IsNotKind(SyntaxKind.ObjectCreationExpression))
            {
                return;
            }

            if (((ObjectCreationExpressionSyntax)assignmentExpression.Right).Initializer?.IsKind(SyntaxKind.CollectionInitializerExpression) ?? false)
            {
                return;
            }

            var variableSymbol = semanticModel.GetSymbolInfo(assignmentExpression.Left).Symbol;
            var assignmentExpressionStatements = FindAssignmentExpressions(semanticModel, expressionStatement, variableSymbol);
            if (!assignmentExpressionStatements.Any())
            {
                return;
            }

            if (HasAssignmentUsingDeclaredVariable(semanticModel, variableSymbol, assignmentExpressionStatements))
            {
                return;
            }

            var diagnostic = Diagnostic.Create(RuleAssignment, expressionStatement.GetLocation(), "You can use initializers in here.");
            context.ReportDiagnostic(diagnostic);
        }

        private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated())
            {
                return;
            }

            var semanticModel = context.SemanticModel;
            if (!(context.Node is LocalDeclarationStatementSyntax localDeclarationStatement))
            {
                return;
            }

            if (localDeclarationStatement.Declaration.Variables.Count != 1)
            {
                return;
            }

            var variable = localDeclarationStatement.Declaration.Variables.Single();
            if (!(variable.Initializer is EqualsValueClauseSyntax equalsValueClauseSyntax))
            {
                return;
            }

            if (equalsValueClauseSyntax.Value.IsNotKind(SyntaxKind.ObjectCreationExpression))
            {
                return;
            }

            var objectCreationExpression = equalsValueClauseSyntax.Value as ObjectCreationExpressionSyntax;
            if (objectCreationExpression?.Initializer?.IsKind(SyntaxKind.CollectionInitializerExpression) ?? false)
            {
                return;
            }

            var variableSymbol = semanticModel.GetDeclaredSymbol(variable);
            if (!(variableSymbol is ILocalSymbol localVariableSymbol))
            {
                return;
            }

            if (localVariableSymbol.Type.TypeKind == TypeKind.Dynamic)
            {
                return;
            }

            var assignmentExpressionStatements = FindAssignmentExpressions(semanticModel, localDeclarationStatement, variableSymbol);
            if (!assignmentExpressionStatements.Any())
            {
                return;
            }

            if (HasAssignmentUsingDeclaredVariable(semanticModel, variableSymbol, assignmentExpressionStatements))
            {
                return;
            }

            var diagnostic = Diagnostic.Create(RuleLocalDeclaration, localDeclarationStatement.GetLocation(), "You can use initializers in here.");
            context.ReportDiagnostic(diagnostic);
        }

        public static bool HasAssignmentUsingDeclaredVariable(SemanticModel semanticModel, ISymbol variableSymbol,
            IEnumerable<ExpressionStatementSyntax> assignmentExpressionStatements)
        {
            foreach (var assignmentExpressionStatement in assignmentExpressionStatements)
            {
                var assignmentExpression = (AssignmentExpressionSyntax)assignmentExpressionStatement.Expression;
                var ids = assignmentExpression.Right.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().ToList();
                if (ids.Any(id =>
                        semanticModel.GetSymbolInfo(id).Symbol
                            ?.Equals(variableSymbol, SymbolEqualityComparer.Default) == true))
                {
                    return true;
                }
            }

            return false;
        }

        public static List<ExpressionStatementSyntax> FindAssignmentExpressions(SemanticModel semanticModel,
            StatementSyntax statement, ISymbol variableSymbol)
        {
            var blockParent = statement.FirstAncestorOrSelf<BlockSyntax>();
            var isBefore = true;
            var assignmentExpressions = new List<ExpressionStatementSyntax>();
            if (blockParent == null)
            {
                return assignmentExpressions;
            }

            foreach (var blockStatement in blockParent.Statements)
            {
                if (isBefore)
                {
                    if (blockStatement.Equals(statement))
                    {
                        isBefore = false;
                    }
                }
                else
                {
                    if (!(blockStatement is ExpressionStatementSyntax expressionStatement))
                    {
                        break;
                    }

                    if (!(expressionStatement.Expression is AssignmentExpressionSyntax assignmentExpression) ||
                        !assignmentExpression.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        break;
                    }

                    if (!(assignmentExpression.Left is MemberAccessExpressionSyntax memberAccess) ||
                        !memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    {
                        break;

                    }

                    if (!(memberAccess.Expression is IdentifierNameSyntax memberIdentifier))
                    {
                        break;
                    }

                    if (!(memberAccess.Name is IdentifierNameSyntax))
                    {
                        break;
                    }

                    var symbol = semanticModel.GetSymbolInfo(memberIdentifier).Symbol;
                    if (symbol == null)
                    {
                        break;
                    }

                    if (!symbol.Equals(variableSymbol, SymbolEqualityComparer.Default))
                    {
                        break;
                    }

                    assignmentExpressions.Add(expressionStatement);
                }
            }

            return assignmentExpressions;
        }
    }
}