---
satisfies: [R1, R4, R9]
---

## Description

Opret Core-only CLI-projekt og kommandokontrakt med System.CommandLine samt testbare parsegrænser. Denne opgave ejer parseproblemer og standard help; .3 ejer én samlet JSON-output-/fejlserialisering. Forbindelsen til sending kommer i .3; opret ikke færdige hjælpeframeworks til senere workflows.

**Size:** M
**Files:** src/Hoboman.Cli/Hoboman.Cli.csproj (ny); src/Hoboman.Cli/CommandLine.cs (ny, inklusive små input-records); Hoboman.slnx; tests/Hoboman.Tests/Hoboman.Tests.csproj; tests/Hoboman.Tests/Cli/CommandLineTests.cs (ny). Et nødvendigt minimalt entrypoint må etableres nu og færdiggøres i .3.

## Approach

Arv repo-TFM; AssemblyName hoboman-cli og RootNamespace Hoboman.Cli. Tilføj kun Core-projektreference, System.CommandLine 2.0.12 og adgang fra eksisterende testassembly efter repoens InternalsVisibleTo-mønster. Core bringer allerede logging abstractions.

Definér list/send og standard help/version. Send har 1-2 positional værdier, gentagne -H/--var og singletons --env/--json/--text/--vars/--body-vars. Valider direct-only kombinationer og singletondubletter; gemte GET-navne bestemmes af arity, ikke metodegæt. Typede options/input-records kan ligge ved parseren; intet ICommand-/handlerregistry.

Microsoft tutorial viser at default Invoke ved parsefejl skriver help på stdout/exit 1. Interceptér Errors og returnér et lille kontrolleret parseproblem uden rå parsertekst/arg-values til den kommende CLI-outputgrænse; serialisér ikke fejl her. JSON-error/exit 2 forbindes og testes samlet i .3. Standard help/version må bruge tekst via InvocationConfiguration.Output/Error. ParserConfiguration.ResponseFileTokenReplacer = null deaktiverer responsefiles; Command.Parse(args, configuration) og ParseResult.Errors/GetValue er dokumenterede APIs. Undgå egen help-renderer. Disable responsefile parsing globalt, så @fil/@@tekst bevares til body-loader i .3.

## Investigation targets

**Required:**
- CLI.md:11-47 — commands og output.
- Directory.Build.props — TFM og nullable.
- Hoboman.slnx — løsningstruktur.
- src/Hoboman/Hoboman.csproj — namespaces/testfriend-pattern.
- tests/Hoboman.Tests/Hoboman.Tests.csproj — eksisterende testprojectref/runner.
- src/Hoboman.Core/Sending/ApiResponse.cs — StatusCode vs status.

**Optional:**
- global.json — Microsoft.Testing.Platform.

## Key context

Læs aktuelle primære System.CommandLine tutorial/syntax fra specens references før implementation; brug 2.x API, ikke gamle beta SetHandler-eksempler. Responsefile-disablement, help/version, arity og kontrollerede parseproblemer er de tidlige checks; bevis også send -- -MinRequest, send -- -GET https://localhost/ og send -- GET -url. Sidste parses direct, mens URL-afvisning/output kommer i .3. Hvis de kræver meget ekstra wrapperkode, genvurder parservalg og opdatér spec før .3; ingen fallbackparser.

## Acceptance

- [x] CLI bygger i løsningen med kun Core-projektreference; eksisterende testproject kan teste CLI uden nye testframeworks eller WPF-reference i CLI.
- [x] Parsing følger 1-vs-2 positional, direct-only optioner, repeatable headers/vars, singleton/conflict/missing/unknown errors og --; table-driven tests dækker konkrete fejl.
- [x] @fil og @@tekst når command-input urørt, også når en faktisk fil eksisterer; parseren udvider dem ikke til argumenter.
- [x] Help/version giver standard tekst uden data-read/write; invalid input giver kontrolleret parseproblem uden argumentværdier/sentinel-secrets. Ingen JSON-error-/response-serialisering eller faux-success send-stubs; samlet stdout/stderr/exit-verifikation tilhører .3.

## Done summary
Implementeret og verificeret; ikke committet endnu.
## Evidence
- Commits:
- Tests: dotnet test --solution .\Hoboman.slnx
- PRs: