using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeCracker.Test
{
    public abstract partial class CodeFixVerifier
    {
        protected static Document CreateCompilerValidCSharpDocument(string source, LanguageVersion languageVersion,
            CSharpCompilationOptions options = null, IEnumerable<MetadataReference> additionalReferences = null)
        {
            var document = CreateDocument(source, LanguageNames.CSharp, languageVersion,
                Microsoft.CodeAnalysis.VisualBasic.LanguageVersion.VisualBasic14);
            var project = document.Project;
            project = project.WithCompilationOptions(options ?? new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            if (additionalReferences != null)
                project = project.AddMetadataReferences(additionalReferences);
            return project.GetDocument(document.Id);
        }

        protected static async Task AssertCompilesAsync(Document document)
        {
            var compilation = await document.Project.GetCompilationAsync();
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.True(errors.Length == 0, "Source must compile:\n" + string.Join("\n", errors.Select(d => d.ToString())));
            using (var stream = new MemoryStream())
            {
                var emitErrors = compilation.Emit(stream).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                Assert.True(emitErrors.Length == 0, "Source must emit:\n" + string.Join("\n", emitErrors.Select(d => d.ToString())));
            }
        }

        protected async Task VerifyCompilerValidCSharpNoDiagnosticAsync(string source, LanguageVersion languageVersion,
            CSharpCompilationOptions options = null)
        {
            var document = CreateCompilerValidCSharpDocument(source, languageVersion, options);
            await AssertCompilesAsync(document);
            Assert.Empty(await GetSortedDiagnosticsFromDocumentsAsync(GetDiagnosticAnalyzer(), new[] { document }));
        }

        protected async Task<Document> VerifyCompilerValidCSharpFixAsync(string source, string expected,
            LanguageVersion languageVersion, CSharpCompilationOptions options = null, int diagnosticCount = 1,
            bool fixAll = false, string diagnosticId = null, string diagnosticText = null)
        {
            var document = CreateCompilerValidCSharpDocument(source, languageVersion, options);
            await AssertCompilesAsync(document);
            var original = document;
            var diagnostics = await GetSortedDiagnosticsFromDocumentsAsync(GetDiagnosticAnalyzer(), new[] { document });
            Assert.Equal(diagnosticCount, diagnostics.Length);
            Assert.NotEmpty(diagnostics);
            var provider = GetCodeFixProvider();
            var originalText = await document.GetTextAsync();
            foreach (var diagnostic in diagnostics)
            {
                Assert.Contains(diagnostic.Id, provider.FixableDiagnosticIds);
                Assert.Equal(document.Id, document.Project.GetDocument(diagnostic.Location.SourceTree).Id);
                if (diagnosticId != null) Assert.Equal(diagnosticId, diagnostic.Id);
                if (diagnosticText != null) Assert.Equal(diagnosticText, originalText.ToString(diagnostic.Location.SourceSpan));
            }

            if (fixAll)
            {
                var context = new FixAllContext(document, provider, FixAllScope.Document,
                    provider.GetType().Name, provider.FixableDiagnosticIds,
                    new CompilerValidDiagnosticProvider(GetDiagnosticAnalyzer()), CancellationToken.None);
                var action = await provider.GetFixAllProvider().GetFixAsync(context);
                Assert.NotNull(action);
                document = await ApplyFixAsync(document, action);
                await AssertCompilesAsync(document);
            }
            else
            {
                for (var i = 0; i < diagnosticCount; i++)
                {
                    diagnostics = await GetSortedDiagnosticsFromDocumentsAsync(GetDiagnosticAnalyzer(), new[] { document });
                    Assert.NotEmpty(diagnostics);
                    var actions = new List<CodeAction>();
                    await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostics[0],
                        (action, _) => actions.Add(action), CancellationToken.None));
                    Assert.Single(actions);
                    document = await ApplyFixAsync(document, actions[0]);
                    await AssertCompilesAsync(document);
                }
            }

            Assert.Empty(await GetSortedDiagnosticsFromDocumentsAsync(GetDiagnosticAnalyzer(), new[] { document }));
            var expectedDocument = CreateCompilerValidCSharpDocument(expected, languageVersion, options);
            await AssertCompilesAsync(expectedDocument);
            var formatted = await Formatter.FormatAsync(document);
            var formattedExpected = await Formatter.FormatAsync(expectedDocument);
            Assert.Equal((await formattedExpected.GetTextAsync()).ToString(), (await formatted.GetTextAsync()).ToString());
            await AssertLocalTypesPreservedAsync(original, document);
            await AssertBindingsPreservedAsync(original, document);
            return document;
        }

        protected static async Task AssertLocalTypesPreservedAsync(Document original, Document rewritten)
        {
            Assert.Equal(await GetLocalSignaturesAsync(original), await GetLocalSignaturesAsync(rewritten));
        }

        protected async Task VerifyCompilerValidCSharpFixAllAsync(string[] sources, string[] expected,
            LanguageVersion languageVersion, int diagnosticCount)
        {
            Assert.Equal(sources.Length, expected.Length);
            var project = CreateProject(sources, LanguageNames.CSharp, languageVersion,
                Microsoft.CodeAnalysis.VisualBasic.LanguageVersion.VisualBasic14)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var originalDocuments = project.Documents.ToArray();
            foreach (var document in originalDocuments) await AssertCompilesAsync(document);
            var diagnostics = await GetSortedDiagnosticsFromDocumentsAsync(GetDiagnosticAnalyzer(), originalDocuments);
            Assert.Equal(diagnosticCount, diagnostics.Length);
            Assert.NotEmpty(diagnostics);
            var provider = GetCodeFixProvider();
            var context = new FixAllContext(originalDocuments[0], provider, FixAllScope.Solution,
                provider.GetType().Name, provider.FixableDiagnosticIds,
                new CompilerValidDiagnosticProvider(GetDiagnosticAnalyzer()), CancellationToken.None);
            var action = await provider.GetFixAllProvider().GetFixAsync(context);
            Assert.NotNull(action);
            project = await ApplyFixAsync(project, action);
            Assert.Empty(await GetSortedDiagnosticsFromDocumentsAsync(GetDiagnosticAnalyzer(), project.Documents.ToArray()));
            for (var i = 0; i < originalDocuments.Length; i++)
            {
                var document = project.GetDocument(originalDocuments[i].Id);
                await AssertCompilesAsync(document);
                var expectedDocument = CreateCompilerValidCSharpDocument(expected[i], languageVersion);
                await AssertCompilesAsync(expectedDocument);
                Assert.Equal((await (await Formatter.FormatAsync(expectedDocument)).GetTextAsync()).ToString(),
                    (await (await Formatter.FormatAsync(document)).GetTextAsync()).ToString());
                await AssertLocalTypesPreservedAsync(originalDocuments[i], document);
                await AssertBindingsPreservedAsync(originalDocuments[i], document);
            }
        }

        protected static async Task AssertBindingsPreservedAsync(Document original, Document rewritten)
        {
            Assert.Equal(await GetBindingSignaturesAsync(original), await GetBindingSignaturesAsync(rewritten));
        }

        private static async Task<string[]> GetBindingSignaturesAsync(Document document)
        {
            var root = await document.GetSyntaxRootAsync();
            var model = await document.GetSemanticModelAsync();
            return root.DescendantNodes().Where(n => n is InvocationExpressionSyntax ||
                    n is BaseObjectCreationExpressionSyntax || n is ElementAccessExpressionSyntax)
                .Select(n => model.GetSymbolInfo(n).Symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                .ToArray();
        }

        private static async Task<string[]> GetLocalSignaturesAsync(Document document)
        {
            var root = await document.GetSyntaxRootAsync();
            var model = await document.GetSemanticModelAsync();
            var format = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
            return root.DescendantNodes().Where(n => n is VariableDeclaratorSyntax ||
                    n is SingleVariableDesignationSyntax || n is ForEachStatementSyntax)
                .Select(n => model.GetDeclaredSymbol(n) as ILocalSymbol)
                .Where(s => s != null)
                .Select(s => $"{s.Name}:{s.Type.ToDisplayString(format)}:{s.NullableAnnotation}:{s.RefKind}:{s.ScopedKind}")
                .ToArray();
        }

        protected static async Task<object> ExecuteCompilerValidCSharpAsync(Document document, string typeName, string methodName)
        {
            await AssertCompilesAsync(document);
            var compilation = await document.Project.GetCompilationAsync();
            using (var stream = new MemoryStream())
            {
                var emit = compilation.Emit(stream);
                Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
                stream.Position = 0;
                var loadContext = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
                try
                {
                    var assembly = loadContext.LoadFromStream(stream);
                    return assembly.GetType(typeName).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                        .Invoke(null, null);
                }
                finally
                {
                    loadContext.Unload();
                }
            }
        }

        private sealed class CompilerValidDiagnosticProvider : FixAllContext.DiagnosticProvider
        {
            private readonly Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer analyzer;

            public CompilerValidDiagnosticProvider(Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer analyzer)
            {
                this.analyzer = analyzer;
            }

            public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
                await GetSortedDiagnosticsFromDocumentsAsync(analyzer, new[] { document });

            public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
                Task.FromResult(Enumerable.Empty<Diagnostic>());

            public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
                await GetSortedDiagnosticsFromDocumentsAsync(analyzer, project.Documents.ToArray());
        }
    }
}
