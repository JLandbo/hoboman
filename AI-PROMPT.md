# Hoboman for AI-agenter

## Hvad en AI kan i Hoboman

En AI arbejder med Hoboman udefra, gennem kommandolinjeprogrammet `hoboman-cli.exe` og filerne i Hobomans datamappe. Appen har ingen API, så en AI kan ikke trykke på knapper, skifte det valgte miljø, se ugemte ændringer eller logge ind i en browser. Appen behøver ikke at være åben, men er den det, ser du det, AI'en laver, med det samme.

| AI'en vil | Sådan | Det sker |
|---|---|---|
| Finde requests | `hoboman-cli list` | Stierne til de gemte requests. Intet skrives |
| Finde workflows og deres parametre | Læse `workflows\<navn>\workflow.json` | Intet skrives |
| Sende en gemt request | `hoboman-cli send <sti>` | Ét kald med requestens egen auth eller mappens. Kaldet står i **Historik** med mærket **CLI** |
| Sende et direkte kald | `hoboman-cli send <METODE> <url>` | Ét kald uden auth ud over de headers, AI'en giver |
| Køre et workflow | `hoboman-cli run <navn>` | Alle trin i rækkefølge. Hvert trin skrives som en JSON-linje, mens det kører, og i `runs\` |
| Følge en kørsel, du startede i appen | Læse den nyeste fil i `runs\<workflow-id>\` | Intet skrives |
| Bygge eller rette et workflow, når du beder om det | Skrive `workflow.json` og `.js`-filer i `workflows\<navn>\` | Workflowet står i appen med det samme |
| Stoppe en kørsel | Ctrl+C | Kørslen slutter som `Cancelled` |

Det gør du selv i appen:

- **Hemmeligheder.** Passwords, tokens og client secrets skrives under Auth i appen og gemmes krypteret for din Windows-bruger. En AI kan hverken læse eller skrive dem.
- **Login med authorization code.** Hent tokenet med refresh-knappen ved Auth, **Hent token** eller **Saved credentials…**, med det miljø valgt, som AI'en bruger. Client-credentials-tokens henter CLI'et selv.
- **Binære svar.** CLI'et giver kun tekst, så en PDF eller et billede gemmes med **Gem** i svaret i appen.

En kørsel med `run` vises ikke i appens workflow-editor; dér vises kun det, der køres fra editoren. Se [CLI.md](CLI.md) for alle detaljer.

## Instruktionen

Kopiér teksten under stregen ind som instruktion til din AI. Udskift `C:\sti\til\Hoboman\publish` med den rigtige mappe.

---

Du kan sende API-kald og køre workflows gennem **Hoboman**, et API-værktøj på brugerens Windows-maskine, med kommandolinjeprogrammet `hoboman-cli.exe`.

## Opsætning

- Programmet ligger i `C:\sti\til\Hoboman\publish\hoboman-cli.exe`. Kald det altid med fuld sti.
- Data ligger i samme mappe som programmet: `requests\`, `request-order.json`, `workflows\`, `runs\`, `history\`, `logs\`, `themes\`, `environments.json`, `credentials.json`, `settings.json`, `secrets.json` og `pending-secret-cleanup.json`. Det er uden betydning, hvilken mappe du står i.
- Hoboman-appen behøver ikke at være åben. Er den det, viser den dine kald og de filer, du skriver, med det samme.
- Kører du i Windows PowerShell 5.1, så sæt begge dele før kald. Ellers bliver æ, ø og å forkerte, eller til `?` uden fejl:

  ```powershell
  [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)  # det, du læser fra hoboman-cli
  $OutputEncoding = [System.Text.UTF8Encoding]::new($false)            # det, du piper ind i hoboman-cli
  ```

  Brug `-Encoding UTF8`, når du læser filer.

## Regler

1. **Rør ikke Hobomans datafiler.** Du må ikke oprette, ændre eller slette noget i `requests\`, `workflows\` eller `environments.json`, medmindre brugeren udtrykkeligt beder om det. `secrets.json`, `credentials.json`, `settings.json`, `request-order.json` og `pending-secret-cleanup.json` skriver du aldrig i.
2. **Læs altid exitkoden** (`$LASTEXITCODE`), før du bruger outputtet. stdout og stderr er JSON, bortset fra `list`, `--help` og `--version`, der skriver tekst.
3. **Giv følsomme værdier via stdin** med `--vars -` eller `--params -`, aldrig som `--var`/`--param` på kommandolinjen, så de ikke havner i shellens historik. `run` skriver dog parametre og variabler i klartekst på stdout og i run-loggen, så giv kun en hemmelighed som parameter, når brugeren har bedt om det.
4. **Gentag ikke hemmeligheder** som tokens, passwords og API-nøgler i dine svar til brugeren. Opsummér i stedet, fx "login lykkedes, token gemt".
5. **Send ikke kald med sideeffekter** (POST, PUT, PATCH, DELETE eller workflows, der ændrer data), uden at brugeren har bedt om netop det. Der er ingen tørkørsel: `run` sender, så snart workflowet er tjekket.
6. **Gæt ikke.** Findes en request, et workflow, et miljø eller en parameter ikke, så fortæl brugeren det, og spørg.

## Kommandoer

```text
hoboman-cli list
hoboman-cli send <gemt request> [--env <miljø>] [--var <navn=værdi>]... [--vars <fil|->]
hoboman-cli send <METODE> <url> [--env <miljø>] [-H "Navn: Værdi"]... [--json <tekst|@fil> | --text <tekst|@fil>] [--var <navn=værdi>]... [--vars <fil|->]
hoboman-cli run <workflow> [--env <miljø>] [--param <navn=værdi>]... [--params <fil|->]
hoboman-cli --help
```

- `list` skriver de gemte requests som stier, én pr. linje, fx `Shop/Login`. Brug netop den sti med `send`. Vil du vide, hvad en request gør, før du sender den, så læs `requests\<sti>.json`; en mappes auth står i `.folder.json` i mappen.
- `--env` skal være miljøets navn præcis, også store og små bogstaver. Miljøernes navne er `name` i `environments.json`. Læs kun navnene, for værdierne er ikke krypteret.
- Uden `--env` bruges det miljø, brugeren har valgt i appen. Har brugeren intet valgt, bruges intet miljø, og `{{navne}}` udfyldes ikke.
- En gemt request sendes med sine egne headers, body og auth. Du kan kun ændre dens `{{variabler}}` med `--var`/`--vars`.
- Et direkte kald (`send METODE url`) har ingen auth ud over de headers, du giver det. `{{navne}}` udfyldes i URL og headers, men ikke i bodyen.
- `--json @fil.json` sender en body fra en fil. Brug det altid i Windows PowerShell 5.1, der fjerner anførselstegnene i en JSON-tekst på kommandolinjen.

## Output fra `send`

Et svar skrives som én JSON-linje på stdout, også ved 4xx og 5xx:

```json
{"status":200,"reason":"OK","elapsedMs":123,"size":17,"headers":[{"name":"Content-Type","value":"application/json"}],"body":"{\"id\":\"42\"}"}
```

`body` er tekst. Er den JSON, skal den parses en gang til.

| Exitkode | Betydning |
|---|---|
| 0 | Svar med status 2xx |
| 1 | Svar med en anden status. Læs `status` og `body` |
| 2 | Intet svar. Fejlen står som `{"error":"..."}` på stderr, og stdout er tom |

## Workflows

Et workflow er en række trin, der hver sender sin egen request, kører et script eller venter et antal sekunder. Værdier fra et svar, fx et token, gemmes i variabler, som de næste trin bruger.

**Find workflows og deres parametre.** Der er ingen `list`-kommando for workflows.
- Hvert workflow er en mappe `workflows\<navn>\` med en `workflow.json`. Mappens navn er det, du giver til `run`.
- Læs `parameters` i `workflow.json`. En parameter uden `default` er påkrævet og skal gives med `--param navn=værdi` eller `--params`.
- `--param` giver altid tekst. Skal en værdi være et tal eller et objekt, så brug `--params` med et JSON-objekt.
- Hvert trin har præcis én af `request` (metode, URL, query, headers, body og auth), `script` (en `.js`-fil i mappen) og `delaySeconds`. Et trin med `"auth": { "kind": "Inherit" }` bruger workflowets fælles `auth`; uden `auth` sender trinnet ingen. Hemmeligheder til auth ligger ikke i filen.
- `run` bruger filerne, som de er gemt. Ugemte ændringer i appen ses ikke.

**Kør et workflow:**

```powershell
$cli = "C:\sti\til\Hoboman\publish\hoboman-cli.exe"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$lines = & $cli run Ordre-sync --env Demo --param orderId=o-17
$code = $LASTEXITCODE
```

Med parametre fra stdin:

```powershell
@{ orderId = 'o-17'; pageSize = 50 } | ConvertTo-Json | & $cli run Ordre-sync --params -
```

**Events.** `run` skriver én JSON-linje pr. event på stdout, mens kørslen sker. Der er altid kun én kørsel pr. kald.

| `type` | Indhold |
|---|---|
| `run.started` | Første linje. `runId`, `workflowId`, `workflow`, `environment`, `runFile` (fuld sti til kørslens logfil) og `parameters` |
| `step.started` | `index` (første trin er 0), `name` (trinnets navn), `method`, `address` |
| `step.finished` | `outcome` (`Succeeded`/`Failed`), `status`, `reason`, `elapsedMs`, `size`, `attempts` (ved `retry`), `error` (ved fejl), `saved` (gemte værdier), `headers`, `body` |
| `step.retrying` | Et trin med `retry` fik et svar, der ikke var klar, og prøves igen: `index`, `attempt`, `status`, `value` eller `error`. Vent på `step.finished` |
| `step.skipped` | Et trin, der ikke blev kørt, fordi et tidligere trin fejlede, eller kørslen blev afbrudt |
| `step.cancelled` | Trinnet, der kørte, da kørslen blev afbrudt |
| `run.finished` | Sidste linje. `outcome` (`Succeeded`/`Failed`/`Cancelled`), `elapsedMs`, `steps` (`index`, `outcome`, `status`) og `variables` (parametre og variabler, der fik en værdi) |

- Små felter står først i hver linje, og `headers` og `body` sidst. En linje kan være stor, fordi hele bodyen er med.
- Værdier er ægte JSON, så et tal er et tal og et objekt et objekt.
- Ignorér felter og event-typer, du ikke kender.
- Mangler `run.finished`, blev kørslen stoppet undervejs. Stilhed betyder ikke succes.

| Exitkode | Betydning for `run` |
|---|---|
| 0 | Alle trin lykkedes. Brug `variables` i `run.finished` som resultat |
| 1 | Kørslen startede, men et trin fejlede, eller den blev afbrudt. Læs `outcome` i `run.finished` først. Ved `Failed` så find `step.finished` med `"outcome":"Failed"`, og rapportér trinnets `name`, `status`, `error` og et uddrag af `body`. Ved `Cancelled` er der intet fejlet trin |
| 2 | Kørslen startede ikke. Intet er sendt. Fejlen står som JSON på stderr |

**Læs et resultat:**

```powershell
if ($code -eq 2) { throw "Workflowet startede ikke." }
$events = $lines | ForEach-Object { $_ | ConvertFrom-Json }
$finished = $events[-1]
if ($finished.type -ne 'run.finished') { throw "Kørslen blev stoppet undervejs." }
switch ($finished.outcome) {
    'Succeeded' { $finished.variables }
    'Cancelled' { "Kørslen blev afbrudt." }
    'Failed' {
        $failed = $events | Where-Object { $_.type -eq 'step.finished' -and $_.outcome -eq 'Failed' } | Select-Object -First 1
        $started = $events | Where-Object { $_.type -eq 'step.started' -and $_.index -eq $failed.index }
        "Trin $($failed.index) ($($started.name)) fejlede: $($failed.status) $($failed.error)"
    }
}
```

**Lange kørsler.**
- Start `run` som en baggrundsopgave, og læs `runFile` fra første linje.
- Eller følg filen live: `Get-Content -LiteralPath $runFile -Wait -Encoding UTF8`. Stop ved `run.finished`.
- Kørsler startet i appen ligger også i `runs\<workflowId>\`. Id'et står i `workflow.json`. Den nyeste fil er den seneste kørsel.
- Ctrl+C afbryder pænt med `run.finished` `Cancelled`. Dræbes processen, fx når en baggrundsopgave når sin tidsgrænse, kommer der ingen `run.finished`.

## Byg et workflow (kun når brugeren beder om det)

- Skriv `workflows\<navn>\workflow.json` som UTF-8. Navnet må ikke indeholde `/`, starte med `.` eller slutte med `.` eller mellemrum. Scripts ligger som `.js`-filer i samme mappe.
- Giv workflowet et nyt `id`, fx fra `[guid]::NewGuid()`. Kopiér aldrig et `id` fra et andet workflow, en request eller et trin.
- Hvert navn, et trin gemmer i, skal stå i `variables`. Et eksisterende workflow er et godt forlæg, og hele formatet står i Hobomans `CLI.md`. Et lille eksempel:

  ```json
  {
    "id": "8cfcb72a-66ca-4f32-830a-cd886d8c478e",
    "parameters": [ { "name": "username", "default": "emilys" }, { "name": "password" } ],
    "variables": [ { "name": "token" }, { "name": "newCart" } ],
    "steps": [
      { "name": "Log ind",
        "request": { "method": "POST", "url": "{{baseUrl}}/auth/login", "bodyKind": "Json",
                     "body": "{\"username\": \"{{username}}\", \"password\": \"{{password}}\"}" },
        "saves": [ { "variable": "token", "from": "$.accessToken" } ] },
      { "name": "Byg ny kurv", "script": "byg-kurv.js", "saves": [ { "variable": "newCart", "from": "$.cart" } ] },
      { "name": "Vent", "delaySeconds": 2 },
      { "name": "Opret kurv",
        "request": { "method": "POST", "url": "{{baseUrl}}/carts/add", "bodyKind": "Json", "body": "{{newCart}}",
                     "headers": [ { "name": "Authorization", "value": "Bearer {{token}}" } ] } }
    ]
  }
  ```

- Skriv egenskaberne i camelCase og enums som tekst, fx `"Json"`, `"Inherit"`, `"OAuth2"`. En ukendt eller stavet forkert egenskab giver `Workflow file is not valid.` med fil, sti og linje.
- Skriv ikke `id` i trinenes `request`; appen giver dem et, når brugeren gemmer workflowet. Skal et trin have auth med en hemmelighed, så bed brugeren udfylde den under Auth i appen og gemme. Auth kan også gives som en header med et token fra et tidligere trin, fx `Authorization: Bearer {{token}}`.
- Et script læser `vars.navn`, som ikke kan ændres, og returnerer det, trinnet gemmer fra med `saves`, fx `"from": "$.cart"`. Det kører som strict JavaScript uden filer, netværk, .NET og `eval` og stoppes efter 5 sekunder.
- Appen viser workflowet med det samme. Har brugeren ugemte ændringer i det samme workflow, genindlæses det ikke, og brugerens næste gem overskriver din fil, så sig til, når du har skrevet. Når brugeren gemmer, skriver appen `variables` ud fra trinenes `saves`.
- Vil du teste workflowet, så spørg først: `run` sender med det samme.
- Slet ikke workflow-mapper selv. Slettes et workflow i appen, slettes dets hemmeligheder også.

## Fejl og hvad du gør

Ved `run` står de samme tekster som `error` i et trins `step.finished` (exitkode 1), fx når en hemmelighed eller et token mangler på et workflow-trin. Et trin med egen auth finder sine hemmeligheder under `id` i trinnets `request`, og et trin med `Inherit` under workflowets `id`. Mangler `id`, eller er hemmeligheden aldrig skrevet under Auth i appen og gemt, så bed brugeren gøre det.

| Fejl (stderr `error`) | Gør |
|---|---|
| `Saved request could not be loaded.` | Stien findes ikke. Kør `list`, og brug en sti derfra |
| `Saved request file is not valid.` | Den gemte request er ugyldig JSON. `file`, `path` og `line` viser hvor. Fortæl brugeren det |
| `Selected environment was not found.` | Miljøet findes ikke. Spørg brugeren om det rigtige navn |
| `Environment settings could not be read.` | Bed brugeren åbne Hoboman og tjekke miljøerne |
| `Fetch a new OAuth token in Hoboman before sending this request.` | Tokenet kunne ikke hentes. Tokens gemmes pr. miljø, så bed brugeren vælge det miljø, du bruger, i appen og hente et token med refresh-knappen ved Auth, **Hent token** eller **Saved credentials…** på requesten, mappen, workflowet eller trinnet. **Hent token** i Credentials-vinduet gemmer ikke et token til requests. Ved client credentials mangler client secret typisk: bed brugeren udfylde den under Auth og gemme |
| `Required authentication secret is missing.` | Bed brugeren udfylde auth på requesten, mappen, workflowet eller workflow-trinnet i Hoboman og gemme |
| `Network request failed.` / `Request timed out.` | Serveren kunne ikke nås eller svarede ikke inden for 100 sekunder. Fortæl det, og prøv højst én gang til efter aftale |
| `Invalid request URL.` / `Invalid request input.` | Tjek URL, metode, headers og variabler. Et ukendt `{{navn}}` bliver stående i URL'en |
| `Input could not be read.` | En fil kunne ikke læses, typisk `@fil`, `--vars fil` eller `--params fil`. Tjek stien. En relativ sti læses fra den mappe, du står i |
| `Request body could not be encoded.` | Bodyen kunne ikke Base64-encodes, fx fordi den ikke er gyldig JSON eller mangler et markeret felt. Bed brugeren rette requesten eller trinnet i Hoboman |
| `Request was cancelled.` | Kaldet blev afbrudt med Ctrl+C |
| `Request failed.` | En anden fejl. Fortæl brugeren det |
| `Invalid command arguments. Use --help for usage.` | Ret kommandoen. `--env`, `--json`, `--text`, `--vars` og `--params` må kun gives én gang |
| `Invalid variable input.` / `Invalid parameter input.` | `--var`/`--param` skal være `navn=værdi`, og `--vars`/`--params` et JSON-objekt. `-` kræver, at der pipes noget ind |
| `Workflow could not be loaded.` | Workflowet findes ikke. Se mapperne i `workflows\` |
| `Workflow file is not valid.` | `workflow.json` er ugyldig. `file`, `path` og `line` viser hvor. Fortæl brugeren det |
| `Workflow cannot run.` | Se `problems` nedenfor |

**`problems` ved `Workflow cannot run.`** Hvert problem har `kind`, `step` (trinnets index, hvis det hører til et trin) og `detail` (et navn, en sti eller et id, aldrig en værdi).

| `kind` | Gør |
|---|---|
| `MissingParameter` | Giv parameteren i `detail` med `--param` eller `--params` |
| `UnknownParameter` | Fjern eller ret parameteren. Se `parameters` i `workflow.json` |
| `UsedBeforeSaved` | Et trin bruger en variabel, før et tidligere trin har gemt den. Workflowet skal rettes |
| `UnknownName` | Navnet findes hverken i workflowet eller i det valgte miljø. Tjek, at det rigtige miljø er valgt (`--env`). Ellers skal workflowet eller miljøet rettes |
| `MissingId`, `MissingUrl`, `InvalidName`, `DuplicateName`, `NotAVariable`, `InvalidSource`, `MixedStep`, `InvalidDelay`, `InvalidRetry` | Workflowet er sat forkert op. Har du selv skrevet det, så ret det; ellers bed brugeren rette det i Hoboman |
| `ScriptNotFound`, `InvalidScript` | Et script-trins `.js`-fil mangler eller har en syntaksfejl (`detail` viser fil og linje). Ret den, hvis du selv har skrevet den; ellers bed brugeren rette den |

Et trin, hvor en værdi i `saves` mangler i svaret, fejler med `error` = `Nothing to save was found at <sti>.` Svaret havde ikke den forventede form. Værdier gemmes kun efter et 2xx-svar.

Et trin med `retry`, hvis svar aldrig blev klar, fejler med det sidste svar, og `attempts` viser antallet af forsøg. Passede `until`-værdien ikke, er `error` `The answer was not ready after <n> attempts.`: det, API'et lavede, blev ikke færdigt i tide. Ellers er det sidste svars `status` eller `error` som ved et almindeligt trin. Fortæl brugeren, hvad det sidste svar sagde, fx en status. Er `error` `Stopped as <sti> was <værdi>.`, stoppede trinnet med det samme, fordi API'et svarede, at det, der ventes på, er fejlet. Det er ikke en timeout.

Et script-trin har `JS` som `method`, en tom `address` og scriptets filnavn som `name`, hvis det intet navn har. Fejler det, er `error` fil, linje og scriptets fejltekst, fx `map.js:2: No order`. Returnerer det intet, mens trinnet har `saves`, er `error` `map.js returned nothing to save.` Et ventetrin har `WAIT` som `method`, en tom `address` og `Wait <n> seconds` som `name`, hvis det intet navn har.

## Godt at vide

- Kald med `send` gemmes i Hobomans historik og vises i appen med mærket **CLI**. Kald, der fejler, før de bygges, fx med forkerte argumenter eller et ukendt miljø, og kald, der afbrydes, gemmes ikke. Trin i et workflow gemmes kun i `runs\`. Historikken og `runs\` er ikke krypteret.
- Client-credentials-tokens hentes automatisk, når de mangler, er udløbet, eller serveren svarer 401, både ved `send` og i et workflow-trin. Det kræver en gemt client secret, og tokenet skrives i `secrets.json`. Authorization-code-tokens kræver, at brugeren logger ind i appen.
- Hele svaret holdes i hukommelsen, og bodies er tekst. Der er ingen binære downloads; bed brugeren gemme et binært svar med **Gem** i svaret i appen.
- Cookies bruges ikke, redirects følges, og et kald venter højst 100 sekunder.
- Der er ingen tørkørsel, og et enkelt trin kan ikke køres for sig.
