# Project component: ASN-02, filtriranje dodela studenta

**Team:** 16

**Student:** Danica Nikolić (PR 98/2022)

**Specification requirements:** ASN-02 (povezano: ASN-20, ASN-05, ASN-06, ASN-09)

## Responsibilities and boundaries

- Processes and user scenarios: student traži listu svojih zadataka. Servis vraća samo dodele sa kurseva na koje je student aktivno upisan, i samo polja koja student sme da vidi.
- Contracts with other modules:
  - `IMyAssignmentsService` (nudimo): koristi ga API endpoint `GET /api/me/assignments` (Lazar, tim 16).
  - `IStudentCourseReader` (tražimo): aktivni kursevi studenta, implementacija nad modulom za upise (tim 10, ACC-02).
  - `IAssignmentRepository` (koristimo): dodele kurseva, modul `Platform.Assignments` (tim 15).
- Implementation status: modul `Platform.AssignmentOverview`, model `StudentAssignmentView`, oba ugovora, `MyAssignmentsService` i 4 NUnit testa su implementirani (PR #114). In-memory podaci, pravila za arhivirane, istekle i duplikate i povezivanje sa pravim modulima su planirani.

## Component architecture

```mermaid
flowchart LR
  subgraph T16["Tim 16: ASN-02"]
    API["GET /api/me/assignments<br/>(Lazar)"]
    SVC["MyAssignmentsService<br/>(Danica)"]
    VIEW["StudentAssignmentView"]
  end

  ID["IIdentityContext<br/>sesija, tim 1 (IDN-02)"]
  ACC["Kontrola pristupa<br/>assignment.read.own, tim 3 (ACC-05)"]
  READER[["IStudentCourseReader"]]
  MEM["Upisi na kurseve<br/>tim 10 (ACC-02)"]
  REPO[["IAssignmentRepository"]]
  ASN["Dodele i rokovi<br/>tim 15 (ASN-04, ASN-06)"]
  GRP["Dodela grupi<br/>tim 13 (ASN-05)"]

  API -->|ID studenta iz sesije| ID
  API -->|da li sme da pozove| ACC
  API -->|IMyAssignmentsService| SVC
  SVC -->|aktivni kursevi studenta| READER
  READER -. adapter, planirano .-> MEM
  SVC -->|dodele za kurseve| REPO
  REPO -. implementacija, planirano .-> ASN
  SVC -. filtriranje po grupi, planirano .-> GRP
  SVC -->|samo dozvoljena polja| VIEW
```

Pune strelice postoje u kodu. Isprekidane su planirane veze sa modulima drugih timova. Kutije sa duplim ivicama su ugovori (interfejsi) na granici modula.

## Data flow diagram

```mermaid
flowchart LR
  subgraph CLIENT["Granica poverenja: klijent (browser)"]
    S["Student"]
  end

  subgraph SERVER["Granica poverenja: server"]
    P1["P1 Endpoint<br/>GET /api/me/assignments"]
    P2["P2 Provera pristupa<br/>(tim 3)"]
    P3["P3 Filtriranje dodela<br/>(MyAssignmentsService)"]
    D1[("D1 Sesije<br/>tim 1")]
    D2[("D2 Upisi na kurseve<br/>tim 10")]
    D3[("D3 Dodele<br/>tim 15")]
  end

  S -->|zahtev + sesijski kolačić| P1
  P1 -->|čita ID studenta| D1
  P1 -->|assignment.read.own?| P2
  P2 -->|dozvoljeno ili odbijeno| P1
  P1 -->|ID studenta iz sesije| P3
  P3 -->|čita aktivne kurseve| D2
  P3 -->|čita dodele za te kurseve| D3
  P3 -->|lista StudentAssignmentView| P1
  P1 -->|JSON lista ili 401/403| S
```

Sve što dolazi od klijenta je nepouzdano. Klijent ne šalje ID studenta: on se uzima iz sesije na serveru. Odluka o tome šta student vidi donosi se na serveru, ne u interfejsu.

## Decisions and dependencies

| Tema | Odluka |
|---|---|
| Tuđi moduli | Ne čitamo tuđe baze, već koristimo ugovore (`IStudentCourseReader`, `IAssignmentRepository`). |
| Polja u odgovoru | `StudentAssignmentView` ne sadrži interna pravila dodele (npr. `AllowStartAfterDeadline`). |
| Odbrana u dubinu | Dodela sa kursa koji nije studentov odbacuje se čak i ako je repozitorijum vrati. |
| ACC-17 (provera aktivnog upisa) | Ne radi nijedan tim. Aktivnost upisa (`Active` + `Student`) proverava adapter za `IStudentCourseReader`. |
| ASN-09 (otkazivanje) | Ne radi nijedan tim. Otkazana dodela = status `Archived`; prikazuju se samo `Active` dodele (sprint 2). |
| ASN-01, ASN-03, ASN-17 | Ne radi nijedan tim. Dodele dolaze iz test podataka; istek računa `Assignment.GetAvailability` (tim 15). |
| Istekle dodele | Ne prikazuju se, uz poštovanje individualnog produženja (ASN-06, tim 15) (sprint 2). |
| ASN-20 (bez duplikata) | Ne radi nijedan tim; pokriva ga naše pravilo protiv duplikata (sprint 2). |
| ASN-05 (grupe) | Radi tim 13; filtriranje po grupi kad model bude dostupan. |
| ID kursa | Kod tima 15 `string`, kod članstava `Guid`; usaglašavanje sa timovima 21 i 15. |

## Security requirements

- Server-side access checks: endpoint traži akciju `assignment.read.own` (tim 3, PR #109); servis vraća samo dodele kurseva na koje je student aktivno upisan.
- Input validation and sensitive data: prazan ID studenta se odbija (`ArgumentException`); odgovor sadrži samo polja iz `StudentAssignmentView`.
- Negative scenarios:

| Pretnja | Kontrola | Test |
|---|---|---|
| Student vidi dodelu tuđeg kursa | filtriranje po aktivnim kursevima | `Student_DoesNotSeeAssignmentFromOtherCourse` |
| Greška u tuđem modulu vrati tuđu dodelu | ponovna provera kursa u servisu | `ForeignAssignment_IsFilteredEvenIfRepositoryReturnsIt` |
| Zahtev bez identiteta studenta | odbijanje praznog ID-ja | `EmptyStudentId_IsRejected` |
| Student traži tuđu listu menjanjem ID-ja (IDOR) | ID se uzima samo iz sesije (endpoint) | planirano, Lazar i Luka |

## Evidence of work

- Pull request: #114 (8 commitova u `feature/ASN-02-ASN-07`).
- Run instructions: `dotnet test backend/Platform.AssignmentOverview.Tests`
- Tests and results: 4/4 prolaze lokalno i u CI-u (`Platform.AssignmentOverview.Tests.dll`).
- Next steps: pravila za arhivirane, istekle i duplikate (sprint 2); adapter nad modulom tima 10 i prava implementacija dodela tima 15 (sprint 3).