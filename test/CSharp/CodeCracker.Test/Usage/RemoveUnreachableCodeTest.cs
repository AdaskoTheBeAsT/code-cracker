using System.Threading.Tasks;
using CodeCracker.CSharp.Usage;
using Microsoft.CodeAnalysis.CodeFixes;
using Xunit;

namespace CodeCracker.Test.CSharp.Usage
{
    public class RemoveUnreachableCodeTest : CodeFixVerifier
    {
        protected override CodeFixProvider GetCodeFixProvider() => new RemoveUnreachableCodeCodeFixProvider();

        [Fact]
        public async Task FixUnreacheableVariableDeclaration()
        {
            const string source = @"
class Foo
{
    void Method()
    {
        return;
        var a = 1;
    }
}";
            const string fixtest = @"
class Foo
{
    void Method()
    {
        return;
    }
}";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task FixUnreacheableInvocation()
        {
            const string source = @"
class Foo
{
    void F() { }
    void Method()
    {
        return;
        F();
    }
}";
            const string fixtest = @"
class Foo
{
    void F() { }
    void Method()
    {
        return;
    }
}";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task FixUnreacheableInvocationWithMemberAccess()
        {
            const string source = @"
class Foo
{
    void Method()
    {
        return;
        System.Diagnostics.Debug.WriteLine("""");
    }
}";
            const string fixtest = @"
class Foo
{
    void Method()
    {
        return;
    }
}";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task FixUnreacheableFor()
        {
            const string source = @"
class Foo
{
    void Method()
    {
        return;
        for(;;) { }
    }
}";
            const string fixtest = @"
class Foo
{
    void Method()
    {
        return;
    }
}";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task FixUnreacheableInvocationInsideFor()
        {
            const string source = @"
class Foo
{
    void F() { }
    void T()
    {
        for (int i = 0; i < 0; F())
        {
            return;
        }
    }
}";
            const string fixtest = @"
class Foo
{
    void F() { }
    void T()
    {
        for (int i = 0; i < 0; )
        {
            return;
        }
    }
}";
            await VerifyCSharpFixAsync(source, fixtest, formatBeforeCompare: false);
        }

        [Fact]
        public async Task FixUnreacheableIncrement()
        {
            const string source = @"
class Foo
{
    void T()
    {
        for (int i = 0; i < 0; i++)
        {
            return;
        }
    }
}";
            const string fixtest = @"
class Foo
{
    void T()
    {
        for (int i = 0; i < 0; )
        {
            return;
        }
    }
}";
            await VerifyCSharpFixAsync(source, fixtest, formatBeforeCompare: false);
        }

        [Fact]
        public async Task FixUnreacheableInIf()
        {
            const string source = @"
class Foo
{
    void T()
    {
        if (false) return 1;
        return 0;
    }
}";
            const string fixtest = @"
class Foo
{
    void T()
    {
        if (false)
        {
        }

        return 0;
    }
}";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task FixUnreacheableInNestedIfWithInvocation()
        {
            const string source = @"
class Foo
{
    int T()
    {
        if (true)
            if (false)
                System.Console.Write(1);
        return 0;
    }
}";
            const string fixtest = @"
class Foo
{
    int T()
    {
        if (true)
            if (false)
            {
            }

        return 0;
    }
}";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task FixUnreacheableInElse()
        {
            const string source = @"
class Foo
{
    void T()
    {
        if (true)
            return 1;
        else
            return 0;
    }
}";
            const string fixtest = @"
class Foo
{
    void T()
    {
        if (true)
            return 1;
    }
}";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task FixUnreacheableInWhile()
        {
            const string source = @"
class Foo
{
    void T()
    {
        while (false) return 1;
        return 0;
    }
}";
            const string fixtest = @"
class Foo
{
    void T()
    {
        while (false)
        {
        }

        return 0;
    }
}";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task FixUnreacheableInLambda()
        {
            const string source = @"
class Foo
{
    void T()
    {
        System.Func<int> q13 = ()=>{ if (false) return 1; return 0; };
    }
}";
            const string fixtest = @"
class Foo
{
    void T()
    {
        System.Func<int> q13 = ()=>{ if (false) { } return 0; };
    }
}";
            await VerifyCSharpFixAsync(source, fixtest, formatBeforeCompare: false);
        }

        [Fact]
        public async Task FixUnreacheableLambda()
        {
            const string source = @"
class Foo
{
    void T()
    {
        return;
        Action f = () => { };
    }
}";
            const string fixtest = @"
class Foo
{
    void T()
    {
        return;
    }
}";
            await VerifyCSharpFixAsync(source, fixtest, formatBeforeCompare: false);
        }

        [Fact]
        public async Task FixAllUnreacheableCode()
        {
            const string source = @"
class Foo
{
    void Method()
    {
        return;
        var a = 1;
        var b = 1;
    }
}";
            const string fixtest = @"
class Foo
{
    void Method()
    {
        return;
    }
}";
            await VerifyCSharpFixAllAsync(source, fixtest);
        }

        [Fact]
        public async Task FixAllProjectUnreacheableCode()
        {
            const string source1 = @"
class Foo
{
    void Method()
    {
        return;
        var a = 1;
        var b = 1;
    }
}";
            const string source2 = @"
class Foo2
{
    void Method()
    {
        return;
        var a = 1;
        var b = 1;
    }
}";
            const string fixtest1 = @"
class Foo
{
    void Method()
    {
        return;
    }
}";
            const string fixtest2 = @"
class Foo2
{
    void Method()
    {
        return;
    }
}";
            await VerifyCSharpFixAllAsync(new[] { source1, source2 }, new[] { fixtest1, fixtest2 });
        }
    }
}