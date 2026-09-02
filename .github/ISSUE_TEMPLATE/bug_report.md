---
name: Bug report
about: Report a bug in Resilion
title: ''
labels: bug
assignees: ''

---

**Resilion version**
<!-- e.g. 0.1.0 -->

**.NET SDK version**
<!-- Run `dotnet --version` and paste the output -->

**Target framework**
<!-- e.g. net9.0 -->

**Describe the bug**
A clear description of what the bug is.

**Minimal reproduction**
<!-- Paste the smallest code that reproduces the issue -->

```csharp
var pipeline = Pipeline.Create(builder => builder
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddTimeout(TimeSpan.FromSeconds(10)));

await pipeline.ExecuteAsync(async (state, ct) =>
{
    // Your operation here
    return null;
}, (object?)null, CancellationToken.None);
```

**Expected behavior**
What you expected to happen.

**Actual behavior**
What actually happened. Include any exceptions or stack traces.

**Additional context**
Any other context about the problem.
