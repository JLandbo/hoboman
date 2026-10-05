# Hoboman: vejledning til MCP-værktøjerne

Du kan sende API-kald og køre workflows gennem **Hoboman**, et API-værktøj på brugerens Windows-maskine, med værktøjerne fra MCP-serveren `hoboman`.

> **UFRAVIGELIG REGEL: Du har IKKE adgang til Hobomans mappe. Din eneste adgang til Hoboman er MCP-værktøjerne fra `hoboman`.**
>
> - Hobomans mappe er programmappen og alt under den. Du må ALDRIG liste, åbne, læse, søge i, skrive, kopiere, flytte, omdøbe eller slette noget i den, hverken med en shell, et filværktøj, en editor eller andet. Det gælder også, hvis du kender stien, hvis brugeren ikke har sagt noget, eller hvis det ville være hurtigere.
> - Kør aldrig `hoboman-cli.exe` selv. Al kørsel og al oprettelse, visning, rettelse og sletning går gennem MCP-værktøjerne. Intet andet.
> - Peg aldrig `out` på en fil i Hobomans mappe. Værktøjet afviser det med `Hoboman's own files cannot be used.`
> - **Kan værktøjerne ikke det, du skal, så stop og sig det til brugeren.** Gå aldrig uden om dem, og find ikke på en anden vej.

> **UFRAVIGELIG REGEL: Passwords og client secrets skrives KUN af brugeren i Hoboman-appen.**
>
> - Du må bruge og sætte auth op med `update`: `kind`, `userName` og `oAuth` på requests, workflows og trin.
> - Passwords, client secrets og tokenet i Bearer-auth skriver brugeren under Auth i appen. Du kan hverken læse eller skrive dem gennem værktøjerne. Vil du bruge et token fra et svar, så giv det i en header, `Authorization: Bearer {{token}}`, med `variables`.
> - Send eller gem ALDRIG et password eller en client secret som tekst: ikke i en URL, header, body, `variables`, `parameters`, et miljø, en parameters `default` eller et script.
> - Et login, der kræver et password, sker med en gemt request, som brugeren har sat op (`send_saved`), eller med auth, som brugeren har udfyldt i appen.
> - Alle andre værdier må gerne gives som variabler og parametre, også et token, du har fået i et svar.
> - Mangler et password eller en client secret, så sig det til brugeren, som skriver det i appen.

## Regler

1. **Ændr kun noget, når brugeren beder om det.** `list`, `show`, `history`, `log` og `check` læser. `new_…`, `update`, `rename`, `move` og `delete` ændrer. Brug kun `delete` med `confirm`, når brugeren har bedt om netop den sletning.
2. **Send ikke kald med sideeffekter** (kald eller workflows, der opretter, ændrer eller sletter data), uden at brugeren har bedt om netop det. Døm efter, hvad kaldet gør, ikke kun metoden: et login eller en søgning med POST ændrer intet. `run` sender, så snart workflowet er tjekket; brug `check` for kun at tjekke.
3. **Gentag ikke hemmeligheder** som tokens, passwords og API-nøgler i dine svar til brugeren. Opsummér i stedet, fx "login lykkedes, token gemt".
4. **Variabler og parametre er ikke hemmelige.** `run` skriver dem i klartekst i sit resultat og i kørslens log, og en værdi i en URL står i historikken. Derfor aldrig et password eller en client secret i dem.
5. **Gæt ikke.** Findes en request, et workflow, et miljø eller en parameter ikke, så fortæl brugeren det, og spørg.
6. **Sig til, når du har ændret noget.** Har brugeren ugemte ændringer i den samme request i appen, mister brugeren dem, for filen vinder. I et workflow overskriver brugerens næste gem din ændring. I vinduet med miljøer eller credentials afviser appen at gemme.

## Værktøjerne

| Værktøj | Parametre | Resultat |
|---|---|---|
| `list` | `kind`: `requests` (standard), `workflows`, `folders` eller `environments` | Én linje pr. ting: id, en tabulator og stien eller navnet |
| `show` | `target` | `{"request": …}`, `{"folder": …}`, `{"workflow": …, "scripts": {…}}` eller `{"environment": …}` |
| `history` | `name` eller `count` (mindst 1, standard 20), ikke begge | De nyeste kald som tekstlinjer, eller ét kald som `{"call": …}` |
| `log` | `workflow`; `run` eller `last`, ikke begge; `step` (kun med `run` eller `last`); `count` (mindst 1, standard 20, kun uden `run` og `last`) | De nyeste kørsler som tekstlinjer, én kørsels events som JSON-linjer uden trinnenes `headers` og `body`, eller med `step` ét trins fulde `step.finished` |
| `check` | `workflow`, `environment`, `parameters` (JSON-objekt) | `{"id", "name"}`, eller en fejl med `problems` |
| `run` | `workflow`, `environment`, `parameters` (JSON-objekt) | Kørslens events som JSON-linjer uden trinnenes `headers` og `body`, når den er færdig |
| `send_saved` | `request`, `environment`, `variables` (JSON-objekt), `out` | Svaret som JSON |
| `send` | `method`, `url`, `environment`, `headers` (liste af `Navn: Værdi`), `json` eller `text`, `variables`, `out` | Svaret som JSON |
| `new_request` | `folder`, `name`, `url`, `method` (standard `GET`), `headers`, `json` eller `text` | `{"id", "path"}` |
| `new_folder` | `folder`, `name` | `{"id", "path"}` |
| `new_workflow`, `new_environment` | `name` | `{"id", "path"}` |
| `update` | `target`, `content` (hele JSON-objektet fra `show`, rettet) | `{"id", "path"}` |
| `rename` | `target`, `name` | `{"id", "path"}` |
| `move` | `target`, `folder` | `{"id", "path"}` |
| `delete` | `target`, `confirm` | `{"id", "path"}`, eller uden `confirm` en fejl, der fortæller, hvad der ville blive slettet |
| `guide` | – | Denne vejledning |

- **Typer.** `variables`, `parameters` og `content` er JSON-objekter, ikke tekst. `headers` er en liste af tekster.
- **Mål.** `target`, `request` og `workflow` er et id eller en sti/et navn. Navne er ikke unikke, så brug id'et fra `list`. Har flere stien eller navnet, giver `target` fejlen `Target is ambiguous. Use its id.`, `request` `Saved request name is ambiguous. Use its id.` og `workflow` `Workflow name is ambiguous. Use its id.` For `target` gør et miljø med samme navn som en request, mappe eller et workflow også navnet flertydigt. `folder` er en mappes id eller sti, eller `.` for øverste niveau.
- **Fejl.** Et værktøj, der fejler, giver et resultat markeret som fejl med `{"error": "…"}` og eventuelt flere felter, fx `path` og `line`. Intet er så ændret, men et kald med `send_saved` eller `send` kan være sendt og står i historikken, fx når `out` ikke kunne skrives. Mangler et påkrævet parameter, eller har et parameter forkert type, giver MCP-serveren selv fejlen `An error occurred invoking '<værktøj>'.` som tekst uden JSON. Et svar med en anden status end 2xx og en kørsel, hvor et trin fejlede, er svar, ikke fejl. Læs `status` eller `outcome`.
- **Store svar.** Claude Code advarer over 10.000 tokens og gemmer et resultat over 25.000 tokens eller 50.000 tegn i en fil i stedet; andre AI-programmer har lignende grænser. Gem store og binære svar med `out`.
- **Filer i resultater.** En fil, du selv har gemt med `out`, må du bruge. En sti ind i Hobomans mappe, fx `file` i en fejl, er kun til at fortælle brugeren, hvor noget ligger. Åbn den aldrig selv.

**Find ting.**
- `list` giver én linje pr. gemt request: id'et, en tabulator og stien gennem mapperne, fx `3f2c…<tab>Shop/Login`. Del ved den første tabulator, for et navn kan selv indeholde `/`. Med `kind` `environments` ender det miljø, brugeren har valgt, med `<tab>selected`.
- En linje uden sti er en fil, der ikke kan læses, eller en request uden `name` på øverste niveau; i en mappe står den som `Mappe/`. I appen står en fil, der ikke kan læses, øverst i samlingerne som **Kan ikke læses (xxxxxxxx…)** med id'ets første 8 tegn. `show` med id'et fortæller med `File is not valid.`, hvilken fil og linje der er galt, og viser en request uden navn med `"name":""`. Fortæl brugeren det, og send den ikke for at finde ud af det.
- Vil du vide, hvad en request gør, før du sender den, så brug `show`. Svaret har metode, URL, query, headers, body og auth uden hemmeligheder. Dens mappe er `folderId`, og `show` med mappens id giver mappens navn, auth og overmappe (`parentId`).
- `history` giver de nyeste kald, nyeste først: `navn<tab>tid<tab>kilde<tab>metode<tab>status<tab>adresse`, hvor tid er UTC, kilde er `App` eller `Cli` (også for MCP), og status er fejlens art, når der ikke kom noget svar. Med `name` giver den hele kaldet med request, svar eller fejl. Svar kan indeholde tokens, så gentag dem ikke.

**Miljøer.**
- `environment` er miljøets navn, uden hensyn til store og små bogstaver, aldrig id'et. Uden `environment` bruges det miljø, brugeren har valgt i appen. Har brugeren intet valgt, bruges intet miljø, og `{{navne}}` udfyldes kun fra `variables`, og i et workflow fra dets egne værdier.
- Et miljø oprettes med `new_environment`, får sine variabler med `show` og `update`, omdøbes med `rename` og slettes med `delete`, der også glemmer miljøets tokens og de credentials, der hører til det. Navne skal være unikke uden hensyn til store og små bogstaver. Hemmeligheder hører til under Auth, ikke i et miljø.
- Sletter du det miljø, brugeren har valgt, er intet valgt længere, og kald uden `environment` bruger intet miljø. Har brugeren slettet det valgte miljø i appen, giver de `Selected environment was not found.`, indtil brugeren vælger et andet.

**Send.**
- `send_saved` sender requesten med dens egne headers, body og auth. Du kan kun ændre dens `{{variabler}}` med `variables`. I bodyen udfyldes de kun, når requesten har `useEnvironmentVariablesInBody` sat til `true` (se `show`). Brug den til et login, der kræver et password.
- `send` har ingen auth ud over de headers, du giver. `{{navne}}` udfyldes i URL og headers, men ikke i bodyen. Skriv aldrig et password eller en client secret i et direkte kald. `json` sender `Content-Type: application/json; charset=utf-8` og `text` `text/plain; charset=utf-8`; en header, du giver, erstatter typen. `json` og `text` er altid teksten selv og læses aldrig fra en fil.
- `variables` vinder over miljøets værdier og gælder kun det ene kald. Værdier, der ikke er tekst, indsættes som deres JSON.
- `out` er kun et filnavn, fx `faktura.pdf`. Filen oprettes i mappen `Downloads\Hoboman` i brugerens profil, og svarets body gemmes i den byte for byte; `file` i svaret er dens fulde sti. Brug det til PDF'er, billeder og andre binære eller store svar. Filen skrives også ved 4xx og 5xx, så læs `status`. Et navn med en mappe, et drev, `:`, `..` eller et enhedsnavn som `NUL` afvises (`Output file must be a plain file name.`), og findes filen, sendes intet (`Output file already exists.`); vælg et andet navn. Kan filen ikke skrives, er kaldet sendt alligevel. Du kan ikke gemme andre steder; det er med vilje.

## Svaret fra `send_saved` og `send`

Et svar er ét JSON-objekt, også ved 4xx og 5xx:

```json
{"status":200,"reason":"OK","elapsedMs":123,"size":11,"headers":[{"name":"Content-Type","value":"application/json"}],"body":"{\"id\":\"42\"}"}
```

`body` er tekst. Er den JSON, skal den parses en gang til. Med `out` står `file` med filens fulde sti i stedet for `body`. `headers` har både svarets og indholdets headers, én pr. værdi.

**Kæd kald sammen.** Log ind med den gemte login-request, som brugeren har sat op (`send_saved`), læs tokenet i `body`, og giv det i næste kald som en variabel: `send` med `headers` `["Authorization: Bearer {{token}}"]` og `variables` `{"token": "…"}`. Historikken gemmer headeren, som den er skrevet, med `{{token}}`, så tokenet står ikke dér. Skriver du tokenet direkte i headeren, står det i klartekst i historikken. Gentag ikke tokenet til brugeren.

## Workflows

Et workflow er en række trin, der hver sender sin egen request, kører et script eller venter et antal sekunder. Værdier fra et svar, fx et token, gemmes i variabler, som de næste trin bruger.

**Find workflows og deres parametre.**
- `list` med `kind` `workflows` giver id og navn på hvert workflow. Giv `run` id'et. Et workflow uden navn kan ikke læses eller mangler `name`. `show` med id'et fortæller, hvad der er galt med en fil, der ikke kan læses, og viser et workflow uden navn med `"name":""`; kør det ikke for at finde ud af det.
- `show` giver `{"workflow": {…}, "scripts": {"navn.js": "kode"}}`. Se `parameters`: en parameter uden `default` er påkrævet og skal gives i `parameters`, fx `{"orderId": "o-17", "pageSize": 50}`. Værdierne beholder deres type.
- `check` tjekker det som `run`, med de samme parametre og miljø, men sender intet. Det tjekker ikke, at hemmeligheder og tokens til auth findes; mangler en, fejler trinnet ved `run`.
- Vil du vide, om et workflow ændrer data, før du spørger brugeren, så se trinnenes `method` med `show`.
- Hvert trin har præcis én af `request` (metode, URL, query, headers, body og auth), `script` (et af workflowets scripts) og `delaySeconds`. Et trin med `"auth": { "kind": "Inherit" }` bruger workflowets fælles `auth`; uden `auth` sender trinnet ingen. Hemmeligheder til auth står aldrig i resultatet.
- `run` og `check` bruger workflowet, som det er gemt. Ugemte ændringer i appen ses ikke.

**Events.** `run` giver én JSON-linje pr. event, når kørslen er færdig. Der er altid kun én kørsel pr. kald. Trinnenes `headers` og `body` er udeladt, så resultatet er lille, og `run.finished` altid er med. Ét trins fulde svar hentes med `log` med `run` (`runId` fra `run.started`) eller `last` og `step` (trinnets `index`).

| `type` | Indhold |
|---|---|
| `run.started` | Første linje. `runId`, `workflowId`, `workflow`, `environment` og `parameters` |
| `step.started` | `index` (første trin er 0), `name` (trinnets navn), `method`, `address` |
| `step.finished` | `index`, `outcome` (`Succeeded`/`Failed`), `status`, `reason`, `elapsedMs`, `size`, `attempts` (ved `retry`), `error` (ved fejl), `saved` (gemte værdier). `headers` og `body` kun fra `log` med `step` |
| `step.retrying` | Et trin med `retry` fik et svar, der ikke var klar, og prøves igen: `index`, `attempt`, `status`, `value` eller `error`. Vent på `step.finished` |
| `step.skipped` | Et trin, der ikke blev kørt, fordi et tidligere trin fejlede, eller kørslen blev afbrudt |
| `step.cancelled` | Trinnet, der kørte, da kørslen blev afbrudt |
| `run.finished` | Sidste linje. `outcome` (`Succeeded`/`Failed`/`Cancelled`), `elapsedMs`, `steps` (`index`, `outcome`, `status`) og `variables` (parametre og variabler, der fik en værdi) |

- Ét trins fulde svar fra `log` med `step` kan være stort, fordi hele bodyen er med, så dit AI-program kan gemme det i en fil.
- Værdier er ægte JSON, så et tal er et tal og et objekt et objekt.
- Ignorér felter og event-typer, du ikke kender.
- Mangler `run.finished`, blev kørslen stoppet undervejs. Stilhed betyder ikke succes.

**Læs et resultat.** Læs `outcome` i `run.finished`:
- `Succeeded`: alle trin lykkedes. Brug `variables` som resultat.
- `Failed`: find `step.finished` med `"outcome":"Failed"`, og rapportér trinnets navn fra `step.started` med samme `index`, dets `status` og `error`. Har du brug for svaret, så hent det med `log` og `step`, og rapportér et uddrag af `body`.
- `Cancelled`: kørslen blev afbrudt. Der er intet fejlet trin. Afbryder dit AI-program selv kaldet, stoppes kørslen, og du får intet svar fra `run`; se så udfaldet med `log` og `last`.

Startede kørslen ikke, giver `run` en fejl, og intet er sendt.

**Kørsler, også dem startet i appen.**
- `log` med kun `workflow` giver én linje pr. kørsel, de nyeste 20 eller `count`, nyeste først: `runId<tab>start<tab>outcome`, hvor `start` er UTC, og `outcome` er `-`, hvis kørslen stadig kører eller blev stoppet uden `run.finished`. Det gælder også kørsler startet i appen.
- `log` med `run` eller `last` giver den kørsels events, så langt den er nået, uden trinnenes `headers` og `body` ligesom `run`. Med `step` giver den det trins fulde `step.finished`. Kører kørslen stadig, så spørg igen senere.
- Et ukendt workflow giver `Workflow could not be found.` Kørsler af et workflow, hvis fil ikke kan læses, kan stadig læses med dets id. Kører en kørsel stadig, så sig det til brugeren, og spørg igen, når brugeren vil vide det. Appen kan være sat til at slette kald og kørsler efter et antal dage.

## Byg og ret (kun når brugeren beder om det)

- Opret et workflow med `new_workflow` og en request med `new_request`. Ret dem med `show` og `update`: tag JSON'en fra `show`, ret i den, og giv hele objektet som `content` til `update`. `update` tager imod præcis den form, `show` gav, og erstatter indholdet. Id, navn og mappe bliver, og dermed hemmeligheder, historik og kørsler.
- Ret aldrig `id`, `name` eller `folderId`; `update` afviser det. Brug `rename` og `move`.
- Skal du lave noget nyt, så opret det med `new_request`, `new_workflow` eller `new_environment`, og byg det med `update` ud fra `show` af det nye eller af et eksisterende, der ligner. `show` viser altid det præcise format. En mappe kan vises, men ikke rettes.
- Et trins `bodyKind` er `Json`, når det udelades. Giv `Xml` eller `Text` for andet. Ved `None` sendes bodyen ikke, og en tom body sendes aldrig.
- Kræver et trin et password eller en client secret i bodyen, fordi API'et ikke bruger Basic eller OAuth, så skriv det aldrig selv: bed brugeren skrive det i trinnet i appen. Kopierer du et trin ud fra en gemt request, følger requestens hemmeligheder ikke med; brugeren udfylder dem på trinnet.
- Hvert navn, et trin gemmer i, skal stå i `variables`. Et lille eksempel på `content` til `update`, hvor `id` er workflowets eget fra `show`. Kræver API'et login, så giv workflowet `auth`, og trinnene `"auth": { "kind": "Inherit" }`; brugeren skriver hemmeligheden i appen:

  ```json
  {
    "workflow": {
      "id": "9bf2fef5-9047-4075-969c-db7c7377a62d",
      "name": "Ny kurv",
      "parameters": [ { "name": "userId", "default": 1 } ],
      "variables": [ { "name": "firstName" }, { "name": "newCart" } ],
      "steps": [
        { "name": "Hent bruger",
          "request": { "method": "GET", "url": "{{baseUrl}}/users/{{userId}}" },
          "saves": [ { "variable": "firstName", "from": "$.firstName" } ] },
        { "name": "Byg ny kurv", "script": "byg-kurv.js", "saves": [ { "variable": "newCart", "from": "$.cart" } ] },
        { "name": "Vent", "delaySeconds": 2 },
        { "name": "Opret kurv",
          "request": { "method": "POST", "url": "{{baseUrl}}/carts/add", "bodyKind": "Json", "body": "{{newCart}}" } }
      ]
    },
    "scripts": { "byg-kurv.js": "return { cart: { userId: vars.userId, products: [ { id: 1, quantity: 1 } ] } };" }
  }
  ```

- Behold `id` i trinnets `request` på de trin, der allerede har et; trinnets hemmeligheder hører til det. Et nyt trin skal ikke have noget `id`; `update` giver det et. Kopiér aldrig et id fra et andet workflow eller en request. Fjernes et trin, glemmes dets hemmeligheder.
- Scripts står i `scripts` med filnavnet som nøgle, fx `"byg-kurv.js"`. Scripts, du ikke giver, bliver liggende, og et script givet som `null` slettes, fx `"gammel.js": null`. Et script, et trin bruger, kan ikke slettes. Skal et trin have auth med et password eller en client secret, så bed brugeren udfylde den under Auth i appen og gemme. Auth kan også gives som en header med et token fra et tidligere trin, fx `Authorization: Bearer {{token}}`.
- Workflowets fælles auth og et trin, der bruger den, ser sådan ud; brugeren skriver tokenet under Auth på workflowet i appen:

  ```json
  "auth": { "kind": "Bearer" },
  "steps": [ { "name": "Hent", "request": { "url": "{{baseUrl}}/me", "auth": { "kind": "Inherit" } } } ]
  ```
- Et script læser `vars.navn`, som ikke kan ændres, og returnerer det, trinnet gemmer fra med `saves`, fx `"from": "$.cart"`. Det returnerede bliver JSON. Skal scriptet lave HTML, XML eller tekst, så giv trinnet `"output": "Html"`, `"Xml"` eller `"Text"` og returnér en tekst. Den bliver outputtet, som den er, og `"from": "$"` gemmer den. Det kører som strict JavaScript uden filer, netværk, .NET og `eval` og stoppes efter 5 sekunder.
- Appen viser workflowet med det samme. Kan workflowet ikke læses, står det under **Workflows** som **Kan ikke læses (xxxxxxxx…)**. Når brugeren gemmer, skriver appen `variables` ud fra trinnenes `saves`.
- Tjek workflowet med `check`. Vil du køre det, så spørg først: `run` sender med det samme.
- Slet med `delete` og `confirm`, når brugeren beder om det, så hemmelighederne også slettes.

## Formatet

Felterne skrives i camelCase og enums som tekst, fx `"Json"`, `"Inherit"`, `"OAuth2"`. Felter, du udelader, får deres standard. Ukendte eller stavet forkert felter afvises med `Input is not valid.` og `path` og `line`. Passwords, client secrets og tokens til auth står aldrig i JSON'en; de skrives under Auth i appen.

**Request** (`{"request": {…}}`)

| Felt | Indhold |
|---|---|
| `id`, `name`, `folderId` | Requestens id, navn og mappe. Ændres ikke med `update`; brug `rename` og `move` |
| `method` | Fx `GET`, `POST`, `PUT`, `PATCH`, `DELETE` eller en anden metode |
| `url` | Adressen, fx `{{baseUrl}}/orders/{{id}}`. Påkrævet |
| `query`, `headers` | Lister af `{"name", "value", "enabled"}`. `enabled` er `true`, når det udelades |
| `bodyKind` | `None`, `Json`, `Xml` eller `Text`. Standard er `Json`. Ved `None` sendes bodyen ikke, og en tom body sendes aldrig |
| `body` | Bodyen som tekst. En JSON-body skrives som en tekst med JSON i |
| `useEnvironmentVariablesInBody` | `true` udfylder `{{navne}}` i bodyen. Standard er `false` for gemte requests |
| `base64` | `{"encode": [stier], "decode": [stier]}`: JSON-stier, der sendes som Base64 eller vises decodet i appen. `body` i svaret til dig er ikke decodet. `$` er hele bodyen |
| `auth` | Se Auth nedenfor |

**Auth** (`"auth": {…}` på en request, en mappe, et workflow eller et trin)

| `kind` | Felter | Hemmelighed, som brugeren skriver i appen |
|---|---|---|
| `Inherit` | Ingen. Bruger mappens auth, eller workflowets på et trin | – |
| `None` | Ingen | – |
| `Basic` | `userName` | Password |
| `Bearer` | Ingen | Token |
| `OAuth2` | `oAuth`: `grant` (`ClientCredentials` eller `AuthorizationCode`), `tokenUrl`, `authorizeUrl` (kun authorization code), `clientId`, `scope`, `clientAuthentication` (`BasicHeader` eller `RequestBody`), `redirectPort` (valgfri) | Client secret. Ved authorization code også login i appen |

Skifter du `kind`, mangler hemmeligheden, indtil brugeren har skrevet den i appen.

**Workflow** (`{"workflow": {…}, "scripts": {…}}`)

| Felt | Indhold |
|---|---|
| `id`, `name` | Ændres ikke med `update`; brug `rename` |
| `parameters` | `[{"name", "default"}]`. `default` kan være enhver JSON-værdi; uden den er parameteren påkrævet |
| `variables` | `[{"name", "default"}]`: de navne, trinnene gemmer i |
| `auth` | Workflowets fælles auth, som trin med `Inherit` bruger. Udelades den, har workflowet ingen |
| `steps` | Trinnene i rækkefølge, se nedenfor |
| `scripts` | `{"navn.js": "kode"}`. Givne scripts skrives, `null` sletter, og andre bliver liggende |

Et trin har `name` (valgfrit) og præcis én af:
- `request`: som en request, men uden `name` og `folderId`. `id` er trinnets egen og hører til dets hemmeligheder; behold det på eksisterende trin, og udelad det på nye. `useEnvironmentVariablesInBody` er `true`, når det udelades. `bodyKind` er `Json`, når det udelades. Uden `auth` sender trinnet ingen auth.
- `script`: navnet på et script i `scripts`. Trinnet kan have `output` (`Json` som standard, `Html`, `Xml` eller `Text`).
- `delaySeconds`: 1-300 sekunder. Et ventetrin kan ikke have `saves`.

Et trin kan desuden have:
- `saves`: `[{"variable", "from"}]`. `from` er `$` for hele bodyen, en sti som `$.data.items[0].id`, `header:Navn`, `status` eller en fast JSON-værdi som `"1"` eller `"\"ja\""`. Der gemmes kun efter et 2xx-svar.
- `retry` (kun request-trin): `{"until": "$.status", "equals": "done", "stopIf": "$.status", "stopEquals": "failed", "times": 30, "waitSeconds": 5}`. Trinnet sendes igen, indtil svaret er 2xx, alt i `saves` findes, og `until` er `equals`. Det stopper med det samme, når `stopIf` er `stopEquals`. Sammenligningen ser bort fra store og små bogstaver. `until` og `equals` gives sammen eller slet ikke, og det samme gælder `stopIf` og `stopEquals`. `times` er 1-100 og `waitSeconds` 0-300. Gentag ikke en request, der opretter noget, medmindre API'et tåler det.

`{{navn}}` udfyldes i URL, query, headers, Basic og Bearer og i bodyen, når `useEnvironmentVariablesInBody` er `true`. Værdien kommer fra workflowets parametre og variabler, og ellers fra miljøet. OAuth-felterne udfyldes kun fra miljøet. Et script læser alle værdier i `vars`.

**Miljø** (`{"environment": {…}}`): `id` og `name` ændres ikke med `update`, og `variables` er `[{"name", "value", "enabled"}]`. Kun variabler med `enabled` `true` udfyldes.

**Mappe** (`{"folder": {…}}`, kun til at læse): `id`, `name`, `parentId` (overmappen; mangler på øverste niveau) og `auth`.

## Fejl og hvad du gør

Ved `run` står de samme tekster som `error` i et trins `step.finished`, fx når en hemmelighed eller et token mangler på et workflow-trin. Et trin med egen auth finder sine hemmeligheder under `id` i trinnets `request`, og et trin med `Inherit` under workflowets id. Mangler trinnets `id`, eller er hemmeligheden aldrig skrevet under Auth i appen og gemt, så bed brugeren gøre det.

| Fejl (`error`) | Gør |
|---|---|
| `Invalid tool arguments: …` | Parametrene passer ikke sammen eller har en ugyldig værdi, fx både `name` og `count`, både `run` og `last`, både `json` og `text`, `step` uden `run` eller `last`, `count` under 1 eller sammen med `run` eller `last`, en header, der er `null`, eller `update` uden `content`. Resten af teksten siger hvad. Ret dem |
| `An error occurred invoking '<værktøj>'.` | Tekst uden JSON fra MCP-serveren selv: et påkrævet parameter mangler, et parameter har forkert type, eller en værdi findes ikke, fx en ukendt `kind`. Tjek parametrene |
| `Step could not be found.` | `log` med `step`: kørslen har intet afsluttet trin med det `index`. Se `steps` i `run.finished` |
| `Saved request could not be loaded.` | Id'et eller stien findes ikke. Brug `list`, og brug et id derfra |
| `Saved request name is ambiguous. Use its id.` | Flere requests har stien. Brug id'et fra `list` |
| `Saved request file is not valid.` | Den gemte request er ugyldig JSON. `file`, `path` og `line` viser hvor. Fortæl brugeren det, og åbn ikke filen |
| `Selected environment was not found.` | Miljøet findes ikke. Spørg brugeren om det rigtige navn |
| `Environment settings could not be read.` | Bed brugeren åbne Hoboman og tjekke miljøerne |
| `Fetch a new OAuth token in Hoboman before sending this request.` | Tokenet kunne ikke hentes. Tokens gemmes pr. miljø, så bed brugeren vælge det miljø, du bruger, i appen og hente et token med refresh-knappen ved Auth, **Hent token** eller **Saved credentials…** på requesten, mappen, workflowet eller trinnet. **Hent token** i Credentials-vinduet gemmer ikke et token til requests. Ved client credentials mangler client secret typisk: bed brugeren udfylde den under Auth og gemme |
| `Required authentication secret is missing.` | Bed brugeren udfylde auth på requesten, mappen, workflowet eller workflow-trinnet i Hoboman og gemme |
| `Network request failed.` / `Request timed out.` | Serveren kunne ikke nås eller svarede ikke inden for 100 sekunder. Fortæl det, og prøv højst én gang til efter aftale |
| `Invalid request URL.` / `Invalid request input.` | Tjek URL, metode, headers og variabler. En header skal være `Navn: Værdi`. Et ukendt `{{navn}}` bliver stående i URL'en |
| `Input could not be read.` | En af Hobomans egne filer kunne ikke læses. Fortæl brugeren det |
| `Request body could not be encoded.` | Bodyen kunne ikke Base64-encodes, fx fordi den ikke er gyldig JSON eller mangler et markeret felt. Bed brugeren rette requesten eller trinnet i Hoboman |
| `Request was cancelled.` | Kaldet blev afbrudt. Afbryder dit AI-program selv kaldet, får du intet svar, og kaldet gemmes ikke i historikken |
| `Request failed.` | En anden fejl. Fortæl brugeren det |
| `Output file must be a plain file name.` | Giv `out` som et filnavn alene, fx `faktura.pdf`. Intet er sendt |
| `Output file already exists.` | Filen i `out` findes. Intet er sendt. Vælg et andet navn |
| `Output file could not be written.` | Filen i `out` kunne ikke skrives. Tjek stien; mappen skal findes. Kaldet er sendt |
| `Hoboman's own files cannot be used.` | `out` peger ind i Hobomans mappe. Det må du ikke; se reglen øverst |
| `Workflow could not be loaded.` | Workflowet findes ikke. Brug `list` med `kind` `workflows`, og brug et id derfra |
| `Workflow name is ambiguous. Use its id.` | Flere workflows har navnet. Brug id'et |
| `Invalid name.` | Et navn må ikke være tomt, kun mellemrum eller have kontroltegn som linjeskift og tabulator |
| `Target could not be found.` / `Folder could not be found.` | Ret målet. Find id'er med `list` |
| `Target is ambiguous. Use its id.` / `Folder is ambiguous. Use its id.` | Flere har stien eller navnet. Brug id'et |
| `A folder cannot be moved into itself.` / `A workflow cannot be moved.` / `An environment cannot be moved.` | Flytningen er ikke mulig. Fortæl brugeren det |
| `Deleting needs confirmation.` | Den forventede forhåndsvisning fra `delete` uden `confirm`. Fortæl brugeren, hvad der ville blive slettet (`path`, `folders` med mappen selv, `requests`), og brug kun `confirm`, hvis brugeren siger ja |
| `File is not valid.` | Filen er ugyldig JSON. `file`, `path` og `line` viser hvor. Fortæl brugeren det, og åbn ikke filen |
| `The change could not be saved.` | En fil er låst eller kunne ikke skrives. Prøv igen senere, eller fortæl brugeren det |
| `Workflow file is not valid.` | Workflowets fil er ugyldig. `file`, `path` og `line` viser hvor. Fortæl brugeren det, og åbn ikke filen |
| `Workflow cannot run.` | Se `problems` nedenfor. Det samme gælder `check` |
| `Input is not valid.` | `content` til `update` passer ikke til formatet. `path` og `line` viser hvor. Ret det, og prøv igen |
| `Use rename to change the name.` / `Use move to change the folder.` / `Target is not the one in the input.` | `update` ændrer ikke id, navn eller mappe. Brug `show`-resultatet uændret i de felter, og `rename` eller `move` |
| `Step id is not one of the workflow's.` | Et trin har et id, workflowet ikke havde, eller to trin har det samme. Fjern `id` fra nye trin |
| `Invalid script name.` | Et navn i `scripts` skal være et filnavn, der ender på `.js` |
| `A folder cannot be updated.` | En mappes auth ændres i appen |
| `Environment name is taken.` | Et andet miljø har navnet, også med andre store og små bogstaver. Vælg et andet |
| `A script a step uses cannot be deleted.` | Fjern først trinnet, eller giv det et andet script |
| `Call could not be found.` | Navnet er ikke i historikken. Brug et navn fra `history` |
| `Target could not be read.` | Det, `show` skulle vise, kunne ikke læses, fx fordi det lige er slettet. Fortæl brugeren det |
| `Workflow could not be found.` / `Run could not be found.` / `Run could not be read.` | `log` fandt ikke workflowet eller kørslen. Brug id'er fra `list` og `log` |

**`problems` ved `Workflow cannot run.`** Hvert problem har `kind`, `step` (trinnets index, hvis det hører til et trin) og `detail` (et navn, en sti eller et id, aldrig en værdi).

| `kind` | Gør |
|---|---|
| `MissingParameter` | Giv parameteren i `detail` i `parameters` |
| `UnknownParameter` | Fjern eller ret parameteren. Se `parameters` i `show` |
| `UsedBeforeSaved` | Et trin bruger en variabel, før et tidligere trin har gemt den. Workflowet skal rettes |
| `UnknownName` | Navnet findes hverken i workflowet eller i miljøet. Tjek, at det rigtige miljø er brugt (`environment`). Ellers skal workflowet eller miljøet rettes |
| `MissingUrl`, `InvalidName`, `DuplicateName`, `NotAVariable`, `InvalidSource`, `MixedStep`, `InvalidDelay`, `InvalidRetry`, `InvalidOutput` | Workflowet er sat forkert op. Har du selv skrevet det, så ret det; ellers bed brugeren rette det i Hoboman |
| `ScriptNotFound`, `InvalidScript` | Et script-trins script mangler i `scripts` eller har en syntaksfejl (`detail` viser fil og linje). Ret det med `update`, hvis du selv har skrevet det; ellers bed brugeren rette det |

Et trin, hvor en værdi i `saves` mangler i svaret, fejler med `error` = `Nothing to save was found at <sti>.` Svaret havde ikke den forventede form. Værdier gemmes kun efter et 2xx-svar.

Et trin med `retry`, hvis svar aldrig blev klar, fejler med det sidste svar, og `attempts` viser antallet af forsøg. Passede `until`-værdien ikke, er `error` `The answer was not ready after <n> attempts.`: det, API'et lavede, blev ikke færdigt i tide. Ellers er det sidste svars `status` eller `error` som ved et almindeligt trin. Fortæl brugeren, hvad det sidste svar sagde, fx en status. Er `error` `Stopped as <sti> was <værdi>.`, stoppede trinnet med det samme, fordi API'et svarede, at det, der ventes på, er fejlet. Det er ikke en timeout.

Et script-trin har `JS` som `method`, `status` 200, en tom `address` og scriptets filnavn som `name`, hvis det intet navn har. Fejler det, er `error` fil, linje og scriptets fejltekst, fx `map.js:2: No order`. Returnerer det intet, mens trinnet har `saves`, er `error` `map.js returned nothing to save.` Returnerer det ikke en tekst, mens trinnet har et andet `output` end `Json`, er `error` fx `html.js must return text when its output is Html.` Et ventetrin har `WAIT` som `method`, en tom `address` og `Wait <n> seconds` som `name`, hvis det intet navn har. Et request-trin uden navn har metoden og adressen uden skema og query som `name`, fx `GET dummyjson.com/products/{{id}}`, med `{{navne}}` uudfyldt; `address` er udfyldt.

## Godt at vide

- Kald med `send_saved` og `send` gemmes i Hobomans historik, som `history` læser, og vises i appen med mærket **CLI**. Kald, der fejler, før de bygges, fx med et ukendt miljø, og kald, der afbrydes, gemmes ikke. Trin i et workflow gemmes kun i kørslerne, som `log` læser. Historikken og kørslerne er ikke krypteret.
- Client-credentials-tokens hentes automatisk, når de mangler, er udløbet, eller serveren svarer 401, både ved `send_saved` og i et workflow-trin. Det kræver en gemt client secret. Authorization-code-tokens kræver, at brugeren logger ind i appen.
- Hele svaret holdes i hukommelsen, og `body` er tekst. Gem binære svar med `out`.
- Cookies bruges ikke, redirects følges, og et kald venter højst 100 sekunder.
- `check` tjekker et workflow uden at sende, men et enkelt trin kan ikke køres for sig.
