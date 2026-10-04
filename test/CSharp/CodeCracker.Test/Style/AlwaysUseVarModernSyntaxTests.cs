using CodeCracker.CSharp.Style;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Threading.Tasks;
using Xunit;

namespace CodeCracker.Test.CSharp.Style
{
    public class AlwaysUseVarModernSyntaxTests : CodeFixVerifier<AlwaysUseVarAnalyzer, AlwaysUseVarCodeFixProvider>
    {
        private static string Source(string declaration) => @"
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
class C
{
    static int GetValue() => 1;
    static int Transform(int x) => x;
    static string Transform(string x) => x;
    static T Identity<T>(T x) => x;
    delegate int CustomFactory();
    void M(bool flag, int[] items)
    {
        " + declaration + @"
    }
}";

        [Theory]
        [InlineData("int[] values = [..items];")]
        [InlineData("Span<int> values = [1, ..items];")]
        [InlineData("ReadOnlySpan<int> values = [..items, 2];")]
        [InlineData("IEnumerable<int> values = [..items];")]
        [InlineData("ImmutableArray<int> values = [1, ..items];")]
        [InlineData("int[][] values = [[1], [..items]];")]
        [InlineData("int[] values = flag ? [..items] : [];")]
        [InlineData("Func<int, int> callback = (int x = 1) => x;")]
        [InlineData("Func<int[], int> callback = (params int[] x) => x.Length;")]
        [InlineData("CustomFactory callback = static () => 1;")]
        [InlineData("Func<int> callback = () => default;")]
        [InlineData("Func<object> callback = () => new();")]
        [InlineData("Func<int, int> callback = Transform;")]
        [InlineData("Func<int, int> callback = Identity;")]
        public async Task SkipsTargetDependentInitializers(string declaration)
        {
            await VerifyCompilerValidCSharpNoDiagnosticAsync(Source(declaration), LanguageVersion.CSharp12);
        }

        [Theory]
        [InlineData("int[] values = (int[])[..items];", "var values = (int[])[..items];", LanguageVersion.CSharp12)]
        [InlineData("ImmutableArray<int> values = ImmutableArray.Create(1, 2);", "var values = ImmutableArray.Create(1, 2);", LanguageVersion.CSharp12)]
        [InlineData("Func<int> callback = static () => 1;", "var callback = static () => 1;", LanguageVersion.CSharp10)]
        [InlineData("Func<int> callback = int () => 1;", "var callback = int () => 1;", LanguageVersion.CSharp10)]
        [InlineData("Func<int, int> callback = static (int x) => x;", "var callback = static (int x) => x;", LanguageVersion.CSharp10)]
        [InlineData("Func<Task<int>> callback = async () => { await Task.Yield(); return 1; };", "var callback = async () => { await Task.Yield(); return 1; };", LanguageVersion.CSharp10)]
        [InlineData("Func<int> callback = GetValue;", "var callback = GetValue;", LanguageVersion.CSharp10)]
        [InlineData("Func<int, int> callback = Identity<int>;", "var callback = Identity<int>;", LanguageVersion.CSharp10)]
        [InlineData("Func<int> callback = [System.Obsolete] () => 1;", "var callback = [System.Obsolete] () => 1;", LanguageVersion.CSharp10)]
        public async Task SupportsIndependentCollectionAndDelegateInference(string declaration, string expected, LanguageVersion language)
        {
            var source = Source(declaration);
            await VerifyCompilerValidCSharpFixAsync(source, source.Replace(declaration, expected), language,
                diagnosticId: "CC0001");
        }

        [Theory]
        [InlineData("ref int", "ref var", LanguageVersion.CSharp7_2)]
        [InlineData("ref readonly int", "ref readonly var", LanguageVersion.CSharp7_2)]
        [InlineData("scoped ref int", "scoped ref var", LanguageVersion.CSharp11)]
        [InlineData("scoped ref readonly int", "scoped ref readonly var", LanguageVersion.CSharp11)]
        public async Task ConditionalReferencesPreserveAliasing(string type, string inferredType, LanguageVersion language)
        {
            var source = @"
public class C
{
    public static int Run()
    {
        var left = 1;
        var right = 2;
        var flag = true;
        " + type + @" selected = ref (flag ? ref left : ref right);
        " + (type.Contains("readonly") ? "return selected;" : "selected = 42; return left;") + @"
    }
}";
            var fixedDocument = await VerifyCompilerValidCSharpFixAsync(source,
                source.Replace(type + " selected", inferredType + " selected"), language,
                diagnosticId: "CC0105", diagnosticText: "int");
            Assert.Equal(type.Contains("readonly") ? 1 : 42,
                await ExecuteCompilerValidCSharpAsync(fixedDocument, "C", "Run"));
        }

        [Fact]
        public async Task PreservesGenericRefReturnAndRefIndexer()
        {
            const string source = @"
class C<T>
{
    private T field;
    ref T GetValue() => ref field;
    ref T this[int index] => ref field;
    void M()
    {
        ref T first = ref GetValue();
        ref readonly T second = ref this[0];
    }
}";
            await VerifyCompilerValidCSharpFixAsync(source,
                source.Replace("ref T first", "ref var first").Replace("readonly T second", "readonly var second"),
                LanguageVersion.CSharp7_2, diagnosticCount: 2, fixAll: true);
        }

        [Fact]
        public async Task SupportsCustomCollectionBuilderOnlyWithExplicitCast()
        {
            const string fixture = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
[CollectionBuilder(typeof(BufferBuilder), nameof(BufferBuilder.Create))]
public readonly struct Buffer : IEnumerable<int>
{
    private readonly int[] items;
    public Buffer(int[] items) => this.items = items;
    public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
public static class BufferBuilder
{
    public static Buffer Create(ReadOnlySpan<int> items) => new Buffer(items.ToArray());
}
class C { void M(int[] items) { DECLARATION } }";
            await VerifyCompilerValidCSharpNoDiagnosticAsync(fixture.Replace("DECLARATION", "Buffer buffer = [1, ..items];"),
                LanguageVersion.CSharp12);
            await VerifyCompilerValidCSharpFixAsync(fixture.Replace("DECLARATION", "Buffer buffer = (Buffer)([1, ..items]);"),
                fixture.Replace("DECLARATION", "var buffer = (Buffer)([1, ..items]);"), LanguageVersion.CSharp12);
        }

        [Fact]
        public async Task TopLevelMultipleDeclarationsRemainGlobalStatements()
        {
            await VerifyCompilerValidCSharpFixAsync("int first = 1, second = first + 1;",
                "var first = 1;\r\n\r\nvar second = first + 1;", LanguageVersion.CSharp9,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        }

        [Theory]
        [InlineData(LanguageVersion.CSharp8)]
        [InlineData(LanguageVersion.CSharp9)]
        public async Task DoesNotInferLambdaBeforeNaturalTypesExist(LanguageVersion language)
        {
            await VerifyCompilerValidCSharpNoDiagnosticAsync(Source("Func<int> callback = () => 1;"), language);
        }

        [Theory]
        [InlineData("int[] values = [..")]
        [InlineData("Func<int> callback = static () =>")]
        [InlineData("ref readonly int value = ref (")]
        public async Task IncompleteModernInitializersDoNotThrow(string declaration)
        {
            await VerifyCSharpHasNoDiagnosticsAsync(Source(declaration), LanguageVersion.CSharp12);
        }
    }
}
