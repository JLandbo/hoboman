---
satisfies: [R2, R3, R4, R5, R7]
---
# fn-2-implicit-oauth-login-i-hoboman.1 Implicit-flow i Core

## Description
Tilføj grant-typen og selve flowet i Core uden nogen brugerflade: indstillinger, login-adresse, callback-match, aflæsning af svaret og fejl. Det kommer først, fordi det er kernen, og fordi de to andre opgaver bygger på grænsefladen.

**Size:** M
**Files:** `src/Hoboman/Desktop/LoginWindowOpener.cs` (ny), `src/Hoboman.Core/Auth/OAuthGrant.cs`, `src/Hoboman.Core/Auth/OAuthSettings.cs`, `src/Hoboman.Core/Auth/OAuthProblem.cs`, `src/Hoboman.Core/Auth/ILoginWindow.cs` (ny), `src/Hoboman.Core/Auth/OAuthClient.cs`, `src/Hoboman.Cli/NoLoginWindow.cs` (ny), `src/Hoboman.Cli/Program.cs`, `src/Hoboman/App.xaml.cs`, `tests/Hoboman.Tests/Auth/OAuthClientTests.cs`, `tests/Hoboman.Tests/Auth/FakeLoginWindow.cs` (ny), `tests/Hoboman.Tests/Auth/UnaskedTokensTests.cs`, `src/Hoboman/Services/AuthRefreshService.cs`
**Touches:** [src/Hoboman/Desktop/LoginWindowOpener.cs, src/Hoboman.Core/Auth/**, src/Hoboman.Cli/NoLoginWindow.cs, src/Hoboman.Cli/Program.cs, src/Hoboman/App.xaml.cs, src/Hoboman/Services/AuthRefreshService.cs, tests/Hoboman.Tests/Auth/**]

### Approach
- `Implicit` tilføjes sidst i `OAuthGrant`. Enums gemmes som navne (`src/Hoboman.Core/Storage/JsonFile.cs:12-26`), så der kræves ingen ændring af formatet.
- `CallbackUrl` tilføjes i `OAuthSettings` med standardværdien tom tekst, og den udfyldes med `variables.Resolve` i `OAuthClient.cs:25-31`.
- `OAuthClient.GetTokenAsync` tjekker i dag token-URL'en før grant-valget (`OAuthClient.cs:33`). Implicit skal forgrene før det tjek og springe `RequestTokenAsync` over.
- Valider Auth-URL og Callback URL med `EndpointOf` og tilføj `OAuthProblem.MissingCallbackUrl`. Tilføj også `LoginClosed` og `LoginUnavailable`, så opgave 2 og 3 kan bygge på dem. Tjek Client ID som i dag.
- Byg login-adressen som i `AuthorizationCodeFormAsync` (`OAuthClient.cs:60-77`), men med `response_type=token` og uden PKCE. `redirect_uri` skal være den udfyldte tekst, aldrig `Uri.AbsoluteUri`.
- Kør ventetiden med samme 5 minutters timeout og `OAuthProblem.TimedOut` som `OAuthClient.cs:73-85`.
- Rækkefølgen efter `GetRedirectAsync` er afgørende, for vinduet returnerer `null` både når brugeren lukker det, og når annullering lukker det. Tjek først `timeout.IsCancellationRequested` og kast `TimedOut`, kald derefter `cancellationToken.ThrowIfCancellationRequested()`, og behandl først derefter `null` som lukket vindue.
- Et `null` efter de to tjek kaster `OAuthProblem.LoginClosed`. `AuthRefreshService.FetchAsync` fanger netop den og returnerer `false`, så både `AuthViewModel.cs:279` og `RequestTabViewModel.cs:243` opfører sig ens uden fejltekst.
- Læs query og fragment med `HttpUtility.ParseQueryString` som `LoopbackCallback.cs`. Tjek `state` først, derefter `error`, derefter tokenet.
- Genbrug Bearer-tjekket og levetidsgrænsen fra `TokenOf` og `SecondsOf` i stedet for at kopiere dem, fx ved at trække en lille fælles metode ud. `expires_in` kommer her som tekst.
- Læg callback-sammenligningen i en offentlig statisk metode i Core, så login-vinduet kan bruge den, og den kan testes uden vindue. Sammenlign parsede `Uri`-værdier på scheme, host, port og `AbsolutePath`.
- `OAuthClient` får `ILoginWindow` i konstruktøren. Opdatér `Program.cs:25-26`, `OAuthClientTests.cs:27-32` og registreringen i `App.xaml.cs:132`. `OAuthClient` hentes ved opstart, så appen skal altid have en registrering. Opret `LoginWindowOpener` som en konkret klasse, hvis `GetRedirectAsync` kaster, og registrér den. Opgave 2 udfylder klassen. Brug ikke en fabrik, der kaster, for den får `AppTests` og opstart til at fejle.
- `NoLoginWindow` følger `NoBrowser.cs` og kaster, hvis den nogensinde nås. `UnaskedTokens.CanFetch` skal fortsat kun gælde `ClientCredentials`.

### Investigation targets
**Required**:
- `src/Hoboman.Core/Auth/OAuthClient.cs:20-90` og `:150-175`
- `src/Hoboman.Core/Auth/LoopbackCallback.cs:85-95`
- `src/Hoboman/Services/AuthRefreshService.cs:38-57`

**Optional**:
- `tests/Hoboman.Tests/Auth/OAuthClientTests.cs:27-55`
- `tests/Hoboman.Tests/Auth/FakeBrowser.cs`

### Key context
Hverken fragmentet eller tokenet må stå i `OAuthException.Detail`, for `AuthViewModel.FetchTokenAsync` logger hele undtagelsen. `Detail` må kun indeholde udbyderens `error` og `error_description`, eller `token_type` ved en ikke-understøttet type, som i de andre grants (besluttet efter impl-review af opgave 1).

Undgå nye sprognøgler her: `TranslationTests` kræver, at hver nøgle bruges i koden. Nøglerne kommer i opgave 3 sammen med `ReasonOf`.
## Acceptance
- [ ] Tests viser login-adressen med de felter, API Contracts nævner, og `redirect_uri` uændret, også uden `/` til sidst.
- [ ] Tests dækker manglende Auth-URL, Callback URL og Client ID, ugyldig adresse og http uden for denne computer, og at login-vinduet så ikke åbnes.
- [ ] Tests dækker et gyldigt svar med udløbstid, forkert og manglende `state`, `error` i fragment og i query, manglende felter og anden token-type.
- [ ] Tests dækker callback-match: anden host, længere sti, anden port, query og fragment samt `https://x` mod `https://x/`.
- [ ] Tests dækker timeout, annullering og lukket vindue. Timeout giver `TimedOut`, også når en falsk login-implementering returnerer `null`, fordi annulleringen lukkede vinduet. Testene viser også, at `AuthRefreshService` returnerer `false` uden undtagelse ved lukket vindue.
- [ ] `UnaskedTokensTests` viser, at Implicit ikke kan hentes uden brugeren.
- [ ] En test af `JsonFile` viser, at en gammel fil uden `callbackUrl` giver tom `CallbackUrl`, og at `grant: Implicit` med `callbackUrl` kan læses.
- [ ] `AppTests` består, og appen starter.
- [ ] Client credentials og Authorization code-tests består uændret.
## Done summary
Added the Implicit grant to Core: `OAuthSettings.CallbackUrl`, `ILoginWindow`, `CallbackAddress.Matches`, the new problems `MissingCallbackUrl`/`LoginClosed`/`LoginUnavailable`, and `OAuthClient.ImplicitTokenAsync`, which builds the login address (`response_type=token`, `client_id`, `redirect_uri` as written, `state`, `scope`), waits 5 minutes, and reads state, error and token from the fragment (errors also from the query). The token and lifetime checks are shared with the token endpoint path; `AuthRefreshService.FetchAsync` returns false on `LoginClosed`; the CLI gets `NoLoginWindow` and the app registers a `LoginWindowOpener` placeholder (throws `LoginUnavailable`) that task 2 fills.

Tests: OAuthClientTests (login address, variables, missing/unsafe fields without opening the window, valid answer, wrong/missing state, error in fragment and query, missing fields, other token type, timeout with null, cancellation, closed window), CallbackAddressTests, OAuthSettingsTests (JsonFile.Parse of an old file and of Implicit), UnaskedTokensTests (Implicit not fetchable), AuthRefreshServiceTests (closed window returns false).

Decisions: after GetRedirectAsync, the timeout check uses the same `&& !cancellationToken.IsCancellationRequested` filter as the existing catch, so when both fire the cancellation wins, as in the authorization code flow. `redirect_uri` is the resolved text trimmed of surrounding whitespace. Implicit tokens carry no scope (the fragment's `scope` is not read, per API Contracts). The JsonFile test lives in tests/Hoboman.Tests/Auth/OAuthSettingsTests.cs because Touches limits tests to Auth.
Not committed: `.flow/` receipt (FORBIDDEN by the run - `.flow/` stays untracked).
Feature routes: none changed (no UI in this task).
Tier: not given by the conductor (model: claude-opus-5-5)

baseline: green (OAuthClientTests 30/30)
stage: impl-review - skipped(config: REVIEW_MODE=none)
## Evidence
- Commits: e40a4820e67739671643b970db07f1bfb672110b
- Tests: dotnet test --project tests/Hoboman.Tests/Hoboman.Tests.csproj --filter-namespace Hoboman.Tests.Auth (102/102 passed), dotnet test --project tests/Hoboman.Tests/Hoboman.Tests.csproj --filter-class AppTests/AuthViewModelTests/WorkflowAuthRefreshTests/RequestTabViewModelTests/FolderAuthViewModelTests/CredentialPickingTests/JsonFileTests (160/160 passed), dotnet test --project tests/Hoboman.Tests/Hoboman.Tests.csproj --filter-namespace Hoboman.Tests.Cli (287/287 passed)
- PRs: