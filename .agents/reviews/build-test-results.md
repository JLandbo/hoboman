# Build and test results

Run on 2026-10-05, ~15:13–15:17 local time. .NET SDK 10.0.401. `global.json` sets only `"test": { "runner": "Microsoft.Testing.Platform" }` (no SDK version pin). This was a read-only run: nothing in source, tests, or git state was changed.

## Build

Command:

```
dotnet build c:\Users\JSL\Documents\Hoboman\Hoboman.slnx
```

Result: Build succeeded. 0 warnings, 0 errors (11.45 s).

Projects built: Hoboman.Core, Hoboman.Cli (hoboman-cli.dll), Hoboman, Hoboman.Tests (all `bin\Debug\net10.0-windows`).

## Tests

Filter syntax, taken from `dotnet test --project <csproj> --help`: xunit.v3 on MTP supports `--filter-not-class <fully qualified type>`, which is a simple filter and cannot be mixed with query or VSTest filters. `--timeout` sets a global run timeout and needs a unit suffix. `--long-running <seconds>` turns on hung-test detection. The class to exclude is `Hoboman.Tests.Auth.AuthRefreshServiceTests`, in namespace `Hoboman.Tests.Auth`, file `tests\Hoboman.Tests\Auth\AuthRefreshServiceTests.cs`.

Command (run once):

```
dotnet test --project "c:\Users\JSL\Documents\Hoboman\tests\Hoboman.Tests\Hoboman.Tests.csproj" --no-build --filter-not-class "Hoboman.Tests.Auth.AuthRefreshServiceTests" --timeout 10m --long-running 60
```

Result: Failed (exit code 2).

| total | succeeded | failed | skipped | duration |
|---|---|---|---|---|
| 1449 | 1447 | 2 | 0 | 1m 28s |

The timeout was not hit and no hung tests were reported.

### Failure 1: `Hoboman.Tests.Views.ScratchBrowserReal.Render` (9s 098ms)

```
System.IO.IOException : The process cannot access the file 'C:\Users\JSL\AppData\Local\Temp\claude\C--Users-JSL-Documents-Hoboman\<guid>\scratchpad\shots\real-bad.png' because it is being used by another process.
  at System.IO.File.Create(String path)
  at Hoboman.Tests.Views.ScratchBrowserReal.Shot(...) in tests\Hoboman.Tests\Views\ScratchBrowserReal.cs:95
  at Hoboman.Tests.Views.ScratchBrowserReal.<Render>b__1 in tests\Hoboman.Tests\Views\ScratchBrowserReal.cs:76
  at Hoboman.Tests.Views.Ui.RunAsync(...) in tests\Hoboman.Tests\Views\Ui.cs:48/52
  at Hoboman.Tests.Views.ScratchBrowserReal.Render() in tests\Hoboman.Tests\Views\ScratchBrowserReal.cs:42
```

This test is not in the repository:

- `tests\Hoboman.Tests\Views\ScratchBrowserReal.cs` does not exist on disk right after the run.
- Git does not list it as tracked, staged, or untracked, and it has no history on any branch.
- It writes screenshots into an external tool's temp scratchpad folder.

The class was compiled into the tested `Hoboman.Tests.dll`, so the source must have been present when the binary was built and removed afterwards. The timestamps point the same way. `bin\...\Hoboman.Tests.dll` is from 15:13:22, which is before this run's restore at 15:13:41. `obj\...\Hoboman.Tests.dll` is from 15:13:56. Six `dotnet` processes were running at the time. Most likely another process was building and running tests in this workspace at the same time. Treat this failure as an artifact of that, not of repository code.

### Failure 2: `Hoboman.Tests.Views.NameDialogTests.NameDialog_WhenLoaded_ThenFocusesAndSelectsTheName(name: "")` (741ms)

```
Assert.Same() Failure: Values are not the same instance
Expected: System.Windows.Controls.TextBox
Actual:   null
  at Hoboman.Tests.Views.NameDialogTests.<...>b__0 in tests\Hoboman.Tests\Views\NameDialogTests.cs:28
  at Hoboman.Tests.Views.Ui.RunAsync(...) in tests\Hoboman.Tests\Views\Ui.cs:48/52
  at Hoboman.Tests.Views.NameDialogTests.NameDialog_WhenLoaded_ThenFocusesAndSelectsTheName(String name) in tests\Hoboman.Tests\Views\NameDialogTests.cs:19
```

Line 28 is `Assert.Same(field, Keyboard.FocusedElement);`, so keyboard focus was null after the dialog was shown. Only the `""` case failed. The other three InlineData cases (`"New request (3)"`, `"Get"`, `"Admin"`) passed. `NameDialogTests.cs` and the NameDialog view have no unstaged or staged changes. Real keyboard focus may have been taken by the concurrent UI test activity described above, so this could be environmental. It was not re-run (single-run instruction), so whether it is flaky or a real regression is unverified.

## Not covered

- `Hoboman.Tests.Auth.AuthRefreshServiceTests` was excluded on purpose (known intermittent hang).
- `--no-build` was used for the test run, so it ran the binaries from the build step. Because of the concurrent activity above, those binaries may not exactly match the current source tree.
