---
satisfies: [R6, R7, R8, R11]
---

## Description

Tilføj én ren Core-operation for midlertidige variabler og bevis, at den eksisterende sender fortsat bruger korrekt miljøspecifikt OAuth-token. Dette er det tidlige proof point.

**Size:** M
**Files:** src/Hoboman.Core/Environments/ApiEnvironment.cs; tests/Hoboman.Tests/Environments/ApiEnvironmentTests.cs; tests/Hoboman.Tests/Sending/HttpRequestSenderTests.cs.

## Approach

Genbrug ApiEnvironment.Resolve og record-semantik fra ApiEnvironment.cs:6-21. En WithVariables-operation returnerer samme Name med aktive overridefelter først; ingen ny variable resolver, mutation, storage eller execution context. Input er valideret/normaliseret af CLI senere; definer duplicate-precedence på dette seam tydeligt og simpelt.

Genbrug eksisterende OAuth-miljøtests ved HttpRequestSenderTests.cs:419-444 og EchoServer til at bevise server-observerede værdier. Undgå ændringer i HttpRequestSender/SecretStore.

## Investigation targets

**Required:**
- src/Hoboman.Core/Environments/ApiEnvironment.cs — resolver og record.
- src/Hoboman.Core/Sending/HttpRequestSender.cs:99-145 — substitution og OAuth-miljø.
- tests/Hoboman.Tests/Environments/ApiEnvironmentTests.cs — eksisterende teststil.
- tests/Hoboman.Tests/Sending/HttpRequestSenderTests.cs:419-444 — separate tokens.
- src/Hoboman.Core/Requests/KeyValue.cs — enabled-felter.

**Optional:**
- tests/Hoboman.Tests/Sending/EchoServer.cs — lokale HTTP-assertions.

## Key context

OAuth key bruger environment.Name, ikke variabelindhold. ApiEnvironment.None er delt og må ikke muteres. Unknown vars og case-sensitive/non-recursive substitution skal forblive som nu. En aktiv override må erstatte et disabled saved felt uden at ændre saved listen.

## Acceptance

- [ ] Ny ren operation bevarer Name og original liste/None og virker med tomt miljø/tomme værdier; tests beviser at originalen stadig resolver originalværdier bagefter.
- [ ] Override vinder over gemt aktiv/disabled variabel; ukendte placeholders, case sensitivity og non-recursion bevares med direkte tests.
- [ ] HTTP-test med overlay og et token kun gemt for Dev sender Dev-token plus overrideværdi til lokal server; et andet miljø bruges aldrig.
- [ ] Core- og relevante sender-tests passer uden ny resolver/auth/storage-logik; hvis proof point kræver større ændringer, stop og beskriv konkret hvorfor før resten.

## Done summary
Implementeret og verificeret; ikke committet endnu.
## Evidence
- Commits:
- Tests: dotnet test --solution .\Hoboman.slnx
- PRs: