# Hoboman CLI

`hoboman-cli.exe` sender gemte og direkte requests og kører workflows uden GUI'en. Scripts og AI-agenter bruger dermed de samme requests, workflows, miljøer, auth og hemmeligheder som appen.

## Installation og data

- `install.ps1` udgiver `hoboman-cli.exe` ved siden af `Hoboman.exe` i `publish\`. Begge programmer deler `requests\`, `workflows\`, `environments.json`, `settings.json`, `secrets.json`, `history\` og `runs\`.
- CLI'et finder altid sine data ved siden af programmet, uanset hvilken mappe du står i. Relative stier i `@fil`, `--vars fil` og `--params fil` læses fra den mappe, du står i.
- Scriptet tilføjer ikke CLI'et til `PATH`. Kald det med fuld sti, eller tilføj `publish\` selv.
- Hemmeligheder er krypteret for din Windows-bruger, så CLI'et skal køre som den samme bruger som appen.

## Kommandoer

```text
hoboman-cli list
hoboman-cli send <gemt request> [--env <navn>] [--var <navn=værdi>]... [--vars <fil|->]
hoboman-cli send <METODE> <url> [--env <navn>] [-H "Navn: Værdi"]... [--json <tekst|@fil>] [--text <tekst|@fil>] [--var <navn=værdi>]... [--vars <fil|->]
hoboman-cli run <workflow> [--env <navn>] [--param <navn=værdi>]... [--params <fil|->]
hoboman-cli --help
hoboman-cli --version
```

- `list` skriver de gemte requests som stier relativt til `requests\`, én pr. linje og sorteret, fx `Brugere/Hent bruger`.
- Ét argument efter `send` er en gemt request. To er en metode og en URL.
- En gemt request sendes med sine egne headers, body, Base64-valg og indstillingen **Brug environment-variabler i body**. Derfor kan `-H`, `--json` og `--text` kun bruges ved direkte kald.
- Et direkte kald har ingen auth ud over de headers, du giver det, og ingen body uden `--json` eller `--text`. `@fil` læser bodyen fra en UTF-8-fil, og `@@tekst` sender `@tekst`.

## Miljøer og variabler

- `--env` vælger miljø for både `send` og `run`. Uden det bruges det miljø, der er valgt i appen. Et ukendt miljø giver en fejl, før noget sendes. Valget gemmes ikke.
- `--var navn=værdi` sætter en midlertidig variabel og kan gentages. `--vars fil.json` eller `--vars -` (stdin) læser et JSON-objekt, fx `{"userId":42}`. Værdier, der ikke er tekst, indsættes som deres JSON.
- `--var` vinder over `--vars`, som vinder over miljøets gemte værdier. De midlertidige værdier gælder kun det ene kald, gemmes aldrig og bruges også, hvor miljøets variabel er slået fra.
- Variabler i bodyen indsættes kun i gemte requests, der har **Brug environment-variabler i body** slået til. Et direkte kald sendes, som det er skrevet.
- Giv følsomme værdier gennem `--vars -` eller `--params -` frem for `--var` og `--param`, så de ikke havner i shellens historik. `run` skriver dog parametre og variabler i klartekst på stdout og i run-loggen, så hemmeligheder hører til i trinnets auth.

## Output og exitkoder

Et svar skrives som én linje JSON på stdout, også ved 4xx og 5xx:

```json
{"status":200,"reason":"OK","elapsedMs":123,"size":17,"headers":[{"name":"Content-Type","value":"application/json"}],"body":"{\"id\":\"42\"}"}
```

`body` er svaret som tekst. Er det JSON, skal det derfor gennem `ConvertFrom-Json` endnu en gang.

Fejl, før der kommer et svar, skrives som `{"error":"..."}` på stderr, og stdout er tom. En gemt request, der ikke er gyldig JSON, angives med fil, JSON-sti og linje, fx `{"error":"Saved request file is not valid.","file":"...","path":"$.headers","line":4}`.

| Exitkode | Betydning |
|---|---|
| 0 | Svar med status 2xx samt `list`, `--help` og `--version` |
| 1 | Svar med anden status |
| 2 | Fejl: forkerte argumenter, filer, der ikke kan læses, ukendt request eller miljø, netværksfejl, timeout, afbrudt kald eller et token, der ikke kan hentes |

## Workflows

`run` kører et workflow, der er gemt i `workflows\<navn>\workflow.json`. `<workflow>` er mappens navn, fx `Ordre-sync`. Et workflow oprettes i appen eller ved at skrive filen selv:

```json
{
  "id": "9bf2fef5-9047-4075-969c-db7c7377a62d",
  "parameters": [ { "name": "orderId" }, { "name": "pageSize", "default": 50 } ],
  "variables": [ { "name": "token" }, { "name": "importId" } ],
  "steps": [
    { "name": "Login",
      "request": { "method": "POST", "url": "{{baseUrl}}/auth/token", "bodyKind": "Json", "body": "{\"user\": \"{{user}}\"}" },
      "saves": [ { "variable": "token", "from": "$.access_token" } ] },
    { "name": "Importér ordre",
      "request": { "method": "POST", "url": "{{baseUrl}}/import/{{orderId}}",
                   "headers": [ { "name": "Authorization", "value": "Bearer {{token}}" } ] },
      "saves": [ { "variable": "importId", "from": "$.importId" } ] }
  ]
}
```

- `id` er workflowets eget id. Kørslerne gemmes under det i `runs\`, så de følger med, når mappen omdøbes.
- Et trin har sin egen `request` med de samme felter som en gemt request: `method` (standard `GET`), `url`, `query`, `headers`, `bodyKind` (`None`, `Json`, `Xml` eller `Text`, standard `None`), `body`, `useEnvironmentVariablesInBody` (standard `true`), `base64` og `auth`. Workflowet bruger ikke gemte requests fra samlingerne, og i appen redigeres trinnet med samme editor som en fane, også Auth.
- `auth` er et objekt med `kind`: `None` (standard), `Inherit`, `Basic`, `Bearer` eller `OAuth2`, fx `"auth": { "kind": "Inherit" }` på et trin og `"auth": { "kind": "OAuth2", "oAuth": { "grant": "ClientCredentials", "tokenUrl": "https://auth.{{env}}.{{site}}.com/oauth2/token", "clientId": "…", "scope": "…", "clientAuthentication": "BasicHeader" } }` på workflowet. Basic har også `userName`. `Inherit` bruger workflowets egen `auth`, som står øverst i `workflow.json` ved siden af `steps`, ligesom en request arver fra sin mappe. Et trin kan altid have sin egen auth i stedet, fx til et andet API. Har workflowet ingen `auth`, sender et trin med `Inherit` ingen.
- Passwords, tokens og client secrets står aldrig i `workflow.json`; de gemmes krypteret i `secrets.json` under trinnets `id`, og workflowets under workflowets `id`. Auth kan også gives som header, fx `Authorization: Bearer {{token}}` med et token fra et tidligere trin.
- `id` sættes af appen, første gang workflowet gemmes med trinnet, og ejer trinnets hemmeligheder. Auth med hemmeligheder virker derfor først i `run`, når workflowet er gemt i appen. Kopiér aldrig et `id` fra en gemt request eller et andet trin, for så deler de hemmeligheder, og sletter man den ene, kan den andens forsvinde.
- Ved client credentials henter `run` selv et nyt token til trinnet eller workflowet, når det mangler, er udløbet, eller serveren svarer 401, ligesom `send`. Mangler client secret, fejler trinnet med `Fetch a new OAuth token in Hoboman before sending this request.` Authorization code kræver, at brugeren henter et token under Auth på trinnet eller workflowet i Hoboman.
- `name` er valgfrit og vises i appen og i events. Uden navn bruges scriptets filnavn, `Wait 30 seconds` for en ventetid eller metoden og requestens adresse uden query, fx `POST {{baseUrl}}/auth/token`.
- Parametre gives ved start og kan ikke gemmes i. En parameters `default` kan være enhver JSON-værdi. Variabler er de navne, trinnene gemmer i med `saves`, og får deres værdi derfra. En variabel kan også have en `default`, som den har, indtil et trin gemmer i den. Appen skriver listen over variabler ud fra trinnene, når workflowet gemmes, og beholder deres `default`, så en fast værdi, som intet trin gemmer i, gives som en parameter med `default`. Et trin kan også gemme en fast værdi i en variabel, se `from` nedenfor.
- Et trin kan i stedet for `request` have `"script": "map.js"`, en JavaScript-fil i workflowets mappe. Scriptet får alle parametre, variabler og miljøets variabler i `vars`, som ikke kan ændres. Et navn, workflowet selv har, vinder over miljøet, som i en request, og det, det returnerer, er trinnets output. Outputtet gemmes med `saves` som et svar, fx `"from": "$"` eller `"$.id"`. I events har trinnet scriptets filnavn som `name`, hvis det intet navn har, `JS` som `method` og outputtet som `body`. Returnerer scriptet intet, fejler trinnet kun, hvis det har noget i `saves`. Et script har ingen adgang til filer eller netværk og stoppes efter 5 sekunder, eller når det holder mere end 512 MB hukommelse. Tal over 2^53 kan ændre sig, når de går gennem et script.
- Et request-trin kan gentages, indtil svaret er klar, fx mens et API laver noget færdigt i baggrunden: `"retry": { "until": "$.result.status", "equals": "succeeded", "times": 60, "waitSeconds": 5 }`.
  - Svaret er klar, når det er 2xx, og alt i `saves` findes. Med `until` skal værdien dér også være `equals`, uden hensyn til store og små bogstaver. `until` er en kilde i svaret som `from` i `saves` (`$`, en sti, `header:Navn` eller `status`), men ikke en fast værdi, og `until` og `equals` gives sammen eller slet ikke.
  - Med `"stopIf": "$.result.status", "stopEquals": "failed"` fejler trinnet med det samme, når værdien dér er `stopEquals`, uden hensyn til store og små bogstaver, fx når det, der ventes på, er fejlet. `error` er så `Stopped as $.result.status was failed.` `stopIf` er en kilde i svaret som `until`, og `stopIf` og `stopEquals` gives sammen eller slet ikke.
  - `times` er det samlede antal forsøg (1-100, standard 30), og `waitSeconds` pausen imellem (0-300, standard 5). Værdierne gemmes kun fra det svar, der var klar.
  - Alle svar og netværksfejl prøves igen. Fejl, der opstår, før noget sendes, fx en ugyldig URL eller en manglende hemmelighed, stopper trinnet med det samme.
  - Er svaret ikke klar efter sidste forsøg, fejler trinnet med det sidste svar: et svar, der ikke er 2xx, har sin `status` uden `error`, en manglende værdi giver `Nothing to save was found at <sti>.`, og en `until`-værdi, der ikke passer, giver `The answer was not ready after 60 attempts.` Ender sidste forsøg i en fejl, fx en timeout, står fejlen i `error`. `attempts` står på `step.finished` i alle tilfældene.
  - Gentag ikke en POST eller anden request, der opretter noget, medmindre API'et tåler det. Et forsøg, der fik en fejl, kan være udført hos serveren alligevel.
- Et trin kan også bare vente: `{ "name": "Vent", "delaySeconds": 30 }`. Det venter mellem 1 og 300 sekunder, har `WAIT` som `method` i events og kan ikke have `saves`. Afbrydes kørslen, stopper ventetiden med det samme.
- `from` i `saves` er `$` for hele bodyen (rå tekst, hvis den ikke er JSON), en sti som `$.data.items[0].id`, `header:Navn`, `status` eller en fast JSON-værdi, der gemmes, som den er, fx `"from": "1"` for tallet 1 og `"from": "\"ja\""` for teksten ja. Der gemmes kun efter et 2xx-svar, og et trin gemmer alle sine værdier eller ingen.
- `{{navn}}` udfyldes i URL, query og headers, i Basic-brugernavn og -password og i Bearer-token og i body, når `useEnvironmentVariablesInBody` er `true`. Navnet får sin værdi fra workflowets parametre og variabler. Kun et navn, som workflowet ikke selv har, hentes fra miljøet. Tekst indsættes uændret, og alt andet som kompakt JSON.
- `--param navn=værdi` giver en parameter som tekst og kan gentages. `--params fil.json` eller `--params -` (stdin) læser et JSON-objekt, hvor værdierne beholder deres type, fx `{"orderId":"o-17","pageSize":50}`. `--param` vinder over `--params`.
- Før første kald tjekkes hele workflowet: at hvert trin har en URL, at parametrene er kendte, at de påkrævede er givet, og at hvert `{{navn}}` i en request har en værdi, når trinnet kører. Et navn, der kun står i bodyen, og som hverken workflowet eller miljøet har, bliver stående som skrevet og tjekkes ikke, så fx en Handlebars-template kan bruge sine egne `{{navne}}`. Navne, et script læser i `vars`, tjekkes ikke. Fejler tjekket, sendes intet.
- Trinene køres ét ad gangen i rækkefølge. Et trin fejler ved en status uden for 2xx, ved en fejl før svaret, eller hvis en værdi i `saves` ikke findes. Så springes resten over.

### Events

Kørslen skriver én JSON-linje pr. event på stdout og de samme linjer i `runs\<workflowId>\<runId>.jsonl`. Linjerne er UTF-8 uden BOM, slutter med `\n` og flushes én ad gangen, så en læser ser dem med det samme.

```json
{"type":"run.started","runId":"20261003-104233-512-7f3a","workflowId":"9bf2fef5-9047-4075-969c-db7c7377a62d","workflow":"Ordre-sync","environment":"Demo","runFile":"C:\\Hoboman\\runs\\9bf2fef5-9047-4075-969c-db7c7377a62d\\20261003-104233-512-7f3a.jsonl","parameters":{"orderId":"o-17","pageSize":50}}
{"type":"step.started","index":0,"name":"Login","method":"POST","address":"shop.example/auth/token"}
{"type":"step.finished","index":0,"outcome":"Succeeded","status":200,"reason":"OK","elapsedMs":196,"size":25,"saved":{"token":"eyJ..."},"headers":[{"name":"Content-Type","value":"application/json"}],"body":"{\"access_token\":\"eyJ...\"}"}
{"type":"step.started","index":1,"name":"Importér ordre","method":"POST","address":"shop.example/import/o-17"}
{"type":"step.finished","index":1,"outcome":"Failed","status":500,"reason":"Internal Server Error","elapsedMs":87,"size":0,"headers":[],"body":""}
{"type":"run.finished","outcome":"Failed","elapsedMs":301,"steps":[{"index":0,"outcome":"Succeeded","status":200},{"index":1,"outcome":"Failed","status":500}],"variables":{"orderId":"o-17","pageSize":50,"token":"eyJ..."}}
```

| Event | Indhold |
|---|---|
| `run.started` | Altid første linje: `runId`, `workflowId`, `workflow` (mappens navn), `environment`, `runFile` (fuld sti til logfilen) og `parameters` |
| `step.started` | `index` (første trin er 0), `name` (trinnets navn), `method` og `address` (vært, port og sti uden skema og query) |
| `step.finished` | `outcome` (`Succeeded` eller `Failed`), svaret med samme navne som ved `send`, `attempts` ved et trin med `retry`, `error` ved en fejl og `saved` med de gemte værdier |
| `step.retrying` | Et forsøg, der ikke var klar, før trinnet prøves igen: `index`, `attempt` (forsøgets nummer), `status` og `value` (det, `until` gav) eller `error` ved en netværksfejl. Uden body |
| `step.skipped` | Et trin, der ikke blev kørt efter en fejl eller en afbrydelse |
| `step.cancelled` | Trinnet, der kørte, da kørslen blev afbrudt |
| `run.finished` | Altid sidste linje: `outcome` (`Succeeded`, `Failed` eller `Cancelled`), `elapsedMs`, `steps` og `variables` med de endelige værdier |

- Små felter står først og headers og body sidst, så en afkortet linje stadig viser, hvordan trinnet gik.
- Værdier er ægte JSON, så et tal er et tal og et objekt et objekt. `body` er tekst ligesom ved `send`.
- `error` beskriver fejlens type med de samme tekster som `send` og kopierer aldrig tekst fra selve fejlen. Findes en værdi fra `saves` ikke, er `error` fx `Nothing to save was found at $.importId.` Et script-trin er undtaget: dets `error` starter med scriptets filnavn og har linje og scriptets egen fejltekst, når scriptet selv fejler, fx `map.js:2: No order`.
- `workflow`, `name` og `environment` er navne fra kørslens start og er kun til læsning. Det er id'erne, der henviser til noget.
- Mangler `run.finished`, blev processen stoppet undervejs. Stilhed betyder ikke, at det gik godt.
- Ignorér felter og event-typer, du ikke kender, så formatet kan udvides.

| Exitkode | Betydning for `run` |
|---|---|
| 0 | Alle trin lykkedes |
| 1 | Kørslen startede, men et trin fejlede, eller den blev afbrudt. stdout slutter med `run.finished` |
| 2 | Kørslen startede ikke: forkerte argumenter eller parametre, et ukendt eller ugyldigt workflow, et ukendt miljø eller fejl i tjekket. Intet er sendt, stdout er tom, og der er ingen logfil |

Ved exitkode 2 står fejlen som JSON på stderr. Fejler tjekket, følger problemerne med. `step` er trinnets index, når problemet hører til et trin, og `detail` er et navn, en sti eller et id, aldrig en værdi:

```json
{"error":"Workflow cannot run.","problems":[{"kind":"UsedBeforeSaved","step":1,"detail":"token"}]}
```

| `kind` | Betydning |
|---|---|
| `MissingId` | Workflowet har intet `id` |
| `InvalidName` | Et navn er tomt eller indeholder `{` eller `}` |
| `DuplicateName` | Samme navn findes flere gange blandt parametre og variabler |
| `UnknownParameter` | En given parameter findes ikke i workflowet |
| `MissingParameter` | En parameter uden `default` blev ikke givet |
| `MissingUrl` | Trinnet har ingen URL |
| `NotAVariable` | `saves` gemmer i noget, der ikke er en variabel |
| `InvalidSource` | `from` i `saves` er hverken en gyldig kilde eller en JSON-værdi |
| `UsedBeforeSaved` | Et navn fra workflowet bruges, før det har en værdi |
| `UnknownName` | Et navn i URL, query, headers eller auth findes hverken i workflowet eller i miljøet. I en body bliver sådan et navn stående som skrevet |
| `ScriptNotFound` | Scriptet findes ikke i workflowets mappe, eller navnet er ugyldigt |
| `InvalidScript` | Scriptet har en syntaksfejl. `detail` er fejlen med fil, linje og kolonne, fx `Unexpected token ';' (map.js:2:11)` |
| `MixedStep` | Trinnet har mere end én af `request`, `script` og `delaySeconds` |
| `InvalidRetry` | `retry` står på et trin, der ikke er en request, har kun den ene af `until` og `equals` eller af `stopIf` og `stopEquals`, en ugyldig `until` eller `stopIf`, eller `times` eller `waitSeconds` uden for grænserne. `detail` er `until` |
| `InvalidDelay` | `delaySeconds` er ikke mellem 1 og 300, eller ventetrinnet har `saves`. `detail` er antallet af sekunder |

En ugyldig `workflow.json` angives med fil, JSON-sti og linje:

```json
{"error":"Workflow file is not valid.","file":"C:\\Hoboman\\workflows\\Ordre-sync\\workflow.json","path":"$.steps[0].request","line":7}
```

### Sådan følger en AI en kørsel

1. **Vent på resultatet.** Kør `run` i forgrunden, og læs sidste linje. Det er nok til korte kørsler. Uden UTF-8 som `[Console]::OutputEncoding` læser Windows PowerShell æ, ø og å forkert.

   ```powershell
   $cli = "C:\sti\til\Hoboman\publish\hoboman-cli.exe"
   [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
   $lines = & $cli run Ordre-sync --env Demo --param orderId=o-17
   $code = $LASTEXITCODE
   if ($code -eq 2) { throw "Workflowet startede ikke." }
   $finished = $lines[-1] | ConvertFrom-Json
   "exit=$code outcome=$($finished.outcome)"
   ```

2. **I baggrunden.** Start `run` som en baggrundsopgave. AI'en får besked, når processen slutter, og læser outputtet eller `runFile` fra første linje.
3. **Live pr. trin.** Kør `run` under Claude Codes Monitor, så hver linje bliver en notifikation. En linje kan være stor, fordi bodyen er med, så klip den. Monitor stopper kommandoen efter højst 30 minutter, så start lange kørsler i baggrunden, og følg deres `runFile` i stedet.

   ```bash
   '/c/sti/til/Hoboman/publish/hoboman-cli.exe' run Ordre-sync --param orderId=o-17 2>&1 | grep --line-buffered -o '^.\{0,300\}'
   ```

4. **Følg en kørsel fra appen eller i baggrunden.** Følg den nyeste fil i `runs\<workflowId>\` med `Get-Content -Wait -Encoding UTF8`, og stop ved `run.finished`. Id'et står i `workflow.json`. Uden `-Encoding UTF8` læser Windows PowerShell æ, ø og å forkert.

   ```powershell
   $runs = "C:\sti\til\Hoboman\publish\runs\9bf2fef5-9047-4075-969c-db7c7377a62d"
   $file = Get-ChildItem $runs -Filter *.jsonl | Sort-Object Name | Select-Object -Last 1
   Get-Content -LiteralPath $file.FullName -Wait -Encoding UTF8 | ForEach-Object {
       $line = $_ | ConvertFrom-Json
       '{0} {1} {2} {3}' -f $line.type, $line.index, $line.outcome, $line.status
       if ($line.type -eq 'run.finished') { break }
   }
   ```

## Kæd kald sammen i PowerShell

Gem `Brugere/Hent bruger` med `{{userId}}` i URL'en. Send derefter id'et fra ét svar videre til det næste kald:

```powershell
$cli = "C:\sti\til\Hoboman\publish\hoboman-cli.exe"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$raw = & $cli send "Brugere/Opret bruger"
if ($LASTEXITCODE -ne 0) { throw "Opret bruger fejlede." }
$user = ($raw | ConvertFrom-Json).body | ConvertFrom-Json
@{ userId = $user.id } | ConvertTo-Json | & $cli send "Brugere/Hent bruger" --vars -
```

Tjek `$LASTEXITCODE` før næste kald. Ved exitkode 2 er stdout tom.

I Windows PowerShell 5.1 styrer `[Console]::OutputEncoding` det, du læser fra CLI'et, og `$OutputEncoding` det, du piper ind i det. Uden UTF-8 i `$OutputEncoding` bliver æ, ø og å til `?`, uden at der kommer en fejl.

En stor body sendes fra en fil ved et direkte kald:

```powershell
& $cli send POST https://api.example.com/import --json "@data.json"
```

## Auth

- En gemt request bruger sin egen auth eller auth fra den nærmeste mappe, ligesom i appen.
- OAuth bruger det token, der er gemt for requesten eller mappen i det valgte miljø. Tokens gemmes under miljøets id, så de følger miljøet, også når det omdøbes.
- Tokens gemt før miljøerne fik id, skal hentes igen én gang: client credentials hentes automatisk, og authorization code kræver ét nyt login i Hoboman pr. miljø.
- Ved client credentials henter CLI'et selv et nyt token med den gemte client secret, når tokenet mangler, er udløbet, eller serveren svarer 401. Tokenet gemmes i `secrets.json` ligesom fra appen, og kaldet sendes én gang til.
- Ved authorization code åbner CLI'et aldrig en browser. Kaldet fejler med besked om at hente et token i Hoboman.

## Historik og hvad CLI'et skriver

- Hvert afsendt kald med `send` gemmes i historikken som et kald fra CLI'et, også direkte kald og kald, der fejler undervejs. Trin i et workflow gemmes kun i kørslens logfil. Requesten gemmes, som den er skrevet, med variablerne uudfyldte. De midlertidige værdier gemmes ikke for sig, men står i den gemte adresse, hvis de bruges i vært, port eller sti.
- Fejl, før kaldet sendes, fx forkerte argumenter eller et ukendt miljø, og kald, du selv afbryder, gemmes ikke.
- Hver kørsel med `run` skrives i `runs\<workflowId>\<runId>.jsonl`. Filerne ryddes ikke op.
- CLI'et skriver kun i `history\`, `runs\` og, når det henter et token, i `secrets.json`. Læser det miljøer uden id, giver det dem id i `environments.json` og sletter tokens gemt under miljønavne i `secrets.json`. `list`, `--help` og `--version` skriver ingenting.
- Kan historikken ikke skrives, fx ved fuld disk eller manglende rettigheder, skrives svaret alligevel, og kaldet sendes ikke igen.
- Historikken og `runs\` er ikke krypteret. Headers, du selv skriver, fx `Authorization`, samt bodies, svar, den udfyldte adresse og gemte værdier som tokens kan indeholde hemmeligheder.

## Grænser

- Hele svaret holdes i hukommelsen, og bodies er tekst. Der er ingen streaming eller binære downloads.
- En gemt request kan ikke få en ny body fra kommandolinjen.
