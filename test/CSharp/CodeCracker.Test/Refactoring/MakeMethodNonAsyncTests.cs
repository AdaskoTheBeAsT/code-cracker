using System.Threading.Tasks;
using CodeCracker.CSharp.Refactoring;
using Microsoft.CodeAnalysis.CodeFixes;
using Xunit;

namespace CodeCracker.Test.CSharp.Refactoring
{
    /// <summary>
    /// DEPRECATED: These tests are for a deprecated code fix provider.
    /// 
    /// The MakeMethodNonAsyncCodeFixProvider is deprecated because:
    /// - CS1998 is already a compiler warning with built-in IDE support
    /// - Modern IDEs provide quick-fixes for this pattern
    /// - The async-without-await pattern is increasingly rare in modern C#
    /// 
    /// These tests are kept for backward compatibility but may be removed in a future version.
    /// </summary>
    public class MakeMethodNonAsyncTests : CodeFixVerifier
    {
        /// <summary>
        /// This test verifies the deprecated code fix behavior.
        /// 
        /// Note: This functionality is deprecated. Modern best practice is to:
        /// 1. Not use async without await in the first place
        /// 2. Use IDE quick-fixes for CS1998 warnings
        /// 3. Return Task/Task&lt;T&gt; directly without async modifier
        /// 
        /// Example modern approach:
        /// <code>
        /// // Instead of: async Task&lt;int&gt; FooAsync() { return 42; }
        /// // Use: Task&lt;int&gt; FooAsync() => Task.FromResult(42);
        /// </code>
        /// </summary>
        [Fact]
#pragma warning disable CS0618 // Type or member is obsolete
        public async Task ShouldRemoveAsyncKeywordAndReplaceReturnedValuesWithTaskFromResultAsync()
        {
            const string codeFileTemplate = @"using System.Threading.Tasks;
class Test
{{
{0}
}}";

            const string testMethod = @"
public static async Task<int> FooAsync()
{
    return 42;
}";
            var testCode = string.Format(codeFileTemplate, testMethod);
            const string fixedMethod = @"
public static Task<int> FooAsync()
{
    return Task.FromResult(42);
}";
            var fixedCode = string.Format(codeFileTemplate, fixedMethod);

            await VerifyCSharpFixAsync(testCode, fixedCode);
        }
#pragma warning restore CS0618 // Type or member is obsolete

        protected override CodeFixProvider GetCodeFixProvider() => new MakeMethodNonAsyncCodeFixProvider();
    }
}
