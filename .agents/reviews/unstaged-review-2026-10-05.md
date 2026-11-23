# Browser-visning med egen origin pr. svar, fejltekst i boksen og ét Content-Type-opslag

Ændringen strammer browser-visningen af svar. Hvert svar får sin egen host under `response.hoboman`, så siderne ikke skal dele cookies og storage, og autofill, gemte adgangskoder, permissions og basic auth slås fra. En browser, der ikke kan starte, vises som tekst i boksen i stedet for at gå til appens fejlhåndtering. Et svar fra historikken med en fil (billede eller PDF) giver nu teksten `Response.OnlyText` i stedet for en ødelagt side, og browser-tilstanden springer tekst-dekodning og formattering over. Ved siden af det samles Content-Type-opslaget i `ApiResponse.ContentType`, `ContentTypeOf` bliver en extension på `ScriptOutput`, WebView2-datamappen sættes fra `App`, og workflowets output-knapper får egne sprognøgler.

Watch for: isolationen pr. svar holder ikke på tværs af `BrowserView`-instanser og genstarter (verificeret); den nye catch-all i `Show()` fjerner logningen af browserfejl (verificeret); et temaskift genindlæser hele svarsiden (verificeret).

**Verdict**: NEEDS_CHANGES (kræver ændringer). Der er ingen kritiske eller høje fund. Mellem-fundet er ikke en regression i forhold til det stagede, men koden lover en isolation, den ikke giver, og det er billigt at rette.

Build: `dotnet build Hoboman.slnx` lykkedes med 0 advarsler og 0 fejl. Test: `ResponseViewModelTests`, `BrowserPageTests`, `TranslationTests`, `WorkflowRunnerTests` og `ResponseDisplayTests` gav 115/115 grønne. Hele suiten uden `AuthRefreshServiceTests` gav 1406/1407. Den ene fejl, `ThemeFileTests.Lys_WhenRead_ThenHasEveryColourOfTheDefault`, kommer fra nye ustagede ændringer i `Themes/Colors.xaml` og `Themes/Lys.json` (nøgler som `JavaScript.Character`), som ikke hører til det reviewede.

Omfang: git-tilstanden ændrede sig, mens reviewet kørte. De 16 filer fra briefen blev staget, `BrowserView.cs` og `ResponseViewModel.cs` blev rettet igen kl. 15:11, og et nyt sæt ustagede ændringer kom til (HtmlTokens og PrettyBody flyttet til `Hoboman.Core/Text`, `ApiResponse.MediaType`, `FileNameOf`, `Colorings`, temaer). Reviewet dækker de 16 filer, som de står i index nu, dvs. briefens ændringer plus rettelserne kl. 15:11. Det nye ustagede arbejde er ikke reviewet. Build og test kørte på arbejdstræet omkring kl. 15:21-15:25 og indeholdt derfor en del af det nye arbejde. Rettelserne kl. 15:11 fjernede tre fejl i `TranslationTests.Danish_WhenItHasAText_ThenTheCodeUsesIt`, som briefens version ville have givet: `Tell("Response.BrowserFailed", …)` og `_translator.Of(decodeWhole ? … : "Response.OnlyText")` fanges ikke af `Key()`-regex'en, så `Response.BrowserMissing`, `Response.BrowserFailed` og `Response.OnlyText` ville stå som ubrugte.

## Overblik

Hosten `https://{n}.response.hoboman/` bygger på `_shown`, som er et felt pr. `BrowserView` og starter forfra i hver instans. Der er mindst to instanser (request-fanen og workflowet), og fanens view bliver lavet på ny ved skift mellem fane og workflow. Derfor får mange forskellige svar `1.response.hoboman`. Profilen ligger på disk i `DataFolder`, så det gælder også på tværs af genstarter.

Den nye catch-all i `Show()` holder fejldialogen væk, men fejlen bliver ikke længere logget. Den fanger også fejl, der sker efter starten, og så forbliver viewet dødt, indtil det bliver lavet på ny.

`Background` er bundet til `Input`, og når den ændrer sig, kalder viewet `Show()`, som navigerer igen. Et temaskift genindlæser derfor hele svaret, selv om kun den tomme side har farven i sin HTML.

`BrowserPage.Of` bruger `ResponseDisplay.FormatOf(...) == BodyFormat.Browser` som test for, om kroppen er en fil. Det dækker kun `image/*` og PDF og binder browserens opførsel til valget af standardvisning.

Hærdningen lukker for permissions og basic auth, men JavaScripts `alert`, `confirm` og `prompt` kan stadig spørge brugeren. Resten er designnits: `DataFolder` er global, muterbar tilstand med en fallback, som den gamle kode udelukkede, og `ScriptOutputs.ContentType` er gjort offentlig med kun én bruger.

<details>
<summary>Detaljer</summary>

### Mellem: origin pr. svar genbruges på tværs af views og sessioner (verificeret, cookie-delen formodet)

`src/Hoboman/Controls/BrowserView.cs:17-18, 27, 58, 69, 135`, `src/Hoboman/Views/MainWindow.xaml:65`, `src/Hoboman/Views/WorkflowView.xaml:547`

`_shown` er et instansfelt, så hver `BrowserView` viser sin første side på `https://1.response.hoboman/`. `ResponseView` findes både i fanens `DataTemplate` og i `WorkflowView`, og `ContentControl Content="{Binding Content}"` i `MainWindow` skifter skabelon mellem `RequestTabViewModel` og `WorkflowViewModel`. Hvert skift laver en ny `BrowserView` med en ny WebView2, som starter forfra på 1. Alle instanser deler den statiske `_environment` og dens standardprofil, som ligger på disk i `DataFolder`.

```
Session 1:  fane      -> 1.response.hoboman  (svar A gemmer i localStorage)
            workflow  -> 1.response.hoboman  (svar B læser A's data)
Session 2:  fane      -> 1.response.hoboman  (svar C læser A's og B's data fra disk)
```

Det betyder noget, fordi kommentaren på linje 17 lover, at cookies og storage holdes adskilt, og fordi HTML-svar er indhold, appen ikke styrer. En side kan køre scripts, der gemmer et token (fx en login-side), og et senere svar uden forbindelse til den kan læse det. Formodet: en side på `3.response.hoboman` kan også sætte en cookie med `Domain=response.hoboman`, som alle undernavne derefter får, fordi Chromium ser `response.hoboman` som det registrerbare domæne under det ukendte TLD `hoboman`.

Forslag: brug en statisk tæller til hosten, adskilt fra `_shown`, som stadig skal afgøre, hvilken `Show()` der vinder i den enkelte instans. Slå desuden InPrivate til, så intet overlever til næste session eller ligger på disk:

```csharp
static int _pages;
// Show(): _current = Page is null ? null : $"https://{++_pages}.{_host}/";
// StartAsync():
var environment = await _environment.Value;
var options = environment.CreateCoreWebView2ControllerOptions();
options.IsInPrivateModeEnabled = true;
await control.EnsureCoreWebView2Async(environment, options);
```

`DownloadStarting` (linje 135) skal så også tage sin host fra `_pages`. Skal cookie-delingen via `Domain=` også lukkes inden for en session, kræver det `browser.CookieManager.DeleteAllCookies()` før hver side.

### Lav: catch-all i `Show()` skjuler fejl for loggen og efterlader et dødt view (verificeret)

`src/Hoboman/Controls/BrowserView.cs:86-92`, se også `src/Hoboman/App.xaml.cs:43-51`

Før gik en undtagelse fra `async void Show()` til `DispatcherUnhandledException`. Den loggede fejlen med stack trace og viste `Common.UnexpectedError`, når vinduet var synligt, og appen stoppede ikke. Nu fanges den, og kun `exception.Message` vises i boksen, uden nogen linje i loggen. Kommentarens præmis, "instead of stopping the app", passer altså ikke. `Response.BrowserFailed` på en brugers maskine kan derfor ikke spores bagefter.

Fanget omfatter også `Navigate`, `NavigateToString` og `DefaultBackgroundColor`, efter browseren er startet. Fejler en af dem, bliver `Content` til `TextBlock`, mens `_browser` er en fuldført task. Alle senere `Show()` springer så over, fordi `Content is WebView2` er falsk. Viewet viser fejlen, indtil det bliver lavet på ny, og den fjernede WebView2 bliver ikke disposed.

Forslag: begræns `catch (Exception)` til starten (`await (_browser ??= StartAsync())`) og log undtagelsen gennem appens logger. Er logning ikke ønsket her, så ret kommentaren, så den beskriver det reelle bytte: ingen dialog, men heller ingen log.

### Lav: temaskift genindlæser hele svarsiden (verificeret)

`src/Hoboman/Controls/BrowserView.cs:41-48, 58-77`, `src/Hoboman/Views/ResponseView.xaml:136`

`OnPropertyChanged` kalder `Show()`, når `Background` ændrer sig. `Show()` tæller `_shown` op, giver en ny host og navigerer igen. Et temaskift kører derfor svarets scripts igen, mister scroll- og PDF-position, henter en stor PDF igen og kører `_asHtml`-fallbacken en gang til (to navigationer). Kun den tomme side behøver at blive vist igen, fordi dens farve står i HTML'en.

Forslag: ved ændring af `Background` sættes kun `control.DefaultBackgroundColor`, og `Show()` kaldes kun, når `Page is null`. Formodet: WebView2 bruger en ny `DefaultBackgroundColor` med det samme uden ny navigation.

### Lav: JavaScript-dialoger kan stadig spørge brugeren (verificeret)

`src/Hoboman/Controls/BrowserView.cs:123-126`

Kommentaren siger "A response asks for nothing of the user", og permissions og basic auth blokeres. `Settings.AreDefaultScriptDialogsEnabled` står dog på standardværdien `true`, så `alert`, `confirm`, `prompt` og `beforeunload`-dialoger fra svaret bliver vist. `prompt("Adgangskode")` er den samme phishing-vej som den basic auth-dialog, ændringen lige har lukket.

Forslag: `browser.Settings.AreDefaultScriptDialogsEnabled = false;`

### Lav: tjekket for "kun tekst" hviler på standardvisningen og dækker kun billeder og PDF (verificeret)

`src/Hoboman/ViewModels/BrowserPage.cs:15-17`

`ResponseDisplay.FormatOf(response) == BodyFormat.Browser` bruges som "kroppen er en fil". `FormatOf` svarer på, hvilken visning der vælges som standard, og giver i dag kun `Browser` for `image/*` og `application/pdf`. Et svar fra historikken med `application/octet-stream`, `application/zip`, `audio/*`, `video/*` eller `font/*` bliver stadig sendt som UTF-8 af den tabsramte tekst. Browseren viser så ødelagt medie eller starter en download, og `_asHtml`-fallbacken viser skrald i stedet for `Response.OnlyText`. Omvendt gælder: vælger `FormatOf` en dag `Browser` for en teksttype som `text/html`, holder HTML fra historikken op med at blive vist, selv om teksten er komplet. Den første del rammer kun, når brugeren selv vælger browseren for sådan en type.

Forslag: lav et selvstændigt teksttjek i `BrowserPage`, fx at kroppen kun regnes for tekst, når typen er `text/*` eller indeholder `json`, `xml`, `html` eller `javascript`, og ellers giv `null`. `MediaType` fra det nye ustagede arbejde kan være stedet til det.

### Lav: `BrowserView.DataFolder` er global, muterbar tilstand med en fallback, som den gamle kode udelukkede (verificeret)

`src/Hoboman/Controls/BrowserView.cs:21, 31-32`, `src/Hoboman/App.xaml.cs:37-38`

Stien er flyttet fra et selvstændigt `static readonly` til en offentlig, settable statisk property, som `App` sætter. `_environment` læser den én gang og lazy. Sættes den senere, bliver det ignoreret uden varsel. Sættes den ikke, bruger WebView2 sin standard, `<exe-mappe>\Hoboman.exe.WebView2`, altså ved siden af appen og brugerens data i `AppFolder`. Det var præcis det, den gamle kommentar lovede aldrig ville ske. Den nye kommentar beskriver fallbacken i stedet for at forhindre den. I dag sætter `App` den, før noget vindue findes, så det er ikke en fejl i praksis.

Forslag: behold stien i `BrowserView` som før, med `typeof(BrowserView).Assembly` i stedet for `Assembly.GetEntryAssembly()!`, hvis det var grunden. Ellers gør den påkrævet (kast, hvis den er `null`) i stedet for at falde tilbage.

### Nit: `ScriptOutputs.ContentType` er gjort offentlig med kun én bruger (verificeret)

`src/Hoboman.Core/Scripts/ScriptOutput.cs:5-17`

Metoden er flyttet fra en privat `static` i `WorkflowRunner` til en offentlig extension i Core. Den eneste kalder er `WorkflowRunner.cs:177`, og CLI'en læser typen fra `ApiResponse`-headeren. Det udvider Cores API uden en anden bruger. Forslag: behold den privat i `WorkflowRunner`, medmindre der er en planlagt bruger.

### Nit: `Tell(params Inline[] lines)` har et misvisende navn (verificeret)

`src/Hoboman/Controls/BrowserView.cs:95`

Argumentet indeholder `Run`, `LineBreak` og `Run`, ikke linjer. Forslag: `inlines` eller `parts`.

### Testdækning

De nye tests dækker `Response.OnlyText` for en PDF fra historikken (`BrowserPageTests`, `ResponseViewModelTests`) og `Response.InvalidBase64` i browser-tilstand. Der er kun tilføjet tests, ingen er svækket. Ikke testet: `image/*` fra historikken; at browser-tilstand springer `Base64Json.Decode` og formattering over, som er grunden til `InBrowser`; `ApiResponse.ContentType` direkte (kun indirekte via `FormatOf`, `FileNameOf` og `RunValues`); `BrowserView` (UI-kontrol, i tråd med reglen om ingen front-end-tests).

</details>

## Åbne spørgsmål

1. Er det rigtigt at reviewe de 16 filer i index, inkl. rettelserne kl. 15:11? Skal det nye ustagede arbejde (`MediaType`, PrettyBody/HtmlTokens i Core, JavaScript-farver i temaerne) have sit eget review? Kører der en anden agent i samme arbejdstræ?
2. Hvorfor er WebView2-datamappen flyttet til `App`? Var det kun for at slippe af med `Assembly.GetEntryAssembly()!`, er `typeof(BrowserView).Assembly` nok.
3. `Workflow.OutputJson`, `Workflow.OutputHtml`, `Workflow.OutputXml` og `Workflow.OutputText` gentager teksterne fra `Body.Json`, `Format.Html`, `Body.Xml` og `Body.Text`, som stadig bruges andre steder. Er det en bevidst afkobling, så workflow-output kan oversættes for sig, eller skal de eksisterende nøgler genbruges?

<details>
<summary>Filoversigt</summary>

- `src/Hoboman.Core/Languages/Dansk.json`, `English.json`: `Response.OnlyText`, `Response.BrowserFailed` og fire `Workflow.Output*`-nøgler, ens i begge filer og uden pladsholdere.
- `src/Hoboman.Core/Scripts/ScriptOutput.cs`: extensionen `ContentType`, flyttet fra `WorkflowRunner`.
- `src/Hoboman.Core/Sending/ApiResponse.cs`: `ContentType` som `[JsonIgnore]`-property.
- `src/Hoboman.Core/Workflows/RunValues.cs`, `src/Hoboman/ViewModels/ResponseDisplay.cs`: bruger `ApiResponse.ContentType`.
- `src/Hoboman.Core/Workflows/WorkflowRunner.cs`: bruger `kind.ContentType`, `ContentTypeOf` er fjernet.
- `src/Hoboman/App.xaml.cs`: sætter `BrowserView.DataFolder`.
- `src/Hoboman/Controls/BodyFolding.cs`: `Xml` omdøbt til `Tags`.
- `src/Hoboman/Controls/BrowserView.cs`: host pr. side, farve fra `Background`, fejltekst via `Tell`, autofill, permissions og basic auth slået fra.
- `src/Hoboman/ViewModels/BrowserPage.cs`: `null` for en fil fra historikken.
- `src/Hoboman/ViewModels/ResponseViewModel.cs`: `InBrowser` og `Response.OnlyText`, `FileNameOf` bruger `ContentType`.
- `src/Hoboman/Views/ResponseView.xaml`: `Background` på `BrowserView`.
- `src/Hoboman/Views/WorkflowView.xaml`: egne nøgler til output-knapperne.
- `tests/Hoboman.Tests/ViewModels/BrowserPageTests.cs`, `ResponseViewModelTests.cs`: tre nye tests.

Den oprindelige ustagede diff findes ikke længere. De 16 filer i index kan ses med `git diff --cached -- <fil>`, men det viser også ændringer, der var staget før briefen.

</details>
