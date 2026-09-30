# Hoboman CLI (planlagt)

Et konsolprogram, så AI-agenter og scripts kan sende requests uden GUI'en, med de samme gemte requests, environments og hemmeligheder.

## Opbygning

- Eget projekt: `src/Hoboman.Cli` (konsolapp), som kun refererer `Hoboman.Core`.
- Udgives som `hoboman-cli.exe` i samme mappe som `Hoboman.exe` og deler derfor `requests\`, `history\`, `environments.json`, `secrets.json` og `settings.json`. Navnet kan ikke være `hoboman.exe`, fordi Windows ikke skelner mellem store og små bogstaver.
- Forudsætning allerede nu: al logik (indlæsning af requests, `{{variabler}}`, auth, hemmeligheder, afsendelse) ligger i `Hoboman.Core` uden WPF. GUI'en er kun visning.

## Kommandoer

```
hoboman-cli list
hoboman-cli send <gemt request> [--env <navn>]
hoboman-cli send <METODE> <url> [--env <navn>] [-H "Navn: Værdi"]... [--json <tekst|@fil>] [--text <tekst|@fil>]
```

- `list` viser de gemte requests som stier relativt til `requests\`, fx `Brugere/Hent bruger`.
- `send <gemt request>` sender en gemt request med dens egne headers, body og auth. Arver den auth, bruges auth fra den nærmeste mappe, der har en, ligesom i GUI'en.
- `send GET https://…` sender et direkte kald. Auth angives med `-H "Authorization: …"`.
- `--env` vælger environment. Uden den bruges `EnvironmentName` fra `settings.json`.
- `@fil` læser body fra en fil.

## Output

Svaret skrives som JSON på stdout:

```json
{
  "status": 200,
  "reason": "OK",
  "elapsedMs": 123,
  "size": 456,
  "headers": [{ "name": "Content-Type", "value": "application/json" }],
  "body": "..."
}
```

Fejl skrives som `{ "error": "..." }` på stderr.

| Exit code | Betydning |
|---|---|
| 0 | Svar med status 2xx |
| 1 | Svar med anden status |
| 2 | Fejl: forkerte argumenter, ukendt request, netværksfejl eller manglende eller udløbet token |

## Regler

- CLI'et skriver kun i historikken (`history\`). Ellers ændrer det ingen filer.
- Hvert kald gemmes i historikken ligesom kald fra GUI'en, også direkte kald og kald, der fejler. Derfor kan de ses i GUI'ens historik.
- `IgnoreCertificateErrors` fra `settings.json` gælder også her.
- OAuth bruger det token, der er gemt i `secrets.json` for requesten eller dens mappe i det valgte environment (`--env`); hvert environment har sit eget token. Mangler det, eller er det udløbet, fejler kaldet med besked om at hente et token i Hoboman. CLI'et åbner aldrig en browser.
