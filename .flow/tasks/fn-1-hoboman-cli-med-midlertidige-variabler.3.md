---
satisfies: [R1, R2, R3, R4, R5, R8, R9, R10, R11, R13]
---

## Description

Forbind CLI til eksisterende Core: list, saved/direct requests, miljøvalg, auth, history, output og cancellation. Hold metoder opdelt efter konkrete ansvar og genbrug eksisterende seams.

**Size:** M
**Files:** src/Hoboman.Cli/Program.cs; src/Hoboman.Cli/CliApplication.cs (ny); src/Hoboman.Cli/RequestInput.cs (ny); src/Hoboman.Cli/CliOutput.cs (ny); tests/Hoboman.Tests/Cli/CliApplicationTests.cs (ny).

## Approach

Program er lille composition root med AppFolder(AppContext.BaseDirectory), TimeProvider.System, Core-stores, HttpClients, HttpRequestSender og RequestRunner. Brug NullLogger og constructor-injection; ingen GUI App.Services eller browser/OAuthClient/watchers/cleanup. Disposér HttpClients og cancellation.

CliApplication modtager root/stores/runner og input/output/cancellation som nødvendigt for tests. Genbrug NamesAsync/LoadAsync og RunAsync med fuld savedsti/HistorySource.Cli. list sorteres deterministisk og læser ingen env/auth. Direct ApiRequest får new Guid, None body uden body-option, AuthSettings.None og null navn.

RequestInput holder direkte header/body-oversættelse og UTF-8 @fil/@@tekst; ingen ekstra JSON stringification eller duplicate URL/query construction. Alle syntaks-/inputfil-/requestload-/miljøfejl sker før RunAsync. Auth/source-resolving og sendfejl forbliver i runner så historik skabes som nu.

Miljøvalg: --env ellers settings; None kun når intet navn er valgt. Ukendt valgt navn fejler uden fallback. Bodyflag for saved respekteres. Output mapping bevarer status-feltnavn og body-string; serialisér JSON direkte til injected outputstream med System.Text.Json i CliOutput i stedet for at bygge en fuld escaped wrapper-string. Help kan stadig bruge TextWriter. Dette undgår én ekstra kopi, men ændrer ikke Core HTTP-buffering/history; normalisér parserens kontrollerede problem og alle runtimefejl til én JSON gennem CliOutput, uden duplikeret serializer eller exception Message/stack dump. UriFormatException/HttpRequestException får korte URL/netværksbeskeder uden resolved values. OAuth-missing/expired får fetch-in-GUI-besked. Timeout og requested cancellation skelnes ved token, men begge giver exit 2.

## Investigation targets

**Required:**
- src/Hoboman/App.xaml.cs:94-113 — nødvendig Core-servicegraf.
- src/Hoboman.Core/Sending/RequestRunner.cs:11-39 — history/cancel/best-effort.
- src/Hoboman.Core/Requests/RequestLibrary.cs:23-77 — list/load/arvet auth.
- src/Hoboman.Core/Sending/HttpRequestSender.cs — genbrugt sending/body/auth.
- src/Hoboman.Core/Environments/EnvironmentStore.cs — eksakt navnmatch.
- src/Hoboman.Core/Settings/SettingsStore.cs — read-only settingsvalg.

**Optional:**
- tests/Hoboman.Tests/Sending/RequestRunnerTests.cs — fake sender/history-pattern.

## Key context

HistorySource.Cli eksisterer allerede i Core/History/HistorySource.cs:3; genbrug uden ændring/compatibilitykode. Store objekter skal bruge body-file, ikke argv eller escaped --vars values. Den fremtidige workflowmotor får typed run-output særskilt; ApiEnvironment og CLI-wrapper er ikke dens datamodel. Core-record ApiResponse serialiserer ikke automatisk det kontraktuelle status-navn. Argument/inputfejl får ingen history; auth-fejl i RunAsync gør. Historical request er original input; materialisér ikke resolved request. History-I/O-fejl bevarer HTTP-response uden retry og uden ny warning-protokol. Erroroutput må ikke bruge rå værdier fra exceptions.

## Acceptance

- [ ] list giver sorteret extensionless/nested output, intet metadata og tomt output/exit 0 ved empty root; tests viser at malformed auth/env/settings ikke påvirker list.
- [ ] Saved send bruger fuld path/original indhold/nærmeste auth; direct send understøtter headers/inline/@fil/@@ og None body uden option, aldrig mappe-auth. Assert server/fake observerede værdier.
- [ ] Default/explicit/None/unknown miljø følger spec og skriver ikke settings; stored own/inherited OAuth bruger korrekt miljøtoken; missing/expired giver GUI-instruktion/exit 2 uden browser.
- [ ] Direkte UTF-8 streamserialisering samt responsefelter/Unicode/body-string/gentagne headers, parserproblemer, exits 0/1/2 og kontrolleret stderr testes; sentinel i URL/header/body vises ikke i stderr, og send -- GET -url giver kontrolleret URL-fejl/exit 2 uden HTTP; CancelKeyPress er koblet til token, disposal sker, ingen logs oprettes.
- [ ] RunAsync registrerer Cli-source/original request ved success/sendfailure, ikke load/inputfejl/cancel; locked/unwritable history giver original response og præcis ét send. Ingen writes udenfor history.

## Done summary
Implementeret og verificeret; ikke committet endnu.
## Evidence
- Commits:
- Tests: dotnet test --solution .\Hoboman.slnx
- PRs: