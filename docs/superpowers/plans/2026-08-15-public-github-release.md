# LocalWhale Public GitHub Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish a reliable, bilingual, MIT-licensed LocalWhale source repository and a verified `v0.1.0` offline installer on public GitHub.

**Architecture:** Capture bounded Harness startup output in a typed exception, classify only the known `.dsh/profiles` module-link race, and retry once through a Core coordinator before the WinUI recovery page is shown. Keep repository presentation, CI, installer validation, and GitHub publication as separate deliverables so source scope and release assets can be independently audited.

**Tech Stack:** .NET 10, C# 14, WinUI 3 / Windows App SDK 2.3.1, WebView2, Node.js 24, native Node test runner, PowerShell, Inno Setup 6, GitHub Actions, GitHub CLI.

## Global Constraints

- Support Windows 11 x64 only for `v0.1.0`.
- Keep the official DeepSeek Harness Web UI unchanged; use only the existing Cordis bridge overlay.
- Never read, copy, delete, or publish API keys or `~/.dsh` content; tests use isolated temporary profile directories.
- Retry the known profile-resolution startup failure at most once and never retry unrelated failures.
- Keep Node.js, pnpm, Harness, WebView2 redistributables, installers, logs, and `artifacts/` outside Git history.
- License LocalWhale source under MIT while retaining the separate DeepSeek Harness MIT license and third-party notices.
- Use Chinese-primary bilingual documentation and publish the installer only as a GitHub Release asset.
- Report the measured shell/private and total-working-set budget misses honestly.
- Do not publish until all tests, WinUI build, runtime smoke, install/start/uninstall acceptance, and secret scans pass.

---

### Task 1: Preserve startup diagnostics in a typed failure

**Files:**
- Create: `src/LocalWhale.Core/Runtime/HarnessStartupException.cs`
- Modify: `src/LocalWhale.Core/Runtime/HarnessRuntimeManager.cs`
- Create: `tests/LocalWhale.Core.Tests/Fixtures/fake-harness-startup-failure.mjs`
- Modify: `tests/LocalWhale.Core.Tests/HarnessRuntimeManagerTests.cs`

**Interfaces:**
- Produces: `HarnessStartupException(int exitCode, IReadOnlyList<string> output)` with `ExitCode` and `Output`.
- Preserves: `IHarnessRuntimeManager.StartAsync(string, CancellationToken)`.

- [ ] **Step 1: Add a fixture that exits before readiness**

```js
console.error(`Error [ERR_MODULE_NOT_FOUND]: Cannot find package '@deepseek-ai/dsh-client-ui-plan' imported from ${process.cwd()}`);
process.exit(1);
```

- [ ] **Step 2: Write the failing manager test**

```csharp
[Fact]
public async Task StartAsync_preserves_bounded_output_when_process_exits_before_ready()
{
    var script = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fake-harness-startup-failure.mjs");
    await using var manager = new HarnessRuntimeManager(
        (version, token) => new HarnessLaunchSpec(ResolveNodeExecutable(), script, script, AppContext.BaseDirectory, token, HarnessVersion: version),
        TimeSpan.FromSeconds(10));

    var error = await Assert.ThrowsAsync<HarnessStartupException>(() =>
        manager.StartAsync("0.1.0-rc.6", TestContext.Current.CancellationToken));

    Assert.Equal(1, error.ExitCode);
    Assert.Contains(error.Output, line => line.Contains("ERR_MODULE_NOT_FOUND", StringComparison.Ordinal));
    Assert.InRange(error.Output.Count, 1, HarnessStartupException.MaximumCapturedLines);
}
```

- [ ] **Step 3: Run the test and verify red**

```powershell
dotnet test .\tests\LocalWhale.Core.Tests\LocalWhale.Core.Tests.csproj -c Release --filter "StartAsync_preserves_bounded_output_when_process_exits_before_ready"
```

Expected: FAIL because early exits currently throw plain `InvalidOperationException`.

- [ ] **Step 4: Implement typed exception and bounded capture**

```csharp
public sealed class HarnessStartupException : InvalidOperationException
{
    public const int MaximumCapturedLines = 256;
    public HarnessStartupException(int exitCode, IReadOnlyList<string> output)
        : base($"Harness exited with code {exitCode} before becoming ready.")
    {
        ExitCode = exitCode;
        Output = output;
    }
    public int ExitCode { get; }
    public IReadOnlyList<string> Output { get; }
}
```

Use a `ConcurrentQueue<string>` per attempt. Both pumps enqueue already-redacted lines and discard the oldest beyond 256. When the process exits, await both pumps and throw an immutable snapshot.

- [ ] **Step 5: Verify focused and full tests**

```powershell
dotnet test .\tests\LocalWhale.Core.Tests\LocalWhale.Core.Tests.csproj -c Release --filter "StartAsync_preserves_bounded_output_when_process_exits_before_ready"
dotnet test .\LocalWhale.slnx -c Release
```

- [ ] **Step 6: Commit Task 1**

```powershell
git add src/LocalWhale.Core/Runtime/HarnessStartupException.cs src/LocalWhale.Core/Runtime/HarnessRuntimeManager.cs tests/LocalWhale.Core.Tests/Fixtures/fake-harness-startup-failure.mjs tests/LocalWhale.Core.Tests/HarnessRuntimeManagerTests.cs
git commit -m "fix: preserve Harness startup diagnostics"
```

---

### Task 2: Retry the profile-link race exactly once

**Files:**
- Create: `src/LocalWhale.Core/Runtime/HarnessStartupRetryPolicy.cs`
- Create: `src/LocalWhale.Core/Runtime/HarnessStartupCoordinator.cs`
- Create: `tests/LocalWhale.Core.Tests/Fixtures/fake-harness-profile-race.mjs`
- Create: `tests/LocalWhale.Core.Tests/HarnessStartupRetryPolicyTests.cs`
- Create: `tests/LocalWhale.Core.Tests/HarnessStartupCoordinatorTests.cs`
- Create: `tests/LocalWhale.Core.Tests/TemporaryTestDirectory.cs`

**Interfaces:**
- Consumes: `HarnessStartupException` from Task 1.
- Produces: `HarnessStartupRetryPolicy.ShouldRetryProfileResolution(HarnessStartupException, string dshHomeDirectory) : bool`.
- Produces: `HarnessStartupCoordinator.StartAsync(IHarnessRuntimeManager, string, string, Action<string>?, CancellationToken) : Task<HarnessRuntimeInfo>`.
- Produces test utility: `TemporaryTestDirectory` with a `Path` property and recursive cleanup in `Dispose()`.

- [ ] **Step 1: Write failing policy tests**

```csharp
[Theory]
[InlineData(true, "ERR_MODULE_NOT_FOUND", "profiles\\web")]
[InlineData(false, "ERR_MODULE_NOT_FOUND", "unrelated\\plugins")]
[InlineData(false, "EADDRINUSE", "profiles\\web")]
public void ShouldRetryProfileResolution_matches_only_the_known_profile_failure(
    bool expected, string error, string importRoot)
{
    var dshHome = Path.Combine(Path.GetTempPath(), "LocalWhale.Policy.User", ".dsh");
    var line = $"{error}: imported from {Path.Combine(dshHome, importRoot)}";
    Assert.Equal(expected, HarnessStartupRetryPolicy.ShouldRetryProfileResolution(
        new HarnessStartupException(1, new[] { line }), dshHome));
}
```

- [ ] **Step 2: Run policy tests and verify red**

```powershell
dotnet test .\tests\LocalWhale.Core.Tests\LocalWhale.Core.Tests.csproj -c Release --filter "HarnessStartupRetryPolicyTests"
```

- [ ] **Step 3: Implement the narrow classifier**

Normalize `Path.Combine(dshHomeDirectory, "profiles")`. Return true only when captured output contains both `ERR_MODULE_NOT_FOUND` and an `imported from` path under that root, using ordinal-ignore-case comparison. Reject other roots, empty output, timeout, port, and bridge failures.

- [ ] **Step 4: Add a real two-process fixture**

The fixture runs from an isolated `.dsh/profiles/web`. First invocation creates `.localwhale-profile-linked`, prints the exact resolution error, and exits 1. Second invocation starts the authenticated health/shutdown server used by `fake-harness.mjs`.

Add `TemporaryTestDirectory` as a focused test utility:

```csharp
internal sealed class TemporaryTestDirectory : IDisposable
{
    public TemporaryTestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LocalWhale.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
```

- [ ] **Step 5: Write failing coordinator tests**

```csharp
[Fact]
public async Task StartAsync_retries_profile_resolution_failure_once_in_a_new_process()
{
    using var workspace = new TemporaryTestDirectory();
    var dshHome = Directory.CreateDirectory(Path.Combine(workspace.Path, ".dsh")).FullName;
    var profile = Directory.CreateDirectory(Path.Combine(dshHome, "profiles", "web")).FullName;
    await using var manager = CreateManager("fake-harness-profile-race.mjs", profile);
    var log = new List<string>();

    var runtime = await HarnessStartupCoordinator.StartAsync(
        manager, "0.1.0-rc.6", dshHome, log.Add, TestContext.Current.CancellationToken);

    Assert.True(manager.IsRunning);
    Assert.Contains(log, line => line.Contains("retrying once", StringComparison.OrdinalIgnoreCase));
    Assert.True(runtime.ProcessId > 0);
}

private static HarnessRuntimeManager CreateManager(string fixtureName, string workingDirectory)
{
    var script = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
    return new HarnessRuntimeManager(
        (version, token) => new HarnessLaunchSpec(ResolveNodeExecutable(), script, script, workingDirectory, token, HarnessVersion: version),
        TimeSpan.FromSeconds(10));
}
```

Add a second test whose unrelated failure is thrown after exactly one launch, plus a double-failure fixture assertion proving no third attempt.

- [ ] **Step 6: Run coordinator tests and verify red**

```powershell
dotnet test .\tests\LocalWhale.Core.Tests\LocalWhale.Core.Tests.csproj -c Release --filter "HarnessStartupCoordinatorTests"
```

- [ ] **Step 7: Implement one bounded retry**

```csharp
try
{
    return await manager.StartAsync(version, cancellationToken).ConfigureAwait(false);
}
catch (HarnessStartupException exception)
    when (HarnessStartupRetryPolicy.ShouldRetryProfileResolution(exception, dshHomeDirectory))
{
    log?.Invoke("Harness profile dependencies were linked during startup; retrying once in a fresh process.");
    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
    return await manager.StartAsync(version, cancellationToken).ConfigureAwait(false);
}
```

Do not catch the second failure.

- [ ] **Step 8: Verify and commit Task 2**

```powershell
dotnet test .\LocalWhale.slnx -c Release
git add src/LocalWhale.Core/Runtime/HarnessStartupRetryPolicy.cs src/LocalWhale.Core/Runtime/HarnessStartupCoordinator.cs tests/LocalWhale.Core.Tests/Fixtures/fake-harness-profile-race.mjs tests/LocalWhale.Core.Tests/HarnessStartupRetryPolicyTests.cs tests/LocalWhale.Core.Tests/HarnessStartupCoordinatorTests.cs tests/LocalWhale.Core.Tests/TemporaryTestDirectory.cs
git commit -m "fix: retry first-run Harness profile linking"
```

---

### Task 3: Integrate retry into WinUI startup

**Files:**
- Modify: `src/LocalWhale.App/MainWindow.xaml.cs:122-161`
- Test: `tests/LocalWhale.Core.Tests/HarnessStartupCoordinatorTests.cs`

**Interfaces:**
- Consumes: Task 2 coordinator.
- Preserves: recovery page, rollback policy, manual retry, and 60-second healthy-start timer.

- [ ] **Step 1: Strengthen coordinator assertions**

Require one retry log, two launches for the profile race, and no third launch when the retry also fails.

- [ ] **Step 2: Replace direct startup in `MainWindow`**

```csharp
var dshHome = Environment.GetEnvironmentVariable("DSH_HOME");
if (string.IsNullOrWhiteSpace(dshHome))
{
    dshHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
}
var runtime = await HarnessStartupCoordinator.StartAsync(
    _runtimeManager, _runtimeState.ActiveVersion, dshHome, _logger.Write, CancellationToken.None);
```

Leave `MarkRuntimeStableAsync` intact: `RegisterHealthyStart` clears the active version count after the existing 60-second stability window.

- [ ] **Step 3: Verify tests and WinUI build**

```powershell
dotnet test .\LocalWhale.slnx -c Release
npm.cmd test --prefix .\bridge
dotnet build .\src\LocalWhale.App\LocalWhale.App.csproj -c Release --no-restore
```

Expected: all tests pass; build has zero errors and warnings.

- [ ] **Step 4: Verify installed first launch**

Install to `D:\Project\LocalWhale\.tools\公开发布 验证\LocalWhale`. Launch it from a PowerShell process with `DSH_HOME=D:\Project\LocalWhale\.tools\公开发布 验证\clean-dsh`, so official Harness creates a clean isolated profile without touching the user's `.dsh`. Require automatic recovery if the profile-link race occurs, a ready bridge, and no recovery page. The fake-fixture tests remain the deterministic proof that unrelated failures do not loop.

- [ ] **Step 5: Commit Task 3**

```powershell
git add src/LocalWhale.App/MainWindow.xaml.cs tests/LocalWhale.Core.Tests/HarnessStartupCoordinatorTests.cs
git commit -m "fix: recover from first-launch profile race"
```

---

### Task 4: Add licensing and bilingual documentation

**Files:**
- Create: `LICENSE`
- Rewrite: `README.md`
- Create: `docs/images/localwhale-main.png`
- Modify: `licenses/THIRD-PARTY-NOTICES.md`

**Interfaces:**
- Produces: public project page and attribution.

- [ ] **Step 1: Add canonical MIT license**

Use `Copyright (c) 2026 LocalWhale contributors`.

- [ ] **Step 2: Capture a privacy-safe real screenshot**

The visible page must contain no conversation titles, usernames, credentials, local paths, or user data. If unsafe, start an isolated clean profile and recapture. Do not fabricate or blur app content.

- [ ] **Step 3: Rewrite README with exact structure**

```markdown
# LocalWhale
## 功能亮点
## 下载与安装
## 使用方式
## 架构
## 数据与安全边界
## Harness 更新与回滚
## 开发与构建
## 测试
## 故障排查
## 已知限制
## English Summary
## License and attribution
```

Document Windows 11 x64, custom paths, offline contents, `%LOCALAPPDATA%\LocalWhale`, preserved `~/.dsh`, random ports, per-launch tokens, Job Object cleanup, one-shot retry, current resource measurements, and build commands. State that LocalWhale is an independent community project not affiliated with DeepSeek.

- [ ] **Step 4: Update third-party notices**

Cover DeepSeek Harness, Node.js, pnpm, Windows App SDK, WebView2, and Inno Setup with upstream links.

- [ ] **Step 5: Scan docs and commit**

```powershell
rg -n "TBD|TODO|FIXME|C:\\Users\\zhong|D:\\AI|127\.0\.0\.1:\d{4,5}" README.md LICENSE licenses docs/images
git add LICENSE README.md docs/images/localwhale-main.png licenses/THIRD-PARTY-NOTICES.md
git commit -m "docs: prepare bilingual open source release"
```

Expected: no placeholders or user-specific data.

---

### Task 5: Add public CI and contribution templates

**Files:**
- Create: `.github/workflows/ci.yml`
- Create: `.github/ISSUE_TEMPLATE/bug_report.yml`
- Create: `.github/pull_request_template.md`

**Interfaces:**
- Produces: public checks for Core, bridge, and WinUI.

- [ ] **Step 1: Verify official action versions**

Check the official repositories for `actions/checkout`, `actions/setup-dotnet`, and `actions/setup-node`; use currently supported stable majors.

- [ ] **Step 2: Create Windows workflow**

```yaml
name: CI
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
permissions:
  contents: read
jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - uses: actions/setup-node@v4
        with:
          node-version: 24
      - run: dotnet test .\LocalWhale.slnx -c Release
      - run: npm test
        working-directory: bridge
      - run: dotnet build .\src\LocalWhale.App\LocalWhale.App.csproj -c Release --no-restore
```

Update majors only if official sources require it.

- [ ] **Step 3: Add bug and PR templates**

Bug reports request shell/Harness/Windows versions, reproduction, and redacted logs, with a warning against API keys, bridge tokens, credentials, or `.credentials.yaml`. PRs request scope, validation, screenshots, and data-boundary impact.

- [ ] **Step 4: Run workflow commands locally and commit**

```powershell
dotnet test .\LocalWhale.slnx -c Release
npm.cmd test --prefix .\bridge
dotnet build .\src\LocalWhale.App\LocalWhale.App.csproj -c Release --no-restore
git add .github
git commit -m "ci: add public repository checks"
```

---

### Task 6: Build and validate `v0.1.0` assets

**Files:**
- Modify: `tools/Build-Release.ps1`
- Generate, do not commit: `artifacts/installer/LocalWhale-Setup-x64.exe`
- Generate, do not commit: `artifacts/installer/LocalWhale-Setup-x64.exe.sha256`

**Interfaces:**
- Produces: two release assets for Task 7.

- [ ] **Step 1: Add release-boundary assertions**

After `dotnet publish`, make `Build-Release.ps1` require `LocalWhale.exe`, `LocalWhale.pri`, `App.xbf`, and `MainWindow.xbf`. After copying runtime/licenses, reject reparse points and reject a publish tree above 500 MiB before invoking Inno Setup.

- [ ] **Step 2: Run source and runtime validation**

```powershell
dotnet test .\LocalWhale.slnx -c Release
npm.cmd test --prefix .\bridge
dotnet build .\src\LocalWhale.App\LocalWhale.App.csproj -c Release --no-restore
dotnet run --project .\tools\LocalWhale.RuntimeTool\LocalWhale.RuntimeTool.csproj -c Release --no-restore -- smoke-runtime .\artifacts\runtime\harness\0.1.0-rc.6 .\artifacts\runtime\node\node.exe 0.1.0-rc.6
```

- [ ] **Step 3: Build offline installer**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-Release.ps1 -SkipRuntime
```

Require `LocalWhale.pri`, `App.xbf`, `MainWindow.xbf`, runtime payloads, licenses, and WebView2 offline installer.

- [ ] **Step 4: Repeat custom-path acceptance**

Require ready UI, HTTP 200, bundled Node, single instance, full exit within 7 seconds, clean uninstall, and unchanged sampled `.dsh/.credentials.yaml` SHA-256.

- [ ] **Step 5: Generate checksum**

```powershell
$setup = Resolve-Path .\artifacts\installer\LocalWhale-Setup-x64.exe
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  LocalWhale-Setup-x64.exe" | Set-Content .\artifacts\installer\LocalWhale-Setup-x64.exe.sha256 -Encoding ascii
```

- [ ] **Step 6: Audit Git boundary**

```powershell
git check-ignore -v artifacts/installer/LocalWhale-Setup-x64.exe
git ls-files artifacts .tools
rg -n --hidden -g '!artifacts/**' -g '!.git/**' -g '!.tools/**' "sk-[A-Za-z0-9]|api[_-]?key\s*[:=]|C:\\Users\\zhong|D:\\AI" .
```

Expected: artifacts ignored, none tracked, no credentials or user-specific paths.

---

### Task 7: Create public repository and GitHub Release

**Files:**
- Stage: approved source, tests, docs, licenses, scripts, bridge, and `.github`.
- Exclude: `.tools/`, `artifacts/`, `bin/`, `obj/`, logs, `.dsh`, WebView2 data.

**Interfaces:**
- Produces: public `LocalWhale` repository on `main` and release `v0.1.0`.

- [ ] **Step 1: Install official GitHub CLI**

```powershell
winget install --id GitHub.cli --exact --source winget --accept-package-agreements --accept-source-agreements
gh --version
```

- [ ] **Step 2: Authenticate through GitHub web flow**

```powershell
gh auth login --hostname github.com --git-protocol https --web
gh auth status
```

Stop if authentication is not confirmed. Never request or store a PAT in the project or chat.

- [ ] **Step 3: Review and commit complete public scope**

```powershell
git add -- Directory.Build.props LocalWhale.slnx README.md LICENSE bridge installer licenses src tests tools docs .github
git diff --cached --check
git diff --cached --name-only
git commit -m "feat: publish LocalWhale desktop shell"
```

- [ ] **Step 4: Fast-forward `main`**

```powershell
git checkout main
git merge --ff-only feature/localwhale-desktop
```

- [ ] **Step 5: Create and push public repository**

Resolve and check the authenticated namespace without a literal placeholder:

```powershell
$githubOwner = gh api user --jq .login
gh repo view "$githubOwner/LocalWhale" --json nameWithOwner
```

Continue only when the second command reports that the repository does not exist.

```powershell
gh repo create LocalWhale --public --source . --remote origin --push --description "A Fluent Windows desktop shell for the official DeepSeek Harness Web UI."
gh repo view --json nameWithOwner,visibility,defaultBranchRef,url
```

Require `PUBLIC` visibility and `main` default branch.

- [ ] **Step 6: Set topics**

```powershell
gh repo edit --add-topic deepseek --add-topic deepseek-harness --add-topic winui3 --add-topic webview2 --add-topic windows-desktop --add-topic fluent-design --add-topic dotnet
```

- [ ] **Step 7: Create release**

Write release notes to `artifacts\installer\release-notes-v0.1.0.md` covering features, Windows 11 x64, offline contents, the one-shot retry, data boundaries, known resource limits, and checksum.

```powershell
gh release create v0.1.0 .\artifacts\installer\LocalWhale-Setup-x64.exe .\artifacts\installer\LocalWhale-Setup-x64.exe.sha256 --title "LocalWhale v0.1.0" --notes-file .\artifacts\installer\release-notes-v0.1.0.md
gh release view v0.1.0 --json url,isDraft,isPrerelease,assets
```

Require a published non-prerelease with exactly two assets.

- [ ] **Step 8: Verify public rendering and CI**

Confirm README screenshot, MIT license, description, topics, default branch, and release render publicly. Wait for `gh run list --workflow CI --limit 1`; debug any failed workflow before completion.

- [ ] **Step 9: Report evidence**

Return repository URL, release URL, `main` commit SHA, installer size/SHA-256, test counts, CI conclusion, and known resource-budget misses without local usernames or auth details.
