# Deprecated Features

This document lists features in CodeCracker that have been deprecated and will be removed in future versions.

## MakeMethodNonAsyncCodeFixProvider (CS1998)

**Deprecated in:** v1.2.0  
**Planned removal:** v2.0.0  
**Reason:** Redundant with built-in compiler and IDE support

### Background

The `MakeMethodNonAsyncCodeFixProvider` was created to address CS1998 warnings ("This async method lacks 'await' operators and will run synchronously"). This code fix would:
- Remove the `async` keyword from methods
- Wrap return values in `Task.FromResult()`

### Why It's Deprecated

1. **Compiler Support**: The C# compiler already issues CS1998 as a warning
2. **IDE Integration**: Modern IDEs (Visual Studio 2022+, Rider, VS Code) provide built-in quick-fixes for CS1998
3. **Rare Pattern**: The async-without-await pattern is increasingly uncommon in modern C# codebases
4. **Better Practices Available**: Modern C# has better patterns for this scenario

### Migration Guide

Instead of relying on this code fix, use one of these approaches:

#### Option 1: Use IDE Quick-Fixes
When you see a CS1998 warning, use your IDE's built-in quick-fix (usually Ctrl+. in Visual Studio):
```csharp
// Before (with CS1998 warning)
public async Task<int> GetValueAsync()
{
    return 42;  // CS1998: This async method lacks 'await' operators
}

// After (IDE quick-fix)
public Task<int> GetValueAsync()
{
    return Task.FromResult(42);
}
```

#### Option 2: Task Eliding (Recommended)
If you're just returning another Task, don't await it:
```csharp
// ? Inefficient - creates unnecessary state machine
public async Task<Data> GetDataAsync()
{
    return await _repository.GetDataAsync();
}

// ? Better - return the task directly
public Task<Data> GetDataAsync()
{
    return _repository.GetDataAsync();
}

// ? Even better - use expression-bodied member
public Task<Data> GetDataAsync() => _repository.GetDataAsync();
```

#### Option 3: Use Task.FromResult for Synchronous Methods
```csharp
// For simple synchronous returns
public Task<int> GetConstantAsync() => Task.FromResult(42);

// For computed values
public Task<string> GetNameAsync() => Task.FromResult(_user.Name);

// For nullable returns
public Task<User?> FindUserAsync(int id) => 
    Task.FromResult(_users.FirstOrDefault(u => u.Id == id));
```

### What to Do If You Were Using This Feature

1. **Immediate**: The feature still works but shows an `[Obsolete]` warning
2. **Short-term**: Update your code to use the patterns above
3. **Long-term**: The feature will be removed in v2.0.0

### Additional Resources

- [Microsoft Docs: Asynchronous programming with async and await](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/)
- [CS1998 Compiler Warning](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs1998)
- [Task-based Asynchronous Pattern (TAP)](https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap)
