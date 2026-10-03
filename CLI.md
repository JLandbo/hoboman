# Hoboman CLI

`hoboman-cli.exe` sender gemte og direkte requests uden GUI'en. Scripts og AI-agenter bruger dermed de samme requests, miljøer, auth og hemmeligheder som appen.

## Installation og data

- `install.ps1` udgiver `hoboman-cli.exe` ved siden af `Hoboman.exe` i `publish\`. Begge programmer deler `requests\`, `environments.json`, `settings.json`, `secrets.json` og `history\`.
- CLI'et finder altid sine data ved siden af programmet, uanset hvilken mappe du står i. Relative stier i `@fil` og `--vars fil` læses fra den mappe, du står i.
- Scriptet tilføjer ikke CLI'et til `PATH`. Kald det med fuld sti, eller tilføj `publish\` selv.
- Hemmeligheder er krypteret for din Windows-bruger, så CLI'et skal køre som den samme bruger som appen.

## Kommandoer

```text
hoboman-cli list
hoboman-cli send <gemt request> [--env <navn>] [--var <navn=værdi>]... [--vars <fil|->]
hoboman-cli send <METODE> <url> [--env <navn>] [-H "Navn: Værdi"]... [--json <tekst|@fil>] [--text <tekst|@fil>] [--var <navn=værdi>]... [--vars <fil|->]
hoboman-cli --help
hoboman-cli --version
```

- `list` skriver de gemte requests som stier relativt til `requests\`, én pr. linje og sorteret, fx `Brugere/Hent bruger`.
- Ét argument efter `send` er en gemt request. To er en metode og en URL.
- En gemt request sendes med sine egne headers, body, Base64-valg og indstillingen **Brug miljøvariabler i body**. Derfor kan `-H`, `--json` og `--text` kun bruges ved direkte kald.
- Et direkte kald har ingen auth ud over de headers, du giver det, og ingen body uden `--json` eller `--text`. `@fil` læser bodyen fra en UTF-8-fil, og `@@tekst` sender `@tekst`.

## Miljøer og variabler

- `--env` vælger miljø. Uden det bruges det miljø, der er valgt i appen. Et ukendt miljø giver en fejl, før noget sendes. Valget gemmes ikke.
- `--var navn=værdi` sætter en midlertidig variabel og kan gentages. `--vars fil.json` eller `--vars -` (stdin) læser et JSON-objekt, fx `{"userId":42}`. Værdier, der ikke er tekst, indsættes som deres JSON.
- `--var` vinder over `--vars`, som vinder over miljøets gemte værdier. De midlertidige værdier gælder kun det ene kald, gemmes aldrig og bruges også, hvor miljøets variabel er slået fra.
- Variabler i bodyen indsættes kun i gemte requests, der har **Brug miljøvariabler i body** slået til. Et direkte kald sendes, som det er skrevet.
- Giv følsomme værdier gennem `--vars -` frem for `--var`, så de ikke havner i shellens historik.

## Output og exitkoder

Et svar skrives som én linje JSON på stdout, også ved 4xx og 5xx:

```json
{"status":200,"reason":"OK","elapsedMs":123,"size":17,"headers":[{"name":"Content-Type","value":"application/json"}],"body":"{\"id\":\"42\"}"}
```

`body` er svaret som tekst. Er det JSON, skal det derfor gennem `ConvertFrom-Json` endnu en gang.

Fejl, før der kommer et svar, skrives som `{"error":"..."}` på stderr, og stdout er tom.

| Exitkode | Betydning |
|---|---|
| 0 | Svar med status 2xx samt `list`, `--help` og `--version` |
| 1 | Svar med anden status |
| 2 | Fejl: forkerte argumenter, filer, der ikke kan læses, ukendt request eller miljø, netværksfejl, timeout, afbrudt kald eller et token, der ikke kan hentes |

## Kæd kald sammen i PowerShell

Gem `Brugere/Hent bruger` med `{{userId}}` i URL'en. Send derefter id'et fra ét svar videre til det næste kald:

```powershell
$cli = "C:\sti\til\Hoboman\publish\hoboman-cli.exe"
$raw = & $cli send "Brugere/Opret bruger"
if ($LASTEXITCODE -ne 0) { throw "Opret bruger fejlede." }
$user = ($raw | ConvertFrom-Json).body | ConvertFrom-Json
@{ userId = $user.id } | ConvertTo-Json | & $cli send "Brugere/Hent bruger" --vars -
```

Tjek `$LASTEXITCODE` før næste kald. Ved exitkode 2 er stdout tom.

En stor body sendes fra en fil ved et direkte kald:

```powershell
& $cli send POST https://api.example.com/import --json "@data.json"
```

## Auth

- En gemt request bruger sin egen auth eller auth fra den nærmeste mappe, ligesom i appen.
- OAuth bruger det token, der er gemt for requesten eller mappen i det valgte miljø.
- Ved client credentials henter CLI'et selv et nyt token med den gemte client secret, når tokenet mangler, er udløbet, eller serveren svarer 401. Tokenet gemmes i `secrets.json` ligesom fra appen, og kaldet sendes én gang til.
- Ved authorization code åbner CLI'et aldrig en browser. Kaldet fejler med besked om at hente et token i Hoboman.

## Historik og hvad CLI'et skriver

- Hvert afsendt kald gemmes i historikken som et kald fra CLI'et, også direkte kald og kald, der fejler undervejs. Requesten gemmes, som den er skrevet, med variablerne uudfyldte; de midlertidige værdier gemmes ikke.
- Fejl, før kaldet sendes, fx forkerte argumenter eller et ukendt miljø, og kald, du selv afbryder, gemmes ikke.
- CLI'et skriver kun i `history\` og, når det henter et token, i `secrets.json`. `list`, `--help` og `--version` skriver ingenting.
- Kan historikken ikke skrives, fx ved fuld disk eller manglende rettigheder, skrives svaret alligevel, og kaldet sendes ikke igen.
- Historikken er ikke krypteret. Headers, du selv skriver, fx `Authorization`, samt bodies, svar og den udfyldte adresse kan indeholde hemmeligheder.

## Grænser

- Hele svaret holdes i hukommelsen, og bodies er tekst. Der er ingen streaming eller binære downloads.
- En gemt request kan ikke få en ny body fra kommandolinjen.
