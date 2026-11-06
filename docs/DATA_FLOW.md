# Data flow — platform-level example

This conceptual example is a starting point. KT1 requires each student to provide a separate DFD for their own component, including its actual processes, actors, and stores.

```mermaid
flowchart LR
  Student[Student] -->|Lab request| API[API and server-side authorization]
  API -->|Check context| Access[Identity and authorization]
  Access -->|Allow / deny| API
  API -->|Authorized revision| Catalog[(Published revisions)]
  API -->|Save response| Attempt[(Attempts)]
  Attempt -->|Response snapshot| Eval[Evaluation]
  Eval -->|Outcome and feedback| Result[(Result history)]
  API -->|Security event| Audit[(Audit log)]
  Result -->|Authorized view| Student
```

## Identity login flow (IDN-02 / IDN-03)

```mermaid
flowchart LR
  User[User] -->|Email and password| Login[AuthController]
  Login --> UseCase[LoginService, IDN-02]
  UseCase --> Verify[ICredentialVerifier, IDN-02]
  Verify -->|Read e-mail, hash and status| Accounts[(identity.accounts)]
  Verify -->|Invalid| Reject[Generic rejection, 401]
  Verify -->|Valid, with status| Status[Status check, IDN-03]
  Status -->|BLOCKED / ARCHIVED| Reject
  Status -->|ACTIVE| Issue[ISessionCreator, IDN-02]
  Issue -->|Session cookie| Login
  Reject --> Login
```

The API checks status only after credentials are valid. Blocked and archived
accounts get the same response as wrong credentials, so the response does not
reveal the account status; the audit event records the reason.

## Component DFD requirements

1. Input and output data, external actors, and request initiators.
2. Processes and the stores they access.
3. Trust boundaries and server-side authorization checks.
4. Negative flows and handling of sensitive data.
5. The distinction between implemented and planned behavior.
