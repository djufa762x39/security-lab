# Team 21, deo 9 — tok podataka za KT1

Tok stvarnih ACC-06 HTTP endpointa na implementacionom HEAD-u `c94882e`. Cookie je nepoverljiv ulaz dok ga IDN ne validira. Team 21 ne implementira login, skladište sesija ili opoziv.

```mermaid
flowchart TB
    client["Spoljašnji klijent<br/>autentifikovan ili anoniman"]
    request["HTTP zahtev: SecurityLab.Session cookie<br/>metod, putanja, ID i telo<br/>X-Test-User / X-Stub-User / druga zaglavlja<br/>NISU pouzdan identitet"]

    subgraph platform["Granica poverenja — zajednički serverski host / IDN"]
        correlation["1. AUD middleware generiše korelacioni ID<br/>TraceIdentifier, scope, odgovor X-Correlation-ID<br/>klijentska vrednost se ignoriše"]
        validate["2. SessionCheckMiddleware / SessionManager<br/>proveravaju cookie, istek i opoziv"]
        sessionstore[("IDN podaci sesije")]
        valid{"Važeća sesija?"}
        actor["3. IIdentityContext → SessionCurrentActor<br/>samo authenticated + Active + neprazan UserId<br/>bez fallback-a na zaglavlja"]
        authenticated{"Autentifikovan actor?"}
    end

    subgraph team["Team 21 — servisna autorizacija i podaci ACC-06"]
        inputs["4. Endpoint bira radnju i resurs<br/>GroupService / MaterialVisibilityService<br/>ne veruje klijentskoj ulozi ili grupi"]
        state["Čitači statusa, članstva i stanja kursa<br/>Development: fakes<br/>ostala okruženja: Inactive / null / null<br/>HTTP testovi: kontrolisane zamene"]
        groupstore[("groups: članstva i pravila publike<br/>stvarni EF čitači, upis preko GroupService")]
        decision{"5. Actor + action + resource<br/>aktivan nalog i članstvo / uloga<br/>stanje kursa i relevantna grupna pravila<br/>dozvoljavaju operaciju?"}
        allow["ALLOW: čitanje / dozvoljeni upis<br/>filtrirana lista ili DTO / referenca<br/>200 / 201 / 204 prema endpointu"]
        deny["DENY: zaštićeni resurs nije otkriven<br/>404 kao za nepostojeći resurs"]
        events["6. Odabrani serverski audit događaji<br/>actor, action, resource, ishod ili razlog<br/>UTC vreme i serverska korelacija"]
        logger["LoggingAuditTrail / LoggingVisibilityAuditWriter"]
    end

    unauthorized["401 — sesija ili Team 21 identitet nisu prihvaćeni"]
    logs[("Strukturisani ILogger izlaz<br/>bez cookie-ja, tokena i sadržaja zahteva")]
    pending["KT2 — nepovezani autoritativni izvori<br/>IDN status, ACC članstvo/delegacije, ACC-01 stanje"]
    auditpending["KT2 — dogovor i adapter za centralni AUD<br/>ovaj tok ga još ne upisuje"]

    client --> request
    request --> correlation
    correlation -->|"zahtev; Access delegira autorizaciju servisima"| validate
    validate -->|"čitanje validacionih podataka"| sessionstore
    sessionstore -->|"podaci za validaciju"| validate
    validate -->|"rezultat provere"| valid
    valid -->|"ne"| unauthorized
    valid -->|"da: validirani rezultat u HttpContext.Items"| actor
    actor --> authenticated
    authenticated -->|"ne: npr. MFA pending"| unauthorized
    authenticated -->|"da: samo serverski UserId"| inputs
    inputs --> decision
    state -->|"status / članstvo / uloga / stanje"| decision
    groupstore -->|"pravila materijala i aktivne grupe"| decision
    decision -->|"da"| allow
    decision -->|"ne"| deny
    allow -->|"samo dozvoljene promene grupa / članova"| groupstore
    allow -->|"odabrane uspešne promene grupa"| events
    deny -->|"odabrana odbijanja"| events
    correlation -->|"isti serverski ID preko ICorrelationContext"| events
    events --> logger
    logger --> logs
    unauthorized --> client
    deny --> client
    allow --> client
    pending -.->|"buduće zamene postojećih čitača"| state
    logger -.->|"buduće mapiranje i pouzdan upis"| auditpending
```

**Legenda:** pune strelice su postojeći tokovi, uključujući testne zamene; isprekidane su isključivo buduće zavisnosti. Pravougaonici predstavljaju procese/podatke, rombovi odluke, a cilindri skladišta ili log izlaz. Serverska granica pretvara cookie u validirani identitet; granica Team 21 označava vlasništvo nad poslovnim odlukama, ne novu mrežnu zonu.

Ovo je sažet autorizacioni tok, ne univerzalni model svih odgovora. Nije svaka operacija zavisna od svih izvora: čitanje grupa proverava nastavnika i aktivan nalog/članstvo, dok promene dodatno traže aktivan kurs/grupu. Materijalni servis kurs i dozvoljene grupe uzima iz serverskog pravila materijala. Za `Read` i aktivan kurs pristup imaju odgovarajući članovi; arhiviranom kursu pristup zadržavaju nastavnik i asistent sa `CanReview`. Student dobija samo CourseWide ili reference dozvoljene njegovim aktivnim grupama. Uspešna lista može biti prazna; skriveni materijal se izostavlja, bez posebnog 404 za svaki element.

Nevažeći ulaz može dati 422, arhiviranje/sukob promene 409, a neispravna putanja može biti odbijena pre servisa. Nisu svi 404 niti uspešna čitanja auditovani. `LoggingAuditTrail` emituje `Outcome` za odabrane promene/odbijanja; visibility writer emituje samo odbijanja sa `Reason`, bez posebnog polja `Outcome`. Session 401 nije obećanje Team 21 audit događaja. Ovi logger-i ne čitaju zaglavlja/telo, ali nisu opšti sanitizer: pozivaoci moraju slati samo dogovorene metapodatke.

Korelacija je generisana na serveru i ostaje ista kroz zahtev i log. Testovi ne koriste stvarne kredencijale: zamenjuju skladišta EF InMemory providerom i spoljne čitače kontrolisanim podacima, uz stvarni IDN validator i Team 21 servise. Time se ne dokazuje status iz autoritativnog IDN repozitorijuma, PostgreSQL ponašanje, stvarni sadržaj LAB/ASN ili centralna AUD perzistencija.

Izvori: [IDN ugovor](../../contracts/IIdentityContext.md), [session middleware](../../../backend/Platform.Api/Middleware/SessionCheckMiddleware.cs), [adapter](../../../backend/Platform.Api/Modules/Team21/Integration/SessionCurrentActor.cs), [GroupService](../../../backend/Team21.Acc.Infrastructure/Groups/Services/GroupService.cs), [MaterialVisibilityService](../../../backend/Team21.Acc.Infrastructure/Groups/Services/MaterialVisibilityService.cs), [HTTP mapiranje](../../../backend/Team21.Acc.Api/Extensions/ResultHttpExtensions.cs), [HTTP testovi](../../../backend/Team21.Acc.Tests/Api/SessionCurrentActorHostIntegrationTests.cs). [Matrica odluka i pretnje](KT1_READINESS.md).
