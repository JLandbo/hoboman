# Hoboman for AI-agenter

## Hvad en AI kan i Hoboman

En AI arbejder med Hoboman udefra og kun gennem kommandolinjeprogrammet `hoboman-cli.exe`. Den læser og skriver aldrig selv i Hobomans datamappe. Appen har ingen API, så en AI kan ikke trykke på knapper, skifte det valgte miljø, se ugemte ændringer eller logge ind i en browser. Appen behøver ikke at være åben, men er den det, ser du det, AI'en laver, med det samme.

| AI'en vil | Sådan | Det sker |
|---|---|---|
| Finde requests | `hoboman-cli list` | Id og sti for hver gemt request. Intet skrives |
| Finde workflows og se deres parametre og trin | `hoboman-cli list workflows` og `show <workflow>` | Intet skrives |
| Se en request, en mappe eller et miljø | `hoboman-cli show <id/sti/navn>`, `list folders`, `list environments` | Intet skrives |
| Se tidligere kald fra appen og CLI'et | `hoboman-cli history` og `history <navn>` | Intet skrives |
| Oprette, rette, omdøbe eller slette et miljø, når du beder om det | `hoboman-cli new environment`, `show` og `update`, `rename`, `delete <…> --yes` | Ændringen står i appen med det samme |
| Tjekke et workflow uden at sende noget | `hoboman-cli check <workflow>` | Intet sendes |
| Sende en gemt request | `hoboman-cli send <id>` | Ét kald med requestens egen auth eller mappens. Kaldet står i **Historik** med mærket **CLI** |
| Sende et direkte kald | `hoboman-cli send <METODE> <url>` | Ét kald uden auth ud over de headers, AI'en giver |
| Hente en fil, fx en PDF | `hoboman-cli send … --out <fil>` | Svaret gemmes byte for byte i filen |
| Køre et workflow | `hoboman-cli run <id>` | Alle trin i rækkefølge. Hvert trin skrives som en JSON-linje, mens det kører, og gemmes, så `log` kan læse det |
| Se eller følge kørsler, også dem startet i appen | `hoboman-cli log <workflow> [--last] [--follow]` | Intet skrives |
| Oprette, omdøbe, flytte eller slette requests, mapper og workflows, når du beder om det | `hoboman-cli new`, `rename`, `move`, `delete <…> --yes` | Ændringen står i appen med det samme |
| Bygge et workflow, når du beder om det | `hoboman-cli new workflow <navn>`, og derefter `show` og `update --file` med trin og scripts | Workflowet står i appen med det samme |
| Rette et workflow eller en gemt request, når du beder om det | `hoboman-cli show`, og derefter `update <…> --file` med det rettede. Navn og mappe ændres med `rename` og `move` | Ændringen står i appen med det samme |
| Stoppe en kørsel | Ctrl+C | Kørslen slutter som `Cancelled` |

Det gør du selv i appen:

- **Hemmeligheder.** Passwords, tokens og client secrets skrives under Auth i appen og gemmes krypteret for din Windows-bruger. En AI kan hverken læse eller skrive dem.
- **Login med authorization code.** Hent tokenet med refresh-knappen ved Auth, **Hent token** eller **Saved credentials…**, med det miljø valgt, som AI'en bruger. Client-credentials-tokens henter CLI'et selv.
- **Mappers auth.** CLI'et kan vise den, men ikke rette den.
- **Filer, der ikke kan læses.** CLI'et fortæller hvor, men kan ikke rette dem.

En kørsel med `run` vises ikke i appens workflow-editor; dér vises kun det, der køres fra editoren. Se [CLI.md](CLI.md) for alle detaljer.

## Instruktionen

Kopiér teksten under stregen ind som instruktion til din AI. Udskift `C:\sti\til\Hoboman\publish` med den rigtige mappe.

---

Du kan sende API-kald og køre workflows gennem **Hoboman**, et API-værktøj på brugerens Windows-maskine, med kommandolinjeprogrammet `hoboman-cli.exe`.

**Du må aldrig tilgå Hobomans mappe direkte.** Det gælder programmappen og alt under den. Du må ikke liste, åbne, læse, skrive, kopiere, flytte eller slette noget i den, hverken med PowerShell, `Get-Content`, `Set-Content`, en editor eller et andet værktøj. Al kørsel og al oprettelse, visning, rettelse og sletning går gennem `hoboman-cli`. Kan CLI'et ikke det, du skal, så sig det til brugeren i stedet for at gå uden om det.

## Opsætning

- Programmet ligger i `C:\sti\til\Hoboman\publish\hoboman-cli.exe`. Kald det altid med fuld sti.
- Hobomans data ligger i samme mappe som programmet. Du læser og skriver aldrig selv i den; alt går gennem `hoboman-cli`. Det er uden betydning, hvilken mappe du står i.
- Hoboman-appen behøver ikke at være åben. Er den det, viser den dine kald og ændringer med det samme. Har brugeren ugemte ændringer i den samme request, det samme workflow eller vinduet med miljøer eller credentials, overskriver brugerens næste gem din ændring, eller appen afviser at gemme. Sig derfor til, når du har ændret noget.
- Kører du i Windows PowerShell 5.1, så sæt begge dele før kald. Ellers bliver æ, ø og å forkerte, eller til `?` uden fejl:

  ```powershell
  [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)  # det, du læser fra hoboman-cli
  $OutputEncoding = [System.Text.UTF8Encoding]::new($false)            # det, du piper ind i hoboman-cli
  ```

  Brug `-Encoding UTF8`, når du læser filer, fx en fil fra `--out`.

## Regler

1. **Rør aldrig Hobomans mappe, kun CLI'et.** Du tilgår aldrig filerne direkte. Alt læses og ændres gennem CLI'et: `list`, `show`, `log` og `history` læser, og `new`, `update`, `rename`, `move` og `delete` ændrer. Ændr kun noget, når brugeren beder om det, og brug kun `delete --yes`, når brugeren har bedt om netop den sletning. Øverste niveau skrives `.`.
2. **Læs altid exitkoden** (`$LASTEXITCODE`), før du bruger outputtet. stdout og stderr er JSON, bortset fra `list`, `log <workflow>` uden kørsel, `history` uden navn, `--help` og `--version`, der skriver tekst.
3. **Giv følsomme værdier via stdin** med `--vars -` eller `--params -`, aldrig som `--var`/`--param` på kommandolinjen, så de ikke havner i shellens historik. `run` skriver dog parametre og variabler i klartekst på stdout og i run-loggen, så giv kun en hemmelighed som parameter, når brugeren har bedt om det.
4. **Gentag ikke hemmeligheder** som tokens, passwords og API-nøgler i dine svar til brugeren. Opsummér i stedet, fx "login lykkedes, token gemt".
5. **Send ikke kald med sideeffekter** (POST, PUT, PATCH, DELETE eller workflows, der ændrer data), uden at brugeren har bedt om netop det. `run` sender, så snart workflowet er tjekket; brug `check` for kun at tjekke.
6. **Gæt ikke.** Findes en request, et workflow, et miljø eller en parameter ikke, så fortæl brugeren det, og spørg.

## Kommandoer

```text
hoboman-cli list [workflows|folders|environments]
hoboman-cli show <request|mappe|workflow|miljø>
hoboman-cli check <workflow> [--env <miljø>] [--param <navn=værdi>]... [--params <fil|->]
hoboman-cli log <workflow> [<runId> | --last] [--follow]
hoboman-cli new request <mappe> <navn> [--method <metode>] [--url <url>] [-H "Navn: Værdi"]... [--json <tekst|@fil> | --text <tekst|@fil>]
hoboman-cli new folder <mappe> <navn>
hoboman-cli new workflow <navn>
hoboman-cli new environment <navn>
hoboman-cli update <request|workflow|miljø> --file <fil|->
hoboman-cli history [<navn>] [--count <antal>]
hoboman-cli rename <mål> <navn>
hoboman-cli move <mål> <mappe>
hoboman-cli delete <mål> [--yes]
hoboman-cli send <gemt request> [--env <miljø>] [--var <navn=værdi>]... [--vars <fil|->] [--out <fil>]
hoboman-cli send <METODE> <url> [--env <miljø>] [-H "Navn: Værdi"]... [--json <tekst|@fil> | --text <tekst|@fil>] [--var <navn=værdi>]... [--vars <fil|->] [--out <fil>]
hoboman-cli run <workflow> [--env <miljø>] [--param <navn=værdi>]... [--params <fil|->]
hoboman-cli --help
```

- `list` skriver én linje pr. gemt request: id'et, en tabulator og stien gennem mapperne, fx `3f2c…<tab>Shop/Login`. Del ved den første tabulator, og giv `send` id'et. Stien virker også, men navne er ikke unikke, og et navn kan selv indeholde `/`.
- En linje uden sti er en fil, der ikke kan læses eller mangler `name`. I appen står en fil, der ikke kan læses, øverst i samlingerne som **Kan ikke læses (xxxxxxxx…)** med id'ets første 8 tegn. `show <id>` fortæller med `File is not valid.`, hvilken fil og linje der er galt. Fortæl brugeren det, og send den ikke for at finde ud af det.
- Vil du vide, hvad en request gør, før du sender den, så kør `show <id>`. Svaret er `{"request": {…}}` med metode, URL, query, headers, body og auth uden hemmeligheder. Dens mappe er `folderId`, og `show <mappens id>` giver mappens navn, auth og overmappe (`parentId`).
- `--env` skal være miljøets navn præcis, også store og små bogstaver. `list environments` giver `id<TAB>navn` for hvert miljø, og det, brugeren har valgt, ender med `<TAB>selected`. `show <miljø>` giver dets variabler og værdier. Har en request, mappe eller et workflow samme navn, giver det `Target is ambiguous. Use its id.`; brug så miljøets id. Hemmeligheder hører til under Auth, ikke i et miljø.
- Et miljø oprettes med `new environment <navn>`, får sine variabler med `show` og `update`, omdøbes med `rename` og slettes med `delete <miljø> --yes`, der også glemmer miljøets tokens og de credentials, der hører til det. Navne skal være unikke uden hensyn til store og små bogstaver. Slettes det miljø, brugeren har valgt, er intet valgt længere, og `send` og `run` uden `--env` giver `Selected environment was not found.`
- `history` giver de nyeste kald, som standard 20, nyeste først: `navn<TAB>tid<TAB>kilde<TAB>metode<TAB>status<TAB>adresse`, hvor tid er UTC, kilde er `App` eller `Cli`, og status er fejlens art, når der ikke kom noget svar. `--count` giver et andet antal. `history <navn>` giver hele kaldet som `{"call": {…}}` med request, svar eller fejl. Svar kan indeholde tokens, så gentag dem ikke.
- Uden `--env` bruges det miljø, brugeren har valgt i appen. Har brugeren intet valgt, bruges intet miljø, og `{{navne}}` udfyldes ikke.
- En gemt request sendes med sine egne headers, body og auth. Du kan kun ændre dens `{{variabler}}` med `--var`/`--vars`.
- Et direkte kald (`send METODE url`) har ingen auth ud over de headers, du giver det. `{{navne}}` udfyldes i URL og headers, men ikke i bodyen.
- `--out fil` gemmer svarets body byte for byte i filen. Brug det til PDF'er, billeder og andre binære svar. Filen skrives også ved 4xx og 5xx, så tjek exitkoden. Læg den aldrig i Hobomans mappe.
- `--json @fil.json` sender en body fra en fil. Brug det altid i Windows PowerShell 5.1, der fjerner anførselstegnene i en JSON-tekst på kommandolinjen.

### Exitkoder for alle kommandoer

Læs `$LASTEXITCODE` lige efter kaldet, før du gør andet. Ved exitkode 2 står fejlen som `{"error": …}` på stderr, stdout er tom, og intet er ændret.

| Kommando | 0 | 1 | 2 |
|---|---|---|---|
| `send` | Svar med status 2xx | Svar med en anden status | Intet svar |
| `run` | Alle trin lykkedes | Startet, men et trin fejlede, eller den blev afbrudt | Startede ikke; intet er sendt |
| `log … --follow` | `run.finished` kom | Kørslen stoppede uden `run.finished`, eller Ctrl+C | Fejl |
| `list`, `show`, `history`, `log`, `check`, `new`, `update`, `rename`, `move`, `delete` | Lykkedes | – | Fejl; intet er ændret |

### Kæd kald sammen

Log ind, tag tokenet fra svaret, og brug det i næste kald. Tokenet gives via stdin, så det ikke står på kommandolinjen:

```powershell
$cli = "C:\sti\til\Hoboman\publish\hoboman-cli.exe"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$login = Join-Path $env:TEMP 'login.json'
[IO.File]::WriteAllText($login, '{"username":"emilys","password":"emilyspass"}')
$answer = & $cli send POST 'https://dummyjson.com/auth/login' --json "@$login"
if ($LASTEXITCODE -ne 0) { throw "Login fejlede: $answer" }
$token = ((($answer | ConvertFrom-Json).body) | ConvertFrom-Json).accessToken
@{ token = $token } | ConvertTo-Json | & $cli send GET 'https://dummyjson.com/auth/me' -H 'Authorization: Bearer {{token}}' --vars -
```

## Output fra `send`

Et svar skrives som én JSON-linje på stdout, også ved 4xx og 5xx:

```json
{"status":200,"reason":"OK","elapsedMs":123,"size":17,"headers":[{"name":"Content-Type","value":"application/json"}],"body":"{\"id\":\"42\"}"}
```

`body` er tekst. Er den JSON, skal den parses en gang til. Med `--out` står `file` med filens fulde sti i stedet for `body`.

| Exitkode | Betydning |
|---|---|
| 0 | Svar med status 2xx |
| 1 | Svar med en anden status. Læs `status` og `body` |
| 2 | Intet svar. Fejlen står som `{"error":"..."}` på stderr, og stdout er tom |

## Workflows

Et workflow er en række trin, der hver sender sin egen request, kører et script eller venter et antal sekunder. Værdier fra et svar, fx et token, gemmes i variabler, som de næste trin bruger.

**Find workflows og deres parametre.**
- `hoboman-cli list workflows` skriver id og navn på hvert workflow, adskilt af en tabulator. Giv `run` id'et. Et workflow uden navn kan ikke læses eller mangler `name`. `show <id>` fortæller hvorfor; kør det ikke for at finde ud af det.
- `show <workflow>` giver `{"workflow": {…}, "scripts": {"navn.js": "kode"}}`. Se `parameters`: en parameter uden `default` er påkrævet og skal gives med `--param navn=værdi` eller `--params`.
- `check <workflow>` tjekker det som `run`, med de samme parametre og `--env`, men sender intet. Exitkode 0 og `{"id","name"}`, eller 2 med `Workflow cannot run.` og `problems`.
- `--param` giver altid tekst. Skal en værdi være et tal eller et objekt, så brug `--params` med et JSON-objekt.
- Hvert trin har præcis én af `request` (metode, URL, query, headers, body og auth), `script` (en af workflowets scripts) og `delaySeconds`. Et trin med `"auth": { "kind": "Inherit" }` bruger workflowets fælles `auth`; uden `auth` sender trinnet ingen. Hemmeligheder til auth står aldrig i outputtet.
- `run` og `check` bruger workflowet, som det er gemt. Ugemte ændringer i appen ses ikke.

**Kør et workflow:**

```powershell
$cli = "C:\sti\til\Hoboman\publish\hoboman-cli.exe"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$id = "9bf2fef5-9047-4075-969c-db7c7377a62d"   # fra list workflows
$lines = & $cli run $id --env Demo --param orderId=o-17
$code = $LASTEXITCODE
```

Med parametre fra stdin:

```powershell
@{ orderId = 'o-17'; pageSize = 50 } | ConvertTo-Json | & $cli run $id --params -
```

**Events.** `run` skriver én JSON-linje pr. event på stdout, mens kørslen sker. Der er altid kun én kørsel pr. kald.

| `type` | Indhold |
|---|---|
| `run.started` | Første linje. `runId`, `workflowId`, `workflow`, `environment`, `runFile` (stien til kørslens log; læs den med `log`, ikke direkte) og `parameters` |
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
- Start `run` som en baggrundsopgave, og læs `runId` fra første linje.
- Følg den med `hoboman-cli log <workflow> <runId> --follow`. Den skriver kørslens events, som `run` gør, og venter på nye, til `run.finished` kommer (exitkode 0). Blev kørslen dræbt, så den aldrig får `run.finished`, stopper den med exitkode 1, når intet skriver i kørslen længere. Ctrl+C stopper den også med exitkode 1.
- `log <workflow>` giver én linje pr. kørsel, nyeste først: `runId<TAB>start<TAB>outcome`, hvor `start` er UTC, og `outcome` er `-`, hvis kørslen stadig kører eller blev stoppet uden `run.finished`. Det gælder også kørsler startet i appen. `log <workflow> --last` giver den nyeste. Appen kan være sat til at slette kald og kørsler efter et antal dage.
- Ctrl+C afbryder pænt med `run.finished` `Cancelled`. Dræbes processen, fx når en baggrundsopgave når sin tidsgrænse, kommer der ingen `run.finished`.

## Byg og ret (kun når brugeren beder om det)

- Opret et workflow med `hoboman-cli new workflow <navn>`, og en request med `new request`. Ret dem med `show` og `update`: tag outputtet fra `show`, ret i det, og giv det til `update <id> --file -` via stdin eller som en fil. `update` tager imod præcis den form, `show` gav, og erstatter indholdet. Id, navn og mappe bliver, og dermed hemmeligheder, historik og kørsler.
- Ret aldrig `id`, `name` eller `folderId`; `update` afviser det. Brug `rename` og `move`.
- Hvert navn, et trin gemmer i, skal stå i `variables`. Et eksisterende workflow er et godt forlæg, og hele formatet står i Hobomans `CLI.md`. Et lille eksempel på input til `update`, hvor `id` er workflowets eget fra `show`:

  ```json
  {
    "workflow": {
      "id": "9bf2fef5-9047-4075-969c-db7c7377a62d",
      "name": "Ny kurv",
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
    },
    "scripts": { "byg-kurv.js": "return { cart: { userId: 1, products: [] } };" }
  }
  ```

- Skriv egenskaberne i camelCase og enums som tekst, fx `"Json"`, `"Inherit"`, `"OAuth2"`. En ukendt eller stavet forkert egenskab giver `Input is not valid.` med `path` og `line`.
- Behold `id` i de trin, der allerede har et; trinets hemmeligheder hører til det. Et nyt trin skal ikke have noget `id`; `update` giver det et. Kopiér aldrig et id fra et andet workflow eller en request. Fjernes et trin, glemmes dets hemmeligheder.
- Scripts står i `scripts` med filnavnet som nøgle, fx `"byg-kurv.js"`. Scripts, du ikke giver, bliver liggende, og et script givet som `null` slettes, fx `"gammel.js": null`. Et script, et trin bruger, kan ikke slettes. Skal et trin have auth med en hemmelighed, så bed brugeren udfylde den under Auth i appen og gemme. Auth kan også gives som en header med et token fra et tidligere trin, fx `Authorization: Bearer {{token}}`.
- Et script læser `vars.navn`, som ikke kan ændres, og returnerer det, trinnet gemmer fra med `saves`, fx `"from": "$.cart"`. Det returnerede bliver JSON. Skal scriptet lave HTML, XML eller tekst, så giv trinnet `"output": "Html"`, `"Xml"` eller `"Text"` og returnér en tekst. Den bliver outputtet, som den er, og `"from": "$"` gemmer den. Det kører som strict JavaScript uden filer, netværk, .NET og `eval` og stoppes efter 5 sekunder.
- Appen viser workflowet med det samme. Kan workflowet ikke læses, står det under **Workflows** som **Kan ikke læses (xxxxxxxx…)**. Har brugeren ugemte ændringer i det samme workflow, genindlæses det ikke, og brugerens næste gem overskriver din ændring, så sig til, når du har ændret noget. Når brugeren gemmer, skriver appen `variables` ud fra trinenes `saves`.
- Tjek workflowet med `check`. Vil du køre det, så spørg først: `run` sender med det samme.
- Slet med `hoboman-cli delete <id> --yes`, når brugeren beder om det, så hemmelighederne også slettes.

### Ret noget med `show` og `update` i PowerShell

Parse outputtet fra `show`, ret felterne, og giv det hele tilbage via stdin. Brug altid `ConvertTo-Json -Depth 100`: uden `-Depth` gemmer Windows PowerShell 5.1 kun to niveauer og gør resten til tekst, så `update` afviser det eller gemmer forkerte værdier.

```powershell
$shown = & $cli show $id
if ($LASTEXITCODE -ne 0) { throw "show fejlede" }
$shown = $shown | ConvertFrom-Json
$shown.request.url = '{{baseUrl}}/products/2'                  # en request
# $shown.workflow.steps += [pscustomobject]@{ name = 'Vent'; delaySeconds = 2 }   # et workflow: tilføj et trin
# $shown.scripts = @{ 'ny.js' = 'return vars.id;'; 'gammel.js' = $null }        # skriv og slet scripts
# $shown.environment.variables += [pscustomobject]@{ name = 'pageSize'; value = '50'; enabled = $true }   # et miljø
$shown | ConvertTo-Json -Depth 100 -Compress | & $cli update $id --file -
if ($LASTEXITCODE -ne 0) { throw "update fejlede" }
```

Skal du lave noget nyt, så opret det med `new`, og byg det ud fra `show` af det nye eller af et eksisterende, der ligner. `show` viser altid det præcise format.

## Formatet

Felterne skrives i camelCase og enums som tekst. Felter, du udelader, får deres standard. Ukendte felter afvises med `Input is not valid.`. Hemmeligheder (passwords, tokens, client secrets) står aldrig i JSON'en; de skrives under Auth i appen.

**Request** (`{"request": {…}}`)

| Felt | Indhold |
|---|---|
| `id`, `name`, `folderId` | Requestens id, navn og mappe. Ændres ikke med `update`; brug `rename` og `move` |
| `method` | Fx `GET`, `POST`, `PUT`, `PATCH`, `DELETE` eller en anden metode |
| `url` | Adressen, fx `{{baseUrl}}/orders/{{id}}`. Påkrævet |
| `query`, `headers` | Lister af `{"name", "value", "enabled"}`. `enabled` er `true`, når det udelades |
| `bodyKind` | `None`, `Json`, `Xml` eller `Text` |
| `body` | Bodyen som tekst. En JSON-body skrives som en tekst med JSON i |
| `useEnvironmentVariablesInBody` | `true` udfylder `{{navne}}` i bodyen. Standard er `false` for gemte requests |
| `base64` | `{"encode": [stier], "decode": [stier]}`: JSON-stier, der sendes som Base64 eller vises decodet. `$` er hele bodyen |
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
| `variables` | `[{"name", "default"}]`: de navne, trinene gemmer i |
| `auth` | Workflowets fælles auth, som trin med `Inherit` bruger. Udelades den, har workflowet ingen |
| `steps` | Trinene i rækkefølge, se nedenfor |
| `scripts` | `{"navn.js": "kode"}`. Givne scripts skrives, `null` sletter, og andre bliver liggende |

Et trin har `name` (valgfrit) og præcis én af:
- `request`: som en request, men uden `name` og `folderId`. `id` er trinnets egen og hører til dets hemmeligheder; behold det på eksisterende trin, og udelad det på nye. `useEnvironmentVariablesInBody` er `true`, når det udelades. Uden `auth` sender trinnet ingen auth.
- `script`: navnet på et script i `scripts`, og `output` (`Json`, `Html`, `Xml` eller `Text`).
- `delaySeconds`: 1-300 sekunder.

Et trin kan desuden have:
- `saves`: `[{"variable", "from"}]`. `from` er `$` for hele bodyen, en sti som `$.data.items[0].id`, `header:Navn`, `status` eller en fast JSON-værdi som `"1"` eller `"\"ja\""`. Der gemmes kun efter et 2xx-svar.
- `retry` (kun request-trin): `{"until": "$.status", "equals": "done", "stopIf": "$.status", "stopEquals": "failed", "times": 30, "waitSeconds": 5}`. Trinnet sendes igen, indtil svaret er 2xx, alt i `saves` findes, og `until` er `equals`. Det stopper med det samme, når `stopIf` er `stopEquals`. `times` er 1-100 og `waitSeconds` 0-300. Gentag ikke en request, der opretter noget, medmindre API'et tåler det.

`{{navn}}` udfyldes i URL, query, headers, Basic og Bearer og i bodyen, når `useEnvironmentVariablesInBody` er `true`. Værdien kommer fra workflowets parametre og variabler, og ellers fra miljøet. OAuth-felterne udfyldes kun fra miljøet. Et script læser alle værdier i `vars`.

**Miljø** (`{"environment": {…}}`): `id` og `name` ændres ikke med `update`, og `variables` er `[{"name", "value", "enabled"}]`.

**Mappe** (`{"folder": {…}}`, kun til at læse): `id`, `name`, `parentId` (overmappen; mangler på øverste niveau) og `auth`.

## Fejl og hvad du gør

Ved `run` står de samme tekster som `error` i et trins `step.finished` (exitkode 1), fx når en hemmelighed eller et token mangler på et workflow-trin. Et trin med egen auth finder sine hemmeligheder under `id` i trinnets `request`, og et trin med `Inherit` under workflowets id. Mangler trinnets `id`, eller er hemmeligheden aldrig skrevet under Auth i appen og gemt, så bed brugeren gøre det.

| Fejl (stderr `error`) | Gør |
|---|---|
| `Saved request could not be loaded.` | Id'et eller stien findes ikke. Kør `list`, og brug et id derfra |
| `Saved request name is ambiguous. Use its id.` | Flere requests har stien. Brug id'et fra `list` |
| `Saved request file is not valid.` | Den gemte request er ugyldig JSON. `file`, `path` og `line` viser hvor. Fortæl brugeren det |
| `Selected environment was not found.` | Miljøet findes ikke. Spørg brugeren om det rigtige navn |
| `Environment settings could not be read.` | Bed brugeren åbne Hoboman og tjekke miljøerne |
| `Fetch a new OAuth token in Hoboman before sending this request.` | Tokenet kunne ikke hentes. Tokens gemmes pr. miljø, så bed brugeren vælge det miljø, du bruger, i appen og hente et token med refresh-knappen ved Auth, **Hent token** eller **Saved credentials…** på requesten, mappen, workflowet eller trinnet. **Hent token** i Credentials-vinduet gemmer ikke et token til requests. Ved client credentials mangler client secret typisk: bed brugeren udfylde den under Auth og gemme |
| `Required authentication secret is missing.` | Bed brugeren udfylde auth på requesten, mappen, workflowet eller workflow-trinnet i Hoboman og gemme |
| `Network request failed.` / `Request timed out.` | Serveren kunne ikke nås eller svarede ikke inden for 100 sekunder. Fortæl det, og prøv højst én gang til efter aftale |
| `Invalid request URL.` / `Invalid request input.` | Tjek URL, metode, headers og variabler. Et ukendt `{{navn}}` bliver stående i URL'en |
| `Input could not be read.` | En fil kunne ikke læses, typisk `@fil`, `--vars fil` eller `--params fil`. Tjek stien. En relativ sti læses fra den mappe, du står i. Gav du ingen fil, er det en af Hobomans egne filer. Fortæl brugeren det |
| `Request body could not be encoded.` | Bodyen kunne ikke Base64-encodes, fx fordi den ikke er gyldig JSON eller mangler et markeret felt. Bed brugeren rette requesten eller trinnet i Hoboman |
| `Request was cancelled.` | Kaldet blev afbrudt med Ctrl+C |
| `Request failed.` | En anden fejl. Fortæl brugeren det |
| `Output file could not be written.` | Filen i `--out` kunne ikke skrives. Tjek stien; mappen skal findes. Kaldet er sendt |
| `Invalid command arguments. Use --help for usage.` | Ret kommandoen. `--env`, `--json`, `--text`, `--out`, `--vars` og `--params` må kun gives én gang |
| `Invalid variable input.` / `Invalid parameter input.` | `--var`/`--param` skal være `navn=værdi`, og `--vars`/`--params` et JSON-objekt. `-` kræver, at der pipes noget ind |
| `Workflow could not be loaded.` | Workflowet findes ikke. Kør `list workflows`, og brug et id derfra |
| `Workflow name is ambiguous. Use its id.` | Flere workflows har navnet. Brug id'et fra `list workflows` |
| `Invalid name.` / `Target could not be found.` / `Folder could not be found.` | Ret navnet eller målet. Find id'er med `list`, `list folders`, `list workflows` eller `list environments` |
| `Target is ambiguous. Use its id.` / `Folder is ambiguous. Use its id.` | Flere har stien eller navnet. Brug id'et |
| `A folder cannot be moved into itself.` / `A workflow cannot be moved.` | Flytningen er ikke mulig. Fortæl brugeren det |
| `Deleting needs --yes.` | Fortæl brugeren, hvad der ville blive slettet (`path`, `folders`, `requests`), og brug kun `--yes`, hvis brugeren siger ja |
| `File is not valid.` | Filen er ugyldig JSON. `file`, `path` og `line` viser hvor. Fortæl brugeren det |
| `The change could not be saved.` | En fil er låst eller kunne ikke skrives. Prøv igen senere, eller fortæl brugeren det |
| `Workflow file is not valid.` | Workflowets fil er ugyldig. `file`, `path` og `line` viser hvor. Fortæl brugeren det |
| `Workflow cannot run.` | Se `problems` nedenfor. Det samme gælder `check` |
| `Input is not valid.` | Inputtet til `update` passer ikke til formatet. `path` og `line` viser hvor. Ret det, og prøv igen |
| `Use rename to change the name.` / `Use move to change the folder.` / `Target is not the one in the input.` | `update` ændrer ikke id, navn eller mappe. Brug `show`-outputtet uændret i de felter, og `rename` eller `move` |
| `Step id is not one of the workflow's.` | Et trin har et id, workflowet ikke havde, eller to trin har det samme. Fjern `id` fra nye trin |
| `Invalid script name.` | Et navn i `scripts` skal være et filnavn, der ender på `.js` |
| `A folder cannot be updated.` | En mappes auth ændres i appen |
| `Environment name is taken.` | Et andet miljø har navnet, også med andre store og små bogstaver. Vælg et andet |
| `An environment cannot be moved.` | Miljøer ligger ikke i mapper |
| `A script a step uses cannot be deleted.` | Fjern først trinnet, eller giv det et andet script |
| `Call could not be found.` | Navnet er ikke i historikken. Brug et navn fra `history` |
| `Target could not be read.` | Det, `show` skulle vise, kunne ikke læses, fx fordi det lige er slettet. Fortæl brugeren det |
| `Workflow could not be found.` / `Run could not be found.` / `Run could not be read.` | `log` fandt ikke workflowet eller kørslen. Brug id'er fra `list workflows` og `log <workflow>` |

**`problems` ved `Workflow cannot run.`** Hvert problem har `kind`, `step` (trinnets index, hvis det hører til et trin) og `detail` (et navn, en sti eller et id, aldrig en værdi).

| `kind` | Gør |
|---|---|
| `MissingParameter` | Giv parameteren i `detail` med `--param` eller `--params` |
| `UnknownParameter` | Fjern eller ret parameteren. Se `parameters` i `show <workflow>` |
| `UsedBeforeSaved` | Et trin bruger en variabel, før et tidligere trin har gemt den. Workflowet skal rettes |
| `UnknownName` | Navnet findes hverken i workflowet eller i det valgte miljø. Tjek, at det rigtige miljø er valgt (`--env`). Ellers skal workflowet eller miljøet rettes |
| `MissingUrl`, `InvalidName`, `DuplicateName`, `NotAVariable`, `InvalidSource`, `MixedStep`, `InvalidDelay`, `InvalidRetry`, `InvalidOutput` | Workflowet er sat forkert op. Har du selv skrevet det, så ret det; ellers bed brugeren rette det i Hoboman |
| `ScriptNotFound`, `InvalidScript` | Et script-trins script mangler i `scripts` eller har en syntaksfejl (`detail` viser fil og linje). Ret det med `update`, hvis du selv har skrevet det; ellers bed brugeren rette det |

Et trin, hvor en værdi i `saves` mangler i svaret, fejler med `error` = `Nothing to save was found at <sti>.` Svaret havde ikke den forventede form. Værdier gemmes kun efter et 2xx-svar.

Et trin med `retry`, hvis svar aldrig blev klar, fejler med det sidste svar, og `attempts` viser antallet af forsøg. Passede `until`-værdien ikke, er `error` `The answer was not ready after <n> attempts.`: det, API'et lavede, blev ikke færdigt i tide. Ellers er det sidste svars `status` eller `error` som ved et almindeligt trin. Fortæl brugeren, hvad det sidste svar sagde, fx en status. Er `error` `Stopped as <sti> was <værdi>.`, stoppede trinnet med det samme, fordi API'et svarede, at det, der ventes på, er fejlet. Det er ikke en timeout.

Et script-trin har `JS` som `method`, `status` 200, en tom `address` og scriptets filnavn som `name`, hvis det intet navn har. Fejler det, er `error` fil, linje og scriptets fejltekst, fx `map.js:2: No order`. Returnerer det intet, mens trinnet har `saves`, er `error` `map.js returned nothing to save.` Returnerer det ikke en tekst, mens trinnet har et andet `output` end `Json`, er `error` fx `html.js must return text when its output is Html.` Et ventetrin har `WAIT` som `method`, en tom `address` og `Wait <n> seconds` som `name`, hvis det intet navn har.

## Godt at vide

- Kald med `send` gemmes i Hobomans historik, som `history` læser, og vises i appen med mærket **CLI**. Kald, der fejler, før de bygges, fx med forkerte argumenter eller et ukendt miljø, og kald, der afbrydes, gemmes ikke. Trin i et workflow gemmes kun i kørslerne, som `log` læser. Historikken og kørslerne er ikke krypteret.
- Client-credentials-tokens hentes automatisk, når de mangler, er udløbet, eller serveren svarer 401, både ved `send` og i et workflow-trin. Det kræver en gemt client secret, og tokenet skrives i `secrets.json`. Authorization-code-tokens kræver, at brugeren logger ind i appen.
- Hele svaret holdes i hukommelsen, og `body` er tekst. Gem binære svar med `--out`.
- Cookies bruges ikke, redirects følges, og et kald venter højst 100 sekunder.
- `check` tjekker et workflow uden at sende, men et enkelt trin kan ikke køres for sig.
