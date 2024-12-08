using CodeCracker.CSharp.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace CodeCracker.Test.CSharp.Usage
{
    public class DisposablesShouldCallSuppressFinalizeTests : CodeFixVerifier<DisposablesShouldCallSuppressFinalizeAnalyzer, DisposablesShouldCallSuppressFinalizeCodeFixProvider>
    {
        [Fact]
        public async Task AlreadyCallsSuppressFinalizeWithArrowMethod()
        {
            const string source = @"
                public class MyType : System.IDisposable
                {
                    public void Dispose() => System.GC.SuppressFinalize(this);
                }";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task AlreadyCallsSuppressFinalize()
        {
            const string source = @"
                public class MyType : System.IDisposable
                {
                    public void Dispose()
                    {
                        System.GC.SuppressFinalize(this);
                    }
                }";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task DoNotWarnIfStructImplmentsIDisposableWithNoSuppressFinalizeCall()
        {
            const string test = @"
                public struct MyType : System.IDisposable
                {
                    public void Dispose()
                    {
                    }
                }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }

        [Fact]
        public async Task WarningIfClassImplmentsIDisposableWithNoSuppressFinalizeCall()
        {
            const string test = @"
                public class MyType : System.IDisposable
                {
                    public void Dispose()
                    {
                    }
                }";

            var expected = new DiagnosticResult(DiagnosticId.DisposablesShouldCallSuppressFinalize.ToDiagnosticId(), DiagnosticSeverity.Warning)
                .WithLocation(4, 33)
                .WithMessage("'MyType' should call GC.SuppressFinalize inside the Dispose method.");

            await VerifyCSharpDiagnosticAsync(test, expected);
        }

        [Fact]
        public async Task DoNotWarnIfClassImplementsIDisposableWithSuppressFinalizeCallInFinally()
        {
            const string test = @"
                 public class MyType : System.IDisposable
                 {
                     public void Dispose()
                     {
                         try
                         {
                         }
                         finally
                         {
                             System.GC.SuppressFinalize(this);
                         }
                     }
                 }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }

        [Fact]
        public async Task DoNotWarnIfClassImplementsIDisposableWithSuppressFinalizeCallInIf()
        {
            const string test = @"
                 public class MyType : System.IDisposable
                 {
                     public void Dispose()
                     {
                         if (true)
                         {
                             System.GC.SuppressFinalize(this);
                         }
                     }
                 }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }

        [Fact]
        public async Task DoNotWarnIfClassImplementsIDisposableWithSuppressFinalizeCallInElse()
        {
            const string test = @"
                 public class MyType : System.IDisposable
                 {
                     public void Dispose()
                     {
                         if (true)
                         {
                         }
                         else
                         {
                             System.GC.SuppressFinalize(this);
                         }
                     }
                 }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }

        [Fact]
        public async Task NoWarningIfClassImplementsDisposableCallsSuppressFinalizeAndCallsDisposeWithThis()
        {
            const string source = @"
            using System;
            public class MyType : System.IDisposable
            {
                public void Dispose()
                {
                    this.Dispose(true);
                    GC.SuppressFinalize(this);
                }
            }";

            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task NoWarningIfClassImplementsDisposableCallsSuppressFinalize()
        {
            const string source = @"
            using System;
            public class MyType : System.IDisposable
            {
                public void Dispose()
                {
                    GC.SuppressFinalize(this);
                }
            }";

            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }


        [Fact]
        public async Task NoWarningIfClassImplmentsIDisposableButDoesNotContainsAPublicConstructor()
        {
            const string test = @"
                public class MyType : System.IDisposable
                {
                    private MyType()
                    {
                    }

                    public void Dispose()
                    {
                    }

                    ~MyType() {}
                }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }


        [Fact]
        public async Task NoWarningIfClassIsAPrivateNestedType()
        {
            const string test = @"
                public class MyType
                {
                    private class MyNestedType : System.IDisposable
                    {
                        public void Dispose()
                        {
                        }

                        ~MyType() {}
                    }
                }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }

        [Fact]
        public async Task NoWarningIfClassIsNestedOfAPrivateNestedType()
        {
            const string test = @"
                public class MyType
                {
                    private class MyType
                    {
                        public class MyNestedType : System.IDisposable
                        {
                            public void Dispose()
                            {
                            }

                            ~MyType() {}
                        }
                    }
                }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }

        [Fact]
        public async Task NoWarningIfStructDoesNotImplementsIDisposable()
        {
            const string test = @"
                public struct MyType
                {
                }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }

        [Fact]
        public async Task NoWarningIfClassIsSealedWithNoUserDefinedFinalizer()
        {
            const string test = @"
                public sealed class MyType : System.IDisposable
                {
                    public void Dispose()
                    {
                    }
                }"
                ;

            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }

        [Fact]
        public async Task WarningIfSealedClassHaveUserDefinedFinalizerImplmentsIDisposableWithNoSuppressFinalizeCall()
        {
            const string test = @"
                public sealed class MyType : System.IDisposable
                {
                    public void Dispose()
                    {
                    }

                    ~MyType() {}
                }";

            var expected = new DiagnosticResult(DiagnosticId.DisposablesShouldCallSuppressFinalize.ToDiagnosticId(), DiagnosticSeverity.Warning)
                .WithLocation(4, 33)
                .WithMessage("'MyType' should call GC.SuppressFinalize inside the Dispose method.");

            await VerifyCSharpDiagnosticAsync(test, expected);
        }

        [Fact]
        public async Task NoWarningIfClassDoesNotImplementsIDisposable()
        {
            const string test = @"
                public class MyType
                {
                }";

            await VerifyCSharpHasNoDiagnosticsAsync(test);
        }


        [Fact]
        public async Task WhenClassImplementsIDisposableCallSuppressFinalize()
        {
            const string source = @"
                    using System;
                    public class MyType : System.IDisposable
                    {
                        public void Dispose()
                        {
                        }
                    }";

            const string fixtest = @"
                    using System;
                    public class MyType : System.IDisposable
                    {
                        public void Dispose()
                        {
                            GC.SuppressFinalize(this);
                        }
                    }";

            await VerifyCSharpFixAsync(source, fixtest, 0);
        }

        [Fact]
        public async Task WhenClassHasParametrizedDisposeMethod()
        {
            const string source = @"
                    using System;
                    public class MyType : System.IDisposable
                    {
                        public void Dispose()
                        {
                            Dispose(true);
                        }
                        protected virtual void Dispose(bool disposing)
                        {
                        }
                    }";

            const string fixtest = @"
                    using System;
                    public class MyType : System.IDisposable
                    {
                        public void Dispose()
                        {
                            Dispose(true);
                            GC.SuppressFinalize(this);
                        }
                        protected virtual void Dispose(bool disposing)
                        {
                        }
                    }";
            await VerifyCSharpFixAsync(source, fixtest, 0);
        }

        [Fact]
        public async Task WhenClassExplicitImplementsOfIDisposableCallSuppressFinalize()
        {
            const string source = @"
                    using System;
                    public class MyType : IDisposable
                    {
                        void IDisposable.Dispose()
                        {
                        }
                    }";

            const string fixtest = @"
                    using System;
                    public class MyType : IDisposable
                    {
                        void IDisposable.Dispose()
                        {
                            GC.SuppressFinalize(this);
                        }
                    }";
            await VerifyCSharpFixAsync(source, fixtest, 0);
        }

        [Fact]
        public async Task WhenClassHasParametrizedDisposeMethodAndExplicitlyImplementsIDisposable()
        {
            const string source = @"
                    using System;
                    public class MyType : System.IDisposable
                    {
                        void IDisposable.Dispose()
                        {
                            Dispose(true);
                        }

                        protected virtual void Dispose(bool disposing)
                        {

                        }
                    }";

            const string fixtest = @"
                    using System;
                    public class MyType : System.IDisposable
                    {
                        void IDisposable.Dispose()
                        {
                            Dispose(true);
                            GC.SuppressFinalize(this);
                        }

                        protected virtual void Dispose(bool disposing)
                        {

                        }
                    }";
            await VerifyCSharpFixAsync(source, fixtest, 0);
        }

        [Fact]
        public async Task AddsSystemGCWhenSystemIsNotImported()
        {
            const string source = @"
                    public class MyType : System.IDisposable
                    {
                        void IDisposable.Dispose()
                        {
                            Dispose(true);
                        }
                        protected virtual void Dispose(bool disposing)
                        {

                        }
                    }";

            const string fixtest = @"
                    public class MyType : System.IDisposable
                    {
                        void IDisposable.Dispose()
                        {
                            Dispose(true);
                            System.GC.SuppressFinalize(this);
                        }
                        protected virtual void Dispose(bool disposing)
                        {

                        }
                    }";
            await VerifyCSharpFixAsync(source, fixtest, 0);
        }

        [Fact]
        public async Task CallingSystemGCSupressFinalizeShouldNotGenerateDiags()
        {
            const string source = @"
                    public class MyType : System.IDisposable
                    {
                        void IDisposable.Dispose()
                        {
                            Dispose(true);
                            System.GC.SuppressFinalize(this);
                        }
                        protected virtual void Dispose(bool disposing)
                        {

                        }
                    }";

            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task CallingGCSupressFinalizeWithAliasShouldNotGenerateDiags()
        {
            const string source = @"using A = System;
                    public class MyType : System.IDisposable
                    {
                        void IDisposable.Dispose()
                        {
                            Dispose(true);
                            A.GC.SuppressFinalize(this);
                        }
                        protected virtual void Dispose(bool disposing)
                        {

                        }
                    }";

            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task UseSystemGCWhenSystemNamespaceWasNotImportedInCurrentContext()
        {
            const string source = @"
                    namespace A
                    {
                        using System;
                    }
                    namespace B
                    {
                        class Foo : System.IDisposable
                        {
                            public void Dispose()
                            {
                            }
                        }
                    }";

            const string fixtest = @"
                    namespace A
                    {
                        using System;
                    }
                    namespace B
                    {
                        class Foo : System.IDisposable
                        {
                            public void Dispose()
                            {
                                System.GC.SuppressFinalize(this);
                            }
                        }
                    }";


            await VerifyCSharpFixAsync(source, fixtest, 0);
        }

        [Fact]
        public async Task CallSupressWhenUsingExpressionBodiedMethod()
        {
            const string source = @"
                   using System;
                   using System.IO;

                   public class MyType : System.IDisposable
                   {
                        MemoryStream memory;

                        public virtual void Dispose() => memory.Dispose();
                   }";

            const string fixtest = @"
                   using System;
                   using System.IO;

                   public class MyType : System.IDisposable
                   {
                        MemoryStream memory;

                        public virtual void Dispose()
                        {
                           memory.Dispose();
                           GC.SuppressFinalize(this);
                        }
                   }";

            await VerifyCSharpFixAsync(source, fixtest, 0);
        }

    }
}