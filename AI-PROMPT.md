# Hoboman for AI-agenter

Kopiér teksten under stregen ind som instruktion til din AI. Udskift `C:\sti\til\Hoboman\publish` med den rigtige mappe.

---

Du kan sende API-kald og køre workflows gennem **Hoboman**, et API-værktøj på brugerens Windows-maskine, med kommandolinjeprogrammet `hoboman-cli.exe`.

## Opsætning

- Programmet ligger i `C:\sti\til\Hoboman\publish\hoboman-cli.exe`. Kald det altid med fuld sti.
- Data ligger i samme mappe som programmet: `requests\`, `workflows\`, `runs\`, `history\`, `environments.json`, `settings.json` og `secrets.json`. Det er uden betydning, hvilken mappe du står i.
- Kører du i Windows PowerShell 5.1, så sæt begge dele før kald. Ellers bliver æ, ø og å forkerte, eller til `?` uden fejl:

  ```powershell
  [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)  # det, du læser fra hoboman-cli
  $OutputEncoding = [System.Text.UTF8Encoding]::new($false)            # det, du piper ind i hoboman-cli
  ```

  Brug `-Encoding UTF8`, når du læser filer.

## Regler

1. **Rør ikke Hobomans datafiler.** Du må ikke oprette, ændre eller slette noget i `requests\`, `workflows\`, `environments.json`, `settings.json` eller `secrets.json`, medmindre brugeren udtrykkeligt beder om det. Workflows og requests sættes op af brugeren i appen.
2. **Læs altid exitkoden** (`$LASTEXITCODE`), før du bruger outputtet. stdout og stderr er JSON, bortset fra `list`, der skriver stierne som tekst.
3. **Giv følsomme værdier via stdin** med `--vars -` eller `--params -`, aldrig som `--var`/`--param` på kommandolinjen, så de ikke havner i shellens historik. `run` skriver dog parametre og variabler i klartekst på stdout og i run-loggen, så giv kun en hemmelighed som parameter, når brugeren har bedt om det.
4. **Gentag ikke hemmeligheder** som tokens, passwords og API-nøgler i dine svar til brugeren. Opsummér i stedet, fx "login lykkedes, token gemt".
5. **Send ikke kald med sideeffekter** (POST, PUT, PATCH, DELETE eller workflows, der ændrer data), uden at brugeren har bedt om netop det.
6. **Gæt ikke.** Findes en request, et workflow, et miljø eller en parameter ikke, så fortæl brugeren det, og spørg.

## Kommandoer

```text
hoboman-cli list
hoboman-cli send <gemt request> [--env <miljø>] [--var <navn=værdi>]... [--vars <fil|->]
hoboman-cli send <METODE> <url> [--env <miljø>] [-H "Navn: Værdi"]... [--json <tekst|@fil>] [--text <tekst|@fil>] [--var <navn=værdi>]... [--vars <fil|->]
hoboman-cli run <workflow> [--env <miljø>] [--param <navn=værdi>]... [--params <fil|->]
```

- `list` skriver de gemte requests som stier, én pr. linje, fx `Shop/Login`. Brug netop den sti med `send`.
- Uden `--env` bruges det miljø, brugeren har valgt i appen.
- En gemt request sendes med sine egne headers, body og auth. Du kan kun ændre dens `{{variabler}}` med `--var`/`--vars`.
- Et direkte kald (`send METODE url`) har ingen auth ud over de headers, du giver det.
- `--json @fil.json` sender en body fra en fil, hvilket er godt til store bodies.

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
- Hvert trin har sin `request` (metode, URL, query, headers, body og auth) direkte i `workflow.json`. Et trin med `"auth": { "kind": "Inherit" }` bruger workflowets fælles `auth`, ellers har det sin egen. Hemmeligheder til auth ligger ikke i filen.

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
| `step.skipped` | Et trin, der ikke blev kørt, fordi et tidligere trin fejlede |
| `step.cancelled` | Trinnet, der kørte, da kørslen blev afbrudt |
| `run.finished` | Sidste linje. `outcome` (`Succeeded`/`Failed`/`Cancelled`), `elapsedMs`, `steps` (`index`, `outcome`, `status`) og `variables` (slutværdier) |

- Små felter står først i hver linje, og `headers` og `body` sidst. En linje kan være stor, fordi hele bodyen er med.
- Værdier er ægte JSON, så et tal er et tal og et objekt et objekt.
- Ignorér felter og event-typer, du ikke kender.
- Mangler `run.finished`, blev kørslen stoppet undervejs. Stilhed betyder ikke succes.

| Exitkode | Betydning for `run` |
|---|---|
| 0 | Alle trin lykkedes. Brug `variables` i `run.finished` som resultat |
| 1 | Kørslen startede, men et trin fejlede, eller den blev afbrudt. Find `step.finished` med `"outcome":"Failed"`, og rapportér trinnets `name`, `status`, `error` og et uddrag af `body` |
| 2 | Kørslen startede ikke. Intet er sendt. Fejlen står som JSON på stderr |

**Læs et resultat:**

```powershell
if ($code -eq 2) { throw "Workflowet startede ikke." }
$events = $lines | ForEach-Object { $_ | ConvertFrom-Json }
$finished = $events[-1]
if ($code -eq 0) { $finished.variables }
elseif ($code -eq 1) {
    $failed = $events | Where-Object { $_.type -eq 'step.finished' -and $_.outcome -eq 'Failed' } | Select-Object -First 1
    $started = $events | Where-Object { $_.type -eq 'step.started' -and $_.index -eq $failed.index }
    "Trin $($failed.index) ($($started.name)) fejlede: $($failed.status) $($failed.error)"
}
```

**Lange kørsler.**
- Start `run` som en baggrundsopgave, og læs `runFile` fra første linje.
- Eller følg filen live: `Get-Content -LiteralPath $runFile -Wait -Encoding UTF8`. Stop ved `run.finished`.
- Kørsler startet i appen ligger også i `runs\<workflowId>\`. Id'et står i `workflow.json`. Den nyeste fil er den seneste kørsel.

## Fejl og hvad du gør

Ved `run` står de samme tekster som `error` i et trins `step.finished` (exitkode 1), fx når en hemmelighed eller et token mangler på et workflow-trin. `run` finder kun hemmeligheder og tokens for trin, der er gemt i appen (trinnets `request` har et `id`), så bed brugeren gemme workflowet i Hoboman.

| Fejl (stderr `error`) | Gør |
|---|---|
| `Saved request could not be loaded.` | Stien findes ikke. Kør `list`, og brug en sti derfra |
| `Selected environment was not found.` | Miljøet findes ikke. Spørg brugeren om det rigtige navn |
| `Environment settings could not be read.` | Bed brugeren åbne Hoboman og tjekke miljøerne |
| `Fetch a new OAuth token in Hoboman before sending this request.` | Tokenet kunne ikke hentes. Ved authorization code kræver det login: bed brugeren hente et nyt token i Hoboman (Auth-fanen). Ved client credentials mangler client secret typisk: bed brugeren udfylde den under Auth på requesten, mappen, workflowet eller trinnet og gemme |
| `Required authentication secret is missing.` | Bed brugeren udfylde auth på requesten, mappen, workflowet eller workflow-trinnet i Hoboman og gemme |
| `Network request failed.` / `Request timed out.` | Serveren kunne ikke nås. Fortæl det, og prøv højst én gang til efter aftale |
| `Invalid request URL.` / `Invalid request input.` | Tjek URL, headers og variabler |
| `Input could not be read.` | En fil kunne ikke læses, typisk `@fil`, `--vars fil` eller `--params fil`. Tjek stien. En relativ sti læses fra den mappe, du står i |
| `Request body could not be encoded.` | Bodyen kunne ikke Base64-encodes, fx fordi den ikke er gyldig JSON eller mangler et markeret felt. Bed brugeren rette requesten eller trinnet i Hoboman |
| `Invalid command arguments. Use --help for usage.` | Ret kommandoen |
| `Invalid variable input.` / `Invalid parameter input.` | `--var`/`--param` skal være `navn=værdi`, og `--vars`/`--params` et JSON-objekt |
| `Workflow could not be loaded.` | Workflowet findes ikke. Se mapperne i `workflows\` |
| `Workflow file is not valid.` | `workflow.json` er ugyldig. `file`, `path` og `line` viser hvor. Fortæl brugeren det |
| `Workflow cannot run.` | Se `problems` nedenfor |

**`problems` ved `Workflow cannot run.`** Hvert problem har `kind`, `step` (trinnets index, hvis det hører til et trin) og `detail` (et navn, en sti eller et id, aldrig en værdi).

| `kind` | Gør |
|---|---|
| `MissingParameter` | Giv parameteren i `detail` med `--param` eller `--params` |
| `UnknownParameter` | Fjern eller ret parameteren. Se `parameters` i `workflow.json` |
| `UsedBeforeSaved` | Et trin bruger en variabel, før et tidligere trin har gemt den. Workflowet skal rettes i Hoboman |
| `UnknownName` | Navnet findes hverken i workflowet eller i det valgte miljø. Tjek, at det rigtige miljø er valgt (`--env`). Ellers skal workflowet eller miljøet rettes i Hoboman |
| `MissingId`, `MissingUrl`, `InvalidName`, `DuplicateName`, `NotAVariable`, `InvalidSource`, `MixedStep`, `InvalidDelay`, `InvalidRetry` | Workflowet er sat forkert op. Bed brugeren rette det i Hoboman |
| `ScriptNotFound`, `InvalidScript` | Et script-trins `.js`-fil mangler eller har en syntaksfejl (`detail` viser fil og linje). Bed brugeren rette den |

Et trin, hvor en værdi i `saves` mangler i svaret, fejler med `error` = `Nothing to save was found at <sti>`. Svaret havde ikke den forventede form.

Et trin med `retry`, hvis svar aldrig blev klar, fejler med det sidste svar, og `attempts` viser antallet af forsøg. Passede `until`-værdien ikke, er `error` `The answer was not ready after <n> attempts.` Ellers er det sidste svars `status` eller `error` som ved et almindeligt trin. Stoppede trinnet, fordi det, der ventes på, fejlede, er `error` `Stopped as <sti> was <værdi>.` Det, API'et ventede på, blev ikke færdigt i tide. Fortæl brugeren, hvad det sidste svar sagde, fx en status.

Et script-trin har `JS` som `method` og scriptets filnavn som `name`, hvis det intet navn har. Fejler det, er `error` fil, linje og scriptets fejltekst, fx `map.js:2: No order`.

## Godt at vide

- Hvert kald med `send` gemmes i Hobomans historik. Trin i et workflow gemmes kun i `runs\`. Historikken og `runs\` er ikke krypteret.
- Client-credentials-tokens hentes automatisk, når de mangler eller er udløbet, både ved `send` og i et workflow-trin. Authorization-code-tokens kræver, at brugeren logger ind i appen.
- Hele svaret holdes i hukommelsen, og bodies er tekst. Der er ingen binære downloads.
