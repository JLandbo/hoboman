---
satisfies: [R6, R7, R9, R11]
---

## Description

Indlæs midlertidige variabler fra repeated --var og én --vars JSON-fil/stdin. Kobl dem til Core-overlay og implementér direct --body-vars uden at ændre saved requests.

**Size:** M
**Files:** src/Hoboman.Cli/VariableInput.cs (ny); src/Hoboman.Cli/CommandLine.cs; src/Hoboman.Cli/CliApplication.cs; tests/Hoboman.Tests/Cli/VariableInputTests.cs (ny); tests/Hoboman.Tests/Cli/CliApplicationTests.cs.

## Approach

Én inputloader med System.Text.Json og injected reader/redirected-state til test. Validér string-only JSON-objekt, nonempty/non-whitespace navne uden braces og duplicate keys. Ingen JSON-to-string-konvertering, OS-vars eller configprovider. --vars er specifikt tekstsubstitution. Fremtidige typed object/array workflow-input må ikke modelleres som escaped string-vars; body-file transport og senere eksplicitte Core-bindings er separate.

--var split ved første =; tomværdi er gyldig. Normalisér med exact ordinal name: saved < JSON < sidste CLI-var, uanset optionernes position. Kalde ApiEnvironment.WithVariables med active overlay; Name bevares. Ingen env-save eller request-materialisering.

--vars - kræver redirected stdin, læses til EOF med token; hold reader åbent når det er caller-owned. Relative filstier er CWD. Invalid input giver error JSON/exit 2 før RunAsync. --body-vars er direct-only, kræver body og aktiverer ApiRequest.UseEnvironmentVariablesInBody kun på den direkte request.

## Investigation targets

**Required:**
- src/Hoboman.Core/Environments/ApiEnvironment.cs — overlay fra .1.
- src/Hoboman.Core/Requests/ApiRequest.cs — body opt-in og record.
- src/Hoboman.Core/Sending/HttpRequestSender.cs:136-156 — variables før Base64.
- src/Hoboman.Core/Base64/Base64Json.cs — gemt encodingadfærd.
- tests/Hoboman.Tests/Environments/ApiEnvironmentTests.cs — resolverkontrakt.
- tests/Hoboman.Tests/Sending/HttpRequestSenderTests.cs:175-210 — body opt-in.

## Key context

Global JSON-escaping af substitutionsværdier er ikke eksisterende adfærd og indføres ikke. Saved body kan indeholde handlebars som matcher et envnavn; flag off skal bevare teksten. Store- eller response-history kan indeholde værdier; løftet er ingen direkte persist af vars-object/resolved request, ikke global redaktion.

## Acceptance

- [ ] Inline/fil/stdin følger præcedens; tests dækker exact names, repeated keys, disabled saved felt, tomværdi og ekstra =, med bevaret miljø Name/original og None.
- [ ] Invalid JSON/types/null/nested/duplicate names/invalid --var, repeated --vars og ikke-redirected - giver exit 2 før HTTP/history; token kan afbryde stdinread og errors dumper ikke input.
- [ ] Saved flag bevares; direct default-off/--body-vars-on/withoutbody invalid/saved option invalid testes med observeret HTTP-body og ukendt handlebars; eksisterende Base64 sker efter substitution.
- [ ] To sekventielle sends demonstrerer chaining med id fra første response; næste uafhængige kald ser ikke override. Saved files byte-identiske, history.Request indeholder placeholders og ingen vars-dictionary.

## Done summary
Implementeret og verificeret; ikke committet endnu.
## Evidence
- Commits:
- Tests: dotnet test --solution .\Hoboman.slnx
- PRs: