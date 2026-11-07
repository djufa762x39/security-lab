# Team 21, deo 9 — arhitektura za KT1

Prikaz radne grane `feature/acc-integration`, implementacioni HEAD `c94882e`. Veze predstavljaju ugovore i korišćene komponente, a redosled HTTP obrade prikazuje [DFD](DFD.md). Vlasništvo je označeno po modulu; host kompozicija i IDN ostaju zajedničke odgovornosti.

```mermaid
flowchart TB
    client["Nepouzdan klijent<br/>HTTP, cookie, ID resursa i zaglavlja"]

    subgraph host["Serverska granica poverenja — zajednički Platform.Api"]
        corr["AUD CorrelationMiddleware<br/>nov serverski TraceIdentifier"]
        session["IDN SessionCheckMiddleware<br/>validacija pre Team 21 endpointa"]
        identity["IDN IIdentityContext<br/>SessionIdentityContext, scoped"]
        adapter["Team 21 host adapter<br/>SessionCurrentActor, scoped"]
        access["ACC-05 delegiranje postojećoj<br/>servisnoj autorizaciji"]
    end

    subgraph idn["IDN vlasništvo — postojeća serverska validacija"]
        validator["SessionManager<br/>istek i opoziv sesije"]
        sessions[("IDN session store<br/>postojeći EF adapter")]
    end

    subgraph api["Team21.Acc.Api — HTTP i DI"]
        endpoints["ACC-06 group / material endpointi<br/>ACC-01 CoursesModule je kostur"]
        correlation["HttpCorrelationContext<br/>Team 21 ICorrelationContext"]
    end

    subgraph core["Team21.Acc.Core — bez HTTP i EF"]
        caller["ICurrentActor"]
        ports["IGroupService / IMaterialVisibilityService<br/>IAccountStatusReader / ICourseMembershipReader<br/>ICourseStateReader / audit ugovori"]
    end

    subgraph infra["Team21.Acc.Infrastructure — ACC-06 vlasništvo"]
        services["GroupService<br/>MaterialVisibilityService<br/>serverske odluke pristupa"]
        readers["GroupMembershipReader<br/>MaterialAccessRuleReader"]
        db["IGroupsDbContext / GroupsDbContext"]
        store[("groups šema<br/>Groups / GroupMembers<br/>MaterialAudiences / GroupMaterialAccess")]
        doubles["Trenutni spoljašnji čitači<br/>Development: FakeAccountStatusReader,<br/>FakeCourseMembershipReader, FakeCourseStateReader<br/>Ostalo: DenyAllAccountStatusReader,<br/>NoCourseMembershipReader, NoCourseStateReader"]
        audit["LoggingAuditTrail<br/>LoggingVisibilityAuditWriter"]
    end

    logs[("ILogger izlaz aplikacije<br/>strukturisana polja i korelacija")]
    pending["KT2 — NEPOVEZANO<br/>autoritativni IDN status<br/>ACC-02/03 članstvo i ACC-07 delegacije<br/>stvarni ACC-01 izvor stanja kursa"]
    content["KT2 — LAB / ASN / AUT<br/>provera stvarnog sadržaja i upis publike"]
    central["KT2 — centralni AUD<br/>katalog, adapter i pouzdan upis"]

    client -->|"HTTP granica: ulaz nije identitet"| corr
    corr -->|"isti zahtev"| access
    access -->|"delegirana Team 21 ruta"| session
    session --> validator
    validator --> sessions
    session -->|"validirani rezultat u HttpContext.Items"| identity
    identity --> adapter
    adapter -->|"implementira"| caller
    session -->|"tek nakon validacije"| endpoints
    endpoints -->|"poziva Core ugovore"| ports
    ports -->|"DI implementacije"| services
    caller --> services
    caller --> audit
    corr -->|"TraceIdentifier"| correlation
    correlation --> services
    correlation --> audit
    services --> readers
    services --> db
    readers --> db
    db --> store
    services -->|"postojeći reader ugovori"| doubles
    services -->|"odabrane promene i odbijanja"| audit
    audit --> logs
    pending -.->|"buduće zamene čitača"| doubles
    content -.->|"budući poziv pre isporuke sadržaja"| ports
    audit -.->|"buduća zamena logger adaptera"| central
```

**Legenda:** puna strelica znači postojeću vezu, uključujući implementirane fake/deny-all zamene. Isprekidana strelica znači samo označenu buduću integraciju. Podgrafovi označavaju vlasništvo i serversku granicu poverenja; slojevi nisu zasebni mrežni servisi.

- Host eksplicitno zamenjuje `ICurrentActor` sa `SessionCurrentActor`; postojeći scoped `IIdentityContext` ostaje isti. Prihvata se samo `IsAuthenticated == true`, nivo `Active` i neprazan `UserId`. `X-Test-User`, `X-Stub-User` i ostala klijentska zaglavlja nisu izvor identiteta. Nema session bypass-a za `/api/courses` ili `/api/materials`, uključujući Development.
- `groups` šema i EF čitači postoje, ali njena PostgreSQL dostupnost nije dokazana InMemory testovima. Ne postoji nacrtana međumodulska FK veza. IDN skladište sesija nije izvor Team 21 statusa naloga; autoritativni status još nije potvrđen.
- `ICourseStateReader` ovde koristi fake ili `null` rezultat. ACC-01 je na ovoj grani kostur; noviji kod na udaljenom `main` nije uključen. Spajanje osnove i stvarni adapter ostaju poseban rad Tima 21.
- Materijalni endpointi vraćaju reference `Type`/`MaterialId`, a ne LAB/ASN sadržaj. Log izlaz nije dokaz centralnog AUD skladištenja niti garantovanog audit zapisa uz transakciju. `LoggingAuditTrail` dobija actor preko `ICurrentActor`; visibility writer dobija već formiran serverski događaj.

Izvori: [host kompozicija](../../../backend/Platform.Api/Program.cs), [session middleware](../../../backend/Platform.Api/Middleware/SessionCheckMiddleware.cs), [sesijski adapter](../../../backend/Platform.Api/Modules/Team21/Integration/SessionCurrentActor.cs), [Team 21 DI](../../../backend/Team21.Acc.Api/Team21AccModule.cs), [ACC-06 DI](../../../backend/Team21.Acc.Api/Groups/GroupsModule.cs), [Core ugovori](../../../backend/Team21.Acc.Core/Groups/Visibility/Interfaces/), [Infrastructure servisi](../../../backend/Team21.Acc.Infrastructure/Groups/Services/). [KT1 dokazi i ograničenja](KT1_READINESS.md).
