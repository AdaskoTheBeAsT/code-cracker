using CodeCracker.CSharp.Style;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Threading.Tasks;
using Xunit;

namespace CodeCracker.Test.CSharp.Style
{
    public class AlwaysUseVarUnsafeSyntaxTests : CodeFixVerifier<AlwaysUseVarAnalyzer, AlwaysUseVarCodeFixProvider>
    {
        private static readonly CSharpCompilationOptions UnsafeOptions =
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);

        private static string Source(string declaration) => @"
using System;
unsafe class C
{
    static int Transform(int value) => value;
    static delegate* unmanaged[Cdecl]<int, int> GetCallback() => (delegate* unmanaged[Cdecl]<int, int>)0;
    void M(bool flag)
    {
        " + declaration + @"
    }
}";

        [Theory]
        [InlineData("int* pointer = (int*)0;", "var pointer = (int*)0;", LanguageVersion.CSharp9)]
        [InlineData("int* pointer = stackalloc int[4];", "var pointer = stackalloc int[4];", LanguageVersion.CSharp7_3)]
        [InlineData("int* pointer = stackalloc[] { 1, 2 };", "var pointer = stackalloc[] { 1, 2 };", LanguageVersion.CSharp9)]
        [InlineData("delegate*<int, int> pointer = (delegate*<int, int>)&Transform;", "var pointer = (delegate*<int, int>)&Transform;", LanguageVersion.CSharp9)]
        [InlineData("delegate* managed<int, int> pointer = (delegate* managed<int, int>)&Transform;", "var pointer = (delegate* managed<int, int>)&Transform;", LanguageVersion.CSharp9)]
        [InlineData("delegate* unmanaged[Cdecl]<int, int> pointer = GetCallback();", "var pointer = GetCallback();", LanguageVersion.CSharp9)]
        [InlineData("Span<int> values = flag ? stackalloc int[2] : stackalloc int[4];", "var values = flag ? stackalloc int[2] : stackalloc int[4];", LanguageVersion.CSharp9)]
        public async Task PreservesIndependentlyInferredPointers(string declaration, string expected, LanguageVersion language)
        {
            var source = Source(declaration);
            // Function-pointer members themselves require C# 9.
            if (language == LanguageVersion.CSharp7_3)
                source = source.Replace("    static delegate* unmanaged[Cdecl]<int, int> GetCallback() => (delegate* unmanaged[Cdecl]<int, int>)0;", "");
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace(declaration, expected), language,
                UnsafeOptions, diagnosticId: "CC0001");
        }

        [Theory]
        [InlineData("Span<int> values = stackalloc int[4];")]
        [InlineData("ReadOnlySpan<int> values = stackalloc[] { 1, 2 };")]
        [InlineData("scoped Span<int> values = stackalloc int[4];")]
        [InlineData("delegate*<int, int> pointer = &Transform;")]
        [InlineData("void* pointer = (int*)0;")]
        [InlineData("var pointer = (int*)0;")]
        public async Task RejectsTargetDependentOrAlreadyInferredPointerDeclarations(string declaration)
        {
            await VerifyCompilerValidCSharpNoDiagnosticAsync(Source(declaration), LanguageVersion.CSharp11, UnsafeOptions);
        }

        [Fact]
        public async Task SupportsExplicitSpanCastInSafeCode()
        {
            const string source = "class C { void M() { System.Span<int> values = (System.Span<int>)stackalloc int[4]; } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("System.Span<int> values", "var values"),
                LanguageVersion.CSharp9, diagnosticId: "CC0001");
        }

        [Theory]
        [InlineData("delegate*<int,")]
        [InlineData("delegate* unmanaged[")]
        [InlineData("int* pointer = stackalloc")]
        public async Task IncompleteUnsafeSyntaxDoesNotThrow(string declaration)
        {
            await VerifyCSharpHasNoDiagnosticsAsync(Source(declaration), LanguageVersion.CSharp11);
        }
    }
}
