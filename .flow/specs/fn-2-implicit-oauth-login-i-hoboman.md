# Implicit OAuth-login i Hoboman

## Conversation Evidence

> user (turn 1, med skærmbillede af Postman): "Kan jeg samme som i postman her? (mørke er postman)"
> user (turn 2): "Det vil jeg da ikke. Jeg vil bruge hoboman."
> user (turn 3): "Hvis der skal ændres noget, så er det i Hoboman. Skriv hvad skal ændres."
> user (turn 4): "kør flow next spec, plan og plan review."

Skærmbilledet fra Postman viser OAuth 2.0 med grant type Implicit, Callback URL `https://{{site-url}}/`, Auth URL `https://auth.{{site-url}}/login`, Client ID `{{client-id}}` og Scope `aciescore/apis`.

## Goal & Context

<!-- Source: 40% user / 40% [paraphrase] / 20% [inferred] -->

Brugeren vil kalde Acies Core-API'erne fra Hoboman med sit eget bruger-login og samme OAuth-opsætning, som virker i Postman. Hoboman kan i dag kun Client credentials og Authorization code, så den opsætning kan ikke gengives. Ændringen skal ligge i Hoboman, og et token må ikke skulle hentes i Postman og kopieres over.

## Architecture & Data Models

<!-- Source: 20% [paraphrase] / 80% [inferred] -->

`Implicit` bliver en tredje grant-type i de eksisterende OAuth-indstillinger, som får ét nyt felt: callback-adressen. Indstillingerne ejes af den request, mappe, workflow, det trin eller den credential, som de ligger på i dag. Tokens gemmes og bruges uændret pr. ejer og miljø, så resten af afsendelsen ikke kender forskel på grant-typerne.

Implicit returnerer tokenet i URL-fragmentet på callback-adressen. Fragmentet sendes aldrig til en server, så Hobomans lokale callback kan ikke modtage det. Appen åbner derfor sit eget login-vindue med WebView2, følger navigationerne og stopper ved callback-adressen. Core kender kun en grænseflade til login-vinduet, så Core stadig er uden WPF.

## API Contracts

<!-- Source: 100% [inferred] -->

OAuth-formatet i gemte filer og i `update` får grant-værdien `Implicit` og feltet `callbackUrl`:

```json
{ "kind": "OAuth2", "oAuth": { "grant": "Implicit", "authorizeUrl": "https://auth.{{site-url}}/login", "callbackUrl": "https://{{site-url}}/", "clientId": "{{client-id}}", "scope": "aciescore/apis" } }
```

Login-adressen er Auth-URL'en tilføjet `response_type=token`, `client_id`, `redirect_uri`, `state` og `scope`, når et scope er udfyldt. `redirect_uri` er callback-adressen, som den er skrevet efter `{{variabler}}` er udfyldt, fordi Cognito kræver et præcist match.

Fragmentet læses for `access_token`, `token_type`, `expires_in`, `state`, `error` og `error_description`. Værdierne percent-decodes. Cognito kan også sende fejl i query-delen, så `state`, `error` og `error_description` læses også derfra.

Core får denne grænseflade til login-vinduet. Den returnerer callback-adressen med fragment, eller `null`, når brugeren lukkede vinduet:

```csharp
public interface ILoginWindow
{
    Task<Uri?> GetRedirectAsync(Uri login, Uri callback, CancellationToken cancellationToken);
}
```

## Edge Cases & Constraints

- Fragmentet indeholder tokenet, så callback-adressen må aldrig logges med fragment. [inferred]
- Kun callback-adressen må stoppes af vinduet. Andre sider i login-flowet, også med en lignende adresse, skal fortsætte normalt. [inferred]
- Svar-visningen kører fremmede sider, så den må ikke dele cookies med login-vinduet. [inferred]
- Eksisterende gemte filer uden `callbackUrl` skal læses og opføre sig som før. [inferred]
- Fejl, som Cognito viser på sin egen side uden redirect, fx et forkert callback, bliver stående i login-vinduet, indtil brugeren lukker det. [inferred]
- Selvom navigationen stoppes, kan browseren nå at sende en anmodning til callback-adressen. Den indeholder ikke fragmentet og dermed ikke tokenet. Det er målt med en lokal test.
- Login-vinduet åbner ingen nye vinduer, downloader intet, starter ingen eksterne programmer og afviser anmodninger om tilladelser. Et link, der vil åbne et nyt vindue, åbnes i samme vindue, så et callback altid fanges. [inferred]

## Acceptance Criteria

- **R1:** I Hobomans Auth-editor kan OAuth 2.0 sættes til grant-typen Implicit, så Postman-opsætningen fra skærmbilledet kan udfyldes. Implicit viser Auth-URL, Callback URL, Client ID og Scope og skjuler Token-URL, Client secret, afsendelse af client ID og secret og Redirect-port. Alle felter kan bruge `{{variabler}}` fra det valgte miljø. Valget virker overalt, hvor editoren bruges, og gemmes og genindlæses uændret. [paraphrase] Errors: ingen fejl ud over R2.
- **R2:** **Hent token**, refresh-knappen og valg af en gemt credential åbner ved Implicit et login-vindue i appen på login-adressen fra API Contracts med et tilfældigt `state`. Systembrowseren åbnes ikke. Vinduet er modalt over det vindue, hentningen startede fra, så der kun er ét login ad gangen, og felterne ikke kan ændres undervejs. Errors: manglende Auth-URL, Callback URL eller Client ID giver hver sin tydelige fejl uden at åbne vinduet; en ugyldig adresse eller http uden for denne computer giver de eksisterende adressefejl.
- **R3:** Når login sender vinduet til callback-adressen, stoppes navigationen, vinduet lukkes, og tokenet fra fragmentet gemmes for det valgte miljø med udløbstid fra `expires_in`. Efterfølgende kald med den auth sender det som Bearer-token, så et kald til `GET /sales/orders/{id}` bruger brugerens eget login. [paraphrase] Errors: forkert eller manglende `state` afviser svaret uden at gemme token og giver fejlen for ugyldigt svar; `error` i fragment eller query vises som afvist login med udbyderens tekst; manglende `access_token` eller `token_type` giver fejlen for ugyldigt svar; en anden type end Bearer giver fejlen for ikke-understøttet type. Tokenet og fragmentet logges aldrig.
- **R4:** Kun en navigation med samme scheme, host, port og sti som callback-adressen stopper login. En adresse, der kun ligner, som en anden host eller en længere sti, fortsætter i vinduet. Query og fragment indgår ikke i sammenligningen, og `https://x` og `https://x/` er samme adresse. Sammenligningen sker efter `{{variabler}}` er udfyldt. Errors: ingen fejl ud over R2.
- **R5:** Lukker brugeren vinduet, annulleres hentningen, eller går der 5 minutter, stopper login uden at gemme noget, og et tidligere gemt token bevares. Vinduet er altid lukket bagefter. Det gælder også for en hentning startet fra en mappes auth. Errors: timeout viser den eksisterende timeout-fejl; lukning og annullering viser ingen fejl.
- **R6:** Login-vinduet gemmer sine cookies i sin egen datamappe, adskilt fra svar-visningen og bevaret mellem kørsler. Et nyt token under en stadig gyldig login-session kræver derfor ikke, at brugeren indtaster sit login igen. Errors: kan WebView2 ikke startes, vises en tydelig fejl, og der gemmes intet.
- **R7:** CLI og MCP åbner aldrig login-vinduet. De kan læse og gemme Implicit-indstillinger med `show` og `update`. De bruger et gyldigt token hentet i appen, og ellers svarer de med den eksisterende fejl om at hente et nyt token i appen. `README.md`, `CLI.md`, `AI-PROMPT.md` og MCP-guiden beskriver Implicit. Errors: ingen fejl ud over den nævnte.

## Early proof point

Task fn-2-implicit-oauth-login-i-hoboman.1 validerer kernen: login-adressen, callback-matchet og aflæsningen af fragmentet. Fejler den, skal tilgangen vurderes igen, før fn-2-implicit-oauth-login-i-hoboman.2 og .3 fortsætter. At WebView2 giver fragmentet med ved redirect, er allerede målt med en lokal test.

## Quick commands

```bash
dotnet test --project tests/Hoboman.Tests/Hoboman.Tests.csproj --filter-class "Hoboman.Tests.Auth.OAuthClientTests"
```

## Boundaries

- Cognito-klienten og andre dele af Acies Core ændres ikke. [paraphrase]
- Client credentials og Authorization code opfører sig som før. [inferred]
- Ingen automatisk fornyelse af Implicit-tokens og ingen brug af refresh tokens. [inferred]
- Ingen id-token-håndtering. Kun `access_token` bruges. [inferred]
- Ingen ny Postman-import eller andre nye OAuth-grants. [inferred]
- Ingen log-ud eller skift af bruger i appen. Den gemte login-session bruges, til den udløber, eller datamappen slettes. [inferred]

## Decision Context

<!-- Source: 30% user / 70% [inferred] -->

Authorization code findes allerede i Hoboman, men kræver, at Cognito-klienten tillader den grant og har Hobomans lokale callback registreret. Brugeren besluttede, at en eventuel ændring skal ske i Hoboman, så Cognito-klienten bevares som den er. Et login-vindue er eneste måde at læse fragmentet på uden at ændre klienten, da callback-adressen peger på Acies-sitet og ikke på denne computer. En systembrowser kan ikke give fragmentet tilbage til appen.

En lokal test med WebView2 1.0.4258.31 og et 302-svar har vist, at `NavigationStarting` giver hele fragmentet med ved redirect, og at navigationen kan annulleres dér. Testen er ikke kørt mod Cognito, så det første rigtige login er stadig det endelige bevis.

Vinduet er modalt, fordi det fjerner flere problemer på én gang: flere samtidige logins på samme profil, ændringer af felter under login og et login, der ellers blev kasseret uden besked. En lukning af vinduet håndteres ét sted i token-hentningen, så både requests og mapper opfører sig ens.

Maintainability (plan review): duplication - `CallbackUrl` tilføjes i tre parallelle kopier i `AuthViewModel` samt i variabel-listen i `OAuthClient`; structure - `OAuthClient.GetTokenAsync` skelner på grant-typen to gange efter opgave 1.

## Requirement coverage

| Req | Description | Task(s) | Gap justification |
| --- | --- | --- | --- |
| R1 | I Hobomans Auth-editor kan OAuth 2.0 sættes til grant-typen Implicit, så Postman-opsætningen fra skærmbilledet kan udfyldes. Implicit viser Auth-URL, Callback URL, Client ID og Scope og skjuler Token-URL, Client secret, afsendelse af client ID og secret og Redirect-port. Alle felter kan bruge `{{variabler}}` fra det valgte miljø. Valget virker overalt, hvor editoren bruges, og gemmes og genindlæses uændret. [paraphrase] Errors: ingen fejl ud over R2. | fn-2-implicit-oauth-login-i-hoboman.3 | — |
| R2 | **Hent token**, refresh-knappen og valg af en gemt credential åbner ved Implicit et login-vindue i appen på login-adressen fra API Contracts med et tilfældigt `state`. Systembrowseren åbnes ikke. Vinduet er modalt over det vindue, hentningen startede fra, så der kun er ét login ad gangen, og felterne ikke kan ændres undervejs. Errors: manglende Auth-URL, Callback URL eller Client ID giver hver sin tydelige fejl uden at åbne vinduet; en ugyldig adresse eller http uden for denne computer giver de eksisterende adressefejl. | fn-2-implicit-oauth-login-i-hoboman.1, fn-2-implicit-oauth-login-i-hoboman.2, fn-2-implicit-oauth-login-i-hoboman.3 | — |
| R3 | Når login sender vinduet til callback-adressen, stoppes navigationen, vinduet lukkes, og tokenet fra fragmentet gemmes for det valgte miljø med udløbstid fra `expires_in`. Efterfølgende kald med den auth sender det som Bearer-token, så et kald til `GET /sales/orders/{id}` bruger brugerens eget login. [paraphrase] Errors: forkert eller manglende `state` afviser svaret uden at gemme token og giver fejlen for ugyldigt svar; `error` i fragment eller query vises som afvist login med udbyderens tekst; manglende `access_token` eller `token_type` giver fejlen for ugyldigt svar; en anden type end Bearer giver fejlen for ikke-understøttet type. Tokenet og fragmentet logges aldrig. | fn-2-implicit-oauth-login-i-hoboman.1, fn-2-implicit-oauth-login-i-hoboman.2 | — |
| R4 | Kun en navigation med samme scheme, host, port og sti som callback-adressen stopper login. En adresse, der kun ligner, som en anden host eller en længere sti, fortsætter i vinduet. Query og fragment indgår ikke i sammenligningen, og `https://x` og `https://x/` er samme adresse. Sammenligningen sker efter `{{variabler}}` er udfyldt. Errors: ingen fejl ud over R2. | fn-2-implicit-oauth-login-i-hoboman.1, fn-2-implicit-oauth-login-i-hoboman.2 | — |
| R5 | Lukker brugeren vinduet, annulleres hentningen, eller går der 5 minutter, stopper login uden at gemme noget, og et tidligere gemt token bevares. Vinduet er altid lukket bagefter. Det gælder også for en hentning startet fra en mappes auth. Errors: timeout viser den eksisterende timeout-fejl; lukning og annullering viser ingen fejl. | fn-2-implicit-oauth-login-i-hoboman.1, fn-2-implicit-oauth-login-i-hoboman.2 | — |
| R6 | Login-vinduet gemmer sine cookies i sin egen datamappe, adskilt fra svar-visningen og bevaret mellem kørsler. Et nyt token under en stadig gyldig login-session kræver derfor ikke, at brugeren indtaster sit login igen. Errors: kan WebView2 ikke startes, vises en tydelig fejl, og der gemmes intet. | fn-2-implicit-oauth-login-i-hoboman.2 | — |
| R7 | CLI og MCP åbner aldrig login-vinduet. De kan læse og gemme Implicit-indstillinger med `show` og `update`. De bruger et gyldigt token hentet i appen, og ellers svarer de med den eksisterende fejl om at hente et nyt token i appen. `README.md`, `CLI.md`, `AI-PROMPT.md` og MCP-guiden beskriver Implicit. Errors: ingen fejl ud over den nævnte. | fn-2-implicit-oauth-login-i-hoboman.1, fn-2-implicit-oauth-login-i-hoboman.3 | — |
