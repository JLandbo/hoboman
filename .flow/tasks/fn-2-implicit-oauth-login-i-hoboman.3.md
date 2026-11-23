---
satisfies: [R1, R2, R7]
---
# fn-2-implicit-oauth-login-i-hoboman.3 Implicit i Auth-editoren og dokumentation

## Description
Gør grant-typen tilgængelig for brugeren og beskriv den. Editoren bruges alle steder, så det dækker requests, mapper, workflows, trin og credentials.

**Size:** M
**Files:** `src/Hoboman/Views/AuthEditor.xaml`, `src/Hoboman/ViewModels/AuthViewModel.cs`, `src/Hoboman.Core/Languages/Dansk.json`, `src/Hoboman.Core/Languages/English.json`, `tests/Hoboman.Tests/ViewModels/AuthViewModelTests.cs`, `README.md`, `CLI.md`, `AI-PROMPT.md`, `src/Hoboman.Cli/McpGuide.md`
**Touches:** [src/Hoboman/Views/AuthEditor.xaml, src/Hoboman/ViewModels/AuthViewModel.cs, src/Hoboman.Core/Languages/**, tests/Hoboman.Tests/ViewModels/AuthViewModelTests.cs, README.md, CLI.md, AI-PROMPT.md, src/Hoboman.Cli/McpGuide.md]

### Approach
- Tilføj `CallbackUrl` i `AuthViewModel` og i alle tre kopier: `UseAsync` (`:100-106`), `Load` (`:148-154`) og `EditedOAuth` (`:354-360`). Mangler den i `EditedOAuth`, kan `ToSettings` og `Matches` ikke se en ændring.
- `AuthEditor.xaml`: tredje knap ved `:230-232`. Skift stilen ved `:9-16` til tre: Auth-URL for begge browser-grants, Callback URL kun for Implicit, og Redirect-port kun for Authorization code.
- Skjul Token-URL, Client secret og afsendelse af client ID og secret ved Implicit (`:261-290`).
- Lad ventetekst-triggeren ved `:250-253` være. Teksten nævner browseren, og det modale vindue dækker alligevel for den.
- Tilføj `MissingCallbackUrl` i `ReasonOf` (`AuthViewModel.cs:363-380`; `OAuthProblem.MissingCallbackUrl` findes allerede fra opgave 1, og opgave 2 har tilføjet `LoginUnavailable`-casene som de sidste to) og tekster i begge sprogfiler (`OAuth.Login*` findes allerede). <!-- Updated by plan-sync: fn-2-implicit-oauth-login-i-hoboman.2 added the LoginUnavailable cases and shifted ReasonOf to :363-380 -->
- Docs: `McpGuide.md:196` og `:240`, `AI-PROMPT.md:26`, `README.md:22`, `:35` og `:116-131` samt `CLI.md:27` og `:446`.

### Investigation targets
**Required**:
- `src/Hoboman/Views/AuthEditor.xaml:9-16` og `:222-300`
- `src/Hoboman/ViewModels/AuthViewModel.cs:92-116`, `:139-163`, `:344-380`

**Optional**:
- `tests/Hoboman.Tests/ViewModels/AuthViewModelTests.cs:199-275` og `:325-356`
- `tests/Hoboman.Tests/Views/CredentialsWindowTests.cs`

### Key context
Editoren er delt, så ingen af de andre vinduer skal ændres. `UseAsync` henter et token, så snart en gemt credential vælges (`AuthViewModel.cs:112-115`), så ved Implicit åbner login-vinduet også dér. Beskriv det i `README.md`. Tilstedeværelsen af `{{variabler}}` i feltet kommer af `controls:VariableTextBox`, som de andre URL-felter bruger.
## Acceptance
- [ ] `ToSettings`-testen med alle felter dækker Implicit og `CallbackUrl`, og værdierne overlever gem og genindlæsning.
- [ ] En test viser, at en ændring af Callback URL ses som en ændring, og at et valgt credential udfylder feltet.
- [ ] En UI-test viser, at Implicit kun viser Auth-URL, Callback URL, Client ID og Scope, og at de to andre grants viser de samme felter som før.
- [ ] En manglende Callback URL giver den nye tekst på begge sprog, og `TranslationTests` består.
- [ ] De fire dokumenter beskriver Implicit og callback-adressen.
## Done summary
The auth editor offers Implicit as a third OAuth grant: AuthViewModel carries `CallbackUrl` through `UseAsync`, `Load` and `EditedOAuth` and tells `MissingCallbackUrl` in both languages (new texts `OAuth.Implicit`, `OAuth.CallbackUrl`, `OAuth.MissingCallbackUrl`). AuthEditor shows only Auth URL, Callback URL, Client ID and Scope for Implicit (styles `OnlyForLogin`, `OnlyForImplicit`, `OnlyForAuthorizationCode`, `NotForImplicit`), and the other grants show the same fields as before. README, CLI.md, AI-PROMPT.md and the MCP guide describe Implicit and the callback URL; README also says that picking an implicit credential opens the login window at once.

Tests (all in tests/Hoboman.Tests/ViewModels/AuthViewModelTests.cs):
- ToSettings_WhenOAuthIsFilledInAndLoadedAgain_ThenKeepsIt: all fields with Implicit and CallbackUrl survive ToSettings and Load (AC1)
- UseAsync_WhenAnImplicitClientIsPicked_ThenFillsInItsCallbackUrl and CredentialName_WhenTheCallbackUrlIsChangedAfterPicking_ThenIsNone (AC2)
- AuthEditor_WhenTheGrantIsChosen_ThenShowsOnlyItsFields: theory over all three grants, visible field labels (AC3)
- FetchTokenAsync_WhenTheCallbackUrlIsMissing_ThenSaysSoInTheLanguage: English and Danish; TranslationTests pass (AC4)

Decisions: the UI test lives in AuthViewModelTests.cs because Touches allows only that test file, so the class got `[Collection("Ui")]` to share the WPF thread safely with the other UI tests; tests/Hoboman.Tests/Views/AuthEditorTests.cs would be the natural home. The Danish label is "Callback-URL", like "Auth-URL" and "Token-URL".
Flaky, not caused by this change: one combined run had 3 CredentialPickerTests failures (keyboard/focus timeouts); they passed alone, combined with AuthViewModelTests, and in a full re-run of the same set (206/206).
Not run: the live app (the UI test drives the real AuthEditor in a window).
Not committed: `.flow/` receipt (FORBIDDEN by the run - `.flow/` stays untracked).
Feature routes: Auth editor OAuth grant segment gains Implicit.
Tier: not given by the conductor (model: claude-opus-5-5)

baseline: green (OAuthClientTests + AuthViewModelTests 70/70)
stage: impl-review - skipped(config: REVIEW_MODE=none)
## Evidence
- Commits: 4e99192f958336036f0c2cedd9c9291da2e1a178
- Tests: dotnet test --project tests/Hoboman.Tests/Hoboman.Tests.csproj --filter-class Hoboman.Tests.Auth.OAuthClientTests --filter-class Hoboman.Tests.ViewModels.AuthViewModelTests --filter-class Hoboman.Tests.Languages.TranslationTests --filter-class Hoboman.Tests.ViewModels.CredentialPickingTests --filter-class Hoboman.Tests.ViewModels.FolderAuthViewModelTests --filter-class Hoboman.Tests.ViewModels.CredentialEditorViewModelTests --filter-class Hoboman.Tests.Views.CredentialPickerTests --filter-class Hoboman.Tests.Views.CredentialsWindowTests --filter-class Hoboman.Tests.Cli.McpToolsTests (206/206)
- PRs: