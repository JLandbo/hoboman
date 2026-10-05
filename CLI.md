# Hoboman CLI

`hoboman-cli.exe` sender gemte og direkte requests og kører workflows uden GUI'en. Scripts bruger dermed de samme requests, workflows, miljøer, auth og hemmeligheder som appen, og resultatet kommer som JSON. AI-agenter bruger ikke kommandolinjen, men MCP-serveren, se [MCP](#mcp).

## Hvad CLI'et kan

Det kan:

- vise de gemte requests (`list`), mapper (`list folders`), workflows (`list workflows`) og miljøer (`list environments`)
- vise en request, mappe, et workflow med dets scripts eller et miljø som JSON (`show`)
- oprette, rette, omdøbe, flytte og slette requests, mapper og workflows (`new`, `update`, `rename`, `move`, `delete`)
- oprette, rette, omdøbe og slette miljøer (`new environment`, `update`, `rename`, `delete`)
- vise de nyeste kald i historikken og et enkelt kald (`history`)
- tjekke et workflow uden at sende noget (`check <workflow>`)
- vise og følge kørsler, også dem startet i appen (`log <workflow>`)
- sende en gemt request med dens egen metode, URL, headers, body, Base64-valg og auth, eller auth fra nærmeste mappe (`send <request>`)
- sende et direkte kald med headers og en JSON- eller tekst-body (`send <METODE> <url>`)
- køre et workflow med parametre og skrive hvert trin som en JSON-linje, mens det kører (`run <workflow>`)
- vælge miljø pr. kald og give midlertidige variabler og parametre
- hente client-credentials-tokens selv, når de mangler, er udløbet eller afvist med 401
- gemme et svar byte for byte i en fil, fx en PDF (`--out`)
- give en exitkode, som et script kan handle på
- give AI-programmer kommandoerne som værktøjer over MCP (`mcp`)

Det kan ikke:

- logge ind med authorization code. Det kræver en browser, så tokenet hentes i appen
- skifte det valgte miljø, ændre indstillinger eller bruge `credentials.json`
- ændre en mappes auth; `show` viser den, men den rettes i appen
- rette en request eller et workflow, hvis fil ikke kan læses; `show` fortæller hvor, men det rettes i appen eller af brugeren
- sende en gemt request med en anden body, andre headers eller anden auth end den gemte; ret den med `update` i stedet
- se ugemte ændringer i appen; det bruger filerne, som de er gemt

## Kom i gang

**Antivirus.** `hoboman-cli.exe` er et .NET-program i én fil, der pakker sig selv ud, når det starter. Den slags bliver nogle gange fejlagtigt markeret som mistænkeligt af antivirus. Sker det, skal `hoboman-cli.exe` kodesigneres med et kodesigneringscertifikat (Authenticode, fx med `signtool sign`), før det bruges. Det samme gælder `Hoboman.exe`. Slå ikke antivirus fra, og undtag ikke mappen uden godkendelse fra den, der står for maskinens sikkerhed.

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
& $cli list                                     # gemte requests, fx <id><tab>Dummyjson/Hent mig
& $cli list workflows                           # workflows, fx <id><tab>Eksempel
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

### Hent en PDF

`body` er tekst, så en PDF eller et billede gemmes med `--out`. Her gemmer et workflow adressen på en PDF i variablen `pdfUrl`, som det næste kald henter:

```powershell
$lines = & $cli run Udskrift
if ($LASTEXITCODE -ne 0) { throw "Udskrift fejlede ($LASTEXITCODE)." }
$pdfUrl = ($lines[-1] | ConvertFrom-Json).variables.pdfUrl
$raw = & $cli send GET $pdfUrl --out dokument.pdf
if ($LASTEXITCODE -ne 0) { throw "PDF'en kunne ikke hentes ($LASTEXITCODE)." }
($raw | ConvertFrom-Json).file
```

- Filen får svarets bytes, præcis som serveren sendte dem, og stdout har `file` med den fulde sti i stedet for `body`.
- Filen skrives også ved 4xx og 5xx, så tjek exitkoden, før du bruger den.

### Kør et workflow, og vis hvordan trinnene gik

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

`log` læser også kørsler startet i appen. Den følger den nyeste og stopper selv ved `run.finished`:

```powershell
& $cli log Ordre-sync --last --follow | ForEach-Object {
    $line = $_ | ConvertFrom-Json
    '{0} {1} {2} {3}' -f $line.type, $line.index, $line.outcome, $line.status
}
```

## Kommandoer

```text
hoboman-cli list [workflows|folders|environments]
hoboman-cli show <id|sti|navn>
hoboman-cli new request <mappe> <navn> [--method <METODE>] [--url <url>] [-H "Navn: Værdi"]... [--json <tekst|@fil> | --text <tekst|@fil>]
hoboman-cli new folder <mappe> <navn>
hoboman-cli new workflow <navn>
hoboman-cli new environment <navn>
hoboman-cli rename <id|sti|navn> <nyt navn>
hoboman-cli move <id|sti> <mappe>
hoboman-cli delete <id|sti|navn> --yes
hoboman-cli update <id|sti|navn> --file <fil|->
hoboman-cli history [<navn> | --count <antal>]
hoboman-cli send <gemt request> [--env <navn>] [--var <navn=værdi>]... [--vars <fil|->] [--out <fil>]
hoboman-cli send <METODE> <url> [--env <navn>] [-H "Navn: Værdi"]... [--json <tekst|@fil> | --text <tekst|@fil>] [--var <navn=værdi>]... [--vars <fil|->] [--out <fil>]
hoboman-cli run <workflow> [--env <navn>] [--param <navn=værdi>]... [--params <fil|->]
hoboman-cli check <workflow> [--env <navn>] [--param <navn=værdi>]... [--params <fil|->]
hoboman-cli log <workflow> [(<runId> | --last) [--follow] | --count <antal>]
hoboman-cli mcp
hoboman-cli --help
hoboman-cli --version
```

- `list` skriver én linje pr. gemt request, sorteret efter stien: id'et, en tabulator og stien gennem mapperne med `/`, fx `3f2c…<tab>Brugere/Hent bruger`. Del linjen ved den første tabulator, for et navn kan selv indeholde `/`.
- `list workflows` skriver på samme måde id'et og navnet på hvert workflow, sorteret efter navnet.
- `list folders` skriver id'et og stien på hver mappe, fx `<id><tab>Shop/Orders`.
- `list environments` skriver id'et og navnet på hvert miljø, sorteret efter navnet. Det miljø, der er valgt i appen og bruges uden `--env`, ender med `<tab>selected`.
- En request eller et workflow, hvis fil ikke kan læses, står med id'et og intet navn. `show` med id'et fortæller, hvad der er galt med filen.

### Vise, oprette, rette, omdøbe, flytte og slette

- `show` skriver det gemte som JSON på stdout: `{"request": …}`, `{"folder": …}`, `{"environment": …}` med variabler og værdier, eller `{"workflow": …, "scripts": {"navn.js": "kode"}}` med alle `.js`-filer i workflowets mappe. Hemmeligheder står aldrig i filerne og derfor heller ikke her. Målet er et id eller en sti/et navn som ved `rename`. Miljøer findes på deres navn eller id. Har en request, mappe eller et workflow samme navn, giver det `Target is ambiguous. Use its id.`, så en ændring aldrig rammer det forkerte.
- `update` erstatter indholdet af en request, et workflow eller et miljø med JSON i præcis den form, `show` skrev, fra en fil eller stdin (`--file -`). Id, navn og mappe bliver, og dermed hemmeligheder, historik og kørsler. `id`, `name` og `folderId` i inputtet skal være uændrede; navn og mappe ændres med `rename` og `move`. Et request-trin uden `id` i sit `request` får et nyt; et `request.id`, workflowet ikke havde, afvises, da det ville dele et andet trins eller en requests hemmeligheder. Fjernes et trin, glemmes dets hemmeligheder. Scripts i `scripts` skrives, et script givet som `null` slettes, og andre bliver liggende. Et script, et trin bruger, kan ikke slettes. Et miljø rettes på samme måde med `{"environment": …}`, hvor variablerne erstattes. Inputtet læses med de samme regler som filerne, så en ukendt egenskab giver `Input is not valid.` med `path` og `line`. En mappe kan ikke rettes; dens auth ændres i appen. `update` tjekker ikke workflowet; brug `check`.
- `new environment <navn>` laver et tomt miljø, og `rename` og `delete --yes` virker også på miljøer. Navne skal være unikke uden hensyn til store og små bogstaver, ellers giver det `Environment name is taken.` `delete` glemmer også miljøets OAuth-tokens og de credentials, der hører til det, som appen gør. Et miljø kan ikke flyttes. Slettes det miljø, der er valgt i appen, med `delete`, er intet valgt længere, og `send`, `run` og `check` uden `--env` bruger intet miljø.
- `history` skriver de nyeste kald, som standard 20, nyeste først: `navn<tab>tid<tab>kilde<tab>metode<tab>status<tab>adresse`. Tid er UTC, kilde er `App` eller `Cli`, og status er fejlens art, når kaldet ikke fik noget svar. `--count <antal>` giver et andet antal, mindst 1, og kan ikke bruges sammen med et navn. `history <navn>` skriver hele kaldet som `{"call": …}` med request, svar eller fejl og miljø.

- `<mappe>` er en mappes id eller sti fra `list folders`, eller `.` for øverste niveau. Det, der ændres, er et id eller en sti/et navn fra `list`, `list folders`, `list workflows` eller `list environments`.
- `new request` laver en request, der arver auth fra sin mappe. Metoden er `GET`, når `--method` ikke gives. `-H`, `--json` og `--text` virker som ved et direkte kald, og `bodyKind` bliver `Json`, `Text` eller `None`.
- `move` flytter en request eller mappe. En mappe kan ikke flyttes ind i sig selv, og et workflow kan ikke flyttes.
- `delete` sletter en request, en mappe med alt i den eller et workflow, og deres hemmeligheder. Uden `--yes` slettes intet, og fejlen `Deleting needs confirmation.` fortæller, hvor mange mapper og requests der ville forsvinde. `folders` tæller mappen selv med, og en enkelt request giver `"requests":1`.
- Navne må ikke være tomme, kun mellemrum eller indeholde kontroltegn som linjeskift og tabulator. Matcher en sti eller et navn flere ting, giver det `Target is ambiguous. Use its id.`.
- Ved succes skrives `{"id": ..., "path": ...}` på stdout med exitkode 0. Ved fejl skrives fejlen på stderr med exitkode 2, og intet er ændret. Nye requests og mapper står sidst i deres mappe.
- `<gemt request>` og `<workflow>` er et id eller en sti/et navn fra `list`, uden forskel på store og små bogstaver. Navne behøver ikke være unikke; har flere samme sti eller navn, giver det en fejl, og så skal id'et bruges.
- Ét argument efter `send` er en gemt request. To er en metode og en URL.
- `--out <fil>` gemmer svarets body byte for byte i en ny fil. Findes filen, sendes intet, og det giver `Output file already exists.` Relative stier læses fra den mappe, du står i, og mappen skal findes.
- `--out`, `@fil`, `--vars <fil>`, `--params <fil>` og `--file <fil>` må ikke pege ind i Hobomans mappe, ellers giver det `Hoboman's own files cannot be used.`, og intet sendes eller ændres. Hobomans filer nås kun gennem kommandoerne. Stien sammenlignes, som den er skrevet, så et link eller et kort 8.3-navn ind i mappen fanges ikke.
- `--help`, `-h` og `-?` virker både alene og efter en kommando. `--version` virker kun alene og skriver versionen og committen, fx `1.0.0+<commit>`. Uden kommando er det en fejl.
- Options kan stå før eller efter argumenterne, og `--env=Demo` virker også. Options med en værdi må kun gives én gang, undtagen `-H`, `--var` og `--param`, som gentages for hver værdi. `--yes`, `--last` og `--follow` må gerne gentages.
- `-H`, `--json` og `--text` virker kun ved direkte kald og `new request`, og `--json` og `--text` kan ikke bruges sammen.
- `@fil` læser bodyen fra en UTF-8-fil, og `@@tekst` sender `@tekst`. Relative stier i `@fil`, `--vars fil` og `--params fil` læses fra den mappe, du står i; CLI'ets egne data findes altid ved siden af programmet.

### Direkte kald

- Metoden bruges, som den er skrevet, og URL'en skal være absolut, når variablerne er udfyldt.
- Der er ingen auth ud over de headers, du giver, og ingen body uden `--json` eller `--text`.
- `--json` sender `Content-Type: application/json; charset=utf-8` og `--text` `text/plain; charset=utf-8`. Til XML bruges `--text @fil.xml -H "Content-Type: application/xml"`, som erstatter typen.
- `-H` deles ved første kolon, så en værdi kan indeholde kolon. En header, der gives flere gange, sendes som én header med værdierne adskilt af komma.

## Miljøer og variabler

- `--env` vælger miljø for både `send` og `run` og skal skrives præcis som miljøets navn, også store og små bogstaver. Valget gemmes ikke.
- Uden `--env` bruges det miljø, der er valgt i appen. Er intet valgt, bruges intet miljø, og `{{navne}}` bliver stående. Er det valgte miljø slettet i appen, giver det `Selected environment was not found.`, indtil brugeren vælger et andet. `--env` tager kun navnet, ikke id'et.
- Kun variabler, der er slået til i miljøet, udfyldes.
- Miljøer oprettes, rettes, omdøbes og slettes med `new environment`, `show` og `update`, `rename` og `delete`, se [Vise, oprette, rette, omdøbe, flytte og slette](#vise-oprette-rette-omdøbe-flytte-og-slette).
- `--var navn=værdi` sætter en midlertidig variabel og deles ved første `=`. `--vars fil.json` eller `--vars -` (stdin) læser et JSON-objekt, fx `{"userId":42}`. Værdier, der ikke er tekst, indsættes som deres JSON.
- `--var` vinder over `--vars`, som vinder over miljøets værdier, og sidste værdi for et navn vinder. De midlertidige værdier gælder kun det ene kald, gemmes aldrig og bruges også, hvor miljøets variabel er slået fra.
- I en gemt request udfyldes URL, query, headers og auth-felter. Bodyen udfyldes kun, når requesten har **Brug environment-variabler i body** slået til.
- I et direkte kald udfyldes URL og headers, men bodyen sendes, som den er skrevet. Et ukendt `{{navn}}` bliver stående og kan give `Invalid request URL.`
- Giv følsomme værdier gennem `--vars -` eller `--params -` frem for `--var` og `--param`, så de ikke havner i shellens historik.

## Output og exitkoder

Et svar skrives som én linje JSON på stdout, også ved 4xx og 5xx:

```json
{"status":200,"reason":"OK","elapsedMs":123,"size":11,"headers":[{"name":"Content-Type","value":"application/json"}],"body":"{\"id\":\"42\"}"}
```

- `body` er svaret som tekst. Er det JSON, skal det derfor gennem `ConvertFrom-Json` endnu en gang.
- `elapsedMs` omfatter hentningen af bodyen, og `size` er bodyens bytes. `headers` har både svarets og indholdets headers, én pr. værdi.
- stdout og stderr er UTF-8 uden BOM. `list`, `history` uden navn, `log <workflow>` uden kørsel, `--help` og `--version` skriver almindelig tekst. `log` med en kørsel skriver én JSON-linje pr. event, og alt andet skriver JSON.

Med `--out` står `file` med filens fulde sti i stedet for `body`.

Fejl, før der kommer et svar, skrives som `{"error":"..."}` på stderr, og stdout er tom. En gemt request, der ikke er gyldig JSON, angives med fil, JSON-sti og linje, når den sendes med sit id, fx `{"error":"Saved request file is not valid.","file":"...","path":"$.headers","line":4}`.

| Exitkode | Betydning |
|---|---|
| 0 | Svar med status 2xx, en kørsel, hvor alle trin lykkedes, eller en anden kommando, der lykkedes |
| 1 | Svar med anden status, en kørsel, hvor et trin fejlede, eller `log --follow`, der stoppede uden `run.finished` eller med Ctrl+C |
| 2 | Fejl: forkerte argumenter, filer, der ikke kan læses, ukendt request eller miljø, netværksfejl, timeout, afbrudt kald eller et token, der ikke kan hentes. Intet er ændret, men med `send` kan kaldet være sendt og stå i historikken, fx når `--out` ikke kan skrives |

| `error` | Betydning |
|---|---|
| `Invalid command arguments. Use --help for usage.` | Kommandoen er forkert, fx en ukendt option eller en option givet to gange |
| `Invalid variable input.` / `Invalid parameter input.` | `--var`/`--param` mangler `=`, `--vars`/`--params` er ikke et JSON-objekt, eller `-` blev brugt uden noget på stdin |
| `Input could not be read.` | En fil i `@fil`, `--vars`, `--params` eller `update --file` kunne ikke læses, eller `--file -` blev brugt uden noget på stdin |
| `Saved request could not be loaded.` / `Saved request file is not valid.` | Requesten findes ikke, eller filen er ugyldig |
| `Saved request name is ambiguous. Use its id.` | Flere requests har stien. Brug id'et fra `list` |
| `Workflow could not be loaded.` / `Workflow file is not valid.` | Workflowet findes ikke, eller `workflow.json` er ugyldig |
| `Workflow name is ambiguous. Use its id.` | Flere workflows har navnet. Brug id'et fra `list workflows` |
| `Invalid name.` | `new` eller `rename` fik et tomt navn, et med kun mellemrum eller et med kontroltegn |
| `Target could not be found.` / `Target is ambiguous. Use its id.` | Det, der skal vises, rettes, omdøbes, flyttes eller slettes, findes ikke, eller flere har stien eller navnet |
| `Folder could not be found.` / `Folder is ambiguous. Use its id.` | Mappen findes ikke, eller flere har stien. Brug id'et fra `list folders` |
| `A folder cannot be moved into itself.` / `A workflow cannot be moved.` | `move` blev afvist |
| `Deleting needs confirmation.` | `delete` uden `--yes` (i MCP uden `confirm`). `path`, `folders` og `requests` fortæller, hvad der ville blive slettet |
| `File is not valid.` | Filen, der skulle ændres, er ugyldig JSON. `file`, `path` og `line` viser hvor |
| `The change could not be saved.` | En fil kunne ikke skrives eller slettes, fx fordi den er låst |
| `Input is not valid.` | Inputtet til `update` passer ikke til formatet. `path` og `line` viser hvor |
| `Use rename to change the name.` / `Use move to change the folder.` / `Target is not the one in the input.` | `update` fik et andet navn, en anden mappe eller et andet id end målets |
| `Step id is not one of the workflow's.` | Et trin i `update` har et id, workflowet ikke havde, eller to trin har det samme |
| `Invalid script name.` | Et navn i `scripts` er ikke et filnavn, der ender på `.js` |
| `A folder cannot be updated.` | `update` fik en mappe |
| `Environment name is taken.` | Et andet miljø har navnet, også med andre store og små bogstaver |
| `An environment cannot be moved.` | `move` fik et miljø |
| `A script a step uses cannot be deleted.` | `update` gav et script som `null`, som et trin stadig bruger |
| `Call could not be found.` | `history` fik et navn, der ikke er i historikken |
| `Target could not be read.` | Det, `show` skulle vise, kunne ikke læses, fx fordi det lige er slettet |
| `Workflow could not be found.` / `Run could not be found.` / `Run could not be read.` | `log` fandt ikke workflowet eller kørslen, eller kørslens fil kunne ikke læses. Et id slås ikke op, så kørsler kan læses, selv om workflowets fil ikke kan; et ukendt id giver en tom liste |
| `Selected environment was not found.` | Miljøet findes ikke |
| `Environment settings could not be read.` | `settings.json` eller `environments.json` kan ikke læses |
| `Workflow cannot run.` | Tjekket fejlede ved `run` eller `check`, se [Workflows](#workflows) |
| `Invalid request URL.` / `Invalid request input.` | URL'en er ugyldig, eller en metode, header eller værdi er ugyldig |
| `Required authentication secret is missing.` | Password eller token til Basic eller Bearer er ikke gemt |
| `Fetch a new OAuth token in Hoboman before sending this request.` | Der er intet gyldigt OAuth-token, og det kan ikke hentes uden appen |
| `Request body could not be encoded.` | Bodyen kunne ikke Base64-encodes |
| `Network request failed.` / `Request timed out.` | Serveren kunne ikke nås eller svarede ikke inden for 100 sekunder |
| `Request was cancelled.` | Kaldet blev afbrudt med Ctrl+C |
| `Request failed.` | En anden fejl |
| `Output file could not be written.` | Filen i `--out` kunne ikke skrives, fx fordi mappen ikke findes. Kaldet er sendt og står i historikken |
| `Output file already exists.` | Filen i `--out` findes. Intet er sendt |
| `Hoboman's own files cannot be used.` | `--out`, `@fil`, `--vars`, `--params` eller `--file` pegede ind i Hobomans mappe. Intet er sendt eller ændret |

`send` tjekker i denne rækkefølge: argumenter, variabler, requesten, miljøet, filen i `--out` og så kaldet. En ukendt request meldes derfor før et ukendt miljø.

## Workflows

`run` kører et gemt workflow. `<workflow>` er dets id eller navn, fx `Ordre-sync`. Et workflow oprettes i appen eller med `new workflow` og fyldes med `update`. `check` tjekker det med de samme argumenter som `run` uden at sende noget og skriver `{"id", "name"}` med exitkode 0, eller `Workflow cannot run.` med exitkode 2. Sådan ser et workflow ud, som `show` viser det under `workflow`:

```json
{
  "name": "Ordre-sync",
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
- Mappens navn er workflowets id. Kørslerne gemmes under det i `runs\`, så de følger med, når workflowet omdøbes. `name` er navnet, der vises i appen og bruges af `run`.
- Et trin har præcis én af `request`, `script` og `delaySeconds`, og kan desuden have `retry` og `saves`.
- `request` har de samme felter som en gemt request undtagen `name` og `folderId`: `method` (standard `GET`), `url`, `query`, `headers`, `bodyKind` (`None`, `Json`, `Xml` eller `Text`, standard `None`), `body`, `useEnvironmentVariablesInBody` (standard `true`), `base64` og `auth`. Workflowet bruger ikke gemte requests fra samlingerne, og i appen redigeres trinnet med samme editor som en fane, også Auth.
- `auth` er et objekt med `kind`: `None` (standard), `Inherit`, `Basic`, `Bearer` eller `OAuth2`, fx `"auth": { "kind": "Inherit" }` på et trin og `"auth": { "kind": "OAuth2", "oAuth": { "grant": "ClientCredentials", "tokenUrl": "https://auth.{{env}}.{{site}}.com/oauth2/token", "clientId": "…", "scope": "…", "clientAuthentication": "BasicHeader" } }` på workflowet. Basic har også `userName`. `Inherit` bruger workflowets egen `auth`, som står øverst i `workflow.json` ved siden af `steps`, ligesom en request arver fra sin mappe. Et trin kan altid have sin egen auth i stedet, fx til et andet API. Har workflowet ingen `auth`, sender et trin med `Inherit` ingen.
- Passwords, tokens og client secrets står aldrig i `workflow.json`. De gemmes krypteret i `secrets.json` under `id` i trinnets `request` og, ved `Inherit`, under workflowets id (mappens navn). Auth kan også gives som header, fx `Authorization: Bearer {{token}}` med et token fra et tidligere trin.
- `id` i trinnets `request` sættes af appen eller `update`, første gang workflowet gemmes med trinnet. Auth med hemmeligheder virker derfor først i `run`, når hemmeligheden er skrevet under Auth i appen, og workflowet er gemt. Skriv ikke `id` direkte på trinnet; det gør filen ugyldig. Kopiér aldrig et `id` fra en gemt request eller et andet trin, for så deler de hemmeligheder, og sletter man den ene, kan den andens forsvinde.
- Ved client credentials henter `run` selv et nyt token til trinnet eller workflowet, når det mangler, er udløbet, eller serveren svarer 401, ligesom `send`. Mangler client secret, fejler trinnet med `Fetch a new OAuth token in Hoboman before sending this request.` Authorization code kræver, at brugeren henter et token under Auth på trinnet eller workflowet i Hoboman.
- Et trins `name` er valgfrit og vises i appen og i events. Uden navn bruges scriptets filnavn, `Wait 30 seconds` for en ventetid eller metoden og requestens adresse uden skema og query, fx `POST dummyjson.com/auth/login`. `{{navne}}` i adressen bliver stående i navnet, mens `address` i events er udfyldt.
- Parametre gives ved start og kan ikke gemmes i. En parameters `default` kan være enhver JSON-værdi, og uden `default` er parameteren påkrævet. Variabler er de navne, trinnene gemmer i med `saves`, og får deres værdi derfra. En variabel kan også have en `default`, som den har, indtil et trin gemmer i den. Appen skriver listen over variabler ud fra trinnene, når workflowet gemmes, og beholder deres `default`, så en fast værdi, som intet trin gemmer i, gives som en parameter med `default`. Et trin kan også gemme en fast værdi i en variabel, se `from` nedenfor.
- Et trin kan i stedet for `request` have `"script": "map.js"`, en JavaScript-fil i workflowets mappe. Scriptet får alle parametre, variabler og miljøets variabler i `vars`, som ikke kan ændres. Et navn, workflowet selv har, vinder over miljøet, som i en request, og det, det returnerer, er trinnets output. Outputtet gemmes med `saves` som et svar, fx `"from": "$"` eller `"$.id"`. Med `"output"` siger trinnet, hvad scriptet returnerer: `Json` (standard, også når den mangler), `Html`, `Xml` eller `Text`. Ved `Json` bliver det returnerede til JSON. Ved de andre skal scriptet returnere en tekst, som bliver outputtet, som den er, ellers fejler trinnet med fx `html.js must return text when its output is Html.` I events har trinnet `JS` som `method`, en tom `address`, outputtet som `body` og outputtets `Content-Type` i `headers`: `application/json`, `text/html; charset=utf-8`, `application/xml; charset=utf-8` eller `text/plain; charset=utf-8`. Returnerer scriptet intet, fejler trinnet kun, hvis det har noget i `saves`, med `map.js returned nothing to save.` Et script kører som strict JavaScript uden adgang til filer, netværk eller .NET og stoppes efter 5 sekunder, eller når det holder mere end 512 MB hukommelse. Tal over 2^53 kan ændre sig, når de går gennem et script.
- Et request-trin kan gentages, indtil svaret er klar, fx mens et API laver noget færdigt i baggrunden: `"retry": { "until": "$.result.status", "equals": "succeeded", "times": 60, "waitSeconds": 5 }`.
  - Svaret er klar, når det er 2xx, og alt i `saves` findes. Med `until` skal værdien dér også være `equals`, uden hensyn til store og små bogstaver. `until` er en kilde i svaret som `from` i `saves` (`$`, en sti, `header:Navn` eller `status`), men ikke en fast værdi, og `until` og `equals` gives sammen eller slet ikke.
  - Med `"stopIf": "$.result.status", "stopEquals": "failed"` fejler trinnet med det samme, når værdien dér er `stopEquals`, uden hensyn til store og små bogstaver, fx når det, der ventes på, er fejlet. `error` er så `Stopped as $.result.status was failed.` `stopIf` er en kilde i svaret som `until`, og `stopIf` og `stopEquals` gives sammen eller slet ikke.
  - `times` er det samlede antal forsøg (1-100, standard 30), og `waitSeconds` pausen imellem (0-300, standard 5). Værdierne gemmes kun fra det svar, der var klar.
  - Alle svar prøves igen og desuden `Network request failed.` og `Request timed out.` Alle andre fejl, fx en ugyldig URL eller en manglende hemmelighed, stopper trinnet med det samme.
  - Er svaret ikke klar efter sidste forsøg, fejler trinnet med det sidste svar: et svar, der ikke er 2xx, har sin `status` uden `error`, en manglende værdi giver `Nothing to save was found at <sti>.`, og en `until`-værdi, der ikke passer, giver `The answer was not ready after 60 attempts.` Ender sidste forsøg i en fejl, fx en timeout, står fejlen i `error`. `attempts` står på `step.finished` i alle tilfældene.
  - Gentag ikke en POST eller anden request, der opretter noget, medmindre API'et tåler det. Et forsøg, der fik en fejl, kan være udført hos serveren alligevel.
- Et trin kan også bare vente: `{ "name": "Vent", "delaySeconds": 30 }`. Det venter mellem 1 og 300 sekunder, har `WAIT` som `method` og en tom `address` i events og kan ikke have `saves`. Afbrydes kørslen, stopper ventetiden med det samme.
- `from` i `saves` er `$` for hele bodyen (som tekst, hvis svarets `Content-Type` ikke er JSON, dvs. `json` eller en type med `+json` som `application/problem+json`, eller hvis bodyen ikke er JSON; en sti læser JSON uanset `Content-Type`), en sti som `$.data.items[0].id`, `header:Navn`, `status` eller en fast JSON-værdi, der gemmes, som den er, fx `"from": "1"` for tallet 1 og `"from": "\"ja\""` for teksten ja. En sti kan ikke bruge `[*]`. Der gemmes kun efter et 2xx-svar, og et trin gemmer alle sine værdier eller ingen.
- `{{navn}}` udfyldes i URL, query og headers, i Basic-brugernavn og -password og i Bearer-token og i body, når `useEnvironmentVariablesInBody` er `true`. Navnet får sin værdi fra workflowets parametre og variabler. Kun et navn, som workflowet ikke selv har, hentes fra miljøet. Tekst indsættes uændret, og alt andet som kompakt JSON. OAuth-felterne udfyldes kun fra miljøet.
- `--param navn=værdi` giver en parameter som tekst og kan gentages. `--params fil.json` eller `--params -` (stdin) læser et JSON-objekt, hvor værdierne beholder deres type, fx `{"orderId":"o-17","pageSize":50}`. `--param` vinder over `--params`.
- Før første kald tjekkes hele workflowet: at hvert trin har en URL, at parametrene er kendte, at de påkrævede er givet, og at hvert `{{navn}}` i en request har en værdi, når trinnet kører. Et navn, der kun står i bodyen, og som hverken workflowet eller miljøet har, bliver stående som skrevet og tjekkes ikke, så fx en Handlebars-template kan bruge sine egne `{{navne}}`. Navne, et script læser i `vars`, tjekkes ikke. Fejler tjekket, sendes intet. `check` laver kun tjekket. Der er ingen måde kun at køre ét trin.
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
| `run.started` | Altid første linje: `runId`, `workflowId`, `workflow` (workflowets navn), `environment` (tom uden miljø), `runFile` (fuld sti til logfilen, som `log` læser) og `parameters`, også dem med standardværdi |
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
| `InvalidOutput` | `output` står på et trin, der ikke er et script. `detail` er værdien |

En ugyldig `workflow.json`, fx med en ukendt eller stavet forkert egenskab, angives med fil, JSON-sti og linje:

```json
{"error":"Workflow file is not valid.","file":"C:\\Hoboman\\workflows\\9bf2fef5-9047-4075-969c-db7c7377a62d\\workflow.json","path":"$.steps[0].request","line":7}
```

`run` tjekker i denne rækkefølge: argumenter, parametre, `workflow.json`, miljøet, tjekket og så kørslen.

### Følg en kørsel

Kørsler fra appen og fra `run` i baggrunden følges med `log`, som i [eksemplet ovenfor](#følg-en-kørsel-fra-appen). `log <workflow>` giver `runId<tab>start<tab>outcome` for de nyeste kørsler, som standard 20, nyeste først, med `start` i UTC og `outcome` som `-`, hvis kørslen kører eller blev stoppet uden `run.finished`. `log <workflow> <runId>` eller `--last` skriver kørslens events, og `--follow` venter på nye, til `run.finished` kommer (exitkode 0). Skriver intet længere i kørslens fil, uden at `run.finished` er kommet, fordi kørslen blev dræbt, stopper `--follow` med exitkode 1. Ctrl+C stopper den også med exitkode 1. `--count <antal>` giver et andet antal kørsler, mindst 1, og kan ikke bruges sammen med en kørsel.

Ctrl+C afbryder pænt: `send` slutter med `Request was cancelled.` og exitkode 2, og `run` med `step.cancelled`, `step.skipped` for resten og `run.finished` med `Cancelled` og exitkode 1. Dræbes processen i stedet, kommer der ingen `run.finished`.

## MCP

`hoboman-cli mcp` gør CLI'et til en MCP-server, så et AI-program kan bruge kommandoerne som værktøjer uden en shell. MCP er en åben standard, så det kan være ethvert program, der understøtter lokale MCP-servere (stdio), fx Claude Code, Claude Desktop, VS Code eller Cursor. Programmet starter selv `hoboman-cli.exe mcp` og taler med den over stdin og stdout; der åbnes ingen porte. De fleste programmer tager serveren i en JSON-opsætning:

```json
{ "mcpServers": { "hoboman": { "command": "C:\\sti\\til\\Hoboman\\publish\\hoboman-cli.exe", "args": ["mcp"] } } }
```

| Værktøj | Kommando |
|---|---|
| `list` (`kind`) | `list [workflows\|folders\|environments]` |
| `show`, `history`, `check` | De samme kommandoer |
| `log` (`workflow`, `run`, `last`, `step`, `count`) | `log` uden `--follow`. Med `step` gives ét trins fulde `step.finished` |
| `run` | `run`, men events uden trinnenes `headers` og `body` |
| `send_saved` / `send` | `send <gemt request>` / `send <METODE> <url>` |
| `new_request`, `new_folder`, `new_workflow`, `new_environment` | `new request`, `new folder`, `new workflow`, `new environment` |
| `update` (`content`) | `update --file -` |
| `rename`, `move` | De samme kommandoer |
| `delete` (`confirm`) | `delete [--yes]` |
| `guide` | Ingen. Giver vejledningen til AI'en, [src/Hoboman.Cli/McpGuide.md](src/Hoboman.Cli/McpGuide.md) |

- Når forbindelsen åbnes, sender serveren en instruktion med de vigtigste regler, og `guide` giver resten. Claude Code beholder kun de første 2.048 tegn af instruktionen og af hver værktøjsbeskrivelse, så de holdes under det; testene tjekker det.
- Værktøjerne er markeret, så AI-programmet kan spørge om lov: `list`, `show`, `history`, `log`, `check` og `guide` læser kun, `update`, `delete`, `send`, `send_saved` og `run` kan ødelægge, og `send`, `send_saved` og `run` taler desuden med omverdenen. `new_request` kræver en `url`.

- Værktøjerne laver kommandoernes input direkte, så en værdi aldrig læses som en option, og reglerne og fejlene er CLI'ets egne.
- `variables`, `parameters` og `content` er JSON-objekter i stedet for `--var`, `--vars`, `--param`, `--params` og `--file`.
- `json` og `text` er altid teksten selv og læses aldrig fra en fil.
- `out` skal være en fuld sti, ellers giver det `Output file must be a full path.`, og intet sendes.
- Parametre, der ikke passer sammen, fx både `name` og `count` i `history`, giver `Invalid tool arguments: …` med, hvad der er galt. Mangler et påkrævet parameter, har det forkert type eller en værdi, der ikke findes, fx en ukendt `kind`, giver MCP-serveren selv `An error occurred invoking '<værktøj>'.` uden JSON.
- `variables`, `parameters` og `content` er markeret som JSON-objekter, `kind` med sine fire værdier, og headers som tekst, så AI-programmet kan tjekke dem.
- Exitkode 0 og 1 giver kommandoens output som resultat, også et svar med en anden status end 2xx og en kørsel, hvor et trin fejlede. Exitkode 2 giver en fejl med JSON'en fra stderr.
- `run` giver alle events, når kørslen er færdig, men uden trinnenes `headers` og `body`, så resultatet er lille, og `run.finished` altid er med. `log` med `run` eller `last` giver det samme, og med `step` (trinnets index) ét trins fulde svar; findes trinnet ikke, giver det `Step could not be found.` En kørsel, der stadig kører, læses med `log`.
- Kald kan komme samtidig og køres hver for sig. Kald fra MCP står i historikken som kald fra CLI'et.
- Lukker AI-programmet forbindelsen, stopper serveren, og kald, der er i gang, afbrydes.

En færdig instruktion til AI-agenter, og hvordan AI'en låses til kun MCP, står i [AI-PROMPT.md](AI-PROMPT.md).

## Auth

- En gemt request bruger sin egen auth eller auth fra den nærmeste mappe, ligesom i appen.
- OAuth bruger det token, der er gemt for requesten eller mappen i det miljø, kaldet bruger. Tokens gemmes under miljøets id, så de følger miljøet, også når det omdøbes. Hent derfor tokenet i appen med det samme miljø valgt, som CLI'et bruger.
- Et token, der udløber inden for 30 sekunder, regnes som udløbet.
- Ved client credentials henter CLI'et selv et nyt token med den gemte client secret, når tokenet mangler, er udløbet, eller serveren svarer 401. Tokenet gemmes i `secrets.json` ligesom fra appen, og kaldet sendes én gang til. Kan tokenet ikke hentes efter en 401, er svaret den 401.
- Ved authorization code åbner CLI'et aldrig en browser. Et token hentet i appen bruges, til det udløber, og så fejler kaldet med `Fetch a new OAuth token in Hoboman before sending this request.`
- OAuth-felterne udfyldes kun fra miljøet, aldrig fra `--var` eller et workflows værdier.
- CLI'et læser ikke `credentials.json`. En credential, der er valgt i appen, er kopieret ind i auth'en på requesten, mappen, workflowet eller trinnet og virker, når den er gemt dér.

## Historik og hvad CLI'et skriver

- Hvert kald med `send` gemmes i historikken som et kald fra CLI'et, også direkte kald og kald, der fejler, mens Hoboman bygger eller sender det, fx en ugyldig URL eller en manglende hemmelighed. Den åbne app viser det med det samme i **Historik** med mærket **CLI**. Trin i et workflow gemmes kun i kørslens logfil. `history` læser historikken, både kald fra appen og fra CLI'et.
- Fejl, før kaldet bygges, gemmes ikke: forkerte argumenter eller variabler, et direkte kalds `-H` eller `@fil`, en request, der ikke kan indlæses, og problemer med miljøet. Kald, du selv afbryder, gemmes heller ikke.
- Requesten gemmes, som den er skrevet, med variablerne uudfyldte. De midlertidige værdier gemmes ikke for sig, men står i den gemte adresse, hvis de bruges i vært, port eller sti.
- Hver kørsel med `run` skrives i `runs\<workflowId>\<runId>.jsonl`. Er **Slet historik efter** udfyldt i appens Indstillinger, sletter appen kald og kørsler, der er ældre end det antal dage, når den starter; ellers ryddes de ikke op.
- CLI'et skriver i `history\`, `runs\`, filen i `--out` og, når det henter et token, i `secrets.json`. `new`, `update`, `rename`, `move` og `delete` skriver requests, mapper, workflows og `environments.json`, og `update` og `delete` kan glemme hemmeligheder i `secrets.json` og, når et miljø slettes, credentials i `credentials.json` og valget i `settings.json`, hvis det var valgt. `delete` af requests og mapper skriver også `pending-secret-cleanup.json`, indtil deres hemmeligheder er glemt. Læser det miljøer uden id, giver det dem id i `environments.json`. Ud over det skriver `list`, `show`, `check`, `log`, `history`, `--help` og `--version` ingenting, og CLI'et skriver ingen logfiler.
- Kan historikken ikke skrives, fx ved fuld disk eller manglende rettigheder, skrives svaret alligevel, og kaldet sendes ikke igen.
- Historikken og `runs\` er ikke krypteret. Headers, du selv skriver, fx `Authorization`, samt bodies, svar, den udfyldte adresse og gemte værdier som tokens kan indeholde hemmeligheder.

## Grænser

- Hele svaret holdes i hukommelsen, og `body` er tekst, så binære svar som PDF'er gemmes med `--out`.
- Et kald venter højst 100 sekunder. Det kan ikke ændres.
- Redirects følges, men cookies bruges ikke. **Ignorér certifikatfejl** fra appens indstillinger gælder også CLI'et og tokenhentning.
- En gemt request kan ikke få en ny body eller nye headers fra kommandolinjen, og et direkte kald kan kun have en JSON- eller tekst-body.
