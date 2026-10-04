using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace CodeCracker.CSharp.Style
{
    /// <summary>
    /// Reports explicit variable types that can be replaced with var without changing verified semantics.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class AlwaysUseVarAnalyzer : DiagnosticAnalyzer
    {
        internal const string Title = "You should use 'var' whenever possible.";
        internal const string MessageFormat = "Use 'var' instead of specifying the type name.";
        internal const string Category = SupportedCategories.Style;
        const string Description = "Usage of an implicit type improve readability of the code.\r\n"
            + "Code depending on types for their readability should be refactored with better variable "
            + "names or by introducing well-named methods.";
        internal static readonly DiagnosticDescriptor RuleNonPrimitives = new DiagnosticDescriptor(
            DiagnosticId.AlwaysUseVar.ToDiagnosticId(),
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.AlwaysUseVar));

        internal static readonly DiagnosticDescriptor RulePrimitives = new DiagnosticDescriptor(
            DiagnosticId.AlwaysUseVarOnPrimitives.ToDiagnosticId(),
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.AlwaysUseVarOnPrimitives));

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(RuleNonPrimitives, RulePrimitives);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.LocalDeclarationStatement);
            context.RegisterSyntaxNodeAction(AnalyzeFor, SyntaxKind.ForStatement);
            context.RegisterSyntaxNodeAction(AnalyzeForEach, SyntaxKind.ForEachStatement);
            context.RegisterSyntaxNodeAction(AnalyzeOut, SyntaxKind.DeclarationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeDeconstruction, SyntaxKind.SimpleAssignmentExpression);
            context.RegisterSyntaxNodeAction(AnalyzeForEachDeconstruction, SyntaxKind.ForEachVariableStatement);
        }

        private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            var localDeclaration = (LocalDeclarationStatementSyntax)context.Node;
            if (localDeclaration.IsConst) return;

            var variableDeclaration = localDeclaration.Declaration;
            var variableTypeName = GetTypeToReplace(variableDeclaration.Type);
            if (variableTypeName.IsVar || variableDeclaration.ContainsDiagnostics || variableDeclaration.Variables.Count == 0) return;

            var semanticModel = context.SemanticModel;
            var firstSymbol = semanticModel.GetDeclaredSymbol(variableDeclaration.Variables[0],
                context.CancellationToken) as ILocalSymbol;
            var variableType = firstSymbol?.Type;
            if (variableType == null || variableType.TypeKind == TypeKind.Error) return;

            var inferredType = variableDeclaration.Type.ReplaceNode(variableTypeName,
                SyntaxFactory.IdentifierName("var").WithTriviaFrom(variableTypeName));

            foreach (var variable in variableDeclaration.Variables)
            {
                if (variable.Initializer == null) return;
                var originalSymbol = variable == variableDeclaration.Variables[0] ? firstSymbol :
                    semanticModel.GetDeclaredSymbol(variable, context.CancellationToken) as ILocalSymbol;
                if (variable.Initializer.Value is LiteralExpressionSyntax literal &&
                    !literal.IsKind(SyntaxKind.DefaultLiteralExpression))
                {
                    var literalType = semanticModel.GetTypeInfo(literal, context.CancellationToken).Type;
                    // Value-type literals have an intrinsic type and need no speculative statement.
                    if (literalType?.IsValueType == true)
                    {
                        if (!SymbolEqualityComparer.IncludeNullability.Equals(originalSymbol?.Type, literalType)) return;
                        continue;
                    }
                }
                // Speculative binding can infer natural delegate types even when the language version rejects them.
                if (((CSharpParseOptions)localDeclaration.SyntaxTree.Options).LanguageVersion < LanguageVersion.CSharp10)
                {
                    var conversion = semanticModel.ClassifyConversion(variable.Initializer.Value, variableType);
                    if (conversion.IsAnonymousFunction || conversion.IsMethodGroup) return;
                }

                // Bind without the declared type so target-typed expressions cannot produce an invalid fix.
                var inferredDeclaration = localDeclaration.WithDeclaration(variableDeclaration
                    .WithType(inferredType)
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(variable)));
                if (!semanticModel.TryGetSpeculativeSemanticModel(localDeclaration.SpanStart, inferredDeclaration,
                    out var inferredModel)) return;
                var inferredVariable = inferredDeclaration.Declaration.Variables[0];
                var inferredSymbol = inferredModel.GetDeclaredSymbol(inferredVariable, context.CancellationToken) as ILocalSymbol;
                if (!AlwaysUseVarSemanticHelper.SameLocal(originalSymbol, inferredSymbol) ||
                    !AlwaysUseVarSemanticHelper.SameBindings(semanticModel, variable.Initializer.Value,
                        inferredModel, inferredVariable.Initializer.Value, context.CancellationToken)) return;
            }

            var rule = variableType.IsPrimitive() ? RulePrimitives : RuleNonPrimitives;
            var diagnostic = Diagnostic.Create(rule, variableTypeName.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }

        internal static TypeSyntax GetTypeToReplace(TypeSyntax type)
        {
            if (type is RefTypeSyntax refType) return GetTypeToReplace(refType.Type);
            if (type is ScopedTypeSyntax scopedType) return GetTypeToReplace(scopedType.Type);
            return type;
        }

        private static void Report(SyntaxNodeAnalysisContext context, TypeSyntax type, ITypeSymbol symbol)
        {
            if (symbol == null || symbol.TypeKind == TypeKind.Error) return;
            context.ReportDiagnostic(Diagnostic.Create(symbol.IsPrimitive() ? RulePrimitives : RuleNonPrimitives,
                type.GetLocation()));
        }

        private static void AnalyzeFor(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            var statement = (ForStatementSyntax)context.Node;
            var declaration = statement.Declaration;
            if (declaration == null || declaration.ContainsDiagnostics || declaration.Variables.Count != 1) return;
            var type = GetTypeToReplace(declaration.Type);
            if (type.IsVar || declaration.Variables[0].Initializer == null) return;
            var originalSymbol = context.SemanticModel.GetDeclaredSymbol(declaration.Variables[0],
                context.CancellationToken) as ILocalSymbol;
            if (originalSymbol == null) return;
            if (((CSharpParseOptions)statement.SyntaxTree.Options).LanguageVersion < LanguageVersion.CSharp10)
            {
                var conversion = context.SemanticModel.ClassifyConversion(declaration.Variables[0].Initializer.Value,
                    originalSymbol.Type);
                if (conversion.IsAnonymousFunction || conversion.IsMethodGroup) return;
            }
            var rewritten = statement.ReplaceNode(type, SyntaxFactory.IdentifierName("var").WithTriviaFrom(type));
            if (!context.SemanticModel.TryGetSpeculativeSemanticModel(statement.SpanStart, rewritten, out var inferred)) return;
            if (!AlwaysUseVarSemanticHelper.SameLocals(context.SemanticModel, declaration.Variables,
                inferred, rewritten.Declaration.Variables, context.CancellationToken) ||
                !AlwaysUseVarSemanticHelper.SameBindings(context.SemanticModel, statement, inferred, rewritten,
                    context.CancellationToken)) return;
            Report(context, type, originalSymbol.Type);
        }

        private static void AnalyzeForEach(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            var statement = (ForEachStatementSyntax)context.Node;
            var type = GetTypeToReplace(statement.Type);
            if (type.IsVar || statement.ContainsDiagnostics) return;
            var rewritten = statement.ReplaceNode(type, SyntaxFactory.IdentifierName("var").WithTriviaFrom(type));
            if (!context.SemanticModel.TryGetSpeculativeSemanticModel(statement.SpanStart, rewritten, out var inferred)) return;
            var originalSymbol = context.SemanticModel.GetDeclaredSymbol(statement, context.CancellationToken) as ILocalSymbol;
            if (!AlwaysUseVarSemanticHelper.SameLocal(originalSymbol,
                inferred.GetDeclaredSymbol(rewritten, context.CancellationToken) as ILocalSymbol) ||
                !AlwaysUseVarSemanticHelper.SameEnumeration(context.SemanticModel.GetForEachStatementInfo(statement),
                    inferred.GetForEachStatementInfo(rewritten)) ||
                !AlwaysUseVarSemanticHelper.SameBindings(context.SemanticModel, statement, inferred, rewritten,
                    context.CancellationToken)) return;
            Report(context, type, originalSymbol.Type);
        }

        private static void AnalyzeOut(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            var declaration = (DeclarationExpressionSyntax)context.Node;
            if (!(declaration.Parent is ArgumentSyntax argument) || !argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword) ||
                declaration.Type.IsVar || declaration.ContainsDiagnostics ||
                !(declaration.Designation is SingleVariableDesignationSyntax)) return;
            var statement = declaration.FirstAncestorOrSelf<StatementSyntax>();
            if (statement == null) return;
            var annotation = new SyntaxAnnotation();
            var replacement = declaration.WithType(SyntaxFactory.IdentifierName("var").WithTriviaFrom(declaration.Type))
                .WithAdditionalAnnotations(annotation);
            // Fix-all may replace several out types in one call. Check their combined effect as well.
            var arguments = argument.Parent as ArgumentListSyntax;
            if (arguments == null) return;
            var declarations = arguments.Arguments
                .Where(a => a.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword))
                .Select(a => a.Expression as DeclarationExpressionSyntax)
                .Where(d => d != null && !d.Type.IsVar && d.Designation is SingleVariableDesignationSyntax).ToArray();
            var rewritten = statement.ReplaceNodes(declarations, (original, _) => original == declaration ? replacement :
                original.WithType(SyntaxFactory.IdentifierName("var").WithTriviaFrom(original.Type)));
            if (!context.SemanticModel.TryGetSpeculativeSemanticModel(statement.SpanStart, rewritten, out var inferred)) return;
            var inferredDeclaration = (DeclarationExpressionSyntax)rewritten.GetAnnotatedNodes(annotation).Single();
            var originalSymbol = context.SemanticModel.GetDeclaredSymbol(declaration.Designation,
                context.CancellationToken) as ILocalSymbol;
            if (!AlwaysUseVarSemanticHelper.SameLocal(originalSymbol,
                inferred.GetDeclaredSymbol(inferredDeclaration.Designation, context.CancellationToken) as ILocalSymbol) ||
                !AlwaysUseVarSemanticHelper.SameBindings(context.SemanticModel, statement, inferred, rewritten,
                    context.CancellationToken)) return;
            Report(context, declaration.Type, originalSymbol.Type);
        }

        private static void AnalyzeDeconstruction(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            var assignment = (AssignmentExpressionSyntax)context.Node;
            if (!(assignment.Parent is ExpressionStatementSyntax statement) || assignment.ContainsDiagnostics ||
                !assignment.Left.DescendantNodesAndSelf().OfType<DeclarationExpressionSyntax>().Any(d => !d.Type.IsVar)) return;
            var replacement = AlwaysUseVarSemanticHelper.CreateInferredDeconstruction(assignment.Left);
            if (replacement == null) return;
            var rewritten = statement.WithExpression(assignment.WithLeft(replacement));
            if (!context.SemanticModel.TryGetSpeculativeSemanticModel(statement.SpanStart, rewritten, out var inferred)) return;
            var rewrittenAssignment = (AssignmentExpressionSyntax)rewritten.Expression;
            if (!AlwaysUseVarSemanticHelper.SameLocals(context.SemanticModel,
                assignment.Left.DescendantNodes().OfType<SingleVariableDesignationSyntax>(), inferred,
                rewrittenAssignment.Left.DescendantNodes().OfType<SingleVariableDesignationSyntax>(), context.CancellationToken) ||
                !AlwaysUseVarSemanticHelper.SameDeconstruction(context.SemanticModel.GetDeconstructionInfo(assignment),
                    inferred.GetDeconstructionInfo(rewrittenAssignment)) ||
                !AlwaysUseVarSemanticHelper.SameBindings(context.SemanticModel, statement, inferred, rewritten,
                    context.CancellationToken)) return;
            var type = assignment.Left.DescendantNodesAndSelf().OfType<DeclarationExpressionSyntax>().First(d => !d.Type.IsVar).Type;
            Report(context, type, context.SemanticModel.GetTypeInfo(type, context.CancellationToken).Type);
        }

        private static void AnalyzeForEachDeconstruction(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            var statement = (ForEachVariableStatementSyntax)context.Node;
            if (statement.ContainsDiagnostics ||
                !statement.Variable.DescendantNodesAndSelf().OfType<DeclarationExpressionSyntax>().Any(d => !d.Type.IsVar)) return;
            var replacement = AlwaysUseVarSemanticHelper.CreateInferredDeconstruction(statement.Variable);
            if (replacement == null) return;
            var rewritten = statement.WithVariable(replacement);
            if (!context.SemanticModel.TryGetSpeculativeSemanticModel(statement.SpanStart, rewritten, out var inferred)) return;
            if (!AlwaysUseVarSemanticHelper.SameLocals(context.SemanticModel,
                statement.Variable.DescendantNodes().OfType<SingleVariableDesignationSyntax>(), inferred,
                rewritten.Variable.DescendantNodes().OfType<SingleVariableDesignationSyntax>(), context.CancellationToken) ||
                !AlwaysUseVarSemanticHelper.SameDeconstruction(context.SemanticModel.GetDeconstructionInfo(statement),
                    inferred.GetDeconstructionInfo(rewritten)) ||
                !AlwaysUseVarSemanticHelper.SameEnumeration(context.SemanticModel.GetForEachStatementInfo(statement),
                    inferred.GetForEachStatementInfo(rewritten)) ||
                !AlwaysUseVarSemanticHelper.SameBindings(context.SemanticModel, statement, inferred, rewritten,
                    context.CancellationToken)) return;
            var type = statement.Variable.DescendantNodesAndSelf().OfType<DeclarationExpressionSyntax>().First(d => !d.Type.IsVar).Type;
            Report(context, type, context.SemanticModel.GetTypeInfo(type, context.CancellationToken).Type);
        }
    }
}