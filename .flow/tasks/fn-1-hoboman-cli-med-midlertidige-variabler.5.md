---
satisfies: [R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11, R13]
---

## Description

Verificér de reelle executable-/filgrænser og sikkerhedsregressioner, som unit/integrationtests ikke kan bevise. Tilføj kun manglende dækning; ingen frontendtests eller testkode der alene spejler implementation.

**Size:** M
**Files:** tests/Hoboman.Tests/Cli/CliProcessTests.cs (ny); tests/Hoboman.Tests/Cli/CliProcess.cs (ny fixture efter behov); tests/Hoboman.Tests/Cli/CliSafetyTests.cs (ny); tests/Hoboman.Tests/Cli/CliTestServer.cs (ny hvis status/fault-cases kræver det). Små relevante rettelser i CLI fra .2-.4 kun ved reproducerede fund.

## Approach

Byg/kopiér CLI-buildoutput til TemporaryFolder uden data-rootflag. Start childprocess med ArgumentList, redirected input/output/error; læs stdout/stderr samtidigt, giv bounded fixture-cleanup/cancellation og brug local server. Manglende non-2xx/fault support kan være lille CLI-testfixture frem for ændring af alle eksisterende EchoServer-tests.

Forbered nested requests, folder auth, separate Dev/Prod DPAPI-testtokens og settings. Snapshot samtlige Hoboman-datafiler i executable-root udenfor history før/efter (SDK-build og runtimeudpakning udenfor root er ikke denne kontrakt) og assert bytes samt ingen nye logs/cleanup/settings. Sentinel-variable skal ikke havne i vars-metadata/resolved requestkopi eller CLI-errors; testresponse kan bevidst echo sentinel for at bevise den dokumenterede plaintext-begrænsning.

To samtidige processes deler temporary data-root og skriver unikke historyposter. Test working directory forskellig fra executable-folder og absolut argv/inputfil-relativitet. Core best-effort-historyfejl verificeres uden dobbelt HTTP. Windows Ctrl+C er dokumenteret manual terminal-smoke; deterministic cancellation testes gennem token-seam, ikke nyt P/Invoke.

## Investigation targets

**Required:**
- tests/Hoboman.Tests/TemporaryFolder.cs — isolation/cleanup.
- tests/Hoboman.Tests/Sending/EchoServer.cs — local HTTP/TLS-pattern.
- tests/Hoboman.Tests/Sending/HttpRequestSenderTests.cs:419-480 — scoped OAuth/TLS.
- tests/Hoboman.Tests/Sending/RequestRunnerTests.cs — history og cancellation.
- src/Hoboman.Core/History/HistoryStore.cs:10-12 — process-safe history names.
- src/Hoboman.Core/Auth/SecretStore.cs:49,110 — current-user DPAPI.
- src/Hoboman.Core/Storage/JsonFile.cs — read/write retry semantics.

Stor payload-regression: generér en 10 MiB UTF-8 JSON-body i temporary inputfil med nested objekt/array, Unicode og quotes, send med --json @fil uden body-vars, og assert lokal servers byte-længde og SHA-256 af modtaget body plus udvalgte JSON-typer. Ingen stor argv-værdi eller escaped vars-string. Fixture kan returnere lille hash/metadata-response for at holde denne inputtest målrettet. Tilføj en separat stor-response streamserialiseringstest i processen/CLI-outputgrænsen, og dokumentér at Core stadig buffer. Optional 100 MiB memory/time-karakterisering er manual og ikke CI-grænse. Dette er ingen typed workflow-/grafafviklingstest.

## Key context

Testprefix Hoboman.Tests.Cli følger filter i spec. Brug faktisk stdout JSON/exit og server-observerede requests, ikke kun helper output. Undgå afhængighed af reel brugerprofil/publishfolder eller live endpoints. DPAPI fixtures skabes kun i temporary secrets.json. Ingen assertion om global secret-redaktion som Core ikke leverer.

## Acceptance

- [ ] Executable list/saved/direct på local server beviser stdout/stderr/exits 0/1/2, Unicode, @fil/@@ og data-root fra anden CWD; 10 MiB body-file-regression bevarer hash/længde/JSON-typer, og stor response-output fungerer via direkte streamserialisering; arguments/errorhelp forurener ikke machine output.
- [ ] Own/inherited OAuth med miljøoverride vælger korrekt testtoken; missing/expired token fejler før HTTP og brugerdata/secrets forbliver byte-identiske. Body-off/on/Base64 får mindst én cross-boundary regression.
- [ ] Vars fil/stdin og fejlcases er dækket gennem process; sentinel dumper ikke til CLI-errors/var-metadata/resolved requests. Echo-response/literal header klartekst i history er explicit expected existing semantics.
- [ ] Inden for Hoboman executable-datafolder: nul writes udenfor history på sends og nul writes på list/help/validation; samtidige sends giver separate Cli-historyposter; historywrite failure/cancel medfører aldrig et ekstra send.
- [ ] Alle nye CLI/Core regressions og øvrige non-UI tests passer; processfixture lukker children/server i finally ved timeout/cancel/failure. Manual Ctrl+C-smokeproceduren er klar til .6.

## Done summary
Implementeret og verificeret; ikke committet endnu.
## Evidence
- Commits:
- Tests: dotnet test --solution .\Hoboman.slnx
- PRs: