# sign-app Windows UI E2E tests

This project launches the real `VMSign.exe` desktop application and drives its
Windows UI Automation tree with NUnit and FlaUI UIA3. The smoke suite protects
the sign-web-compatible shell behavior that is visible on first launch:

- PDF workspace, preview empty state, merchant selector, and log pane
- logged-out session state and initialized dashboard log
- disabled PDF signing action before the document/placement prerequisites exist
- native PDF picker, two-page preview, and canvas placement gating of the sign action
- session flyout credentials, certificate selector, and login actions
- System menu navigation to the SDK settings workspace

## Prerequisites

- Windows with .NET 8 SDK installed
- An unlocked, interactive desktop session (the tests are headful; they do not
  work in Windows Session 0, a locked screen, or a non-interactive service)
- The test process and VMSign must run at the same elevation level

## Run

From the repository root:

```powershell
dotnet test .\samples\sign-app-e2e\sign-app-e2e.csproj
```

`VMSign.csproj` is a project reference with `ReferenceOutputAssembly="false"`,
so `dotnet test` builds the desktop application without linking it into the test
assembly. Every launch includes `--disable-updates` to keep the UI run local and
deterministic. It also sets `VMSIGN_CONFIG_DIR` to a fresh temporary directory,
then removes that directory on teardown, so tests do not modify your normal
VMSign profile under `~/.config/vimes-sign`.

For a Release run:

```powershell
dotnet test .\samples\sign-app-e2e\sign-app-e2e.csproj -c Release
```

The executable is normally resolved from the referenced project's matching
build output. To test a packaged or manually built binary instead, override it:

```powershell
$env:VMSIGN_E2E_EXE = 'C:\path\to\VMSign.exe'
dotnet test .\samples\sign-app-e2e\sign-app-e2e.csproj --no-build
```

`VMSIGN_E2E_EXE` may also point to a directory containing `VMSign.exe`.

## Opt-in live MySign signing

The `Live` category contains a real, credentialed MySign signing test. It is
skipped unless both credential variables are present. Configure the MySign
base URL, profile, client ID, and client secret in the application's local
`appsettings.json`, then run:

```powershell
$env:VMSIGN_MYSIGN_USERNAME = '<account>'
$env:VMSIGN_MYSIGN_PASSWORD = '<password-or-pin>'
dotnet test .\samples\sign-app-e2e\sign-app-e2e.csproj `
  --filter 'TestCategory=Live'
Remove-Item Env:VMSIGN_MYSIGN_USERNAME
Remove-Item Env:VMSIGN_MYSIGN_PASSWORD
```

This test performs a real provider login and signature operation. It creates
an isolated `E2E TEST ONLY` PDF and output directory under the operating
system's temporary directory, drives the VIETTEL login/certificate/PDF
placement UI, waits up to 180 seconds for signing, and validates the embedded
PDF signature with iText. Temporary PDFs are removed after the run.

Credential values are never written by the test to source, NUnit output, or
the child process environment. Before a screenshot is written, the session
pill, credential inputs, certificate selector, and log pane are redacted.
Screenshots remain local, are attached to the test result, and are also saved
under a unique run directory:

```text
samples/sign-app-e2e/bin/<Configuration>/net8.0-windows/screenshots/mysign-live/<run-id>
```

Both `01-before-sign.png` and either `02-after-success.png` or
`02-after-failure.png` are captured.
