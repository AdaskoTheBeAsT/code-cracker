using Microsoft.CodeAnalysis;
using System;
using System.Linq;
using Xunit;

namespace CodeCracker.Test.CSharp.Helpers
{
    public class CodeFixVerifierTests : CodeFixVerifier
    {
        [Theory]
        [InlineData("", "", "")]
        [InlineData("", "A", "A")]
        [InlineData("A", "", "")]
        [InlineData("A,B", "B", "")]
        [InlineData("A,B,C", "A,C", "")]
        [InlineData("A,B", "B,A", "")]
        [InlineData("A,A,B", "A,B", "")]
        [InlineData("A", "A,A", "A")]
        [InlineData("A,B", "B,C", "C")]
        [InlineData("A,B", "C,B,D", "C,D")]
        public void OnlyNewDiagnosticOccurrencesAreReported(string before, string after, string expected)
        {
            var diagnostics = GetNewDiagnostics(CreateDiagnostics(before), CreateDiagnostics(after));

            Assert.Equal(expected, string.Join(",", diagnostics.Select(diagnostic => diagnostic.Id)));
        }

        private static Diagnostic[] CreateDiagnostics(string ids) =>
            ids.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(id => Diagnostic.Create(
                    new DiagnosticDescriptor(id, id, id, "Test", DiagnosticSeverity.Warning, true),
                    Location.None))
                .ToArray();
    }
}
