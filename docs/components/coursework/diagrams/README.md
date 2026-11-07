# KT1 diagrams

Each `.mmd` is editable Mermaid source. The matching `.svg` is rendered and opens in a browser without Mermaid tools.

| Diagram | Component / purpose |
|---|---|
| acc04-architecture | ACC-04 components, interfaces and dependencies |
| acc04-dfd | ACC-04 actors, processes, stores and trust boundaries |
| adm08-architecture | ADM-08 components, interfaces and dependencies |
| adm08-dfd | ADM-08 administrative data flow and trust boundaries |
| access-admin-domain | Unified entities, ownership and course scope |
| access-admin-flow | Shared decision and snapshot-bound read/write flow |

Provider ports are trusted server contracts. In KT1 their adapters run in the in-memory simulator; separate boxes identify ownership and trust boundaries, not deployed microservices. The administrative store contains no student answers or teaching memberships.

Malformed transport input (such as an invalid ID/body or a missing mutation header) can be rejected before application audit processing. The audit flows represent application outcomes and relevant changes; they do not claim to log every framework/parser rejection.
