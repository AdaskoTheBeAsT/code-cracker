using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCracker.Test
{
    /// <summary>
    /// Diagnostic Producer class with extra methods dealing with applying codefixes
    /// All methods are static
    /// </summary>
    public abstract partial class CodeFixVerifier
    {
        /// <summary>
        /// Apply the inputted CodeAction to the inputted document.
        /// Meant to be used to apply codefixes.
        /// </summary>
        /// <param name="document">The Document to apply the fix on</param>
        /// <param name="codeAction">A CodeAction that will be applied to the Document.</param>
        /// <returns>A Document with the changes from the CodeAction</returns>
        private static async Task<Document> ApplyFixAsync(Document document, CodeAction codeAction)
        {
            var operations = await codeAction.GetOperationsAsync(CancellationToken.None).ConfigureAwait(true);
            var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
            return solution.GetDocument(document.Id);
        }

        private static async Task<Project> ApplyFixAsync(Project project, CodeAction codeAction)
        {
            var operations = await codeAction.GetOperationsAsync(CancellationToken.None).ConfigureAwait(true);
            var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
            return solution.GetProject(project.Id);
        }

        /// <summary>
        /// Compare diagnostic counts by Id, ignoring changes in order and source location.
        /// Return occurrences in the second collection that exceed the original count for that Id.
        /// </summary>
        /// <param name="diagnostics">The Diagnostics that existed in the code before the CodeFix was applied</param>
        /// <param name="newDiagnostics">The Diagnostics that exist in the code after the CodeFix was applied</param>
        /// <returns>A list of Diagnostics that only surfaced in the code after the CodeFix was applied</returns>
        protected static IEnumerable<Diagnostic> GetNewDiagnostics(IEnumerable<Diagnostic> diagnostics, IEnumerable<Diagnostic> newDiagnostics)
        {
            var remainingCounts = diagnostics.GroupBy(d => d.Id).ToDictionary(group => group.Key, group => group.Count());
            foreach (var diagnostic in newDiagnostics.OrderBy(d => d.Location.SourceSpan.Start))
            {
                if (remainingCounts.TryGetValue(diagnostic.Id, out var count) && count > 0)
                    remainingCounts[diagnostic.Id] = count - 1;
                else
                    yield return diagnostic;
            }
        }

        /// <summary>
        /// Get the existing compiler diagnostics on the inputted document.
        /// </summary>
        /// <param name="document">The Document to run the compiler diagnostic analyzers on</param>
        /// <returns>The compiler diagnostics that were found in the code</returns>
        private static async Task<IEnumerable<Diagnostic>> GetCompilerDiagnosticsAsync(Document document)
        {
            var syntaxTree = await document.GetSyntaxTreeAsync().ConfigureAwait(true);
            var compilation = await document.Project.GetCompilationAsync().ConfigureAwait(true);
            return compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Location == Location.None || diagnostic.Location.SourceTree == syntaxTree);
        }
    }
}
