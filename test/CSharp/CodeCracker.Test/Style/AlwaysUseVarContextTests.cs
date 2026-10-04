using CodeCracker.CSharp.Style;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace CodeCracker.Test.CSharp.Style
{
    public class AlwaysUseVarContextTests : CodeFixVerifier<AlwaysUseVarAnalyzer, AlwaysUseVarCodeFixProvider>
    {
        [Fact]
        public async Task RefStructsStayWithinAsyncScopes()
        {
            const string source = @"
using System;
using System.Threading.Tasks;
class C
{
    async Task M(int[] items)
    {
        await Task.Yield();
        {
            Span<int> values = new Span<int>(items);
            ref int first = ref values[0];
            first++;
        }
        await Task.Yield();
    }
}";
            await VerifyCompilerValidCSharpFixAsync(source,
                source.Replace("Span<int> values", "var values").Replace("ref int first", "ref var first"),
                LanguageVersion.CSharp13, diagnosticCount: 2, fixAll: true);
        }

        [Fact]
        public async Task RefStructsStayWithinIteratorScopes()
        {
            const string source = @"
using System;
using System.Collections.Generic;
class C
{
    IEnumerable<int> M(int[] items)
    {
        var count = 0;
        {
            scoped Span<int> values = new Span<int>(items);
            count = values.Length;
        }
        yield return count;
    }
}";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("Span<int> values", "var values"),
                LanguageVersion.CSharp13);
        }

        [Fact]
        public async Task PatternBasedRefStructDisposalStillRuns()
        {
            const string source = @"
public ref struct Scope
{
    public static int Count;
    public void Dispose() => Count++;
}
public class C
{
    public static int Run()
    {
        { using Scope scope = new Scope(); }
        return Scope.Count;
    }
}";
            var document = await VerifyCompilerValidCSharpFixAsync(source, source.Replace("Scope scope", "var scope"),
                LanguageVersion.CSharp8);
            Assert.Equal(1, await ExecuteCompilerValidCSharpAsync(document, "C", "Run"));
        }

        [Fact]
        public async Task SupportsFieldBackedProperty()
        {
            const string source = @"
class C
{
    public int Value
    {
        get { int current = field; return current; }
        set => field = value;
    }
}";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("int current", "var current"),
                LanguageVersion.CSharp14, diagnosticId: "CC0105");
        }

        [Fact]
        public async Task SupportsGenericExtensionBlocks()
        {
            const string source = @"
using System.Collections.Generic;
static class Extensions
{
    extension<T>(List<T> items)
    {
        public int CountAgain() { int count = items.Count; return count; }
        public int Size { get { int size = items.Count; return size; } }
    }
}";
            await VerifyCompilerValidCSharpFixAsync(source,
                source.Replace("int count", "var count").Replace("int size", "var size"),
                LanguageVersion.CSharp14, diagnosticCount: 2, fixAll: true);
        }

        [Theory]
        [InlineData("System.Span<int> values = items;")]
        [InlineData("System.ReadOnlySpan<int> values = items;")]
        [InlineData("System.ReadOnlySpan<int> values = items.AsSpan();")]
        public async Task KeepsExplicitSpanConversions(string declaration)
        {
            var source = "using System; class C { void M(int[] items) { " + declaration + " } }";
            await VerifyCompilerValidCSharpNoDiagnosticAsync(source, LanguageVersion.CSharp14);
        }

        [Fact]
        public async Task PreservesOverloadSelectedForExplicitSpanCast()
        {
            const string source = @"
using System;
public class C
{
    static int Choose(int[] value) => 1;
    static int Choose(ReadOnlySpan<int> value) => 2;
    public static int Run()
    {
        ReadOnlySpan<int> values = (ReadOnlySpan<int>)new int[1];
        return Choose(values);
    }
}";
            var document = await VerifyCompilerValidCSharpFixAsync(source,
                source.Replace("ReadOnlySpan<int> values", "var values"), LanguageVersion.CSharp14);
            Assert.Equal(2, await ExecuteCompilerValidCSharpAsync(document, "C", "Run"));
        }

        [Theory]
        [InlineData("record Item<T>(T Value);", "Item<int> value = item with { Value = 2 };",
            "Item<int>", LanguageVersion.CSharp9)]
        [InlineData("record struct Item<T>(T Value);", "Item<int> value = item with { Value = 2 };",
            "Item<int>", LanguageVersion.CSharp10)]
        public async Task SupportsRecordWithExpressions(string record, string declaration, string type, LanguageVersion language)
        {
            var source = record + " class C { void M(" + type + " item) { " + declaration + " } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace(type + " value", "var value"), language);
        }

        [Fact]
        public async Task SupportsPrimaryConstructorCapture()
        {
            const string source = "class C(int seed) { int M() { int value = seed; return value; } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("int value", "var value"), LanguageVersion.CSharp12);
        }

        [Fact]
        public async Task PreservesNestedNullableTupleNames()
        {
            const string source = @"
#nullable enable
using System.Collections.Generic;
class C
{
    void M()
    {
        List<(int Count, (string? Name, int Total) Details)>? values =
            new List<(int Count, (string? Name, int Total) Details)>();
    }
}";
            await VerifyCompilerValidCSharpFixAsync(source,
                source.Replace("List<(int Count, (string? Name, int Total) Details)>? values", "var values"),
                LanguageVersion.CSharp8);
        }

        [Fact]
        public async Task ConservativelyKeepsNullableGenericLocalFunction()
        {
            const string source = @"
#nullable enable
class C
{
    void M<T>(T? item) where T : class
    {
        T? Local() { T? copy = item; return copy; }
        System.Func<T?> callback = () => { T? copy = item; return copy; };
        _ = Local();
        _ = callback();
    }
}";
            await VerifyCompilerValidCSharpNoDiagnosticAsync(source, LanguageVersion.CSharp8);
        }

        [Fact]
        public async Task SupportsGenericLocalFunctionAndLambda()
        {
            const string source = @"
class C
{
    void M<T>(T item)
    {
        T Local() { T copy = item; return copy; }
        System.Func<T> callback = () => { T copy = item; return copy; };
        _ = Local();
        _ = callback();
    }
}";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("T copy", "var copy"),
                LanguageVersion.CSharp8, diagnosticCount: 2, fixAll: true);
        }

        [Theory]
        [InlineData("value is int number", "number", LanguageVersion.CSharp7)]
        [InlineData("value is int number and > 0", "number", LanguageVersion.CSharp9)]
        [InlineData("value is int[] { Length: 1 } numbers", "numbers.Length", LanguageVersion.CSharp8)]
        [InlineData("value is int[] numbers && numbers is [var number, ..]", "number", LanguageVersion.CSharp11)]
        public async Task PatternTypesAreNotRewrittenButNearbyLocalsCanBe(string pattern, string initializer, LanguageVersion language)
        {
            var source = "class C { void M(object value) { if (" + pattern + ") { int copy = " + initializer + "; } } }";
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace("int copy", "var copy"), language);
        }

        [Fact]
        public async Task DependentMultipleLocalsKeepTheirTypes()
        {
            const string source = "class C { void M() { int first = 1, second = first + 1; } }";
            await VerifyCompilerValidCSharpFixAsync(source,
                "class C { void M() { var first = 1; var second = first + 1; } }", LanguageVersion.CSharp7);
        }

        [Fact]
        public async Task BuiltInArrayAccessNeedsNoMemberSymbol()
        {
            const string source = "class C { void M(int[] items) { int copy = items[0]; ref int alias = ref items[0]; } }";
            await VerifyCompilerValidCSharpFixAsync(source,
                source.Replace("int copy", "var copy").Replace("ref int alias", "ref var alias"),
                LanguageVersion.CSharp7_2, diagnosticCount: 2, fixAll: true);
        }

        [Fact]
        public async Task InvalidAsyncLifetimeDoesNotThrow()
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
            var document = CreateCompilerValidCSharpDocument(source, LanguageVersion.CSharp13);
            var compilation = await document.Project.GetCompilationAsync(TestContext.Current.CancellationToken);
            using (var stream = new MemoryStream())
                Assert.Contains(compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Diagnostics,
                    d => d.Id == "CS4007");
            await VerifyCSharpHasNoDiagnosticsAsync(source, LanguageVersion.CSharp13);
        }
    }
}
