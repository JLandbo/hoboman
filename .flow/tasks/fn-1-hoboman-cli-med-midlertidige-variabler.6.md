---
satisfies: [R1, R11, R12, R13]
---

## Description

Udgiv CLI side om side med GUI gennem eksisterende install-flow, og gør README/CLI.md til korrekt brugerdokumentation med et konkret eksternt chaining-eksempel.

**Size:** M
**Files:** src/Hoboman.Cli/Hoboman.Cli.csproj; install.ps1; CLI.md; README.md.

## Approach

Følg eksisterende GUI publish-settings og install.ps1. Publish begge win-x64 self-contained single-file executables til samme publish-folder med separate navne og exitkontrol efter hvert publish. Installer må ikke rydde datafolder, ændre settings/secrets eller tilføje PATH/CLI-genvej. Vent eksplicit på både Hoboman.exe og hoboman-cli.exe med full path i target publish-folder før publish, med brugerens egen lukning som eksisterende GUI-flow; dræb ikke processes.

Rett docs først efter grønne smokes: fjern planlagt/notimplemented, tilføj src/Hoboman.Cli i arkitektur/projecttree og copy-pasteable PowerShell list/saved/direct/@fil/--var/--vars/stdin/body-opt-in. Forklar wrapper.body kræver ekstra ConvertFrom-Json for chaining; kontrollér $LASTEXITCODE inden næste send. Følsomme values vises gennem stdin. Body-variabler skal være slået til i gemt request for body-chaining.

Docs beskriver root vs input-CWD, default/unknown env, exits, auth nearest folder/stored OAuth/current-Windows-user/no browser, history-only writes, historybest-effort og plaintext literal headers/responses/varsbegrænsning. Dokumentér at store payloads bruger --json @fil/--text @fil på direkte requests, mens --vars kun er tekstværdier og saved sends endnu ikke har body-override. Den fremtidige grafiske pipeline bruger en særskilt typed Core-run-context og bindings. Beskriv nuværende fuld HTTP-buffering og at 10 MiB er en teststørrelse, ikke ubegrænset memory-garanti. Ingen løfter om endnu ikke implementeret workflows.

## Investigation targets

**Required:**
- install.ps1 — nuværende GUI publish/shortcut.
- src/Hoboman/Hoboman.csproj — self-contained single-file-konvention.
- Directory.Build.props — inherited framework.
- CLI.md — eksisterende kontrakt som erstattes af guide.
- README.md:89-160 — arkitektur/projecttree/data/CLI status.
- Hoboman.slnx — fuld løsningverification.
- global.json — korrekt testkommando.

## Key context

Executable-root skal være fælles; dotnet run bruger egne buildfolders og deler ikke automatisk GUI's publishdata. Publish-smoke bruger temporary output og fixtures, ikke install.ps1 mod brugerens data. Runtime er inkluderet i self-contained output; SDK kræves til lokal build/install, ikke til brug af den udgivne exe.

## Acceptance

- [ ] Isoleret publish giver Hoboman.exe og hoboman-cli.exe i samme output; list fra anden CWD bruger executable-root, eksisterende fixturedata bevares, ingen logs/settings writes fra CLI.
- [ ] install.ps1 publisher begge, checker failures og venter eksplicit på både GUI og CLI i target-folder før publish og overskriver ikke kørende exe uden brugerens lukning; ingen dataoprydning, PATH/shortcut-extras eller migration.
- [ ] README/CLI.md matcher alle kommando-/output-/exit-/miljø-/body-/OAuth-/historykontrakter; konkrete PowerShell-eksempler inkl. scalar-chaining, stdin og stor body-file til direkte send er smoke-verificeret med lokale fixtures; typed workflow/bounded-memory begrænsninger beskrives tydeligt.
- [ ] dotnet build og hele relevante test-suite er grøn; isolated terminal Ctrl+C-smoke passer; ændringer reviewes for Core-genbrug, ansvar, deadcode og scope. Ingen commit uden brugerens separate besked.

## Done summary
Implementeret og verificeret; ikke committet endnu.
## Evidence
- Commits:
- Tests: dotnet test --solution .\Hoboman.slnx
- PRs: