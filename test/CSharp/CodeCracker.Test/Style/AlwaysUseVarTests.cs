using CodeCracker.CSharp.Style;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace CodeCracker.Test.CSharp.Style
{
    public class AlwaysUseVarTests : CodeFixVerifier<AlwaysUseVarAnalyzer, AlwaysUseVarCodeFixProvider>
    {

        [Fact]
        public async Task IgnoresConstantDeclarations()
        {
            const string test = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                const int a = 10;
            }
        }
    }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);

        }

        [Fact]
        public async Task IgnoresVarDeclarations()
        {
            const string test = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                var a = 10;
            }
        }
    }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);

        }
        [Theory]
        [InlineData("ref var")]
        [InlineData("ref readonly var")]
        [InlineData("scoped ref var")]
        [InlineData("scoped ref readonly var")]
        public async Task IgnoresImplicitlyTypedRefDeclarations(string type)
        {
            var test = @"
class Reader
{
    void Read(ref int value)
    {
        " + type + @" entry = ref value;
    }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(test, LanguageVersion.Latest);
        }

        [Theory]
        [InlineData("System.Collections.Generic.List<int> values = new();")]
        [InlineData("System.Collections.Generic.List<int> values = (new());")]
        [InlineData("System.Collections.Generic.List<int> values = true ? new() : new();")]
        [InlineData("System.Collections.Generic.List<int> values = 0 switch { _ => new() };")]
        [InlineData("int[] values = [];")]
        [InlineData("int[] values = [1, 2, 3];")]
        [InlineData("int[] values = true ? [] : [1];")]
        [InlineData("int[] values = 0 switch { _ => [] };")]
        [InlineData("int value = default;")]
        [InlineData("int value = (default);")]
        [InlineData("string value = null;")]
        [InlineData("System.Collections.Generic.List<int> first = new System.Collections.Generic.List<int>(), second = new();")]
        [InlineData("System.Linq.Expressions.Expression<System.Func<int>> value = () => 1;")]
        [InlineData("System.Func<long> value = () => 1;")]
        public async Task IgnoresInitializersThatRequireTheDeclaredType(string declaration)
        {
            var test = @"
class Reader
{
    void Read()
    {
        " + declaration + @"
    }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(test, LanguageVersion.Latest);
        }

        [Theory]
        [InlineData("(int Count, int Total) value = (First: 1, Second: 2);")]
        [InlineData("(int Count, int Total) value = (1, 2);")]
        [InlineData("System.Collections.Generic.List<(int Count, int Total)> values = new System.Collections.Generic.List<(int First, int Second)>();")]
        [InlineData("string value = \"text\";")]
        public async Task IgnoresDeclarationsWhoseInferredTypeChangesNamesOrNullability(string declaration)
        {
            var test = @"
#nullable enable
class Reader
{
    void Read()
    {
        " + declaration + @"
    }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(test, LanguageVersion.Latest);
        }

        [Theory]
        [InlineData("MissingType value = Missing();")]
        [InlineData("ref MissingType value = ref Missing();")]
        [InlineData("ref readonly MissingType value = ref Missing();")]
        [InlineData("dynamic value = Missing();")]
        [InlineData("int value = ;")]
        [InlineData("ref readonly var value = ref ;")]
        public async Task IgnoresIncompleteOrUnresolvedDeclarations(string declaration)
        {
            var test = @"
class Reader
{
    void Read()
    {
        " + declaration + @"
    }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(test, LanguageVersion.Latest);
        }

        [Theory]
        [InlineData("ref int", "ref var")]
        [InlineData("ref readonly int", "ref readonly var")]
        [InlineData("scoped ref int", "scoped ref var")]
        [InlineData("scoped ref readonly int", "scoped ref readonly var")]
        [InlineData("ref readonly /* type */ int", "ref readonly /* type */ var")]
        public async Task FixPreservesRefDeclarationModifiers(string type, string inferredType)
        {
            var test = @"
class Reader
{
    void Read(ref int value)
    {
        " + type + @" entry = ref value;
    }
}";
            var expected = test.Replace(type + " entry", inferredType + " entry");
            await VerifyCSharpFixAsync(test, expected, languageVersionCSharp: LanguageVersion.Latest);
        }

        [Theory]
        [InlineData("using")]
        [InlineData("await using")]
        public async Task FixPreservesUsingDeclarationModifiers(string modifiers)
        {
            var test = @"
class Reader
{
    async System.Threading.Tasks.Task Read()
    {
        " + modifiers + @" System.IO.MemoryStream stream = new System.IO.MemoryStream();
        await System.Threading.Tasks.Task.Yield();
    }
}";
            var expected = test.Replace("System.IO.MemoryStream stream", "var stream");
            await VerifyCSharpFixAsync(test, expected, languageVersionCSharp: LanguageVersion.Latest);
        }

        [Fact]
        public async Task FixPreservesMultipleUsingDeclarations()
        {
            const string test = @"
class Reader
{
    void Read()
    {
        using System.IO.MemoryStream first = new System.IO.MemoryStream(), second = new System.IO.MemoryStream();
    }
}";
            const string expected = @"
class Reader
{
    void Read()
    {
        using var first = new System.IO.MemoryStream();
        using var second = new System.IO.MemoryStream();
    }
}";
            await VerifyCSharpFixAsync(test, expected, languageVersionCSharp: LanguageVersion.Latest);
        }

        [Theory]
        [InlineData("scoped System.Span<int> values = new System.Span<int>(new int[1]);",
                    "scoped var values = new System.Span<int>(new int[1]);")]
        [InlineData("(int Count, string Name) value = (Count: 1, Name: \"name\");",
                    "var value = (Count: 1, Name: \"name\");")]
        [InlineData("nint value = (nint)1;", "var value = (nint)1;")]
        [InlineData("string value = \"\"\"text\"\"\";", "var value = \"\"\"text\"\"\";")]
        [InlineData("System.Func<int> value = () => 1;", "var value = () => 1;")]
        [InlineData("int value = 0 switch { _ => 1 };", "var value = 0 switch { _ => 1 };")]
        public async Task FixSupportsNewerInferredTypes(string declaration, string inferredDeclaration)
        {
            var test = @"
class Reader
{
    void Read()
    {
        " + declaration + @"
    }
}";
            var expected = test.Replace(declaration, inferredDeclaration);
            await VerifyCSharpFixAsync(test, expected, languageVersionCSharp: LanguageVersion.Latest);
        }

        [Fact]
        public async Task CreatesDiagnosticForTopLevelDeclaration()
        {
            const string test = "int value = 1;";
            var expected = new DiagnosticResult(DiagnosticId.AlwaysUseVarOnPrimitives.ToDiagnosticId(), DiagnosticSeverity.Warning)
                .WithLocation(1, 1)
                .WithMessage("Use 'var' instead of specifying the type name.");
            await VerifyCSharpDiagnosticAsync(test, expected, LanguageVersion.Latest);
        }

        [Fact]
        public async Task FixAllSupportsRefAndUsingDeclarations()
        {
            const string test = @"
class Reader
{
    void Read(ref int value)
    {
        ref readonly int entry = ref value;
        using System.IO.MemoryStream stream = new System.IO.MemoryStream();
        scoped ref int mutableEntry = ref value;
    }
}";
            var expected = test.Replace("readonly int entry", "readonly var entry")
                .Replace("System.IO.MemoryStream stream", "var stream")
                .Replace("ref int mutableEntry", "ref var mutableEntry");
            await VerifyCSharpFixAllAsync(test, expected, languageVersionCSharp: LanguageVersion.Latest);
        }

        [Fact]
        public async Task SupportsRefReturnsInFileScopedNamespace()
        {
            const string test = @"
namespace Serialization;
class Reader
{
    private int value;
    private ref readonly int GetValueAt(int index) => ref value;

    (int Value, int Index)? Read(int index)
    {
        ref readonly var entry = ref GetValueAt(index);
        return (entry, index);
    }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(test, LanguageVersion.Latest);
        }


        [Fact]
        public async Task IgnoresDeclarationsWithNoInitializers()
        {
            const string test = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                int a = 10, b;
            }
        }
    }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);

        }

        [Fact]
        public async Task IgnoresDeclarationsWithNoIdentityConversions()
        {
            const string test = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                IFee fee = new Fee();
            }
        }
        interface IFee{}
        class Fee: IFee {}
    }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);

        }

        [Fact]
        public async Task IgnoresDeclarationsWithDynamicVaribleName()
        {
            const string test = @"
    using System;
    namespace ConsoleApplication1
    {
        class TypeName
        {
            void Foo()
            {
                dynamic fee = Fee();
            }
            object Fee() { return 42; }
        }
    }";
            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }


        [Fact]
        public async Task DeclarationsWithDynamicVaribleNameWithInitializerAlsoDynamicCreatesDiagnosic()
        {
            const string test = @"
    using System;
    namespace ConsoleApplication1
    {
        class TypeName
        {
            void Foo()
            {
                dynamic fee = Fee();
            }
            dynamic Fee() { return 42; }
        }
    }";
            var expected = new DiagnosticResult(DiagnosticId.AlwaysUseVar.ToDiagnosticId(), DiagnosticSeverity.Warning)
                .WithLocation(9, 17)
                .WithMessage("Use 'var' instead of specifying the type name.");
            await VerifyCSharpDiagnosticAsync(test, expected);
        }

        [Theory]
        [InlineData("int")]
        [InlineData("System.Int32")]
        public async Task CreateDiagnosticsWhenAssigningValueWithSameDeclaringTypePrimitive(string value)
        {
            var test = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                " + value + @" a = 10;
            }
        }
    }";
            var expected = new DiagnosticResult(DiagnosticId.AlwaysUseVarOnPrimitives.ToDiagnosticId(), DiagnosticSeverity.Warning)
                .WithLocation(10, 17)
                .WithMessage("Use 'var' instead of specifying the type name.");

            await VerifyCSharpDiagnosticAsync(test, expected);
        }

        [Theory]
        [InlineData("string a = \"10\";")]
        [InlineData("DateTime date1 = new DateTime(2013, 6, 1, 12, 32, 30);")]
        public async Task CreateDiagnosticsWhenAssigningValueWithSameDeclaringTypeNonPrimitive(string value)
        {
            var test = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                " + value + @"
            }
        }
    }";
            var expected = new DiagnosticResult(DiagnosticId.AlwaysUseVar.ToDiagnosticId(), DiagnosticSeverity.Warning)
                .WithLocation(10, 17)
                .WithMessage("Use 'var' instead of specifying the type name.");

            await VerifyCSharpDiagnosticAsync(test, expected);
        }

        [Fact]
        public async Task FixReplacesDeclaringTypeWithVarIdentifier()
        {
            const string test = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                int a = 10;
            }
        }
    }";

            const string expected = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                var a = 10;
            }
        }
    }";
            await VerifyCSharpFixAsync(test, expected);
        }

        [Fact]
        public async Task FixReplacesDeclaringTypeWithVarIdentifierFixAll()
        {
            const string source1 = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public void Foo()
            {
                int a = 10;
                int b = 10;
            }
        }
    }";
            const string source2 = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public void Foo()
            {
                string a = ""10"";
                string b = ""10"";
            }
        }
    }";

            const string fixtest1 = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public void Foo()
            {
                var a = 10;
                var b = 10;
            }
        }
    }";
            const string fixtest2 = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public void Foo()
            {
                var a = ""10"";
                var b = ""10"";
            }
        }
    }";
            await VerifyCSharpFixAllAsync(new[] { source1, source2 }, new[] { fixtest1, fixtest2 });
        }


        [Fact]
        public async Task FixReplacesMultipleDeclarationWithMultipleVars()
        {
            const string test = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                int a = 10, b = 12;
            }
        }
    }";

            const string expected = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                var a = 10;
                var b = 12;
            }
        }
    }";
            await VerifyCSharpFixAsync(test, expected);
        }



        [Fact]
        public async Task FixPreservesTriviaSensibly()
        {
            const string test = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                int a = 10; //Blue

                /*variables for use*/ string /*desc of b*/b = /* why not*/ ""12"", /*Formatter does this My next variable*/ c /* is quite nice and it */ = ""23"";
            }
        }
    }";

            const string expected = @"
    using System;

    namespace ConsoleApplication1
    {
        class TypeName
        {
            public async Task Foo()
            {
                var a = 10; //Blue

                /*variables for use*/ var /*desc of b*/b = /* why not*/ ""12"";
                var
/*Formatter does this My next variable*/ c /* is quite nice and it */ = ""23"";
            }
        }
    }";
            await VerifyCSharpFixAllAsync(test, expected);
        }
    }
}