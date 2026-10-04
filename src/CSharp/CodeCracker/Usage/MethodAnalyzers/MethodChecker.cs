using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeCracker.CSharp.Usage.MethodAnalyzers
{
    /// <summary>
    /// Validates constant arguments of matching method and constructor calls and reports validation exceptions as diagnostics.
    /// </summary>
    public class MethodChecker
    {
        private readonly SyntaxNodeAnalysisContext context;
        private readonly DiagnosticDescriptor diagnosticDescriptor;

        /// <summary>
        /// Initializes a checker with an analysis context and a diagnostic descriptor for validation failures.
        /// </summary>
        /// <param name="context">The analysis context for the target syntax or symbol.</param>
        /// <param name="diagnosticDescriptor">The descriptor reported when argument validation throws an exception.</param>
        public MethodChecker(SyntaxNodeAnalysisContext context, DiagnosticDescriptor diagnosticDescriptor)
        {
            this.context = context;
            this.diagnosticDescriptor = diagnosticDescriptor;
        }

        /// <summary>
        /// Validates constant arguments when the analysis context's object creation matches the target constructor signature.
        /// </summary>
        /// <param name="methodInformation">The target signature, argument validator, and diagnostic location index.</param>
        public void AnalyzeConstructor(MethodInformation methodInformation)
        {
            if (ConstructorNameNotFound(methodInformation) || MethodFullNameNotFound(methodInformation.MethodFullDefinition))
                return;
            var argumentList = ((ObjectCreationExpressionSyntax)context.Node).ArgumentList;
            var arguments = GetArguments(argumentList);
            Execute(methodInformation, arguments, argumentList);
        }

        private bool ConstructorNameNotFound(MethodInformation methodInformation)
        {
            return AbreviatedConstructorNameNotFound(methodInformation) && QualifiedConstructorNameNotFound(methodInformation);
        }

        private bool AbreviatedConstructorNameNotFound(MethodInformation methodInformation)
        {
            var objectCreationExpressionSyntax = (ObjectCreationExpressionSyntax) context.Node;
            var identifier = objectCreationExpressionSyntax.Type as IdentifierNameSyntax;
            return identifier?.Identifier.ValueText != methodInformation.MethodName;
        }

        private bool QualifiedConstructorNameNotFound(MethodInformation methodInformation)
        {
            var objectCreationExpressionSyntax = (ObjectCreationExpressionSyntax)context.Node;
            var identifier = objectCreationExpressionSyntax.Type as QualifiedNameSyntax;
            return identifier?.Right.ToString() != methodInformation.MethodName;
        }

        /// <summary>
        /// Validates constant arguments when the analysis context's invocation matches the target method signature.
        /// </summary>
        /// <param name="methodInformation">The target signature, argument validator, and diagnostic location index.</param>
        public void AnalyzeMethod(MethodInformation methodInformation)
        {
            if (MethodNameNotFound(methodInformation) ||
                MethodFullNameNotFound(methodInformation.MethodFullDefinition))
            {
                return;
            }
            var argumentList = ((InvocationExpressionSyntax)context.Node).ArgumentList;
            var arguments = GetArguments(argumentList);

            Execute(methodInformation, arguments, argumentList);
        }

        private bool MethodNameNotFound(MethodInformation methodInformation)
        {
            var invocationExpression = (InvocationExpressionSyntax)context.Node;
            var memberExpression = invocationExpression.Expression as MemberAccessExpressionSyntax;
            return memberExpression?.Name?.Identifier.ValueText != methodInformation.MethodName;
        }

        private bool MethodFullNameNotFound(string methodDefinition)
        {
            var memberSymbol = context.SemanticModel.GetSymbolInfo(context.Node).Symbol;
            return memberSymbol?.ToString() != methodDefinition;
        }

        private void Execute(MethodInformation methodInformation, List<object> arguments, ArgumentListSyntax argumentList)
        {
            if (!argumentList.Arguments.Any())
            {
                return;
            }
            try
            {
                methodInformation.MethodAction.Invoke(arguments);
            }
            catch (Exception ex)
            {
                while (ex.InnerException != null)
                {
                    ex = ex.InnerException;
                }
                var diag = Diagnostic.Create(diagnosticDescriptor, argumentList.Arguments[methodInformation.ArgumentIndex].GetLocation(), ex.Message);
                context.ReportDiagnostic(diag);
            }
        }

        private List<object> GetArguments(ArgumentListSyntax argumentList)
        {
            return argumentList.Arguments
                .Select(a => a.Expression)
                .Select(l => l == null ? null : context.SemanticModel.GetConstantValue(l).Value)
                .ToList();
        }
    }
}