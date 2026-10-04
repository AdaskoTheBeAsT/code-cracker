# Modern C# syntax coverage plan

## Implementation progress

| Phase | Status | Evidence |
| --- | --- | --- |
| 1. Compiler-validity infrastructure | Complete | Eight strict-helper tests passed, including emit-only errors and additional metadata; shared helpers build for net8.0, net9.0, and net10.0. Legacy defaults unchanged. |
| 2. High-priority local syntax | Complete | Unsafe and modern-initializer suites pass, including C# 7.2 through 12 boundaries. |
| 3. Modern contexts and type preservation | Complete | C# 13/14, ref lifetimes, disposal execution, overloads, records, and nested nullable tuples pass; 132 focused tests total. |
| 4. Additional declaration forms | Complete | Out/constructor binding, nested/custom deconstruction, single-variable for, pattern/ref/await foreach pass. Typed patterns excluded. |
| 5. Robustness and compatibility | Complete | All 1,953 C# and 465 Visual Basic tests pass; all shared target frameworks build. Mixed fix-all, suppression, cancellation, C# 6-14, and performance checks included. |

Statuses are updated only after the corresponding checks pass. Compiler or
environment limitations will be recorded here instead of being marked complete.

### Findings during implementation

- Strict executable fixtures exposed a top-level code-fix exception: replacing
  a statement with a list is invalid inside `GlobalStatementSyntax`. Single
  replacements now stay single; multiple top-level declarations are wrapped
  as separate global statements.
- Speculative binding infers natural delegate types even for pre-C#-10 parse
  options. The analyzer now explicitly rejects anonymous-function/method-group
  inference on those older language versions.
- Declared local symbols, rather than type-syntax `ConvertedType`, are required
  to preserve actual nullable annotations. Nullable generic locals inside local
  functions/lambdas remain conservatively unchanged when speculative types do
  not compare equal.
- Async ref-struct lifetime diagnostics can appear only during emit. The
  deliberately invalid lifetime fixture checks emit diagnostics separately.
- Multiple out replacements are checked together before reporting diagnostics;
  independently safe replacements could otherwise make fix-all ambiguous.
- Numeric/boolean/character value-type literals use their intrinsic type instead
  of a speculative statement. The 1,000-declaration performance fixture measured
  178 ms / 17,824,368 bytes for the pre-expansion path and 167 ms / 18,932,464 bytes
  for the current path across three runs. Measurements are machine-dependent,
  not a performance guarantee or a complete benchmark of all syntax forms.
- Built-in array indexing has no indexer member symbol. Binding comparison
  handles matching array receiver types separately so ordinary and ref array
  element locals remain eligible for safe fixes.

### Final validation

- Full C# suite: **1,953 passed**, zero failures or skipped tests.
- Full Visual Basic suite: **465 passed**, zero failures or skipped tests.
- Focused AlwaysUseVar coverage: **196 cases**, included in the full C# run.
- Strict compiler-helper coverage: **8 cases**, included in the full C# run and
  rerun successfully after the final test-only cancellation-token adjustment.
- Style namespace: **580 passed** before the final array-index regression was
  added; that regression passed in the subsequent full C# run.
- Shared test helpers: net8.0, net9.0, and net10.0 builds passed.
- Existing Roslyn analyzer/deprecation warnings and Visual Basic package-pruning
  warnings remain outside this task's scope.
- No SDK/package upgrades or edits to unrelated user changes were required.

### Intentional exclusions

- Multi-variable `for` initializers cannot become one implicitly typed declaration.
- Typed patterns retain their matching types.
- Mixed deconstruction assignment/declaration forms are not normalized.
- Typed out discards and out declarations without an enclosing statement
  (for example, an expression-bodied member) are conservatively unchanged.
- Ambiguous overloads, changed nullable annotations, changed iteration
  conversions, and unavailable speculative binding produce no suggestion.

### New diagnostic locations

The diagnostic remains on the type being replaced for locals, out variables,
for initializers, and foreach iteration variables (inside ref/scoped wrappers).
Whole-deconstruction rewrites report once on the first explicit element type;
the code fix replaces the full designation. Expanded scope can therefore
introduce additional `CC0001`/`CC0105` warnings in consumer projects.

## Goal and boundaries

Extend `AlwaysUseVarAnalyzer` and `AlwaysUseVarCodeFixProvider` with reliable
modern-syntax regression coverage and carefully scoped support for additional
declaration forms. A suggestion must preserve inferred types, binding, reference
semantics, disposal, and lifetime restrictions.

This is an implementation plan, not a claim that the uncovered cases are bugs.
It does not propose a repository-wide syntax audit of unrelated analyzers.
Preserve existing user changes and diagnostic IDs `CC0001` and `CC0105`.

## Pre-implementation baseline

- The analyzer registers only `LocalDeclarationStatement`.
- Ref and scoped type wrappers are unwrapped before checking for existing `var`.
- Inferred declarations are bound speculatively and compared with the declared
  type, including nullability.
- The code fix preserves ref/scoped wrappers, `using`, `await using`, and comments.
- Existing tests cover basic refs, scoped declarations, target-typed expressions,
  collections, tuple names, nullability, raw strings, native integers, simple
  lambdas, switch expressions, top-level statements, and file-scoped namespaces.
- The pre-implementation validation passed 59 focused tests and all 1,808 C# tests. These counts
  are a baseline, not an exhaustive syntax-coverage measure.

## Safety rules

1. Already inferred declarations must not produce a diagnostic.
2. Suggest `var` only when the rewritten declaration compiles and keeps the
   original static type, tuple names, and nullable annotations.
3. Preserve binding of overloaded members and the selected conversions.
4. Preserve ref aliasing, readonly access, scoped escape restrictions, and
   disposal behavior.
5. Do not remove a type required by an initializer, overload, or pattern.
6. Incomplete, unresolved, or unsupported syntax must not produce `AD0001`.
7. Failed or unavailable speculative binding must produce no suggestion.
8. Do not apply the local-statement splitting strategy to declaration forms
   whose grammar or scope does not permit it.

## Phase 1: Strengthen the test infrastructure

Complete this before adding the remaining syntax tests.

### Work

- Add opt-in helpers that assert zero compiler errors for original and fixed
  sources. Do not change the default behavior of existing tests that intentionally
  contain unresolved symbols or invalid code.
- Allow test-specific compilation options: unsafe code, nullable context, and
  executable output for valid top-level-statement examples.
- Allow explicit language versions and required framework/custom metadata
  references. Do not rely exclusively on `LanguageVersion.Latest`.
- Require expected diagnostic IDs, locations, and code-action counts for positive
  cases. A fix test must prove that an action was actually offered and applied.
- For negative cases, require compiler-valid source and no diagnostic or action.
- Keep malformed-source tests separate: assert analyzer stability rather than
  source validity.
- Add semantic assertions for original/fixed local symbols, tuple element names,
  nullability, selected overloads, and ref kinds where relevant.
- Use small execution tests when compilation and symbol comparison cannot prove
  reference aliasing, disposal order, or preserved behavior.
- Test each new helper, including deliberate compiler failures and unsafe
  compilation, without changing unrelated verifier behavior.

### Files

- `test/Common/CodeCracker.Test.Common/Helpers/DiagnosticVerifier.Helper.cs`
- `test/Common/CodeCracker.Test.Common/Helpers/CodeFixVerifier.Helper.cs`
- `test/Common/CodeCracker.Test.Common/Verifiers/DiagnosticVerifier.cs`
- `test/Common/CodeCracker.Test.Common/Verifiers/CodeFixVerifier.cs`
- `test/CSharp/CodeCracker.Test/Helpers/CodeFixVerifierTests.cs`

Use the existing legacy verifier infrastructure. Do not migrate the whole test
suite or upgrade dependencies merely to add these tests.

### Acceptance

Valid-source tests fail on compiler errors, malformed-source tests remain usable,
and existing helper callers retain their current behavior.

## Phase 2: High-priority coverage within the current analyzer scope

For each family, add safe-fix, must-not-fix, already-inferred, and malformed-source
cases where applicable. First determine behavior with the installed Roslyn
version; fix only defects demonstrated by the tests.

| Family | Cases to add | Required outcome |
| --- | --- | --- |
| Stack allocation | Explicit and inferred stackalloc element types; `Span<T>`, `ReadOnlySpan<T>`, and scoped spans; pointer locals in unsafe blocks; nested conditional expressions | Reject replacements that infer a pointer instead of a span or violate safe-context rules. Preserve valid pointer types when inference is supported. |
| Conditional references | `ref (condition ? ref left : ref right)`; mutable, readonly, and scoped locals; ref-returning methods, properties, and indexers; generic ref returns | Preserve the referred storage, ref kind, and escape restrictions. Mutating an inferred mutable ref must still mutate its original target. |
| Unsafe pointers and function pointers | Pointer casts and address-of expressions; `delegate*` with managed/unmanaged calling conventions; ref parameters and ref returns; explicit function-pointer casts | Reject address-of method groups requiring a target type. Allow independently typed initializers only when the complete signature and calling convention are preserved. |
| Collection expressions | Spreads such as `[..items]`; nested collections; arrays, spans, interfaces, immutable collections, and custom collection builders; explicit casts; conditional/switch branches | Reject untyped collection expressions and any replacement changing the constructed collection type. Allow typed casts only when inference and binding are unchanged. |
| Lambdas and method groups | Static/async lambdas; explicit return types; typed parameters; optional parameters and params forms; lambda attributes; overloaded/generic method groups; custom delegates and expression trees | Preserve the delegate type and selected overload. Skip natural delegate types that differ from the declared delegate, and lambdas whose body depends on the target type. |

Representative cases:

```csharp
Span<int> values = stackalloc int[4];             // Do not infer a pointer.
ref int selected = ref (flag ? ref left : ref right);
delegate*<int, int> callback = &Transform;         // Target type is required.
int[] combined = [..items];                       // No independent collection type.
Func<int> factory = static () => 42;              // Verify independent inference.
```

Each example must be embedded in a compiler-valid fixture with the necessary
members, references, and compilation options.

## Phase 3: Modern contexts and deeper type preservation

### 3.1 Ref structs in async methods and iterators

- Add permitted ref-struct and ref-local declarations that do not cross an
  `await` or `yield` suspension point.
- Include scoped spans and pattern-based `using` disposal on ref structs.
- Verify unchanged lifetime errors for deliberately invalid examples separately
  from valid-source tests.
- Include single fixes and fix-all in methods with multiple suspension points.

### 3.2 C# 14 contexts

- Add local declarations inside field-backed property accessors using `field`.
- Add locals inside extension-block methods and accessors, including generic
  receivers and captures available from the surrounding declaration.
- Add array-to-span and span-to-readonly-span scenarios affected by first-class
  span conversions. Preserve the declared span type and selected overload.
- Confirm speculative binding works in these contexts. If it does not, use a
  supported conservative binding strategy; do not emit an unverified diagnostic.

### 3.3 Other initializer and enclosing-context coverage

- Record and struct `with` expressions, including generic records.
- Nested nullable tuples in arrays and generic arguments, with matching and
  mismatching tuple names.
- Generic constraints, nullable type parameters, and nested aliases.
- Locals that reference captured primary-constructor parameters.
- Locals inside lambdas, local functions, pattern branches, and generic methods.
- Multi-variable declarations in which later initializers reference earlier
  variables, especially when one initializer requires its declared type.

### Acceptance

Positive cases preserve type and binding. Negative cases remain diagnostic-free.
All valid fixtures compile before and after fixes.

## Phase 4: Expand supported declaration forms

These additions need new syntax registrations and form-specific code fixes.
Implement them separately from the local-declaration safety changes. Do not feed
them into the existing `LocalDeclarationStatementSyntax` cast.

### 4.1 Out-variable declarations

- Support declaration expressions in arguments, including overloaded and generic
  methods and constructors.
- Consider `TryParse(text, out int value)` to `TryParse(text, out var value)`.
- Rebind the enclosing invocation and require the same selected method and
  inferred generic arguments, not merely the same local type.
- Skip replacements that make overload resolution ambiguous or change binding.
- Preserve existing `out var`, discards, scope, and definite assignment.

### 4.2 Typed deconstruction

- Cover tuple expressions and custom `Deconstruct` methods, including overloaded
  and generic methods.
- Support declarations such as `(int count, string name) = GetPair()` only when
  converting to `var (count, name)` preserves every element type and binding.
- Include nested deconstruction, discards, and already inferred elements.
- Leave mixed assignment/declaration forms and conversions requiring explicit
  element types unchanged unless a semantics-preserving rewrite is proven.

### 4.3 For initializers

- Analyze `ForStatementSyntax.Declaration`.
- Support single-variable initializers when inference preserves semantics.
- Skip multiple-variable loop declarations: C# does not allow multiple declarators
  in an implicitly typed local declaration.
- Do not split the loop header into statements or hoist declarations outside the
  loop. Preserve loop-local scope, execution order, and definite assignment.

### 4.4 Foreach declarations

- Cover ordinary, ref, ref-readonly, await, and deconstructing foreach forms.
- Compare the original iteration-variable type with the compiler-inferred
  iteration type.
- Skip explicit iteration conversions that `var` would remove.
- Preserve ref aliasing, readonly behavior, await behavior, and the selected
  enumeration/deconstruction pattern.

### 4.5 Typed patterns: explicit exclusion

- Add boundary tests for declaration, recursive, property, list, and relational
  patterns with surrounding local declarations.
- Do not convert a type pattern such as `value is int item` to `value is var item`;
  the latter changes matching behavior.
- Include definite-assignment and pattern-variable scope cases so speculative
  analysis of nearby ordinary locals remains reliable.

### Architecture and acceptance

- Extract reusable type-preservation checks without weakening safety rules.
- Dispatch code fixes by the owning declaration form.
- Rebind the relevant enclosing construct for each form; comparing only a local
  symbol is insufficient when overload resolution or deconstruction can change.
- Preserve existing diagnostic IDs and classify primitive/nonprimitive types
  consistently. Document newly supported diagnostic locations.
- Test each form independently before enabling mixed-form fix-all.
- Document that expanded scope can introduce new warnings in consumer projects.

## Phase 5: Cross-cutting robustness and compatibility

- Add fix-all across mixed ordinary, using, ref, loop, out, and deconstruction
  declarations in multiple documents.
- Include safe and unsafe candidates together; unsupported candidates must not
  prevent unrelated safe fixes.
- Check comments, directives, trivia around type wrappers, and nullable directives.
- Check invalid or partially typed versions of every newly handled syntax family.
- Retain generated-code and diagnostic-suppression behavior.
- Exercise cancellation and avoid repeated full-compilation work in hot paths.
- Compare analysis time and allocations on representative declaration-heavy
  sources before and after the change. Investigate material regressions.
- Verify unchanged behavior on older supported C# language versions.

Use explicit feature-version boundaries where possible:

- C# 7.2/7.3: conditional refs, stackalloc initializers, and ref iteration.
- C# 8: using declarations, await foreach, and nullable reference types.
- C# 9: static lambdas, function pointers, target-typed construction, and records.
- C# 10: natural lambda/method-group types and explicit lambda return types.
- C# 11: scoped declarations and raw strings.
- C# 12: collection expressions, primary constructors, and lambda defaults.
- C# 13: permitted ref/unsafe usage in async methods and iterators.
- C# 14: field-backed properties, extension blocks, and span conversions.

Use the language versions actually supported by the repository's Roslyn package.
Report unsupported requested features explicitly rather than silently skipping
them or relying on preview syntax.

## Implementation files and test organization

Primary implementation:

- `src/CSharp/CodeCracker/Style/AlwaysUseVarAnalyzer.cs`
- `src/CSharp/CodeCracker/Style/AlwaysUseVarCodeFixProvider.cs`

Keep existing tests in:

- `test/CSharp/CodeCracker.Test/Style/AlwaysUseVarTests.cs`

Add focused sibling test classes as coverage grows:

- `AlwaysUseVarModernSyntaxTests`: initializer and enclosing-context coverage.
- `AlwaysUseVarUnsafeSyntaxTests`: pointers, function pointers, and stackalloc.
- `AlwaysUseVarDeclarationFormsTests`: out variables, deconstruction, and loops.

Share minimal fixtures and compiler-validity helpers, not one large source file
that makes failures difficult to isolate.

## Validation commands

Run from the repository root using its configured Microsoft.Testing.Platform
runner. These commands were used during implementation; the completed validation
results are recorded above.

```powershell
dotnet test --project .\test\CSharp\CodeCracker.Test\CodeCracker.Test.csproj --filter-class "CodeCracker.Test.CSharp.Style.AlwaysUseVar*" --no-progress
dotnet test --project .\test\CSharp\CodeCracker.Test\CodeCracker.Test.csproj --filter-namespace CodeCracker.Test.CSharp.Style --no-progress
dotnet test --project .\test\CSharp\CodeCracker.Test\CodeCracker.Test.csproj --no-progress
git diff --check
```

Build the shared test project for all of its target frameworks after changing
shared helpers. Run the Visual Basic tests when shared-helper changes affect
their execution path, using that project's configured runner.

## Completion criteria

- Every listed syntax family has explicit coverage or a documented intentional
  exclusion backed by tests.
- Positive fixtures and their fixed results compile without errors.
- Negative fixtures prove there is no unsafe diagnostic or code action.
- Malformed-source fixtures produce no analyzer exceptions.
- Semantic and execution checks demonstrate unchanged behavior where needed.
- Fix-all preserves comments, scopes, modifiers, and independent safe fixes.
- Existing and new C# tests pass; shared-helper compatibility checks pass.
- Compiler/version limitations and existing unrelated warnings are reported.
- Review confirms no unrelated user changes were overwritten.

## Recommended delivery sequence

1. Compiler-validity helpers and their own tests.
2. Stackalloc, conditional refs, unsafe/function-pointer cases.
3. Collection spreads/builders and lambda/method-group inference.
4. Async/iterator ref structs, C# 14 contexts, and deeper type checks.
5. Out-variable support.
6. Deconstruction support.
7. For and foreach support, plus typed-pattern exclusion tests.
8. Mixed-form fix-all, performance checks, full-suite validation, and documentation.

Keep these as reviewable changes. Complete validation for one phase before
starting the next; do not bundle scope expansion into an unverified crash fix.

## Completed deliverables

- [x] Opt-in compiler-validity, emit-validity, unsafe/options/reference helpers.
- [x] Semantic local-type/ref-kind/scoped checks and selected-binding assertions.
- [x] Execution tests for aliasing, disposal, and overload behavior.
- [x] Stackalloc, pointers/function pointers, collections/builders, and delegates.
- [x] Async/iterator ref-struct lifetimes, C# 14 contexts, and deep type coverage.
- [x] Safe out-variable, typed-deconstruction, for, and foreach support.
- [x] Multi-document mixed fix-all and joint out-overload safety.
- [x] Malformed-source, generated-code, suppression, and cancellation checks.
- [x] Explicit language-version boundaries and performance measurement.
- [x] Full C#/Visual Basic and multi-target shared-helper validation.
- [x] Intentional exclusions and additional consumer diagnostics documented.
