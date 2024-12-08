using System.Threading.Tasks;
using CodeCracker.CSharp.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CodeCracker.Test.CSharp.Usage
{
    public class RemovePrivateMethodNeverUsedAnalyzerTest : CodeFixVerifier<RemovePrivateMethodNeverUsedAnalyzer, RemovePrivateMethodNeverUsedCodeFixProvider>
    {

        [Theory]
        [InlineData("Fact")]
        [InlineData("ContractInvariantMethod")]
        [InlineData("System.Diagnostics.Contracts.ContractInvariantMethod")]
        [InlineData("DataMember")]
        public async Task DoesNotGenerateDiagnosticsWhenMethodAttributeIsAnException(string value)
        {
            var source = @"
class Foo
{
    [" + value + @"]
    private void PrivateFoo() { }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Theory]
        [InlineData("[Obsolete, Fact]")]
        [InlineData("[Obsolete]\n[Fact]")]
        public async Task DoesNotGenerateDiagnosticsWhenMethodAttributeIsAnExceptionAndMixedWithOtherAttributes(string value)
        {
            var source = @"
class Foo
{
    " + value + @"
    private void PrivateFoo() { }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task GenerateDiagnosticsOnNotIgnoredAttributes()
        {
            const string source = @"
class Foo
{
    [Obsolete]
    private void PrivateFoo() { }
}";
            const string fixtest = @"
class Foo
{
}";
            await VerifyCSharpFixAsync(source, fixtest);
        }



        [Fact]
        public async Task DoesNotGenerateDiagnostics()
        {
            const string test = @"
  public class Foo
{
    public void PublicFoo()
    {
        PrivateFoo();
    }

    private void PrivateFoo()
    {
       PrivateFoo2();
    }

    private void PrivateFoo2() { }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }

        [Fact]
        public async Task DoesNotGenerateDiagnosticsWhenPrivateMethodIsInvokedInPartialClasses()
        {
            const string test = @"
public partial class Foo
{
    public void PublicFoo()
    {
        PrivateFoo();
    }
}

public partial class Foo
{
    private void PrivateFoo()
    {
    }
}
";

            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }


        [Fact]
        public async Task DoesNotGenerateDiagnosticsWhenPrivateMethodIsInvokedInPartialClasses2()
        {
            const string test = @"
public partial class foo
{
    public foo()
    {

    }

    private void test()
    {
    }
}

public partial class foo
{
    public void test2()
    {
        test();
    }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }


        [Fact]
        public async Task FixRemovesPrivateMethodWhenItIsNotInvokedInPartialClasses()
        {
            const string test = @"
public partial class Foo
{
    public void PublicFoo()
    {
    }
}

public partial class Foo
{
    private void PrivateFoo()
    {
    }
}
";

            const string expected = @"
public partial class Foo
{
    public void PublicFoo()
    {
    }
}

public partial class Foo
{
}
";

            await VerifyCSharpFixAsync(test, expected);
        }

        [Fact]
        public async Task WhenPrivateMethodUsedDoesNotGenerateDiagnostics()
        {
            const string test = @"
  public class Foo
{
    public void PublicFoo()
    {
        PrivateFoo();
    }

    private void PrivateFoo() { }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }

        [Fact]
        public async Task WhenPrivateMethodUsedInAttributionDoesNotGenerateDiagnostics()
        {
            const string test = @"
using System;

public class Foo
{
    public void PublicFoo()
    {
        Action method = PrivateFoo;
    }

    private void PrivateFoo() { }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }

        [Fact]
        public async Task WhenPrivateMethodDoesNotUsedShouldCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    private void PrivateFoo() { }
}";
            const string fixtest = @"
class Foo
{
}";
            await VerifyCSharpFixAsync(source, fixtest);

        }

        [Fact]
        public async Task GenericMethodDoesNotCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    void PrivateFoo<T>() { }
    public void Go()
    {
        PrivateFoo<int>();
    }
}
";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task GenericMethodWithConstraintDoesNotCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    void PrivateFoo<T>() where T : Foo { }
    public void Go()
    {
        PrivateFoo<Foo>();
    }
}
";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task StaticMethodDoesNotCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    static void PrivateFoo() { }
    public void Go()
    {
        PrivateFoo();
    }
}
";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task PrivateGenericStaticWithConstraintDoesNotCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    private static SymbolAnalysisContext GetSymbolAnalysisContext<T>(string code, string fileName = ""a.cs"") where T : SyntaxNode
    {
    }
    public void Go()
    {
        GetSymbolAnalysisContext<ClassDeclarationSyntax>(""class TypeName { }"", ""TemporaryGeneratedFile_.cs"");
    }
}
";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task MainMethodEntryPointReturningVoidDoesNotCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    static void Main(String[] args)
    {
    }
}
";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task MainMethodEntryPointReturningIntegerDoesNotCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    static int Main(string[] args)
    {
    }
}
";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task MainMethodEntryPointWithoutParameterDoesNotCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    static int Main()
    {
    }
}
";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task MainMethodEntryPointWithoutStaticModifierShouldCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    int Main(string[] args)
    {
    }
}
";
            const string fixtest = @"
class Foo
{
}
";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task MainMethodEntryPointWithMoreThanOneParameterShouldCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    static int Main(string[] args, string[] args2)
    {
    }
}
";
            const string fixtest = @"
class Foo
{
}
";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task MainMethodEntryPointWithDifferentParameterShouldCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    static int Main(string args)
    {
    }
}
";
            const string fixtest = @"
class Foo
{
}
";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task MainMethodEntryPointWithDifferentReturnTypeShouldCreateDiagnostic()
        {
            const string source = @"
class Foo
{
    static string Main(string[] args)
    {
    }
}
";
            const string fixtest = @"
class Foo
{
}
";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task ExplicitlyImplementedInterfaceMethodDoesNotCreateDiagnostic()
        {
            const string source = @"
public class Foo : System.IEquatable<Foo>
{
    bool System.IEquatable<Foo>.Equals(Foo other)
    {
        throw new System.NotImplementedException();
    }
}";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        // see https://msdn.microsoft.com/en-us/library/53b8022e(v=vs.110).aspx
        [Fact]
        public async Task WinFormsPropertyDefaultValueDefinitionMethodsShouldBeIgnored()
        {
            var source = @"
public int PropertyXXX {
    get;
    set;
}

private bool ShouldSerializePropertyXXX() => true;

private void ResetPropertyXXX() { };
".WrapInCSharpClass();
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        private static DiagnosticResult CreateDiagnosticResult(int line, int column)
        {
            return new DiagnosticResult(DiagnosticId.RemovePrivateMethodNeverUsed.ToDiagnosticId(), DiagnosticSeverity.Info)
                .WithLocation(line, column)
                .WithMessage(RemovePrivateMethodNeverUsedAnalyzer.Message);
        }

        [Fact]
        public async Task WinFormsPropertyDefaultValueDefinitionMethodsMustHaveCorrectSignature()
        {
            var source = @"
public int Property1 { get; set; }
public int Property2 { get; set; }
public int Property3 { get; set; }

private int ShouldSerializeProperty1() => 1;
private bool ShouldSerializeProperty2(int i) => true;
private void ShouldSerializeProperty3() { };

private bool ResetProperty1() => true;
private void ResetProperty2(int i) { };
".WrapInCSharpClass();
            var result1 = CreateDiagnosticResult(13, 1);
            var result2 = CreateDiagnosticResult(14, 1);
            var result3 = CreateDiagnosticResult(15, 1);
            var result4 = CreateDiagnosticResult(17, 1);
            var result5 = CreateDiagnosticResult(18, 1);
            await VerifyCSharpDiagnosticAsync(source, new DiagnosticResult[] { result1, result2, result3, result4, result5 });
        }

        [Fact]
        public async Task WinFormsPropertyDefaultValueDefinitionMethodsMustHaveCorrespondingProperty()
        {
            var source = @"
private bool ShouldSerializePropertyXXX() => true;

private void ResetPropertyXXX() { };
".WrapInCSharpClass();
            var result1 = CreateDiagnosticResult(9, 1);
            var result2 = CreateDiagnosticResult(11, 1);
            await VerifyCSharpDiagnosticAsync(source, new DiagnosticResult[] { result1, result2 });
        }

        [Fact]
        public async Task WinFormsPropertyDefaultValueDefinitionMethodsMustHaveASuffix()
        {
            var source = @"
private bool ShouldSerialize() => true;

private void ResetProperty() { };
".WrapInCSharpClass();
            var result1 = CreateDiagnosticResult(9, 1);
            var result2 = CreateDiagnosticResult(11, 1);
            await VerifyCSharpDiagnosticAsync(source, new DiagnosticResult[] { result1, result2 });
        }
    }
}