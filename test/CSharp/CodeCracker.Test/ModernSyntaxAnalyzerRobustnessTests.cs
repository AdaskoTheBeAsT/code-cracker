#if NET11_0_OR_GREATER
using CodeCracker.CSharp.Refactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeCracker.Test.CSharp
{
    public class ModernSyntaxAnalyzerRobustnessTests
    {
        private static readonly Type[] AnalyzerTypes = typeof(PropertyChangedEventArgsUnnecessaryAllocationAnalyzer).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type)
                && type.GetCustomAttribute<DiagnosticAnalyzerAttribute>()?.Languages.Contains(LanguageNames.CSharp) == true)
            .OrderBy(type => type.FullName)
            .ToArray();

        private static readonly ImmutableArray<MetadataReference> References =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToImmutableArray();

        public static IEnumerable<object[]> ValidSources
        {
            get
            {
                foreach (var sample in Samples())
                {
                    yield return new object[] { sample.Name, sample.Source, sample.Preview, false };
                    yield return new object[] { sample.Name, sample.Source, sample.Preview, true };
                }
            }
        }

        public static IEnumerable<object[]> IncompleteSources
        {
            get
            {
                foreach (var sample in Samples())
                {
                    var tree = CSharpSyntaxTree.ParseText(sample.Source,
                        ParseOptions(sample.Preview, sample.Source.StartsWith("#!", StringComparison.Ordinal)));
                    var tokens = tree.GetRoot().DescendantTokens().Where(token => !token.IsMissing).ToArray();
                    foreach (var fraction in new[] { 1, 2, 3 })
                    {
                        var position = tokens[tokens.Length * fraction / 4].SpanStart;
                        yield return new object[] { sample.Name + " prefix " + fraction, sample.Source.Substring(0, position), sample.Preview };
                    }
                }

                yield return new object[] { "unfinished qualified generic creation", "class C { object M() => new global::System.Collections.Generic.List<", false };
                yield return new object[] { "unfinished event args", "class C { object M() => new System.ComponentModel.PropertyChangedEventArgs", false };
                yield return new object[] { "unfinished field property", "class C { string Name { get; set => field = ", false };
                yield return new object[] { "unfinished extension block", "static class E { extension<T>(System.Collections.Generic.IEnumerable<T> source) { public T", false };
                yield return new object[] { "unfinished union", "public union Result(Success,", false };
                yield return new object[] { "unfinished collection arguments", "class C { int[] M() => [with(", false };
                yield return new object[] { "event args initializer without argument list", "class C { object M() => new System.ComponentModel.PropertyChangedEventArgs { }; }", false };
            }
        }

        [Fact]
        public async Task AnalyzerDiscoveryActuallyExecutesRules()
        {
            const string source = """
                class C
                {
                    public void M()
                    {
                        int value = 1;
                        var args = new System.ComponentModel.PropertyChangedEventArgs("Name");
                        System.Console.WriteLine("{0}", value);
                    }
                }
                """;
            var diagnostics = await AnalyzeAsync("analyzer execution control", source, preview: false,
                enableAllRules: true, requireValidCompilation: true);

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CC0106");
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CC0105");
        }

        [Theory]
        [MemberData(nameof(ValidSources))]
        public async Task AllAnalyzersHandleValidModernSyntax(string name, string source, bool preview, bool enableAllRules)
        {
            await AnalyzeAsync(name, source, preview, enableAllRules, requireValidCompilation: true);
        }

        [Theory]
        [MemberData(nameof(IncompleteSources))]
        public async Task AllAnalyzersHandleIncompleteModernSyntax(string name, string source, bool preview)
        {
            await AnalyzeAsync(name, source, preview, enableAllRules: true, requireValidCompilation: false);
        }

        private static CSharpParseOptions ParseOptions(bool preview, bool fileBasedProgram)
        {
            Assert.True(LanguageVersionFacts.TryParse("15.0", out var languageVersion));
            var options = new CSharpParseOptions(preview ? LanguageVersion.Preview : languageVersion);
            var features = new Dictionary<string, string>();
            if (preview) features.Add("updated-memory-safety-rules", "true");
            if (fileBasedProgram) features.Add("FileBasedProgram", "true");
            return options.WithFeatures(features);
        }

        private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string name, string source, bool preview, bool enableAllRules, bool requireValidCompilation)
        {
            Assert.NotEmpty(AnalyzerTypes);
            var analyzers = AnalyzerTypes.Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type)).ToImmutableArray();
            var tree = CSharpSyntaxTree.ParseText(source,
                ParseOptions(preview, source.StartsWith("#!", StringComparison.Ordinal)), name + ".cs");
            var outputKind = tree.GetRoot().DescendantNodes().OfType<GlobalStatementSyntax>().Any()
                ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary;
            var options = new CSharpCompilationOptions(outputKind, allowUnsafe: true);
            if (enableAllRules)
            {
                options = options.WithSpecificDiagnosticOptions(analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics)
                    .Select(rule => rule.Id).Distinct().ToDictionary(id => id, id => ReportDiagnostic.Warn));
            }
            var compilation = CSharpCompilation.Create("ModernSyntax", new[] { tree }, References, options);
            if (requireValidCompilation)
            {
                var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                Assert.True(!errors.Any(), name + " must compile:\n" + string.Join("\n", errors));
                using (var stream = new MemoryStream())
                {
                    var result = compilation.Emit(stream);
                    Assert.True(result.Success, name + " must emit:\n" + string.Join("\n",
                        result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
                }
            }

            var exceptions = new ConcurrentQueue<string>();
            var analyzerOptions = new CompilationWithAnalyzersOptions(new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                (exception, analyzer, diagnostic) => exceptions.Enqueue(analyzer.GetType().Name + ": " + exception),
                concurrentAnalysis: true, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: false);
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
            {
                cancellation.CancelAfter(TimeSpan.FromSeconds(30));
                var diagnostics = await compilation.WithAnalyzers(analyzers, analyzerOptions)
                    .GetAnalyzerDiagnosticsAsync(cancellation.Token);
                Assert.True(exceptions.IsEmpty, name + " analyzer exceptions:\n" + string.Join("\n", exceptions));
                Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
                return diagnostics;
            }
        }

        // C# 15 feature inventory: https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-15
        private static IEnumerable<(string Name, string Source, bool Preview)> Samples()
        {
            yield return ("collection expression constructor arguments", """
                using System;
                using System.Collections.Generic;
                class C
                {
                    public List<string> Names(string[] values) => [with(capacity: values.Length * 2), .. values];
                    public HashSet<string> Set() => [with(StringComparer.OrdinalIgnoreCase), "Hello", "HELLO"];
                }
                """, false);
            yield return ("union types", """
                public record Success(string Value);
                public record Failure(int Code);
                public union Result(Success, Failure);
                public class C
                {
                    public string Describe(Result result) => result switch
                    {
                        Success(var value) => value,
                        Failure(var code) => code.ToString()
                    };
                    public Result Make() => new Success("ok");
                }
                """, false);
            yield return ("closed hierarchies", """
                public closed record GateState;
                public record Closed : GateState;
                public record Open(float Percent) : GateState;
                public class C
                {
                    public string Describe(GateState state) => state switch
                    {
                        Closed => "closed",
                        Open(var percent) => $"{percent}% open"
                    };
                }
                """, false);
            yield return ("extension indexers", """
                using System.Collections.Generic;
                using System.Linq;
                public static class SequenceIndexer
                {
                    extension(IEnumerable<int> sequence)
                    {
                        public int this[int index] => sequence.ElementAt(index);
                    }
                }
                public class C { public int Get(IEnumerable<int> source) => source[0]; }
                """, false);
            yield return ("labeled break and continue", """
                class C
                {
                    public int Search(int[,] grid)
                    {
                        outer: for (int row = 0; row < grid.GetLength(0); row++)
                        {
                            for (int column = 0; column < grid.GetLength(1); column++)
                            {
                                if (grid[row, column] < 0) continue outer;
                                if (grid[row, column] == 1) break outer;
                            }
                        }
                        done: switch (grid.Length)
                        {
                            case 0: break done;
                            default: break;
                        }
                        return grid.Length;
                    }
                }
                """, false);
            yield return ("static interface members", """
                public interface IMeasurement
                {
                    public static double ToMeters(double centimeters) => centimeters / 100;
                    public static int Count { get; set; }
                }
                public class C { public double M() => IMeasurement.ToMeters(250); }
                """, false);
            yield return ("memory safety pointer relaxations and unsafe expressions", """
                class C
                {
                    public int Read()
                    {
                        int number = 42;
                        int* pointer = &number;
                        int[] numbers = [10, 20, 30];
                        fixed (int* first = numbers)
                        {
                            return unsafe(*pointer + first[0]) + sizeof(decimal);
                        }
                    }
                }
                """, true);
            yield return ("memory safety contracts", """
                using System.Runtime.InteropServices;
                [StructLayout(LayoutKind.Explicit)]
                public struct Header
                {
                    [FieldOffset(0)] public safe int Value;
                }
                public class C
                {
                    [DllImport("unused")] public static safe extern int Native();
                    private static readonly int Signature = unsafe(ReadSignature());
                    private static unsafe int ReadSignature()
                    {
                        unsafe { int value = 42; return *(&value); }
                    }
                }
                """, true);
            yield return ("extension blocks", """
                using System.Collections.Generic;
                using System.Linq;
                public static class Extensions
                {
                    extension<T>(IEnumerable<T> source)
                    {
                        public bool IsEmpty => !source.Any();
                        public T FirstValue() => source.First();
                    }
                    extension<T>(IEnumerable<T>)
                    {
                        public static IEnumerable<T> Identity => Enumerable.Empty<T>();
                        public static IEnumerable<T> operator +(IEnumerable<T> left, IEnumerable<T> right) => left.Concat(right);
                    }
                }
                """, false);
            yield return ("field backed properties", """
                using System;
                using System.Collections.Generic;
                public class C
                {
                    public string Name { get; set => field = value ?? throw new ArgumentNullException(nameof(value)); } = "";
                    public List<int> Values { get => field ??= new List<int>(); }
                    public int Count { get => field; set { if (field != value) field = value; } }
                }
                """, false);
            yield return ("null conditional assignments", """
                public class C
                {
                    public C Next { get; set; }
                    public string Name { get; set; }
                    public int Count { get; set; }
                    public int[] Values { get; set; }
                    public void M(C other)
                    {
                        other?.Name = "value";
                        other?.Count += 1;
                        other?.Next?.Name ??= "next";
                        other?.Values?[0] = 2;
                    }
                }
                """, false);
            yield return ("unbound generic nameof", """
                using System.Collections.Generic;
                class C
                {
                    public string Name => nameof(List<>) + nameof(Dictionary<,>);
                }
                """, false);
            yield return ("implicit lambda parameter modifiers", """
                public delegate bool Parser(string text, out int result);
                public delegate int Reader(ref readonly int value);
                class C
                {
                    public Parser Parse => (text, out result) => int.TryParse(text, out result);
                    public Reader Read => (ref readonly value) => value;
                }
                """, false);
            yield return ("partial constructors and events", """
                using System;
                public partial class C
                {
                    public partial C(int value);
                    public partial event EventHandler Changed;
                }
                public partial class C
                {
                    public partial C(int value) { }
                    public partial event EventHandler Changed { add { } remove { } }
                }
                """, false);
            yield return ("instance compound and increment operators", """
                public class Counter
                {
                    public int Value { get; private set; }
                    public void operator +=(int amount) { Value += amount; }
                    public void operator ++() { Value++; }
                    public void M() { Counter counter = new(); counter += 2; counter++; }
                }
                """, false);
            yield return ("span conversions and params collections", """
                using System;
                using System.Collections.Generic;
                class C
                {
                    public int Sum(params ReadOnlySpan<int> values) { int sum = 0; foreach (var value in values) sum += value; return sum; }
                    public int Count(params IEnumerable<int> values) => System.Linq.Enumerable.Count(values);
                    public int M()
                    {
                        int[] values = [1, 2, 3];
                        ReadOnlySpan<int> span = values;
                        ReadOnlySpan<char> text = "abc";
                        return Sum(span) + Sum(1, 2, 3) + Count(1, 2) + text.Length;
                    }
                }
                """, false);
            yield return ("ref structs interfaces and anti constraints", """
                public interface IValue { int Get(); }
                public ref struct Value : IValue { public int Get() => 1; }
                public class C<T> where T : IValue, allows ref struct
                {
                    public int Read(scoped T value) => value.Get();
                }
                """, false);
            yield return ("ref locals in async and iterators", """
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class C
                {
                    public async Task<int> ReadAsync()
                    {
                        int result;
                        { Span<int> span = stackalloc int[1]; ref int value = ref span[0]; value = 1; result = value; }
                        await Task.Yield();
                        return result;
                    }
                    public IEnumerable<int> Read()
                    {
                        int result;
                        { Span<int> span = stackalloc int[1]; ref int value = ref span[0]; value = 2; result = value; }
                        yield return result;
                    }
                }
                """, false);
            yield return ("partial properties and indexers", """
                public partial class C
                {
                    public partial int Value { get; set; }
                    public partial int this[int index] { get; }
                }
                public partial class C
                {
                    private int value;
                    public partial int Value { get => value; set => this.value = value; }
                    public partial int this[int index] => value + index;
                }
                """, false);
            yield return ("implicit index initializers and lock", """
                using System.Threading;
                class C
                {
                    private readonly Lock gate = new();
                    public int[] Buffer { get; set; } = new int[3];
                    public C Create() { lock (gate) { return new C { Buffer = { [^1] = 0, [^2] = 1 } }; } }
                    public string Escape => "\e[31m";
                }
                """, false);
            yield return ("overload priority and method group inference", """
                using System;
                using System.Runtime.CompilerServices;
                class C
                {
                    [OverloadResolutionPriority(1)] public static int Read(ReadOnlySpan<int> values) => values.Length;
                    public static int Read(int[] values) => values.Length;
                    public static T Identity<T>(T value) => value;
                    public int M() { var read = Identity<int>; return read(1) + Read([1, 2]); }
                }
                """, false);
            yield return ("primary constructors and records", """
                using System;
                public class C(string name, int count)
                {
                    private readonly string value = name ?? throw new ArgumentNullException(nameof(name));
                    public int Count => count;
                    public C() : this("", 0) { }
                }
                public record Person(string Name) { public int Age { get; init; } }
                public readonly record struct Point(int X, int Y);
                public class D { public Person Copy(Person person) => person with { Name = "new" }; }
                """, false);
            yield return ("collection expressions spread and list patterns", """
                using System;
                using System.Collections.Generic;
                class C
                {
                    public List<int> Copy(int[] values) => [0, .. values, 4];
                    public int Match(int[] values) => values switch { [1, .. var rest, 3] => rest.Length, [] => 0, _ => -1 };
                    public int Slice(int[] values) { Span<int> span = stackalloc[] { 1, 2 }; return values[1..^1].Length + span[^1]; }
                }
                """, false);
            yield return ("raw interpolated and utf8 strings", """"
                using System;
                class C
                {
                    public string Json(int value) => $$"""{"value": {{value}}}""";
                    public ReadOnlySpan<byte> Bytes => "hello"u8;
                    public string Multiline => """
                        first
                        second
                        """;
                }
                """", false);
            yield return ("required init file types and aliases", """
                using Pair = (int X, int Y);
                file class Helper { public required string Name { get; init; } }
                public class C
                {
                    public Pair Position() { var helper = new Helper { Name = "ok" }; return (helper.Name.Length, 0); }
                }
                """, false);
            yield return ("generic attributes and lambda defaults", """
                using System;
                public class TagAttribute<T> : Attribute { }
                [Tag<int>] public class C
                {
                    public int M() { var add = (int value = 1) => value + 1; return add(); }
                }
                """, false);
            yield return ("static abstract interfaces and checked operators", """
                public interface INumber<T> where T : INumber<T>
                {
                    static abstract T operator +(T left, T right);
                    static virtual int Zero => 0;
                }
                public readonly struct Number(int value) : INumber<Number>
                {
                    public int Value => value;
                    public static Number operator +(Number left, Number right) => new(left.Value + right.Value);
                    public static Number operator checked +(Number left, Number right) => new(checked(left.Value + right.Value));
                }
                """, false);
            yield return ("top level statements target typed new and using declarations", """
                using System;
                using System.Collections.Generic;
                using System.IO;
                List<int> values = new() { 1, 2 };
                using MemoryStream stream = new();
                Console.WriteLine(values.Count);
                """, false);
            yield return ("top level await and async disposal", """
                using System.IO;
                using System.Threading.Tasks;
                await using var stream = new MemoryStream();
                await Task.Yield();
                await stream.WriteAsync(new byte[] { 1, 2 });
                """, false);
            yield return ("recursive relational and logical patterns", """
                public record Person(string Name, int Age);
                class C
                {
                    public bool Match(object value) => value is Person { Name.Length: > 0, Age: >= 18 and < 100 } and not null;
                    public int Read(object value) => value switch { Person(_, > 0 and var age) => age, int number when number > 0 => number, _ => 0 };
                }
                """, false);
            yield return ("function pointers ref returns and scoped parameters", """
                using System;
                unsafe class C
                {
                    private static int Identity(int value) => value;
                    public int M() { delegate*<int, int> pointer = &Identity; return pointer(1); }
                    public ref int First(Span<int> values) => ref values[0];
                    public int Read(scoped ref readonly int value) => value;
                }
                """, false);
            yield return ("deconstruction tuple foreach and ref foreach", """
                using System;
                class C
                {
                    public int M((int Left, int Right)[] pairs)
                    {
                        int sum = 0;
                        foreach (var (left, right) in pairs) sum += left + right;
                        Span<int> values = stackalloc[] { 1, 2 };
                        foreach (ref int value in values) value++;
                        (int first, _) = pairs[0];
                        return sum + first;
                    }
                }
                """, false);
            yield return ("JSON source generator metadata shapes", """
                public class Payload { public string Name { get; set; } }
                public class Context
                {
                    public object Create() => new global::System.Text.Json.Serialization.Metadata.JsonObjectInfoValues<Payload>
                    {
                        ObjectCreator = static () => new Payload(),
                        PropertyMetadataInitializer = _ => new global::System.Text.Json.Serialization.Metadata.JsonPropertyInfo[]
                        {
                            global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreatePropertyInfo<string>(
                                new global::System.Text.Json.JsonSerializerOptions(),
                                new global::System.Text.Json.Serialization.Metadata.JsonPropertyInfoValues<string>
                                {
                                    IsProperty = true,
                                    IsPublic = true,
                                    DeclaringType = typeof(Payload),
                                    PropertyName = "Name",
                                    Getter = static obj => ((Payload)obj).Name,
                                    Setter = static (obj, value) => ((Payload)obj).Name = value
                                })
                        }
                    };
                }
                """, false);
            yield return ("file based app directives", """
                #!/usr/bin/env dotnet
                #:property LangVersion=15.0
                #:property Nullable=enable
                System.Console.WriteLine("file based app");
                """, false);
        }
    }
}
#endif
