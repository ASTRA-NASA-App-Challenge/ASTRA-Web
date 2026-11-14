# ASTRA accounts

| Role | Web credentials | Medical record access |
| --- | --- | --- |
| Crew | Username or email + unique six-digit PIN as password | Own records |
| Medic | Username or email + unique six-digit PIN as password | Everyone's records |
| GroundControl | Username or email + conventional password | Everyone's records |

PINs are strings, so leading zeroes are preserved. Identity stores salted password
hashes for all accounts. The same PIN is also stored encrypted with ASP.NET Core
Data Protection so it can be displayed to its owner and ground control. A separate
HMAC fingerprint enforces PIN uniqueness with a database unique index; it is not
another credential. Ground control passwords
must have at least 12 characters, including uppercase, lowercase, a digit, and a
non-alphanumeric character. Failed web logins count toward Identity's five-attempt,
15-minute account lockout.

## Provisioning

There is no registration page; the registration routes from the Identity UI
library are removed. Only ground control accounts are provisioned on startup from
`Identity:Accounts`. Keep credentials in user secrets for development or the
deployment secret store, never committed appsettings files. For example:

```powershell
dotnet user-secrets set 'Identity:PinLookupKey' '<random stable secret of at least 32 bytes>' --project Web
dotnet user-secrets set 'Identity:Accounts:0:UserName' 'ground-control' --project Web
dotnet user-secrets set 'Identity:Accounts:0:Email' 'ground@example.org' --project Web
dotnet user-secrets set 'Identity:Accounts:0:Role' 'GroundControl' --project Web
dotnet user-secrets set 'Identity:Accounts:0:Password' '<strong account password>' --project Web
```

Use subsequent account indices for additional ground control accounts. Crew and
medics are created by `OnboardMemberService.CreateAsync`, which requires an
authenticated ground control principal, username, email and role (`Crew` or
`Medic`). It generates a cryptographically random, unique six-digit PIN and
creates the account and role assignment in one database transaction. No PIN is
supplied by the operator. The admin panel and onboard members page will call this
service when those screens are built. Roles are seeded even when no accounts are
configured. Provisioned emails are treated as already verified.
Existing configured accounts are checked for role consistency; their passwords
are not overwritten on restart. Password changes/reset use the same validation
and fingerprint logic as account creation. Do not change the PIN lookup key
without rebuilding all existing PIN fingerprints with the new key.

## Viewing PINs

Crew and medics have a **My PIN** navigation link after signing in.
`OnboardMemberService.GetPinAsync` permits a member to read only their own PIN,
and ground control to read any onboard member's PIN. A medic cannot read another
member's PIN, even though they can read their medical records. The future onboard
members page must use this service when displaying codes. Ground control accounts
have no PIN and their passwords cannot be displayed.

The PIN page sends `Cache-Control: no-store`. The encrypted copy is updated
alongside the password hash when a PIN changes. Persist and back up the Data
Protection key ring separately from the database; all deployed app instances must
share the same key ring and application name (`ASTRA`). Losing those keys makes
stored PINs unreadable, though the login password hashes still work. Existing
accounts created before encrypted storage need a PIN reset before their PIN can
be displayed. PIN values must never be logged or included in general user DTOs.

The old SQLite Identity schema created with `EnsureCreated` is adopted as the
initial migration on startup, preserving accounts. Existing accounts receive no
role automatically: explicitly assign the appropriate role and reset crew/medic
passwords to valid, unique PINs before putting them into service. Ground control
accounts must have `IsGroundControl = true` before setting their passwords. The
flag selects credential validation; Identity roles grant record access. Change
these together through controlled provisioning, never from user-submitted input.

## Applying access rules

There are no medical record endpoints in the current app. Every future record
endpoint must enforce `ReadMedicalRecord` on the server using the actual patient
Identity user ID, before returning any data:

```csharp
var access = await authorizationService.AuthorizeAsync(
    User, record.PatientUserId, MedicalPolicies.ReadRecord);
if (!access.Succeeded) return Forbid();
```

For lists, filter crew queries to the signed-in user's ID before materializing
records. This policy grants read access only; write permissions need their own
explicit policy. Do not return Identity entities as medical record responses.

The hardware/API authentication endpoint is a future integration. It should use
username/email plus the same Identity password (the PIN), enforce lockout, and
issue the chosen API authentication mechanism. Do not add a separate PIN field or
bypass Identity's password verification and access rules.
