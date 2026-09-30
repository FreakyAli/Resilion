# Claim Verification

Every factual claim in [future-plans.md](future-plans.md), [tradeoffs.md](tradeoffs.md),
[telemetry.md](telemetry.md) and [architecture.md](architecture.md) about *what the code currently
does* must be re-verified with a command from this file before it is written or re-asserted.

**Never assert "grep confirms X" without pasting the command and its output into the PR
description.** This file exists because `future-plans.md` once carried three items documented twice
with opposite verdicts — one set of entries asserting "grep confirms zero tag usage" and "zero
matches for PublicAPI" while a block above them declared the same items complete. Both were written
from memory. One of them was wrong in a way that left the 1.0 API freeze completely unenforced.

Run everything from the repository root.

**Last full sweep:** 2026-09-30, after the 1.0 hardening pass. Expectations below describe the
code as it stands now; lines marked *(was …)* record what the same command returned before that
pass, because several of these claims had drifted.

---

## Telemetry

```bash
# Does the sync hedging path increment the counter?  (future-plans #55)
grep -n 'HedgingAttempts' src/Resilion/Hedging/HedgingStrategy.cs
# Expected: two hits — the async launch loop and the sync loop. (was: one, line 92 only, so the
# sync path reported zero for every sequential sync hedge.)

# Which execution entry points start a span?  (future-plans #56)
grep -rn 'StartActivity' src/ --include='*.cs' | grep -v '/obj/'
# Expected: 16 hits across 7 files, all via StrategyActivity.Start. (was: 10 across 5 — typed
# retry, typed circuit breaker and sync hedging emitted no spans at all.)
grep -c 'StrategyActivity.Start' src/Resilion/CircuitBreaker/CircuitBreakerTypedStrategy.cs
# Expected: 2 (async + sync). (was: 0.)

# Which span tag keys does the code actually set?  (future-plans #58)
grep -rho 'SetTag("[^"]*"' src --include='*.cs' | sort -u
# Expected: only operation.key, outcome, pipeline.name, strategy.name — now set in one place,
# StrategyActivity. telemetry.md must document exactly this set.

# Do all counter increments carry both tags?  (retired #49)
grep -rn '\.Add(1' src/ --include='*.cs' | grep -v '/obj/' | grep -vc 'PipelineNameTag'
# Expected: 0 — every .Add(1, ...) site passes PipelineNameTag.
grep -rn '\.Add(1' src/ --include='*.cs' | grep -v '/obj/' | wc -l
# Expected: 14 sites (the sync hedging increment was added).

# Does hedging copy PipelineName to per-attempt contexts?  (future-plans #59)
grep -n 'PipelineName\|OperationKey\|ContinueOnCapturedContext' src/Resilion/Hedging/HedgingStrategy.cs
# Expected today: OperationKey and ContinueOnCapturedContext are copied; PipelineName is NOT.
```

## Public API surface

```bash
# Are the tracking files in the format the analyzer consumes?  (future-plans #57)
grep -c '^Resilion\.' src/Resilion/PublicAPI.Unshipped.txt
# Shipped.txt is the frozen 1.0 surface; Unshipped.txt holds only `#nullable enable`.
grep -c '^Resilion\.' src/Resilion/PublicAPI.Shipped.txt      # expected: 293
# (was: Unshipped held 242 hand-written lines in a format the analyzer cannot parse, and
#  Shipped.txt held only comments — so RS0016 fired 742 times and RS0017 508 times.)
grep -vE '^(#nullable enable|Resilion)' src/*/PublicAPI.Unshipped.txt
# Expected AFTER the fix: no output.

# Is anything enforced?
grep -rn 'TreatWarningsAsErrors\|WarningsAsErrors\|RS00' \
  --include='*.props' --include='*.csproj' --include='.editorconfig' .
# Expected: a WarningsAsErrors line in src/Directory.Build.props scoped to RS0016;RS0017.
dotnet build Resilion.sln -c Release --no-incremental 2>&1 | grep -oE 'warning RS[0-9]+' | sort | uniq -c
# Expected AFTER the fix: no output.

# Does the gate actually bite? This is the ONLY proof that matters.
dotnet build Resilion.sln -c Release -p:ContinuousIntegrationBuild=true --no-incremental
# Expected: exit 0. Then add a stray `public int Sentinel;` to any public class and re-run:
# must exit non-zero with `error RS0016`. Then delete a line from a PublicAPI file and re-run:
# must exit non-zero with `error RS0017`. Revert both.
```

## Packaging

```bash
# Is SourceLink / are symbols configured?  (future-plans #52)
grep -rn 'PublishRepositoryUrl\|EmbedUntrackedSources\|IncludeSymbols\|SymbolPackageFormat\|ContinuousIntegrationBuild\|SourceLink\|DebugType' \
  src/ .github/ --include='*.props' --include='*.csproj' --include='*.yml'
# Expected today: 0 hits.

# Do they actually end up in the package? Configuration alone proves nothing.
dotnet pack src/Resilion/Resilion.csproj -c Release -p:ContinuousIntegrationBuild=true -o "$TMPDIR/rpack"
ls "$TMPDIR/rpack"                                             # expect a .snupkg beside the .nupkg
unzip -p "$TMPDIR/rpack"/Resilion.*.nupkg Resilion.nuspec | grep -i repository
# expect: <repository type="git" url="https://github.com/FreakyAli/Resilion" commit="<sha>" />
strings src/Resilion/bin/Release/net8.0/Resilion.pdb | grep -m1 raw.githubusercontent
# expect a raw.githubusercontent.com/FreakyAli/Resilion/<sha>/... source map
unzip -l "$TMPDIR/rpack"/Resilion.*.snupkg | grep '\.pdb'      # expect lib/net8.0/Resilion.pdb

# How many packages does the release workflow expect?
grep -n 'dotnet pack\|COUNT\|PROJECTS' .github/workflows/release-nuget.yml
# Today: three explicit pack lines and a hardcoded `-ne 3`. Every new package breaks
# every release until this is data-driven.

# Version stamping
grep -n 'Version\|VersionPrefix' src/Directory.Build.props
grep -nE '^## \[' CHANGELOG.md
# The changelog's newest released section and the props version must not contradict each other.
```

## Correctness

```bash
# Is the timeout cancellation classification still token-read-based?  (future-plans #46)
grep -n 'WasCancelledByTimeout' src/Resilion/Timeout/TimeoutStrategy.cs
sed -n '216,226p' src/Resilion/Timeout/TimeoutStrategy.cs
# Expected: a single-read `=> cause.WasTimeout`, plus the CancellationCause type. (was: a
# two-read `linkedCts.IsCancellationRequested && !userToken.IsCancellationRequested`.)

# Does a typed/result mismatch throw, or silently skip?  (future-plans #60)
grep -n 'WarnTypeMismatch\|InvalidOperationException' src/Resilion/Internal/PipelineComponent.cs
# Expected: no WarnTypeMismatch, two throw sites.
#
# Reachability: a typed component must not be constructible into an untyped pipeline.
grep -n 'public PipelineBuilder AddPipeline\|public PipelineBuilder<TResult> AddPipeline' src/Resilion/PipelineBuilder.cs
# Expected: the untyped builder accepts only `Pipeline`. If a `Pipeline<T>` overload ever appears
# here, the mismatch path becomes reachable and the guard stops being defensive.
#
# Note: the Debug.WriteLine in WarnTypeMismatch is [Conditional("DEBUG")], so it is compiled OUT
# of the Release assembly that ships to NuGet. Do not describe it as a diagnostic consumers see.

# Is WhenAny still called in a loop?  (future-plans #11)
grep -n 'Task.WhenAny\|Task.WhenEach' src/Resilion/Hedging/HedgingStrategy.cs
# Expected today: WhenAny at :69, :70, :143, :333. No WhenEach (net8.0 target).
# :69-70 is the hedging-delay race and is correct as written — see the comment at :74-77.

# How many independent retry loops exist?  (future-plans #27)
grep -c 'for (var attempt = 0' src/Resilion/Retry/RetryStrategy.cs    # expected today: 4
wc -l src/Resilion/Retry/RetryStrategy.cs                             # expected today: 302
```

## Claims made in the README and docs

```bash
# Is the synchronous-execution claim qualified?  (future-plans #61)
grep -n 'Task.Run' src/Resilion/Fallback/FallbackAction.cs src/Resilion/ResilienceEventHandler.cs
grep -n 'WaitHandle' src/Resilion/Retry/RetryStrategy.cs
# Expected today: FallbackAction.cs:64 and ResilienceEventHandler.cs:93 (sync-over-async under a
# SynchronizationContext); RetryStrategy.cs:170 and :295 (sync delay bypassing TimeProvider).
grep -n 'sync-over-async\|No blocking calls\|true sync' README.md docs/comparison-with-polly.md
# Every hit must be accompanied by the four documented exceptions, or the claim is overbroad.

# Do the published benchmark numbers still match the code?
grep -n 'ns /' README.md
dotnet run -c Release --project benchmarks/Resilion.Benchmarks
# Any change that adds or removes an allocation on a benchmarked path invalidates these tables.
```

## Build configuration

```bash
# Target frameworks and lock primitive  (future-plans #54)
grep -n 'TargetFramework\|TargetFrameworks\|LangVersion' src/Directory.Build.props
grep -rn 'System.Threading.Lock\|NET9_0_OR_GREATER' src/ --include='*.cs' | grep -v '/obj/'
# Expected today: net8.0 (singular), LangVersion 12.0, 0 hits for Lock/NET9.

# Coverage tooling  (future-plans #47)
grep -n 'coverlet\|CodeCoverage\|TestingPlatformDotnetTestSupport' tests/Directory.Build.props
# coverlet.collector is a VSTest collector and a verified silent no-op under MTP.

# Test-dependency drift
grep -rn 'TimeProvider.Testing' tests/
# Expected today: 8.0.0 in Resilion.Tests, 9.0.0 in Resilion.Extensions.Tests — inconsistent.

# Vulnerability disclosure policy  (future-plans #53)
ls SECURITY.md .github/SECURITY.md 2>&1        # expected today: neither exists
```

## Self-consistency of `future-plans.md`

These are the checks behind CONTRIBUTING.md § Future-plans hygiene. All five must print nothing
except the counts.

```bash
# No duplicate section headings.
grep -n '^## ' docs/future-plans.md | sed 's/.*## //' | sort | uniq -d

# Matrix rows and entries are the same set. Strip the "## Retired items" appendix first —
# its rows deliberately have no entries, and including them is a false positive.
diff <(sed '/^## Retired items/,$d' docs/future-plans.md | grep -oE '^\| [0-9]+ \|' | tr -cd '0-9\n' | sort -n) \
     <(grep -oE '^### [0-9]+\.' docs/future-plans.md | tr -cd '0-9\n' | sort -n)

# No completion markers. Anchor to the front-matter field, not the bare word "Complete" —
# the file's own "How to read this file" section states the rule and would match itself.
grep -nE '^\*\*Status:\*\*.*Complete|✅' docs/future-plans.md

# No unverified assertions.
grep -niE 'grep (confirms|across)|confirmed via grep' docs/future-plans.md

# Every entry carries front-matter. All three counts must be equal.
echo "entries: $(grep -c '^### [0-9]' docs/future-plans.md)  \
status: $(grep -c '^\*\*Status:\*\*' docs/future-plans.md)  \
verified: $(grep -c '^\*\*Verified:\*\*' docs/future-plans.md)"
# Expected 2026-09-30: 18 / 18 / 18 (ten P0 items shipped and were retired).

# No dangling item references anywhere in the docs. A reference resolves if it matches either an
# active entry or a row in the Retired items table — pointing at a retired number is legitimate.
RETIRED=$(sed -n '/^## Retired items/,$p' docs/future-plans.md | grep -oE '^\| [0-9]+ \|' | tr -cd '0-9\n')
for n in $(grep -rhoE '#[0-9]{1,2}' docs/*.md README.md benchmarks/results/README.md \
           | tr -d '#' | sort -un); do
  grep -q "^### $n\." docs/future-plans.md && continue
  echo "$RETIRED" | grep -qx "$n" && continue
  echo "dangling reference: #$n"
done
# Known acceptable output: "#1", from the prose "the #1 onboarding path" in future-plans #39.
# Any other number is a real dangling reference and must be fixed or removed.

# Every anchor into future-plans.md resolves. Hand-check each fragment against the heading list.
grep -rn 'future-plans.md#' docs/ README.md
grep -n '^### ' docs/future-plans.md
```

---

## Pre-PR checklist

Before opening a PR that touches `docs/future-plans.md`:

1. Run the five self-consistency checks above. All must pass.
2. For every claim you wrote or changed, run its command from this file and paste the output into
   the PR description.
3. Update the `**Verified:**` line of every entry you touched with today's date and the command you
   ran.
4. If you implemented an item, **delete it** — entry and matrix row — add a `## Retired items` row,
   and add the `CHANGELOG.md` entry. Do not mark it complete in place.
5. If you only partially closed an item, file the remainder under a **new** number and retire the
   old one. Do not leave a half-true entry.
6. Update the "Last full sweep" line at the top of this file if you re-ran everything.
