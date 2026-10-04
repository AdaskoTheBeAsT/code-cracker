using CodeCracker.CSharp;
using CodeCracker.CSharp.Style;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CodeCracker.Test.CSharp.Style
{
    [CollectionDefinition("AlwaysUseVar performance", DisableParallelization = true)]
    public class AlwaysUseVarPerformanceCollection
    {
    }

    [Collection("AlwaysUseVar performance")]
    public class AlwaysUseVarPerformanceTests : CodeFixVerifier<AlwaysUseVarAnalyzer, AlwaysUseVarCodeFixProvider>
    {
        private readonly ITestOutputHelper output;

        public AlwaysUseVarPerformanceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public async Task CompareDeclarationHeavySourceWithPreExpansionAnalyzer()
        {
            var source = new StringBuilder("class C { void M() {");
            for (var i = 0; i < 500; i++)
                source.Append($"int typed{i} = {i}; var inferred{i} = {i};");
            source.Append("} }");
            var document = CreateCompilerValidCSharpDocument(source.ToString(), LanguageVersion.CSharp12);
            await AssertCompilesAsync(document);
            var compilation = await document.Project.GetCompilationAsync(TestContext.Current.CancellationToken);
            var baseline = new PreExpansionAnalyzer();
            var current = new AlwaysUseVarAnalyzer();
            await MeasureAsync(compilation, baseline, 1);
            await MeasureAsync(compilation, current, 1);
            var before = await MeasureAsync(compilation, baseline, 3);
            var after = await MeasureAsync(compilation, current, 3);
            output.WriteLine($"Pre-expansion: {before.ElapsedMilliseconds} ms, {before.Allocations} bytes; current: {after.ElapsedMilliseconds} ms, {after.Allocations} bytes (3 runs, 1,000 declarations each).");
            // Report measurements, not a machine-dependent performance threshold.
            Assert.Equal(1500, before.Diagnostics);
            Assert.Equal(before.Diagnostics, after.Diagnostics);
        }

        private static async Task<Measurement> MeasureAsync(Compilation compilation, DiagnosticAnalyzer analyzer, int repetitions)
        {
            var allocations = GC.GetTotalAllocatedBytes(true);
            var stopwatch = Stopwatch.StartNew();
            var diagnostics = 0;
            for (var i = 0; i < repetitions; i++)
                diagnostics += (await compilation.WithAnalyzers(ImmutableArray.Create(analyzer))
                    .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken)).Length;
            stopwatch.Stop();
            return new Measurement { ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                Allocations = GC.GetTotalAllocatedBytes(true) - allocations, Diagnostics = diagnostics };
        }

        private sealed class Measurement
        {
            internal long ElapsedMilliseconds;
            internal long Allocations;
            internal int Diagnostics;
        }

        // Pre-expansion local-only path for this ordinary-int declaration fixture.
#pragma warning disable RS1001 // Test-only analyzer, never shipped as a compiler extension.
        private sealed class PreExpansionAnalyzer : DiagnosticAnalyzer
        {
            private static readonly ImmutableArray<DiagnosticDescriptor> Rules = new AlwaysUseVarAnalyzer().SupportedDiagnostics;
            private static readonly DiagnosticDescriptor PrimitiveRule = Rules.First(d => d.Id == "CC0105");
            public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
                Rules;

#pragma warning disable RS1025, RS1026 // Match the pre-expansion analyzer's registration for the comparison.
            public override void Initialize(AnalysisContext context) =>
                context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.LocalDeclarationStatement);
#pragma warning restore RS1025, RS1026

            private static void Analyze(SyntaxNodeAnalysisContext context)
            {
                if (context.IsGenerated()) return;
                var statement = (LocalDeclarationStatementSyntax)context.Node;
                if (statement.IsConst) return;
                var declaration = statement.Declaration;
                var typeSyntax = declaration.Type;
                if (typeSyntax.IsVar || declaration.ContainsDiagnostics) return;
                var type = context.SemanticModel.GetTypeInfo(typeSyntax).ConvertedType;
                if (type == null || type.TypeKind == TypeKind.Error) return;
                foreach (var variable in declaration.Variables)
                {
                    if (variable.Initializer == null) return;
                    var replacement = statement.WithDeclaration(declaration.WithType(SyntaxFactory.IdentifierName("var"))
                        .WithVariables(SyntaxFactory.SingletonSeparatedList(variable)));
                    if (!context.SemanticModel.TryGetSpeculativeSemanticModel(statement.SpanStart, replacement, out var inferred)) return;
                    var symbol = inferred.GetDeclaredSymbol(replacement.Declaration.Variables[0]) as ILocalSymbol;
                    if (symbol == null || !SymbolEqualityComparer.IncludeNullability.Equals(type, symbol.Type)) return;
                }
                context.ReportDiagnostic(Diagnostic.Create(PrimitiveRule, typeSyntax.GetLocation()));
            }
        }
#pragma warning restore RS1001
    }
}
