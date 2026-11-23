---
satisfies: [R2, R3, R4, R5, R6]
---
# fn-2-implicit-oauth-login-i-hoboman.2 Login-vindue med WebView2

## Description
Byg WPF-implementeringen af `ILoginWindow`: et modalt vindue med sin egen WebView2-profil, som stopper ved callback-adressen.

**Size:** M
**Files:** `src/Hoboman/Views/LoginWindow.xaml` og `.xaml.cs` (ny), `src/Hoboman/Desktop/LoginWindowOpener.cs`, `src/Hoboman/ViewModels/AuthViewModel.cs`, `src/Hoboman/App.xaml.cs`, `src/Hoboman.Core/Languages/Dansk.json`, `src/Hoboman.Core/Languages/English.json`, `tests/Hoboman.Tests/Views/LoginWindowTests.cs` (ny)
**Touches:** [src/Hoboman/Views/LoginWindow*, src/Hoboman/Desktop/**, src/Hoboman/ViewModels/AuthViewModel.cs, src/Hoboman/App.xaml.cs, src/Hoboman.Core/Languages/**, tests/Hoboman.Tests/Views/LoginWindowTests.cs]

### Approach
- Følg `DialogWindow` (`src/Hoboman/Views/DialogWindow.cs:12-21`) for stil og Esc. Sæt `Owner` til det aktive vindue og ellers hovedvinduet, så det også ligger øverst over et mappe- eller credentials-vindue. Vis med `ShowDialog`.
- `OAuthClient` kører med `ConfigureAwait(false)`, så opener-klassen skal skifte til `Application.Current.Dispatcher`. Afslut med `TaskCompletionSource` og `TrySetResult`, så callback, luk og annullering kun kan afslutte én gang.
- Sæt miljøet op som `BrowserView.cs:21`, men med sin egen datamappe, fx `LocalAppData\Hoboman\WebView2Login`, sat i `App.xaml.cs:39` sammen med `BrowserView.DataFolder`. Ingen InPrivate, for cookies skal overleve (R6).
- `NavigationStarting`: brug `CallbackAddress.Matches(Uri address, Uri callback)` fra opgave 1 (`src/Hoboman.Core/Auth/CallbackAddress.cs`) <!-- Updated by plan-sync: fn-2-implicit-oauth-login-i-hoboman.1 named the Core method CallbackAddress.Matches -->. Ved match sættes `e.Cancel = true`, adressen afleveres, og vinduet lukkes.
- `NewWindowRequested`: sæt `Handled` og naviger i samme visning. `DownloadStarting`: annullér. `LaunchingExternalUriScheme`: annullér. `PermissionRequested`: afvis som `BrowserView.cs:103-139`.
- `LoginWindowOpener` findes allerede som pladsholder (`src/Hoboman/Desktop/LoginWindowOpener.cs`, registreret som `ILoginWindow` i `App.xaml.cs`) og kaster `LoginUnavailable`; udfyld den, og tilføj ikke en ny registrering. `OAuthProblem.LoginUnavailable` og `LoginClosed` findes allerede, og `LoginClosed` håndteres i `AuthRefreshService`, så den skal ikke have tekst i `ReasonOf`. <!-- Updated by plan-sync: fn-2-implicit-oauth-login-i-hoboman.1 added the placeholder and problems -->
- Fejl ved start af WebView2 følger `BrowserView.cs:80-92`, men kastes som `OAuthException` med `OAuthProblem.LoginUnavailable` fra opgave 1. Tilføj tekst og case i `AuthViewModel.ReasonOf` (`AuthViewModel.cs:363-378`).
- Luk vinduet, når `cancellationToken` annulleres, og returnér `null`. Core skelner mellem timeout, annullering og lukning. Det dækker timeout, ændrede felter og lukkede faner.
- Titlen skal bruge `DynamicResource` (`TranslationTests.cs:98`).

### Investigation targets
**Required**:
- `src/Hoboman/Controls/BrowserView.cs:21` og `:80-139`
- `src/Hoboman/Views/DialogWindow.cs:12-21`
- `src/Hoboman/App.xaml.cs:37-39` og `:132`

**Optional**:
- `tests/Hoboman.Tests/Views/Ui.cs`
- `tests/Hoboman.Tests/Views/CredentialsWindowTests.cs`
- `tests/Hoboman.Tests/Languages/TranslationTests.cs:78-118`

### Key context
Der findes ingen WebView2-tests i dag. Skriv `LoginWindowTests` som `[Collection("Ui")]` med `Ui.RunAsync` og en lokal server, der svarer 302 til en callback med fragment, ligesom den test, der blev kørt under planlægningen. Findes WebView2-runtime ikke, springes testen over. Testen skal bruge en midlertidig datamappe, aldrig brugerens.

Log aldrig `e.Uri`, for den indeholder tokenet.

Opgave 2 og 3 ændrer begge `ReasonOf` og sprogfilerne, så de skal køres efter hinanden.
## Acceptance
- [ ] En test med rigtig WebView2 og lokal 302 viser, at vinduet returnerer callback-adressen med fragment og lukker.
- [ ] Tests viser, at en adresse, der kun ligner, ikke stopper login, at lukning giver `null`, og at annullering lukker vinduet.
- [ ] Cookies ligger i en anden mappe end `BrowserView.DataFolder` og overlever, at vinduet lukkes og åbnes igen.
- [ ] Manglende WebView2-runtime giver en tydelig fejl og intet gemt token.
- [ ] `TranslationTests` består, og appen starter med `ValidateOnBuild`.
## Done summary
Added `LoginWindow` (a DialogWindow with a WebView2 in its own environment and data folder) and filled `LoginWindowOpener`: it shows the window modally on the UI thread over the active window (else the main window), stops at the callback via `CallbackAddress.Matches` and returns the address with its fragment; closing or cancelling closes the window and returns null, and a TaskCompletionSource with TrySet* lets only one of them finish it. New windows navigate in place; downloads, external schemes and permission requests are refused. The app sets `LoginWindow.DataFolder` to LocalAppData\Hoboman\WebView2Login next to `BrowserView.DataFolder`. A missing runtime throws `LoginUnavailable` without detail ("OAuth.LoginMissing"), any other start failure with the message ("OAuth.LoginFailed"), both in `AuthViewModel.ReasonOf`; the window title is the new `OAuth.Login` text.

Tests (tests/Hoboman.Tests/Views/LoginWindowTests.cs, real WebView2 + local Kestrel 302s, temporary data folder, skipped without the runtime): callback with fragment returned and window closed; look-alike `/callback/more` goes on with the window open; user close gives null; cancellation closes the window and gives null; a persistent cookie survives closing and reopening and lands in the login's own folder; AuthViewModel shows both LoginUnavailable texts and holds no token.

Decisions: the window sets AllowsTransparency="False" (with the Surface background), as the HwndHost-based WebView2 does not render in a layered window; this keeps the Dialog style but without its transparent shadow margin. The environment is one static Lazy like BrowserView's. The "no token saved" part of the missing-runtime AC is shown at AuthViewModel level with a stubbed LoginUnavailable; the WebView2RuntimeNotFoundException mapping itself cannot be exercised without removing the runtime.
Not verified: the window's look on screen (not run in the app; the tests show it off-screen), Esc while focus is inside the browser, and a real Cognito login. The app was not started (AppTests covers ValidateOnBuild); starting it from source would touch the user's real data folders.
Leftovers: each test run leaves %TEMP%\Hoboman.Tests\WebView2Login-<guid> behind, as the browser process holds it until the test process exits.
Feature routes: none changed (task 3 wires Implicit into the Auth editor).
Not committed: `.flow/` receipt (FORBIDDEN by the run).
Tier: not given by the conductor (model: claude-opus-5-5)

baseline: green (OAuthClientTests 50/50)
stage: impl-review - skipped(config: REVIEW_MODE=none)
## Evidence
- Commits: eb665d2f09bb64a98a1651b43206fb9643c14d7e
- Tests: dotnet test --project tests/Hoboman.Tests/Hoboman.Tests.csproj --filter-class Hoboman.Tests.Views.LoginWindowTests (7/7), dotnet test --project tests/Hoboman.Tests/Hoboman.Tests.csproj --filter-class TranslationTests/AppTests/AuthViewModelTests/OAuthClientTests/AuthRefreshServiceTests/CredentialsWindowTests (97/97)
- PRs: