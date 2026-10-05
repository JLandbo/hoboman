# Hoboman for AI-agenter

## Hvad en AI kan i Hoboman

En AI arbejder med Hoboman udefra og kun gennem Hobomans MCP-server, `hoboman-cli.exe mcp`. Den får en række værktøjer og rører aldrig selv Hobomans datamappe. Appen har ingen API, så en AI kan ikke trykke på knapper, skifte det valgte miljø, se ugemte ændringer eller logge ind i en browser. Appen behøver ikke at være åben, men er den det, ser du det, AI'en laver, med det samme.

| AI'en vil | Værktøj | Det sker |
|---|---|---|
| Finde requests, mapper, workflows og miljøer | `list` | Id og sti eller navn for hver. Intet skrives |
| Se en request, mappe, et workflow eller et miljø | `show` | Det gemte som JSON uden hemmeligheder. Intet skrives |
| Se tidligere kald fra appen, CLI'et og MCP | `history` | Intet skrives |
| Tjekke et workflow uden at sende noget | `check` | Intet sendes |
| Sende en gemt request | `send_saved` | Ét kald med requestens egen auth eller mappens. Kaldet står i **Historik** med mærket **CLI** |
| Sende et direkte kald | `send` | Ét kald uden auth ud over de headers, AI'en giver |
| Hente en fil, fx en PDF | `send_saved` eller `send` med `out` | Svaret gemmes byte for byte i en ny fil i `Downloads\Hoboman` i din profil. AI'en giver kun et filnavn og kan ikke gemme andre steder, og en fil, der findes, overskrives aldrig |
| Køre et workflow | `run` | Alle trin i rækkefølge. Kørslen gemmes, så `log` kan læse den |
| Se kørsler, også dem startet i appen | `log` | Intet skrives |
| Oprette, omdøbe, flytte eller slette requests, mapper, workflows og miljøer, når du beder om det | `new_request`, `new_folder`, `new_workflow`, `new_environment`, `rename`, `move`, `delete` | Ændringen står i appen med det samme |
| Bygge eller rette et workflow, en request eller et miljø, når du beder om det | `show`, og derefter `update` med det rettede | Ændringen står i appen med det samme |
| Læse vejledningen til værktøjerne | `guide` | Intet skrives |

Det gør du selv i appen:

- **Passwords og client secrets.** De skrives under Auth i appen og gemmes krypteret for din Windows-bruger, ligesom tokens til Bearer-auth. En AI kan hverken læse eller skrive dem og må aldrig sende eller gemme dem som tekst. Alle andre værdier, også et token fra et svar, må den gerne give som variabler.
- **Login med password.** Kræver et API et login med password, så lav en gemt login-request med auth eller body, som AI'en kan sende med `send_saved`.
- **Login med authorization code.** Hent tokenet med refresh-knappen ved Auth, **Hent token** eller **Saved credentials…**, med det miljø valgt, som AI'en bruger. Client-credentials-tokens henter Hoboman selv.
- **Mappers auth.** AI'en kan se den, men ikke rette den.
- **Filer, der ikke kan læses.** AI'en får at vide hvor, men kan ikke rette dem.

En kørsel med `run` vises ikke i appens workflow-editor; dér vises kun det, der køres fra editoren.

## Opsætning

**Registrér MCP-serveren** i det AI-program, du bruger. MCP er en åben standard, så det kan være ethvert program, der understøtter MCP-servere, der kører lokalt (stdio), fx Claude Code, Claude Desktop, VS Code eller Cursor. De fleste tager serveren i en JSON-opsætning:

```json
{ "mcpServers": { "hoboman": { "command": "C:\\sti\\til\\Hoboman\\publish\\hoboman-cli.exe", "args": ["mcp"] } } }
```

I Claude Code kan den også tilføjes én gang for din bruger med:

```bash
claude mcp add --scope user hoboman -- "C:\sti\til\Hoboman\publish\hoboman-cli.exe" mcp
```

Programmet starter selv `hoboman-cli.exe mcp` og taler med den over stdin og stdout. Der åbnes ingen porte.

**Lås AI'en til MCP.** Serverens instruktion forbyder AI'en at gå uden om værktøjerne, men kun dit AI-program kan håndhæve det, og hvordan afhænger af programmet. I Claude Code tillades de værktøjer, der kun læser, så AI'en spørger dig, før den sender, kører eller ændrer noget, og shell og adgang til Hobomans mappe nægtes i `permissions` i `settings.json`. Mappen skrives som POSIX-sti med `//` foran:

```json
{
  "permissions": {
    "allow": ["mcp__hoboman__list", "mcp__hoboman__show", "mcp__hoboman__history", "mcp__hoboman__log", "mcp__hoboman__check", "mcp__hoboman__guide"],
    "deny": ["Bash", "PowerShell", "Read(//c/sti/til/Hoboman/publish/**)", "Edit(//c/sti/til/Hoboman/publish/**)"]
  }
}
```

Med `mcp__hoboman__*` i `allow` godkendes alle værktøjerne, også `send`, `run`, `update` og `delete`, uden at du bliver spurgt. Skal AI'en bruge en shell til andet, så lad `Bash` og `PowerShell` være, og nøjes med at nægte mappen. Så er den dog kun forbudt, ikke umulig, at køre `hoboman-cli.exe` fra en shell.

## Instruktionen

Du skal ikke kopiere noget ind i din AI. Serveren giver selv AI'en sin instruktion:

- **Når forbindelsen åbnes**, sender den de vigtigste regler: rør aldrig Hobomans mappe og kør aldrig `hoboman-cli.exe` selv, passwords og client secrets skrives kun i appen og sendes aldrig som tekst, ændr intet og send intet med sideeffekter uden at spørge, gæt ikke, og sig til, når noget er ændret. Reglerne holdes under 2.048 tegn, for Claude Code skærer resten af.
- **Værktøjet `guide`** giver hele vejledningen: reglerne, alle værktøjer, JSON-formaterne til `update`, events fra `run` og alle fejl med, hvad AI'en skal gøre. Reglerne beder AI'en læse den, før den bygger eller retter noget, og når et resultat eller en fejl er uklar. Vejledningen står i [src/Hoboman.Cli/McpGuide.md](src/Hoboman.Cli/McpGuide.md).
- **Hvert værktøj** har en beskrivelse af, hvad det gør, hvad parametrene er, og hvad det giver, og er markeret som kun læsende, som et, der kan ødelægge noget, og som et, der taler med omverdenen. AI-programmer kan bruge markeringerne, når de beder dig om lov.