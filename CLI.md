# Hoboman CLI

`hoboman-cli.exe` sender gemte og direkte requests og kører workflows uden GUI'en. Scripts og AI-agenter bruger dermed de samme requests, workflows, miljøer, auth og hemmeligheder som appen, og resultatet kommer som JSON.

## Hvad CLI'et kan

Det kan:

- vise de gemte requests (`list`)
- sende en gemt request med dens egen metode, URL, headers, body, Base64-valg og auth, eller auth fra nærmeste mappe (`send <request>`)
- sende et direkte kald med headers og en JSON- eller tekst-body (`send <METODE> <url>`)
- køre et workflow med parametre og skrive hvert trin som en JSON-linje, mens det kører (`run <workflow>`)
- vælge miljø pr. kald og give midlertidige variabler og parametre
- hente client-credentials-tokens selv, når de mangler, er udløbet eller afvist med 401
- give en exitkode, som et script kan handle på

Det kan ikke:

- logge ind med authorization code. Det kræver en browser, så tokenet hentes i appen
- skifte det valgte miljø, ændre indstillinger eller bruge `credentials.json`
- vise workflows; de er mapperne i `workflows\`
- give en gemt request en anden body, andre headers eller anden auth
- se ugemte ændringer i appen; det bruger filerne, som de er gemt
- gemme binære svar; `body` er altid tekst

## Kom i gang

`install.ps1` udgiver `hoboman-cli.exe` ved siden af `Hoboman.exe` i `publish\`, men tilføjer det ikke til `PATH`. Kald det med fuld sti, og kør det som den samme Windows-bruger som appen, for hemmelighederne er krypteret for den bruger. Appen behøver ikke at være åben.

I Windows PowerShell 5.1 skal begge encodings være UTF-8, ellers bliver æ, ø og å forkerte eller til `?` uden fejl. `[Console]::OutputEncoding` styrer det, du læser fra CLI'et, og `$OutputEncoding` det, du piper ind i det:

```powershell
$cli = "C:\sti\til\Hoboman\publish\hoboman-cli.exe"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
```

De første kommandoer:

```powershell
& $cli --help                                   # hjælp; også fx send --help
& $cli list                                     # gemte requests, fx Dummyjson/Hent mig
& $cli send "Dummyjson/Hent mig" --env Demo     # en gemt request i miljøet Demo
& $cli send GET https://dummyjson.com/products/1
& $cli run Stresstest                          # et workflow med standardværdier
```

## Gode eksempler

Eksemplerne bruger det offentlige test-API dummyjson.com og workflowene Eksempel og Stresstest fra `publish\workflows\`. Skift navnene ud med dine egne.

### Kæd kald sammen

Find en bruger, og send brugerens id videre til næste kald som en midlertidig variabel:

```powershell
$raw = & $cli send GET 'https://dummyjson.com/users/search?q=Emily&limit=1'
if ($LASTEXITCODE -ne 0) { throw "Søgningen fejlede ($LASTEXITCODE)." }
$user = (($raw | ConvertFrom-Json).body | ConvertFrom-Json).users[0]

$raw = & $cli send GET 'https://dummyjson.com/carts/user/{{userId}}' --var "userId=$($user.id)"
(($raw | ConvertFrom-Json).body | ConvertFrom-Json).carts | Select-Object id, total
```

- `body` er tekst, så et JSON-svar skal gennem `ConvertFrom-Json` to gange.
- `{{userId}}` i et direkte kald udfyldes fra `--var`. Bodyen i et direkte kald udfyldes ikke.
- Tjek `$LASTEXITCODE` før næste kald. Ved exitkode 2 er stdout tom.

En JSON-body sendes bedst fra en fil, for Windows PowerShell 5.1 fjerner anførselstegnene i `--json '{"a":1}'`, før CLI'et får den:

```powershell
& $cli send POST https://dummyjson.com/products/add --json "@ny-vare.json" -H "X-Kilde: hoboman-cli"
```

### Kør et workflow, og vis hvordan trinene gik

Stresstest har standardværdier til alle sine parametre, så `& $cli run Stresstest` virker alene. Her får to parametre tal fra stdin, og én får tekst fra kommandolinjen:

```powershell
$lines = @{ pageSize = 10; slowMs = 500 } | ConvertTo-Json | & $cli run Stresstest --params - --param searchTerm=laptop
$code = $LASTEXITCODE
if ($code -eq 2) { throw "Stresstest startede ikke." }
$events = $lines | ForEach-Object { $_ | ConvertFrom-Json }
$events | Where-Object type -eq 'step.finished' | Format-Table index, outcome, status, elapsedMs, attempts, error
"exit=$code outcome=$($events[-1].outcome)"
```

- `--params` beholder typen, så `pageSize` er tallet 10. `--param` giver altid tekst og vinder over `--params`.
- Outputtet er én JSON-linje pr. event, så det parses linje for linje.
- `& $cli run Stresstest --param failCode=418` viser en fejl: trinnet Styret stop fejler med `Stopped as status was 418.`, resten kommer som `step.skipped`, `run.finished` har `outcome` `Failed`, og exitkoden er 1.

### Giv en hemmelig parameter

Eksempel kræver `password`, som ikke har en standardværdi. Giv den gennem stdin, så den ikke havner i shellens historik:

```powershell
$lines = @{ password = Read-Host 'Password' } | ConvertTo-Json | & $cli run Eksempel --params -
if ($LASTEXITCODE -ne 0) { throw "Eksempel fejlede ($LASTEXITCODE)." }
$finished = $lines[-1] | ConvertFrom-Json
"Kurv $($finished.variables.cartId) til $($finished.variables.total)"
```

- Uden `password` er exitkoden 2, og stderr er `{"error":"Workflow cannot run.","problems":[{"kind":"MissingParameter","detail":"password"}]}`. Intet er sendt.
- `run` skriver parametre og gemte værdier i klartekst på stdout og i `runs\`, så rigtige hemmeligheder hører til i trinnets auth i appen.

### Følg en kørsel fra appen

En kørsel startet i appen skrives også i `runs\<workflowId>\`. Følg den nyeste fil, og stop ved `run.finished`. Id'et står i `workflow.json`:

```powershell
$runs = "C:\sti\til\Hoboman\publish\runs\5d3c8f0e-2b7a-4c61-9a1e-7f4b2d9c6e10"
$file = Get-ChildItem $runs -Filter *.jsonl | Sort-Object Name | Select-Object -Last 1
Get-Content -LiteralPath $file.FullName -Wait -Encoding UTF8 | ForEach-Object {
    $line = $_ | ConvertFrom-Json
    '{0} {1} {2} {3}' -f $line.type, $line.index, $line.outcome, $line.status
    if ($line.type -eq 'run.finished') { break }
}
```

Uden `-Encoding UTF8` læser Windows PowerShell æ, ø og å forkert.

## Kommandoer

```text
hoboman-cli list
hoboman-cli send <gemt request> [--env <navn>] [--var <navn=værdi>]... [--vars <fil|->]
hoboman-cli send <METODE> <url> [--env <navn>] [-H "Navn: Værdi"]... [--json <tekst|@fil> | --text <tekst|@fil>] [--var <navn=værdi>]... [--vars <fil|->]
hoboman-cli run <workflow> [--env <navn>] [--param <navn=værdi>]... [--params <fil|->]
hoboman-cli --help
hoboman-cli --version
```

- `list` skriver de gemte requests som stier relativt til `requests\`, med `/` og uden `.json`, én pr. linje og sorteret, fx `Brugere/Hent bruger`. Det er netop den sti, `send` skal have.
- Ét argument efter `send` er en gemt request. To er en metode og en URL.
- `<workflow>` er mappens navn i `workflows\`.
- `--help`, `-h` og `-?` virker både alene og efter en kommando. `--version` virker kun alene og skriver versionen og committen, fx `1.0.0+<commit>`. Uden kommando er det en fejl.
- Options kan stå før eller efter argumenterne, og `--env=Demo` virker også. `--env`, `--json`, `--text`, `--vars` og `--params` må kun gives én gang; `-H`, `--var` og `--param` gentages for hver værdi.
- `-H`, `--json` og `--text` virker kun ved direkte kald, og `--json` og `--text` kan ikke bruges sammen.
- `@fil` læser bodyen fra en UTF-8-fil, og `@@tekst` sender `@tekst`. Relative stier i `@fil`, `--vars fil` og `--params fil` læses fra den mappe, du står i; CLI'ets egne data findes altid ved siden af programmet.

### Direkte kald

- Metoden bruges, som den er skrevet, og URL'en skal være absolut, når variablerne er udfyldt.
- Der er ingen auth ud over de headers, du giver, og ingen body uden `--json` eller `--text`.
- `--json` sender `Content-Type: application/json; charset=utf-8` og `--text` `text/plain; charset=utf-8`. Til XML bruges `--text @fil.xml -H "Content-Type: application/xml"`, som erstatter typen.
- `-H` deles ved første kolon, så en værdi kan indeholde kolon. En header, der gives flere gange, sendes én gang pr. værdi.

## Miljøer og variabler

- `--env` vælger miljø for både `send` og `run` og skal skrives præcis som miljøets navn, også store og små bogstaver. Valget gemmes ikke.
- Uden `--env` bruges det miljø, der er valgt i appen. Er intet valgt, bruges intet miljø, og `{{navne}}` bliver stående. Er det valgte miljø slettet, giver det `Selected environment was not found.`
- Kun variabler, der er slået til i miljøet, udfyldes.
- `--var navn=værdi` sætter en midlertidig variabel og deles ved første `=`. `--vars fil.json` eller `--vars -` (stdin) læser et JSON-objekt, fx `{"userId":42}`. Værdier, der ikke er tekst, indsættes som deres JSON.
- `--var` vinder over `--vars`, som vinder over miljøets værdier, og sidste værdi for et navn vinder. De midlertidige værdier gælder kun det ene kald, gemmes aldrig og bruges også, hvor miljøets variabel er slået fra.
- I en gemt request udfyldes URL, query, headers og auth-felter. Bodyen udfyldes kun, når requesten har **Brug environment-variabler i body** slået til.
- I et direkte kald udfyldes URL og headers, men bodyen sendes, som den er skrevet. Et ukendt `{{navn}}` bliver stående og kan give `Invalid request URL.`
- Giv følsomme værdier gennem `--vars -` eller `--params -` frem for `--var` og `--param`, så de ikke havner i shellens historik.

## Output og exitkoder

Et svar skrives som én linje JSON på stdout, også ved 4xx og 5xx:

```json
{"status":200,"reason":"OK","elapsedMs":123,"size":17,"headers":[{"name":"Content-Type","value":"application/json"}],"body":"{\"id\":\"42\"}"}
```

- `body` er svaret som tekst. Er det JSON, skal det derfor gennem `ConvertFrom-Json` endnu en gang.
- `elapsedMs` omfatter hentningen af bodyen, og `size` er bodyens bytes. `headers` har både svarets og indholdets headers, én pr. værdi.
- stdout og stderr er UTF-8 uden BOM. `list`, `--help` og `--version` skriver almindelig tekst, alt andet JSON.

Fejl, før der kommer et svar, skrives som `{"error":"..."}` på stderr, og stdout er tom. En gemt request, der ikke er gyldig JSON, angives med fil, JSON-sti og linje, fx `{"error":"Saved request file is not valid.","file":"...","path":"$.headers","line":4}`.

| Exitkode | Betydning |
|---|---|
| 0 | Svar med status 2xx samt `list`, `--help` og `--version` |
| 1 | Svar med anden status |
| 2 | Intet svar: forkerte argumenter, filer, der ikke kan læses, ukendt request eller miljø, netværksfejl, timeout, afbrudt kald eller et token, der ikke kan hentes |

| `error` | Betydning |
|---|---|
| `Invalid command arguments. Use --help for usage.` | Kommandoen er forkert, fx en ukendt option eller en option givet to gange |
| `Invalid variable input.` / `Invalid parameter input.` | `--var`/`--param` mangler `=`, eller `--vars`/`--params` er ikke et JSON-objekt |
| `Input could not be read.` | En fil i `@fil`, `--vars` eller `--params` kunne ikke læses |
| `Saved request could not be loaded.` / `Saved request file is not valid.` | Requesten findes ikke, eller filen er ugyldig |
| `Workflow could not be loaded.` / `Workflow file is not valid.` | Workflowet findes ikke, eller `workflow.json` er ugyldig |
| `Selected environment was not found.` | Miljøet findes ikke |
| `Environment settings could not be read.` | `settings.json` eller `environments.json` kan ikke læses |
| `Workflow cannot run.` | Tjekket fejlede, se [Workflows](#workflows) |
| `Invalid request URL.` / `Invalid request input.` | URL'en er ugyldig, eller en metode, header eller værdi er ugyldig |
| `Required authentication secret is missing.` | Password eller token til Basic eller Bearer er ikke gemt |
| `Fetch a new OAuth token in Hoboman before sending this request.` | Der er intet gyldigt OAuth-token, og det kan ikke hentes uden appen |
| `Request body could not be encoded.` | Bodyen kunne ikke Base64-encodes |
| `Network request failed.` / `Request timed out.` | Serveren kunne ikke nås eller svarede ikke inden for 100 sekunder |
| `Request was cancelled.` | Kaldet blev afbrudt med Ctrl+C |
| `Request failed.` | En anden fejl |

`send` tjekker i denne rækkefølge: argumenter, variabler, requesten, miljøet og så kaldet. En ukendt request meldes derfor før et ukendt miljø.

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

- `run` bruger filerne, som de er gemt. Ugemte ændringer i appens workflow-editor ses ikke.
- `id` er workflowets eget id. Kørslerne gemmes under det i `runs\`, så de følger med, når mappen omdøbes.
- Et trin har præcis én af `request`, `script` og `delaySeconds`, og kan desuden have `retry` og `saves`.
- `request` har de samme felter som en gemt request: `method` (standard `GET`), `url`, `query`, `headers`, `bodyKind` (`None`, `Json`, `Xml` eller `Text`, standard `None`), `body`, `useEnvironmentVariablesInBody` (standard `true`), `base64` og `auth`. Workflowet bruger ikke gemte requests fra samlingerne, og i appen redigeres trinnet med samme editor som en fane, også Auth.
- `auth` er et objekt med `kind`: `None` (standard), `Inherit`, `Basic`, `Bearer` eller `OAuth2`, fx `"auth": { "kind": "Inherit" }` på et trin og `"auth": { "kind": "OAuth2", "oAuth": { "grant": "ClientCredentials", "tokenUrl": "https://auth.{{env}}.{{site}}.com/oauth2/token", "clientId": "…", "scope": "…", "clientAuthentication": "BasicHeader" } }` på workflowet. Basic har også `userName`. `Inherit` bruger workflowets egen `auth`, som står øverst i `workflow.json` ved siden af `steps`, ligesom en request arver fra sin mappe. Et trin kan altid have sin egen auth i stedet, fx til et andet API. Har workflowet ingen `auth`, sender et trin med `Inherit` ingen.
- Passwords, tokens og client secrets står aldrig i `workflow.json`. De gemmes krypteret i `secrets.json` under `id` i trinnets `request` og, ved `Inherit`, under workflowets `id`. Auth kan også gives som header, fx `Authorization: Bearer {{token}}` med et token fra et tidligere trin.
- `id` i trinnets `request` sættes af appen, første gang workflowet gemmes med trinnet. Auth med hemmeligheder virker derfor først i `run`, når hemmeligheden er skrevet under Auth i appen, og workflowet er gemt. Skriv ikke `id` direkte på trinnet; det gør filen ugyldig. Kopiér aldrig et `id` fra en gemt request eller et andet trin, for så deler de hemmeligheder, og sletter man den ene, kan den andens forsvinde.
- Ved client credentials henter `run` selv et nyt token til trinnet eller workflowet, når det mangler, er udløbet, eller serveren svarer 401, ligesom `send`. Mangler client secret, fejler trinnet med `Fetch a new OAuth token in Hoboman before sending this request.` Authorization code kræver, at brugeren henter et token under Auth på trinnet eller workflowet i Hoboman.
- `name` er valgfrit og vises i appen og i events. Uden navn bruges scriptets filnavn, `Wait 30 seconds` for en ventetid eller metoden og requestens adresse uden skema og query, fx `POST dummyjson.com/auth/login`.
- Parametre gives ved start og kan ikke gemmes i. En parameters `default` kan være enhver JSON-værdi, og uden `default` er parameteren påkrævet. Variabler er de navne, trinnene gemmer i med `saves`, og får deres værdi derfra. En variabel kan også have en `default`, som den har, indtil et trin gemmer i den. Appen skriver listen over variabler ud fra trinnene, når workflowet gemmes, og beholder deres `default`, så en fast værdi, som intet trin gemmer i, gives som en parameter med `default`. Et trin kan også gemme en fast værdi i en variabel, se `from` nedenfor.
- Et trin kan i stedet for `request` have `"script": "map.js"`, en JavaScript-fil i workflowets mappe. Scriptet får alle parametre, variabler og miljøets variabler i `vars`, som ikke kan ændres. Et navn, workflowet selv har, vinder over miljøet, som i en request, og det, det returnerer, er trinnets output. Outputtet gemmes med `saves` som et svar, fx `"from": "$"` eller `"$.id"`. I events har trinnet `JS` som `method`, en tom `address` og outputtet som `body`. Returnerer scriptet intet, fejler trinnet kun, hvis det har noget i `saves`, med `map.js returned nothing to save.` Et script kører som strict JavaScript uden adgang til filer, netværk eller .NET og stoppes efter 5 sekunder, eller når det holder mere end 512 MB hukommelse. Tal over 2^53 kan ændre sig, når de går gennem et script.
- Et request-trin kan gentages, indtil svaret er klar, fx mens et API laver noget færdigt i baggrunden: `"retry": { "until": "$.result.status", "equals": "succeeded", "times": 60, "waitSeconds": 5 }`.
  - Svaret er klar, når det er 2xx, og alt i `saves` findes. Med `until` skal værdien dér også være `equals`, uden hensyn til store og små bogstaver. `until` er en kilde i svaret som `from` i `saves` (`$`, en sti, `header:Navn` eller `status`), men ikke en fast værdi, og `until` og `equals` gives sammen eller slet ikke.
  - Med `"stopIf": "$.result.status", "stopEquals": "failed"` fejler trinnet med det samme, når værdien dér er `stopEquals`, uden hensyn til store og små bogstaver, fx når det, der ventes på, er fejlet. `error` er så `Stopped as $.result.status was failed.` `stopIf` er en kilde i svaret som `until`, og `stopIf` og `stopEquals` gives sammen eller slet ikke.
  - `times` er det samlede antal forsøg (1-100, standard 30), og `waitSeconds` pausen imellem (0-300, standard 5). Værdierne gemmes kun fra det svar, der var klar.
  - Alle svar prøves igen og desuden `Network request failed.` og `Request timed out.` Alle andre fejl, fx en ugyldig URL eller en manglende hemmelighed, stopper trinnet med det samme.
  - Er svaret ikke klar efter sidste forsøg, fejler trinnet med det sidste svar: et svar, der ikke er 2xx, har sin `status` uden `error`, en manglende værdi giver `Nothing to save was found at <sti>.`, og en `until`-værdi, der ikke passer, giver `The answer was not ready after 60 attempts.` Ender sidste forsøg i en fejl, fx en timeout, står fejlen i `error`. `attempts` står på `step.finished` i alle tilfældene.
  - Gentag ikke en POST eller anden request, der opretter noget, medmindre API'et tåler det. Et forsøg, der fik en fejl, kan være udført hos serveren alligevel.
- Et trin kan også bare vente: `{ "name": "Vent", "delaySeconds": 30 }`. Det venter mellem 1 og 300 sekunder, har `WAIT` som `method` og en tom `address` i events og kan ikke have `saves`. Afbrydes kørslen, stopper ventetiden med det samme.
- `from` i `saves` er `$` for hele bodyen (rå tekst, hvis den ikke er JSON), en sti som `$.data.items[0].id`, `header:Navn`, `status` eller en fast JSON-værdi, der gemmes, som den er, fx `"from": "1"` for tallet 1 og `"from": "\"ja\""` for teksten ja. En sti kan ikke bruge `[*]`. Der gemmes kun efter et 2xx-svar, og et trin gemmer alle sine værdier eller ingen.
- `{{navn}}` udfyldes i URL, query og headers, i Basic-brugernavn og -password og i Bearer-token og i body, når `useEnvironmentVariablesInBody` er `true`. Navnet får sin værdi fra workflowets parametre og variabler. Kun et navn, som workflowet ikke selv har, hentes fra miljøet. Tekst indsættes uændret, og alt andet som kompakt JSON. OAuth-felterne udfyldes kun fra miljøet.
- `--param navn=værdi` giver en parameter som tekst og kan gentages. `--params fil.json` eller `--params -` (stdin) læser et JSON-objekt, hvor værdierne beholder deres type, fx `{"orderId":"o-17","pageSize":50}`. `--param` vinder over `--params`.
- Før første kald tjekkes hele workflowet: at hvert trin har en URL, at parametrene er kendte, at de påkrævede er givet, og at hvert `{{navn}}` i en request har en værdi, når trinnet kører. Et navn, der kun står i bodyen, og som hverken workflowet eller miljøet har, bliver stående som skrevet og tjekkes ikke, så fx en Handlebars-template kan bruge sine egne `{{navne}}`. Navne, et script læser i `vars`, tjekkes ikke. Fejler tjekket, sendes intet. Der er ingen måde kun at tjekke eller kun at køre ét trin.
- Trinene køres ét ad gangen i rækkefølge. Et trin fejler ved en status uden for 2xx, ved en fejl før svaret, eller hvis en værdi i `saves` ikke findes. Så springes resten over.

### Events

Kørslen skriver én JSON-linje pr. event på stdout og de samme linjer i `runs\<workflowId>\<runId>.jsonl`. Linjerne er UTF-8 uden BOM, slutter med `\n` og flushes én ad gangen, så en læser ser dem med det samme. `runId` er starttidspunktet i UTC og fire tilfældige hex-cifre.

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
| `run.started` | Altid første linje: `runId`, `workflowId`, `workflow` (mappens navn), `environment` (tom uden miljø), `runFile` (fuld sti til logfilen) og `parameters`, også dem med standardværdi |
| `step.started` | `index` (første trin er 0), `name` (trinnets navn), `method` og `address` (vært, port og sti uden skema og query; tom for script og ventetid) |
| `step.finished` | `outcome` (`Succeeded` eller `Failed`), svaret med samme navne som ved `send`, `attempts` ved et trin med `retry`, `error` ved en fejl og `saved` med de gemte værdier. En ventetid har kun `index`, `outcome` og `elapsedMs` |
| `step.retrying` | Et forsøg, der ikke var klar, før trinnet prøves igen: `index`, `attempt` (forsøgets nummer), `status` og `value` (det, `until` gav, højst 200 tegn) eller `error` ved en netværksfejl. Uden body |
| `step.skipped` | Et trin, der ikke blev kørt efter en fejl eller en afbrydelse |
| `step.cancelled` | Trinnet, der kørte, da kørslen blev afbrudt |
| `run.finished` | Altid sidste linje: `outcome` (`Succeeded`, `Failed` eller `Cancelled`), `elapsedMs`, `steps` og `variables` med parametre og variabler, der fik en værdi |

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

En ugyldig `workflow.json`, fx med en ukendt eller stavet forkert egenskab, angives med fil, JSON-sti og linje:

```json
{"error":"Workflow file is not valid.","file":"C:\\Hoboman\\workflows\\Ordre-sync\\workflow.json","path":"$.steps[0].request","line":7}
```

`run` tjekker i denne rækkefølge: argumenter, parametre, `workflow.json`, miljøet, tjekket og så kørslen.

### Sådan følger en AI en kørsel

1. **Vent på resultatet.** Kør `run` i forgrunden, og læs sidste linje. Det er nok til korte kørsler.

   ```powershell
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

4. **Følg en kørsel fra appen eller i baggrunden** som i [eksemplet ovenfor](#følg-en-kørsel-fra-appen).

Ctrl+C afbryder pænt: `send` slutter med `Request was cancelled.` og exitkode 2, og `run` med `step.cancelled`, `step.skipped` for resten og `run.finished` med `Cancelled` og exitkode 1. Dræbes processen i stedet, kommer der ingen `run.finished`.

En færdig instruktion til AI-agenter står i [AI-PROMPT.md](AI-PROMPT.md).

## Auth

- En gemt request bruger sin egen auth eller auth fra den nærmeste mappe, ligesom i appen.
- OAuth bruger det token, der er gemt for requesten eller mappen i det miljø, kaldet bruger. Tokens gemmes under miljøets id, så de følger miljøet, også når det omdøbes. Hent derfor tokenet i appen med det samme miljø valgt, som CLI'et bruger.
- Et token, der udløber inden for 30 sekunder, regnes som udløbet.
- Ved client credentials henter CLI'et selv et nyt token med den gemte client secret, når tokenet mangler, er udløbet, eller serveren svarer 401. Tokenet gemmes i `secrets.json` ligesom fra appen, og kaldet sendes én gang til. Kan tokenet ikke hentes efter en 401, er svaret den 401.
- Ved authorization code åbner CLI'et aldrig en browser. Et token hentet i appen bruges, til det udløber, og så fejler kaldet med `Fetch a new OAuth token in Hoboman before sending this request.`
- OAuth-felterne udfyldes kun fra miljøet, aldrig fra `--var` eller et workflows værdier.
- CLI'et læser ikke `credentials.json`. En credential, der er valgt i appen, er kopieret ind i auth'en på requesten, mappen, workflowet eller trinnet og virker, når den er gemt dér.
- Tokens gemt før miljøerne fik id, skal hentes igen én gang: client credentials hentes automatisk, og authorization code kræver ét nyt login i Hoboman pr. miljø.

## Historik og hvad CLI'et skriver

- Hvert kald med `send` gemmes i historikken som et kald fra CLI'et, også direkte kald og kald, der fejler, mens Hoboman bygger eller sender det, fx en ugyldig URL eller en manglende hemmelighed. Den åbne app viser det med det samme i **Historik** med mærket **CLI**. Trin i et workflow gemmes kun i kørslens logfil.
- Fejl, før kaldet bygges, gemmes ikke: forkerte argumenter eller variabler, et direkte kalds `-H` eller `@fil`, en request, der ikke kan indlæses, og problemer med miljøet. Kald, du selv afbryder, gemmes heller ikke.
- Requesten gemmes, som den er skrevet, med variablerne uudfyldte. De midlertidige værdier gemmes ikke for sig, men står i den gemte adresse, hvis de bruges i vært, port eller sti.
- Hver kørsel med `run` skrives i `runs\<workflowId>\<runId>.jsonl`. Filerne ryddes ikke op.
- CLI'et skriver kun i `history\`, `runs\` og, når det henter et token, i `secrets.json`. Læser det miljøer uden id, giver det dem id i `environments.json` og sletter tokens gemt under miljønavne i `secrets.json`. `list`, `--help` og `--version` skriver ingenting, og CLI'et skriver ingen logfiler.
- Kan historikken ikke skrives, fx ved fuld disk eller manglende rettigheder, skrives svaret alligevel, og kaldet sendes ikke igen.
- Historikken og `runs\` er ikke krypteret. Headers, du selv skriver, fx `Authorization`, samt bodies, svar, den udfyldte adresse og gemte værdier som tokens kan indeholde hemmeligheder.

## Grænser

- Hele svaret holdes i hukommelsen, og bodies er tekst, så binære svar bliver ødelagt. Gem dem med **Gem** i appens svar i stedet.
- Et kald venter højst 100 sekunder. Det kan ikke ændres.
- Redirects følges, men cookies bruges ikke. **Ignorér certifikatfejl** fra appens indstillinger gælder også CLI'et og tokenhentning.
- En gemt request kan ikke få en ny body eller nye headers fra kommandolinjen, og et direkte kald kan kun have en JSON- eller tekst-body.
