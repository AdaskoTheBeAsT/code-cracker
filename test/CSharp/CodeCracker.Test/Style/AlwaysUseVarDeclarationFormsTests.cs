using CodeCracker.CSharp.Style;
using Microsoft.CodeAnalysis.CSharp;
using System.Threading.Tasks;
using Xunit;

namespace CodeCracker.Test.CSharp.Style
{
    public class AlwaysUseVarDeclarationFormsTests : CodeFixVerifier<AlwaysUseVarAnalyzer, AlwaysUseVarCodeFixProvider>
    {
        [Fact]
        public async Task SupportsOutVariable()
        {
            const string source = "class C { void M(string text) { if (int.TryParse(text, out int value)) { _ = value; } } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("out int", "out var"), LanguageVersion.CSharp7,
                diagnosticId: "CC0105", diagnosticText: "int");
        }

        [Fact]
        public async Task SupportsGenericOutVariableWithIndependentInference()
        {
            const string source = @"
class C
{
    static void Copy<T>(T input, out T output) => output = input;
    void M() { Copy(1, out int value); }
}";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("out int value", "out var value"),
                LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task SupportsConstructorOutVariable()
        {
            const string source = @"
class C
{
    C(out int value) { value = 1; }
    static void M() { _ = new C(out int value); }
}";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("new C(out int", "new C(out var"),
                LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task KeepsOutTypesNeededToResolveOverloads()
        {
            const string source = @"
class C
{
    static void Get(out int value) => value = 1;
    static void Get(out string value) => value = null;
    void M() { Get(out int value); }
}";
            await VerifyCompilerValidCSharpNoDiagnosticAsync(source, LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task KeepsOutTypeNeededForGenericInference()
        {
            const string source = "class C { static void Get<T>(out T value) => value = default(T); void M() { Get(out int value); } }";
            await VerifyCompilerValidCSharpNoDiagnosticAsync(source, LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task JointOutReplacementsMustNotMakeFixAllAmbiguous()
        {
            const string source = @"
class C
{
    static void Get(out int first, out int second) { first = 1; second = 2; }
    static void Get(out string first, out string second) { first = null; second = null; }
    void M() { Get(out int first, out int second); }
}";
            await VerifyCompilerValidCSharpNoDiagnosticAsync(source, LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task MultipleOutVariablesCanBeFixedTogetherWhenUnambiguous()
        {
            const string source = @"
class C
{
    static void Get(out int first, out int second) { first = 1; second = 2; }
    void M() { Get(out int first, out int second); }
}";
            await VerifyCompilerValidCSharpFixAsync(source,
                source.Replace("Get(out int first, out int second);", "Get(out var first, out var second);"),
                LanguageVersion.CSharp7, diagnosticCount: 2, fixAll: true);
        }

        [Fact]
        public async Task OutDeclarationsWithoutStatementBindingAreConservativelyIgnored()
        {
            const string source = "class C { bool M(string text) => int.TryParse(text, out int value); }";
            await VerifyCompilerValidCSharpNoDiagnosticAsync(source, LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task KeepsAlreadyInferredOutVariablesAndTypedPatterns()
        {
            const string source = "class C { void M(object item) { _ = int.TryParse(\"1\", out var value); if (item is int number) { _ = number; } } }";
            await VerifyCompilerValidCSharpNoDiagnosticAsync(source, LanguageVersion.CSharp7);
        }

        [Theory]
        [InlineData("(int count, string name)", "var (count, name)", "(1, \"name\")")]
        [InlineData("(int count, (int first, int second))", "var (count, (first, second))", "(1, (2, 3))")]
        [InlineData("(int count, int _)", "var (count, _)", "(1, 2)")]
        [InlineData("(var count, string name)", "var (count, name)", "(1, \"name\")")]
        public async Task SupportsTypedDeconstruction(string declaration, string inferred, string initializer)
        {
            var source = "class C { void M() { " + declaration + " = " + initializer + "; } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace(declaration, inferred), LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task SupportsCustomDeconstructMethod()
        {
            const string source = @"
class Pair
{
    public void Deconstruct(out int count, out string name) { count = 1; name = ""name""; }
}
class C { void M(Pair pair) { (int count, string name) = pair; } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("(int count, string name)", "var (count, name)"),
                LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task SupportsGenericDeconstructAndNestedDiscards()
        {
            const string source = @"
class Pair<T>
{
    public void Deconstruct(out T count, out (int, int) details) { count = default(T); details = (1, 2); }
}
class C { void M(Pair<int> pair) { (int count, (int first, int _)) = pair; } }";
            await VerifyCompilerValidCSharpFixAsync(source,
                source.Replace("(int count, (int first, int _))", "var (count, (first, _))"), LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task DeconstructionRetainsElementComments()
        {
            const string source = "class C { void M() { (/* count */ int count, /* name */ string name) = (1, \"name\"); } }";
            const string expected = "class C { void M() { var (/* count */ count, /* name */ name) = (1, \"name\"); } }";
            await VerifyCompilerValidCSharpFixAsync(source, expected, LanguageVersion.CSharp7);
        }

        [Theory]
        [InlineData("(long first, int second) = (1, 2);")]
        [InlineData("var (first, second) = (1, 2);")]
        [InlineData("(var first, var second) = (1, 2);")]
        [InlineData("var first = 0; (first, int second) = (1, 2);")]
        public async Task SkipsChangingOrAlreadyInferredDeconstruction(string declaration)
        {
            await VerifyCompilerValidCSharpNoDiagnosticAsync("class C { void M() { " + declaration + " } }", LanguageVersion.CSharp10);
        }

        [Fact]
        public async Task KeepsAmbiguousDeconstructOverload()
        {
            const string source = @"
class Pair
{
    public void Deconstruct(out int count, out int number) { count = 1; number = 2; }
    public void Deconstruct(out long count, out long number) { count = 1; number = 2; }
}
class C { void M(Pair pair) { (int count, int number) = pair; } }";
            // Typed deconstruction does not resolve this ambiguity either.
            await VerifyCSharpHasNoDiagnosticsAsync(source, LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task SupportsForInitializerWithoutMovingItsScope()
        {
            const string source = "class C { void M() { for (int index = 0; index < 2; index++) { _ = index; } } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("int index", "var index"), LanguageVersion.CSharp7);
        }

        [Theory]
        [InlineData(LanguageVersion.CSharp8)]
        [InlineData(LanguageVersion.CSharp9)]
        public async Task ForInitializerRespectsNaturalDelegateLanguageBoundary(LanguageVersion language)
        {
            const string source = "class C { void M() { for (System.Func<int> factory = () => 1; false;) { } } }";
            await VerifyCompilerValidCSharpNoDiagnosticAsync(source, language);
        }

        [Fact]
        public async Task ForInitializerCanInferNaturalDelegateInCSharp10()
        {
            const string source = "class C { void M() { for (System.Func<int> factory = () => 1; false;) { } } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("System.Func<int> factory", "var factory"),
                LanguageVersion.CSharp10);
        }

        [Theory]
        [InlineData("for (int first = 0, second = 1; first < second; first++) { }")]
        [InlineData("for (long index = 0; index < 2; index++) { }")]
        [InlineData("for (var index = 0; index < 2; index++) { }")]
        [InlineData("for (System.Collections.Generic.List<int> items = new(); items.Count < 0;) { }")]
        public async Task SkipsUnsupportedOrTypeDependentForInitializers(string loop)
        {
            await VerifyCompilerValidCSharpNoDiagnosticAsync("class C { void M() { " + loop + " } }", LanguageVersion.CSharp9);
        }

        [Fact]
        public async Task SupportsForeachWithSameIterationType()
        {
            const string source = "class C { void M(int[] items) { foreach (int item in items) { _ = item; } } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("int item", "var item"), LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task PreservesPatternBasedEnumeration()
        {
            const string source = @"
class Items { public Enumerator GetEnumerator() => new Enumerator(); }
struct Enumerator { public bool MoveNext() => false; public int Current => 1; }
class C { void M(Items items) { foreach (int item in items) { _ = item; } } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("int item", "var item"), LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task PreservesExtensionGetEnumerator()
        {
            const string source = @"
class Items { }
static class Extensions { public static Enumerator GetEnumerator(this Items items) => new Enumerator(); }
struct Enumerator { public bool MoveNext() => false; public int Current => 1; }
class C { void M(Items items) { foreach (int item in items) { _ = item; } } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("int item", "var item"), LanguageVersion.CSharp9);
        }

        [Theory]
        [InlineData("foreach (long item in items) { }")]
        [InlineData("foreach (object item in items) { }")]
        [InlineData("foreach (var item in items) { }")]
        public async Task PreservesExplicitForeachConversions(string loop)
        {
            await VerifyCompilerValidCSharpNoDiagnosticAsync("class C { void M(int[] items) { " + loop + " } }", LanguageVersion.CSharp7);
        }

        [Theory]
        [InlineData("ref int", "ref var")]
        [InlineData("ref readonly int", "ref readonly var")]
        public async Task SupportsRefForeach(string type, string inferred)
        {
            var source = "class C { void M(System.Span<int> items) { foreach (" + type + " item in items) { _ = item; } } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace(type + " item", inferred + " item"),
                LanguageVersion.CSharp7_3);
        }

        [Fact]
        public async Task SupportsAwaitForeach()
        {
            const string source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
class C
{
    async Task M(IAsyncEnumerable<int> items)
    {
        await foreach (int item in items) { _ = item; }
    }
}";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("int item", "var item"), LanguageVersion.CSharp8);
        }

        [Fact]
        public async Task SupportsDeconstructingForeach()
        {
            const string source = "class C { void M((int, string)[] items) { foreach ((int count, string name) in items) { _ = count; } } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("(int count, string name)", "var (count, name)"),
                LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task RefForeachKeepsArrayMutation()
        {
            const string source = @"
using System;
public class C
{
    public static int Run()
    {
        var items = new[] { 1 };
        foreach (ref int item in items.AsSpan()) { item = 42; }
        return items[0];
    }
}";
            var document = await VerifyCompilerValidCSharpFixAsync(source, source.Replace("ref int item", "ref var item"),
                LanguageVersion.CSharp7_3);
            Assert.Equal(42, await ExecuteCompilerValidCSharpAsync(document, "C", "Run"));
        }

        [Fact]
        public async Task SupportsAwaitDeconstructingForeach()
        {
            const string source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
class C
{
    async Task M(IAsyncEnumerable<(int, string)> items)
    {
        await foreach ((int count, string name) in items) { _ = count; }
    }
}";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("(int count, string name)", "var (count, name)"),
                LanguageVersion.CSharp8);
        }

        [Fact]
        public async Task KeepsDeconstructingForeachConversions()
        {
            const string source = "class C { void M((int, int)[] items) { foreach ((long first, int second) in items) { } } }";
            await VerifyCompilerValidCSharpNoDiagnosticAsync(source, LanguageVersion.CSharp7);
        }

        [Theory]
        [InlineData("int.TryParse(\"1\", out int );")]
        [InlineData("(int first, string ) = ;")]
        [InlineData("for (int index = ;")]
        [InlineData("foreach (ref readonly int item in ) { }")]
        public async Task IncompleteDeclarationFormsDoNotThrow(string statement)
        {
            await VerifyCSharpHasNoDiagnosticsAsync("class C { void M() { " + statement + " } }", LanguageVersion.CSharp12);
        }
    }
}
