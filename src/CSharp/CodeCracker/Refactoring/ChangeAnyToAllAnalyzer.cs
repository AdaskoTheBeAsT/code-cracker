using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace CodeCracker.CSharp.Refactoring
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ChangeAnyToAllAnalyzer : DiagnosticAnalyzer
    {
        private const string speculativeAnnotationDescription = "ChangeAnyToAllAnalyzer_speculativeAnnotation";
        private static readonly SyntaxAnnotation speculativeAnnotation = new SyntaxAnnotation(speculativeAnnotationDescription);
        internal const string MessageAny = "Change Any to All";
        internal const string MessageAll = "Change All to Any";
        internal const string TitleAny = MessageAny;
        internal const string TitleAll = MessageAll;
        internal const string Category = SupportedCategories.Refactoring;

        internal static readonly DiagnosticDescriptor RuleAny = new DiagnosticDescriptor(
            DiagnosticId.ChangeAnyToAll.ToDiagnosticId(),
            TitleAny,
            MessageAny,
            Category,
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.ChangeAnyToAll));
        internal static readonly DiagnosticDescriptor RuleAll = new DiagnosticDescriptor(
            DiagnosticId.ChangeAllToAny.ToDiagnosticId(),
            TitleAll,
            MessageAll,
            Category,
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.ChangeAllToAny));

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(RuleAny, RuleAll);

        public override void Initialize(AnalysisContext context) => context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

        public static readonly SimpleNameSyntax allName = (SimpleNameSyntax)SyntaxFactory.ParseName("All");
        public static readonly SimpleNameSyntax anyName = (SimpleNameSyntax)SyntaxFactory.ParseName("Any");

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation.Parent == null) return;
            if (invocation.Parent.IsKind(SyntaxKind.ExpressionStatement)) return;
            var diagnosticToRaise = GetCorrespondingDiagnostic(context.SemanticModel, invocation);
            if (diagnosticToRaise == null) return;
            var diagnostic = Diagnostic.Create(diagnosticToRaise, GetName(invocation).GetLocation());
            context.ReportDiagnostic(diagnostic);
        }

        private static DiagnosticDescriptor GetCorrespondingDiagnostic(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
        {
            var methodName = GetName(invocation);
            var methodNameText = methodName?.ToString();
            var nameToCheck = methodNameText == "Any" ? allName : methodNameText == "All" ? anyName : null;
            if (nameToCheck == null) return null;
            var methodSymbol = semanticModel.GetSymbolInfo(invocation.Expression).Symbol as IMethodSymbol;
            if (methodSymbol?.Parameters.Length != 1) return null;
            if (!IsLambdaWithoutBody(invocation)) return null;
            if (!OtherMethodExists(invocation, nameToCheck, semanticModel)) return null;
            return methodNameText == "Any" ? RuleAny : RuleAll;
        }

        public static SimpleNameSyntax GetName(InvocationExpressionSyntax invocation)
        {
            SimpleNameSyntax methodName = null;
            if (invocation.Expression.IsKind(SyntaxKind.MemberBindingExpression))
                methodName = ((MemberBindingExpressionSyntax)invocation.Expression).Name;
            else if (invocation.Expression.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                methodName = ((MemberAccessExpressionSyntax)invocation.Expression).Name;
            return methodName;
        }

        private static bool IsLambdaWithoutBody(InvocationExpressionSyntax invocation)
        {
            var arg = invocation.ArgumentList?.Arguments.First();
            var lambda = arg.Expression as LambdaExpressionSyntax;
            if (lambda == null) return false;
            return !(lambda.Body is BlockSyntax);
        }

        private static bool OtherMethodExists(InvocationExpressionSyntax invocation, SimpleNameSyntax nameToCheck, SemanticModel semanticModel)
        {
            var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
            if (memberAccess == null) return false;
            
            var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol == null) return false;
            
            var receiverType = methodSymbol.ReceiverType;
            if (receiverType == null) return false;
            
            var targetMethodName = nameToCheck.ToString();
            var members = receiverType.GetMembers(targetMethodName);
            
            if (!members.Any()) return false;
            
            var targetMethod = members.OfType<IMethodSymbol>().FirstOrDefault(m => 
                m.Parameters.Length == 1 && 
                m.IsExtensionMethod == methodSymbol.IsExtensionMethod);
            
            return targetMethod != null;
        }

        public static ExpressionSyntax CreateExpressionWithNewName(InvocationExpressionSyntax invocation, SimpleNameSyntax nameToCheck)
        {
            var otherExpression = invocation.Expression.IsKind(SyntaxKind.MemberBindingExpression)
                ? (ExpressionSyntax)((MemberBindingExpressionSyntax)invocation.Expression).WithName(nameToCheck).WithAdditionalAnnotations(speculativeAnnotation)
                //avoid this, already checked before: if (invocation.Expression.IsKind(SyntaxKind.SimpleMemberAccessExpression)):
                : ((MemberAccessExpressionSyntax)invocation.Expression).WithName(nameToCheck).WithAdditionalAnnotations(speculativeAnnotation);
            return otherExpression;
        }
    }
}