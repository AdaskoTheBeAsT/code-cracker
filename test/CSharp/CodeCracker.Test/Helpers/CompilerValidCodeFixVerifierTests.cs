using CodeCracker.CSharp.Style;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace CodeCracker.Test.CSharp.Helpers
{
    public class CompilerValidCodeFixVerifierTests : CodeFixVerifier<AlwaysUseVarAnalyzer, AlwaysUseVarCodeFixProvider>
    {
        [Fact]
        public async Task RejectsInvalidOriginalSource()
        {
            await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() =>
                VerifyCompilerValidCSharpNoDiagnosticAsync("class C { void M() { var x = Missing(); } }", LanguageVersion.CSharp12));
        }

        [Fact]
        public async Task RejectsFixWithoutDiagnostic()
        {
            await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() =>
                VerifyCompilerValidCSharpFixAsync("class C { void M() { var x = 1; } }",
                    "class C { void M() { var x = 1; } }", LanguageVersion.CSharp12));
        }

        [Fact]
        public async Task SupportsUnsafeCompilation()
        {
            const string source = "unsafe class C { void M() { var p = (int*)0; } }";
            await VerifyCompilerValidCSharpNoDiagnosticAsync(source, LanguageVersion.CSharp9,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() =>
                VerifyCompilerValidCSharpNoDiagnosticAsync(source, LanguageVersion.CSharp9));
        }

        [Fact]
        public async Task SupportsExecutableTopLevelStatements()
        {
            await VerifyCompilerValidCSharpFixAsync("int x = 1;", "var x = 1;", LanguageVersion.CSharp9,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication), diagnosticId: "CC0105", diagnosticText: "int");
        }

        [Fact]
        public async Task RejectsInvalidExpectedSource()
        {
            await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() =>
                VerifyCompilerValidCSharpFixAsync("class C { void M() { int x = 1; } }",
                    "class C { void M() { var x = Missing(); } }", LanguageVersion.CSharp12));
        }

        [Fact]
        public async Task CanExecuteCompilerValidSource()
        {
            var document = CreateCompilerValidCSharpDocument("public class C { public static int Run() => 42; }",
                LanguageVersion.CSharp12);
            Assert.Equal(42, await ExecuteCompilerValidCSharpAsync(document, "C", "Run"));
        }

        [Fact]
        public async Task RejectsLifetimeErrorsReportedOnlyDuringEmit()
        {
            const string source = @"
class C
{
    async System.Threading.Tasks.Task M()
    {
        var values = new System.Span<int>(new int[1]);
        await System.Threading.Tasks.Task.Yield();
        _ = values.Length;
    }
}";
            await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() =>
                VerifyCompilerValidCSharpNoDiagnosticAsync(source, LanguageVersion.CSharp13));
        }

        [Fact]
        public async Task SupportsAdditionalMetadataReferences()
        {
            var compilation = CSharpCompilation.Create("Extra",
                new[] { CSharpSyntaxTree.ParseText("public class Extra { }", cancellationToken: TestContext.Current.CancellationToken) },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (var stream = new MemoryStream())
            {
                Assert.True(compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success);
                var reference = MetadataReference.CreateFromImage(stream.ToArray());
                var document = CreateCompilerValidCSharpDocument("class C { Extra M() => new Extra(); }",
                    LanguageVersion.CSharp9, additionalReferences: new[] { reference });
                await AssertCompilesAsync(document);
            }
        }
    }
}
