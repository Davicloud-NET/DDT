# DDT, the Davicloud Deployment Toolkit

DDT is a self-hostable, open-source replacement for the Microsoft Deployment Toolkit, aimed at small
networks of 10 to 100 machines that may span several sites over a VPN. It netboots machines into
Windows PE and runs an agent there that executes a task sequence: it applies a Windows image, goes
on in the installed Windows where the sequence asks for it, and reports progress live to a web UI.
Windows PE is the only deployment environment: Linux is deployed from inside WinPE by writing a raw
disk image and a cloud-init seed partition, so there is a single agent and a single boot path. DDT
never ships its own EFI bootloader. It serves the Microsoft-signed `bootmgfw.efi` from the Windows
ADK and does everything interesting after the boot manager has loaded, which is what lets it work on
stock PCs with UEFI Secure Boot enabled.

## Architecture

One .NET solution, one container image, three runtime roles selected by the `DDT:Roles`
configuration value.

```
                         +--------------------------------------------------+
                         |  container image "ddt", roles from DDT:Roles     |
                         |                                                  |
   browser  ---HTTPS-->  |  web      ASP.NET Core host                      |
                         |           web UI, agent API, image library,      |
                         |           task sequences, packages, database     |
                         |                                                  |
   firmware ---UDP--->   |  pxe      ProxyDHCP 67 + 4011, TFTP 69, HTTP     |
            <--TFTP--    |           boot 8080. Host networking. One        |
                         |           central instance serves every site.    |
                         |                                                  |
                         |  builder  not a service: a PowerShell script     |
                         |           run on Windows with the ADK installed  |
                         +--------------------------------------------------+
                                 |                              |
                                 | HTTPS                        | plain HTTP
                                 | /api, /hubs                  | /boot/*
                                 |                              | (UEFI HTTP Boot cannot
                                 v                              |  validate a private CA)
                    +------------------------+                  v
                    |  DDT.Agent (NativeAOT) |          +----------------+
                    |  running in Windows PE |          |  firmware      |
                    +------------------------+          +----------------+
```

Boot flow:

1. Client firmware sends a DHCP Discover with option 60 `PXEClient`, or `HTTPClient` for UEFI HTTP
   Boot. The site's own DHCP server hands out the IP. Either that server names DDT itself in
   options 66 and 67, or the `pxe` role answers as ProxyDHCP with the boot server and file. Option
   93 carries the client architecture and selects the boot file.
2. The firmware loads `bootmgfw.efi` over TFTP, and the boot manager reads `BCD`, `boot.sdi` and
   `boot.wim` (about 475 MB, or 330 MB built without PowerShell) over TFTP as well. That is
   everything TFTP carries.
3. WinPE starts, `startnet.cmd` launches `DDT.Agent`, which registers with the `web` role over
   HTTPS using its MAC addresses and SMBIOS UUID, receives a task sequence, executes it, streams
   log lines and progress, and survives reboots by persisting state to the local disk. Everything
   after the boot files, images and task sequences included, travels over HTTPS.
4. Steps that need the installed Windows run there after Windows setup, in the same agent running
   as a temporary service that removes itself when the run is over.

A machine that already runs Windows is re-imaged by netbooting it as well. Staging Windows PE on a
local partition from a running Windows, without PXE, is left for later.

## Repository layout

```
DDT.slnx
Directory.Build.props      build policy: net10.0, nullable, warnings as errors
Directory.Packages.props   central package management
global.json                SDK pin, Aspire MSBuild SDK version, test runner
.editorconfig              C# formatting, naming and var usage
LICENSE                    GNU General Public License, version 3
NOTICE                     attribution notice and the additional terms under GPL section 7
THIRD-PARTY-NOTICES.md     software by others in DDT's built artefacts, and its licences
licenses/                  licence texts of that software, one folder per component
docs/settings.md           design of the settings: their store, sections, rules and API
docs/roadmap.md            the milestones after M6 and what each one holds
docs/web-ui.md             how the web UI is built, for anyone changing it
src/
  DDT.Core/                domain model, image library, hashing, task sequences, GPT, FAT and
                           cloud-init seeds. No ASP.NET, no EF
  DDT.Protocols/           DHCP/PXE codec and TFTP state machine. Pure, no sockets
  DDT.Contracts/           DTOs shared with the agent, source-generated JSON
  DDT.Pxe/                 hosted services that bind the UDP sockets and drive DDT.Protocols
  DDT.Server/              EF Core, image storage, minimal API endpoints, SignalR hubs
  DDT.Host/                ASP.NET Core entry point. Registers roles, serves the API, hubs and SPA
  DDT.Web/                 Vite + React + TypeScript SPA: React Aria components, Tailwind CSS, Lingui
  DDT.Design/              design tokens, the one source of the look, and the generator for the theme
  DDT.Agent/               NativeAOT agent for Windows PE, and its temporary service in Windows
  DDT.AppHost/             Aspire orchestration, development only
  DDT.ServiceDefaults/     OpenTelemetry, health checks, service discovery
tests/
  DDT.Core.Tests/
  DDT.Protocols.Tests/
  DDT.Pxe.Tests/
  DDT.Server.Tests/
  DDT.Agent.Tests/
  DDT.E2E/                 the real host and the published agent in dry runs, not in the default run
build/
  Dockerfile
  compose.yaml
  Build-BootImage.ps1      WinPE boot files, BCD and both boot managers, laid out for the pxe role
  Publish-Agent.ps1        DDT.Agent as one NativeAOT executable for Windows PE
  New-TestVm.ps1           Hyper-V Generation 2 test machine with Secure Boot on
  Start-DevHost.ps1        DDT from source with the pxe role, reachable from the test machine
```

`DDT.Pxe` is a class library of hosted services, not a separate executable. There is one image with
one entry point, and `DDT:Roles` decides what runs inside it.

## Prerequisites

- .NET SDK 10.0.201 or a later 10.0 feature band
- Node.js 24 LTS (see `src/DDT.Web/.nvmrc`). Node 25 works but is outside the engine ranges
  declared by Vitest and jsdom, and that line is no longer maintained
- Docker, for the container build and for the Postgres integration tests
- Visual Studio C++ build tools, to publish `DDT.Agent` with NativeAOT
- The Windows ADK with the WinPE add-on 10.1.26100.2454 or later, to build the boot files
- Hyper-V, for the netboot test machine

## Building and running

```bash
dotnet build DDT.slnx
```

```bash
dotnet test --solution DDT.slnx -- --filter-not-trait "Category=E2E" --ignore-exit-code 8
```

The solution uses the Microsoft Testing Platform, so `dotnet test` needs `--solution` rather than a
positional solution path. `--ignore-exit-code 8` is required because filtering every test out of
`DDT.E2E` makes that assembly report "zero tests ran", which is otherwise a failure. Tests that need
an elevated prompt or real hardware carry the same trait. Server tests run the real host on SQLite
and a temporary store directory, not an in-memory store, because images are served as files. The
PostgreSQL tests, of the migrations and of a run, start a PostgreSQL container and run only while
Docker is running; they are skipped otherwise.

```bash
dotnet test --project tests/DDT.E2E
```

runs whole task sequences end to end on this PC, in about a minute and a half. It publishes the
agent from the sources with `build\Publish-Agent.ps1`, and is skipped when vswhere finds no Visual
C++ build tools, which the publish needs; any other failure to publish fails it. It starts the host
on a free localhost port with SQLite and a store in a temporary directory, uploads made-up images
and packages, and runs the agent in dry runs, which change nothing on the PC, against it. The
processes it starts end with it, even when it is killed, and its temporary directories go when it
ends or is cancelled.

Run the whole development stack, host plus SPA dev server, through Aspire:

```bash
dotnet run --project src/DDT.AppHost
```

Aspire starts Kestrel on a dynamic port and passes it to Vite as
`services__ddt-host__http__0`, which `vite.config.ts` reads to target its `/api` and `/hubs`
proxies. Run the host on its own instead with:

```bash
dotnet run --project src/DDT.Host
```

The development database is SQLite, created directly from the model. It is not migrated: when the
schema changes, the host refuses to start and names what is missing. Delete
`ddt-dev.db` in `DDT:StorePath` and start again; a new administrator password is printed.

The SPA on its own:

```bash
cd src/DDT.Web && npm install && npm run dev
```

`npm run build` writes into `src/DDT.Host/wwwroot`, which the host serves with a fallback to
`index.html`. That directory is entirely build output and is not tracked.

Container:

```bash
docker build -f build/Dockerfile -t ddt:dev .
```

```bash
echo "DDT_DB_PASSWORD=$(openssl rand -hex 24)" > build/.env
docker compose -f build/compose.yaml up
```

The compose file runs PostgreSQL next to DDT, as the `db` service with its own volume, and starts
DDT once the database answers. Compose refuses to start until `DDT_DB_PASSWORD` is set, in
`build/.env` next to the compose file or in the environment; git ignores that file. Use letters and
digits only, because the password is written into a connection string. The database listens only on
this host's loopback, at port 5433 unless `DDT_DB_PORT` says otherwise.

An install that ran on SQLite before, as the compose file of earlier versions did, starts empty on
PostgreSQL and prints a new first administrator password. The image files stay in the store volume,
and uploading one again stores no second copy. `ddt-dev.db` in the store volume can then be deleted.

The compose file uses host networking, because the `pxe` role has to see DHCP broadcasts and bind
UDP 67, 4011 and 69. Host networking is a Linux host feature. On Docker Desktop the container
starts but broadcast traffic is not delivered to it, so PXE cannot be tested there.

The image itself sets what every container needs: `DDT__StorePath=/var/lib/ddt`, the HTTPS endpoint
`Kestrel__Endpoints__Https__Url=https://0.0.0.0:8443`, and the certificate at
`/var/lib/ddt/certs/ddt.pem` with its key `ddt-key.pem` next to it. An environment variable of the
same name overrides each of them. The store path is DDT's own default as well, repeated in the
image. The endpoint and the certificate paths are not defaults of DDT itself, because a declared
Kestrel endpoint makes Kestrel ignore the URLs that Aspire and launch profiles assign.

## The web UI

The `web` role serves the web UI at the server's address. Its top bar holds five categories, each
with a row of pages, and every setting sits on the page of the thing it configures:

| Category | Pages |
|---|---|
| Machines | All machines, a page for each machine with its run and live log, Run history, Approval and zero touch |
| Deployment | Task sequences, Assignment rules, Deployment defaults |
| Library | OS images, Drivers, Files |
| Boot | Boot image, Network boot |
| Administration | Users and roles, Sign-in, API tokens, Server, Audit log |

- **Live.** The pages hold one connection to the server's hub, and what anyone changes, a machine
  that registers, a step that finishes, a sequence another administrator saves, shows at once
  without a reload. An action puts the server's answer on the page instead of reading the list
  again. While the connection is down a banner says so, the lists are read every 5 seconds, and
  everything the hub would have changed is read once when it is back.
- **Roles.** A viewer sees machines and runs and changes nothing. An operator also approves and
  removes machines, starts and ends their deployments, and reads the deployment defaults and the
  approval settings. An administrator changes everything else, the accounts, the sign-in and the
  server's settings included, see [Users and roles](#users-and-roles). A page for administrators
  only says so to anyone else, and reads nothing from the server.
- **Languages.** English and German. The UI starts in the first language of the browser that it
  has, else English, and the account menu changes it for that browser. The server's refusals and
  validation messages carry codes, so they are shown in the chosen language too. The agent's
  console in Windows PE is English.
- **Look.** Light and dark, following the system unless the account menu picks one. The look,
  Switchgear, is defined once in `src/DDT.Design/tokens.json`, the source of the web's theme and
  later of the agent's console in Windows PE.
- **Keyboard and phone.** Ctrl K opens a search over the pages, machines, task sequences and OS
  images. Every control works from the keyboard, and the pages fit a phone's width, for approving a
  machine or watching a run away from a desk.

[docs/web-ui.md](docs/web-ui.md) describes how the UI is built, for anyone changing it.

## Configuration

`DDT:Roles` is a single comma separated string, not a list:

```bash
DDT__Roles=web,pxe
```

It is deliberately not a bound array. Indexed environment variables such as `DDT__Roles__0` merge
with a configured array rather than replacing it, so a shipped default plus an override would leave
unwanted roles running. Unknown role names fail at startup rather than being ignored.

The same goes for keys: DDT refuses to start when a key under `DDT` is not one it reads, in any
section and whichever roles run, and names it, so a misspelled setting cannot silently keep its
default. The names of map entries, the groups in `DDT:Ldap:GroupRoleMap` and the architectures in
`DDT:Pxe:BootTargets`, are free. Values it can check, it checks before it starts anything, and it
lists the problems with keys and values in one message, each after its full key, such as
`DDT:Deployment:TimeZone: 'Europe/Berlin' is not a Windows time zone id`. The `DDT:Pxe` values are
checked only where the `pxe` role runs. Two checks stop it on their own, outside that message: an
unknown role in `DDT:Roles`, checked first, and a missing HTTPS endpoint, checked later.

What a deployed Windows is set up with comes from the Deployment defaults page,
described under [What Windows shows at its first start](#what-windows-shows-at-its-first-start) and
[Joining a domain](#joining-a-domain). Task sequences, packages and rules are not configuration:
they live in the database and are managed on their pages.

### Settings in the web UI

Since M6.5 the settings an admin changes in normal operation are edited in the web UI, each on the
page of what it configures, and stored in the database in `ddt."SettingsSections"`, one row per
section:

| Section | Page |
|---|---|
| `deployment` | Deployment, Deployment defaults |
| `machines` | Machines, Approval and zero touch |
| `pxe` | Boot, Network boot |
| `ldap`, `oidc` | Administration, Sign-in |
| `certificate` (the server names, see [The server certificate](#the-server-certificate)), `proxies`, `logging` | Administration, Server, which also shows the state of every section and uploads the agent |

A change there is checked when it is saved, applies without a restart and leaves an audit row that
names every field it changed. The keys this README names,
such as `DDT:Machines:ZeroTouchNetworks`, are the fields of those sections, and configuration still
sets them: [docs/settings.md](docs/settings.md) is the design.

- **Configuration wins, and locks.** A field whose key is present in configuration, from any source
  and even with an empty value, takes the configured value, and the page shows it locked with both
  spellings of the key, `DDT:Deployment:Domain:Name` and `DDT__Deployment__Domain__Name`, and
  whether the page's own value differs. A save leaves a locked field alone, and the page's value
  applies again once the key is removed. A map or a list, such as `DDT:Ldap:GroupRoleMap` or
  `DDT:Oidc:Scopes`, is locked as a whole and read on its own: configured entries replace the
  defaults rather than adding to them, so `DDT:Oidc:Scopes` lists every scope, `openid` first.
- **Configuration is imported once.** At every start, each field that was never written takes its
  configured value, if the key is present, secrets encrypted on the way in, and a
  `settings.imported` audit row names them. So an install upgraded to M6.5 keeps its values; once
  the keys are removed from the environment, the page edits them.
- **Secrets are written, never read back.** The deployment passwords, `DDT:Ldap:BindPassword` and
  `DDT:Oidc:ClientSecret` are encrypted with the Data Protection key ring, for their section and
  field alone. The page learns only whether one is set, keeps it when a save leaves it out, and sets
  or clears it on request. The LDAP bind password is kept only while host, port and transport stay
  the same, and the client secret only while the authority does: a save or a test that points a
  stored secret at another server is refused and audited as `settings.refused`.
- **Fields that grant roles or trust need a fresh password.** Zero touch networks, the trusted
  proxies, the LDAP connection and group map, the OpenID Connect authority, group claim and map
  and the provisioning of new accounts change only with a token from
  `POST /api/settings/reauthenticate`, valid for 5 minutes, bound to the account and its security
  stamp. An account without a password DDT can check, and an API token, cannot get one.
- **Some saves ask first.** A network wider than a /16 or a /48, unencrypted LDAP, a new
  immutable-id attribute, a group map without an administrator group, new accounts from single
  sign-on as operators, a directory or single sign-on save while no local administrator is enabled,
  and an HTTP boot file outside DDT's `/boot/` are refused until the save confirms them.
- **Bad stored values close their section, not the server.** A stored value that fails a check,
  written by an older build or through the database, keeps the server running and the section closed
  until the page fixes it: no new runs for `deployment`; zero touch off and web approval on for
  `machines`; no directory sign-in; no single sign-on scheme; no trusted proxy, and no zero touch;
  no netboot; the default log levels. A configured value that fails a check still stops the start,
  as before.
- **Several processes, one database.** A save applies at once in the process that made it. The
  others read the store every 15 seconds. Every process on one database has to share the key ring
  in `DDT:StorePath/keys`; one that cannot read it saves nothing and says so on the Server page. The pxe,
  oidc and proxies sections are rebuilt inside the running server, and the page shows for each host
  whether it applied the newest version, in `ddt."SettingsHostStates"`.

When the page cannot fix itself, two commands run next to the running server with only the database
and the key ring, and write through the store with an audit row by `console`. The servers apply them
within 15 seconds:

```bash
docker exec ddt ./DDT.Host settings reset ldap      # the code defaults, secrets cleared
docker exec ddt ./DDT.Host settings create-admin    # a local administrator, or admin enabled again
```

`create-admin` prints the new password once. For an existing local account it also ends a lockout
and turns off its second factor. A configured key still overrides a field after `reset`, so a key such
as `DDT__Ldap__Enabled=false` is the other way back in.

### What stays in configuration

Configuration keeps only what the server needs before it can serve the web UI, and stays
available as an override for recovery and for installs managed as code:

| Setting | Why it stays in configuration |
|---|---|
| `ConnectionStrings:ddtdb` | The settings are stored in this database. |
| `DDT:StorePath` | It holds the key ring that decrypts stored secrets, and the SQLite file. |
| `DDT:Roles` | It decides what a process runs, and two processes on one database can differ. |
| `DDT:RequireHttps` | It names the cookies and refuses to start without HTTPS. |
| `DDT:Https:GenerateSelfSignedCertificate`, `DDT:Https:SubjectAlternativeNames` | They let DDT make its root and issue, renew and reissue the server certificate from it, so a fresh install has a certificate to serve the page with. |
| `Kestrel:Endpoints:*`, `Kestrel:Certificates:Default:Path`, `KeyPath`, `Password` | The listener and the certificate that serve the page. |
| `DDT:Pxe:HttpBootPort` | A Kestrel endpoint: a port in use stops the whole host. |
| `DDT:Pxe:BootDirectory` | Everything below it is served to anyone, so one edit on a page could publish the store. |
| `ASPNETCORE_URLS`, `Urls`, `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_HTTPS_PORTS` | Listener settings of the framework. |
| `AllowedHosts` | A wrong value locks every browser out, the page included. |
| `OTEL_*` | The exporters are built once at startup, and the headers may carry credentials. |
| `ASPNETCORE_ENVIRONMENT` | Development switches such as HSTS. Production leaves it unset. |

A standard container install then sets only the connection string, `DDT__Roles` and the names for
the certificate. The Server page shows these read-only on its overview: the values of
`DDT:Roles`, `DDT:StorePath`, `DDT:RequireHttps`, the Kestrel endpoint URLs and certificate paths,
`DDT:Pxe:HttpBootPort` and `BootDirectory`, `AllowedHosts`, the environment, the OTLP endpoint without
user info, and the provider, host and database of the connection string. Every other key shows only
whether it is set and where; one whose last segment is `Password`, `Secret`, `Key` or `Headers`, and
every connection string, never shows a value. `DDT:Agent:BinaryPath` stays a configuration override
for development, see [Updating the agent without a new boot image](#updating-the-agent-without-a-new-boot-image).

### Rules for new settings

A new setting follows these rules:

1. It is a field of a settings section, on the page of what it configures. Only a setting that
   passes the test above stays in configuration, and joins the table.
2. Task sequences, drivers per model and assignment by MAC address or model are database entities
   with an API and a page, never configuration.
3. It is a property of its section's option class with its default, a field of the section's
   definition in `DDT.Server/Settings`, a member of the section's record in `DDT.Contracts/Settings`,
   and covered by the section's pure validator, which returns `SettingProblem` values. It is read
   from `DdtSettings.Current` where it is used, once per request or decision, never while the
   services are registered and never through an `IOptions<T>`. A secret is `[JsonIgnore]` in the
   option class and a secret field of the definition.
4. Values the agent needs travel in server responses, as new members. They never go into
   `agent.json`, which the server cannot rewrite.
5. Anything built into the boot image says why it cannot come from the server.
6. A secret whose destination is configurable is bound to that destination. A setting that grants
   roles or trust needs the administrator to prove who they are again before it changes.

## Authentication

DDT authenticates people with ASP.NET Core Identity. Nothing about the credential handling is
hand written: password hashing, lockout, security stamps, TOTP and recovery codes are Microsoft's
implementations.

Three sources of accounts, all optional except the first:

- **Local accounts.** Passwords are PBKDF2-HMAC-SHA512 at 210,000 iterations, above the framework
  default of 100,000. Raising it later is safe, because Identity rehashes on the next sign in.
- **LDAP.** Turn it on on the Sign-in page, or set `DDT:Ldap:Enabled`. Accounts are keyed on the directory's immutable identifier
  (`objectGUID` on Active Directory, `entryUUID` on OpenLDAP), never on the user name or the
  distinguished name, because both change when someone is renamed or moved. Group membership maps
  onto DDT roles through `DDT:Ldap:GroupRoleMap`, from the group's distinguished name, compared
  without regard to case, to a role; nested groups count. While the map has entries the directory
  stays authoritative: each sign-in gives the account the highest role its groups map to and no
  other, a role removed there is removed here, and a user in none of the mapped groups is refused,
  loses the role it had, and is told why by the sign-in page. With the map empty the directory only
  checks the password, and administrators give directory accounts their roles on the Users page. A
  role in the map that DDT does not have, or a map with `DDT:Ldap:ResolveNestedGroups` off, which
  reads no groups, is refused: configured, it stops the server at startup, and on the Sign-in page
  the save. The page's Test tries the values before they are saved, with a user's password if one
  is given, and shows the groups and the role a sign-in would get; a directory administrator saves
  new connection values or a new map only after such a test kept them an administrator. The Users
  page shows the map with the names the
  directory has for its groups, finds groups by name for it, and checks what a sign-in would give a
  user and why, all with the bind account and without the user's password; these need
  `DDT:Ldap:Host` and `DDT:Ldap:BaseDn`. Groups are Active Directory's `objectClass=group`, as nested
  groups are read with Active Directory's matching rule.
- **OpenID Connect.** Turn it on on the Sign-in page, or set `DDT:Oidc:Enabled`, to point DDT at
  Entra ID, Keycloak, Authentik or any other provider. DDT never links an external identity to an
  existing local account by email address, because a provider that does not verify addresses could
  then take over any account.
  Link from an authenticated session, or turn on `DDT:Oidc:AutoProvision` to create new accounts
  keyed on issuer and subject. They get the role in `DDT:Oidc:AutoProvisionRole`, `Viewer` by
  default. A role that does not exist is refused, and so is `Administrator`, which would make every
  identity the provider signs in an administrator: configured, it stops the server at startup. `Operator` is allowed,
  but think before choosing it: every operator can read the deployment passwords by running a
  sequence, so everyone the provider lets sign in could. When linking the identity or granting the
  role fails, the new account is deleted again and the sign in fails. A linked account with an
  authenticator still enters its code after the provider's sign in, and a disabled or locked out
  account is refused as with a password. The sign-in page learns the providers to offer, with
  `DDT:Oidc:DisplayName`, from the anonymous `GET /api/auth/external/providers`. Turning single
  sign-on on or off on the Sign-in page adds or removes it at once, with no restart; values the
  provider's handler does not accept leave it off on that host, which the page shows, while local
  sign-in keeps working. The page's Test reads the provider's discovery document and shows the
  redirect URI to register there, `/api/auth/external/callback` on DDT's own address.

  Group claims map onto roles the same way as directory groups. `DDT:Oidc:GroupsClaim`, `groups` by
  default, names the claim that carries them, one claim per group or one holding a JSON array, in
  the ID token or in the userinfo response. `DDT:Oidc:GroupRoleMap` maps its values, compared
  without regard to case, to roles: group names or paths as Keycloak and Authentik send them, or
  the object ids Entra ID sends. While the map has entries, each sign-in of an account single
  sign-on made gives it the highest role its groups map to and no other, before
  `DDT:Oidc:AutoProvisionRole`, and an identity in none of them is refused, with
  `/sign-in?error=no-role`, and loses the role it had; no account is made for one that never had a
  role. A local account linked to an identity keeps the role an administrator gave it. Entra ID
  leaves the groups out of the token when a user is in more than 200 of them; assign the groups
  that matter to the application and let it send only those. Both are fields of the `oidc` section
  on the Sign-in page, and a role in the map that DDT does not have is refused.

Two factor authentication is TOTP with recovery codes. Passkeys are not enabled, but the schema
carries the passkey table from the first migration so turning them on later needs no migration.

Roles are Administrator, Operator and Viewer. Endpoints deny by default: a new endpoint is closed
until it explicitly opts out.

### Users and roles

Administrators manage the accounts on Administration > Users and roles, through `/api/users`. The
page lists every account with its source (local, directory or single sign-on), its role and where
the role comes from: an administrator set it, the account's directory or single sign-on groups
decide it at each sign-in, or DDT gave it when single sign-on created the account and no
administrator has changed it since. An account shows the highest role it holds, and a role set on
the page is then the only one it has. A role that groups decide is not changed on the page: the
request is refused with the place to change it instead, the groups or the group map.

A new account is a local one. DDT makes up its password and shows it once; the account has to
change it at its first sign-in, and until then it reaches nothing but its Account page and
authorizes no machine, so only its owner knows the password it then uses. A password reset works
the same way, and also ends a lockout. A reset of the second factor turns it off with a new key, for
an account that lost its authenticator.

Disabling an account changes its security stamp, which ends its sessions at their next check,
within a minute, and closes its live connections at once. Taking a role away also closes them, and
the page connects again with what the account holds now, because the live connection reads the
account when it connects rather than trusting the cookie. Deleting a directory or single sign-on
account only lasts until its next sign-in; disabling it is what keeps it out.

DDT never leaves itself without an enabled administrator: disabling, deleting or demoting the last
one is refused, and so is an administrator disabling, deleting or demoting their own account, or
resetting their own password or second factor there, which the Account page does with the current
password or code. Every change is written to the audit table (`user.created`, `user.changed` with
each field and the role before and after, `user.disabled`, `user.enabled`, `user.deleted`,
`user.password-reset` and `user.two-factor-reset`), and reaches the pages of the other
administrators as it happens, as does every sign-in.

Single sign-on accounts were stored as directory accounts before M6.5. DDT tells them apart at
start, since a directory account always carries the directory's identifier, so that a password
typed for one no longer goes to the directory.

### API tokens

A script or another system calls the API with an API token, which a user makes for themselves with
`POST /api/tokens`: a name, a role no higher than their own, and a lifetime of 1 to 365 days, 90 by
default. The answer carries the secret, `ddt_` and 43 letters and digits, once; DDT keeps only its
SHA-256. The prefix lets secret scanners recognise a token that leaked into a repository or a log.
The token goes in every request as `Authorization: Bearer ddt_...`, and such a request needs no
antiforgery token: it is authenticated by that header alone, never by a session cookie that rides
along, and a page on another site cannot make a browser send the header.

A token acts with the lower of its own role and its user's current highest role, so demoting a user
demotes their tokens, and it stops working the moment it is revoked or expires, or its user is
disabled or locked out. It cannot change its account: the password, two factor authentication,
linking an external sign in and making tokens answer it with 403. `GET /api/tokens` lists a user's
tokens with when and from where each was last used, recorded at most once a minute, and
`GET /api/tokens/all` every user's, for administrators. `DELETE /api/tokens/{id}` revokes one, by its
owner or an administrator, and keeps its row. What a token does is audited under its user's name and
the token's, as `alice (token build-server)`.

### The first administrator

A fresh deployment creates an `admin` account and prints its password once, at warning level:

```
Created the first administrator. User name admin, password <generated>. This is printed once: sign in and change it.
```

No bootstrap credential is read from configuration, because an environment variable holding an
administrator password tends to stay set long after it was needed.

The Account page is where that password is changed, and where an account adds an authenticator and
keeps its recovery codes. A directory account changes its password in the directory instead.

### TLS is required

`DDT:RequireHttps` defaults to true and the host refuses to start without an HTTPS endpoint. This
is deliberate rather than cautious: `Secure` cookies are silently dropped over plain HTTP, so an
auth stack on an HTTP listener appears to work while every request after sign in is anonymous.

When `Kestrel:Certificates:Default:Path` and `KeyPath` are set, as the container image does, DDT
looks after the certificate in those two files itself, as
[The server certificate](#the-server-certificate) describes. Every name and address DDT is reached
by has to be in the certificate, because the agent validates the hostname against the chain it pins.
List them in `DDT:Https:SubjectAlternativeNames`.

Set `DDT:RequireHttps` to false only when a reverse proxy terminates TLS in front of DDT, and then
tell DDT which addresses the proxy connects from. Both settings are comma separated:

```bash
DDT__ForwardedHeaders__KnownProxies=10.10.0.5,10.10.0.6
DDT__ForwardedHeaders__KnownNetworks=10.10.8.0/24
```

Until one of them is set, DDT ignores `X-Forwarded-For` and `X-Forwarded-Proto`, so every request
appears to come from the proxy over plain HTTP: all clients share one sign in limit, all machines
share the registration limit and the cap on waiting machines per address, zero touch sees only the
proxy's address, the audit table and the Machines page show it too, and cookies are not marked
`Secure`. Once set, the headers are read only on connections from a listed address, and only the
last entry of `X-Forwarded-For`, the one the proxy added, counts. A client cannot choose its own
address by sending the header through the proxy.

The proxy has to append the client's address to `X-Forwarded-For`, set `X-Forwarded-Proto`, and pass
`Host` through unchanged, because `X-Forwarded-Host` is not read. List the proxies and nothing else:
DDT believes whatever client address a listed address reports, so a listed network that also holds
clients lets them pick their own address by connecting to DDT directly. Loopback is not trusted
unless it is listed, so a proxy on the same host is listed as `127.0.0.1` or `::1`. A Unix socket or
named pipe endpoint has no address to list, so a proxy connects over TCP.
`ASPNETCORE_FORWARDEDHEADERS_ENABLED` is ignored, because it trusts every address.

With the `pxe` role active, DDT adds a plain HTTP Kestrel endpoint named `Boot` for boot files.
Kestrel then ignores `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS` and launch profile URLs, so the
application endpoint has to be declared under `Kestrel:Endpoints` as well: `Https` as the
container image does, or a plain HTTP one behind a reverse proxy. The host refuses to start
without it, whatever `DDT:RequireHttps` says.

### The server certificate

DDT runs its own small certificate authority, so that a new server certificate needs neither a new
boot image nor a change in any browser. Its files sit next to `Kestrel:Certificates:Default:Path`,
`/var/lib/ddt/certs` in the container:

| File | Contents |
|---|---|
| `ddt-root.pem` | DDT's root certificate, valid for 20 years. Boot images pin it, browsers trust it |
| `ddt-root-key.pem` | The root's private key. As with `ddt-key.pem`, only the account DDT runs as can read it, and on Windows also SYSTEM and administrators |
| `ddt.pem`, `ddt-key.pem` | The server certificate and its key, as the two configured paths name them |
| `ddt.previous.pem`, `ddt-key.previous.pem` | The pair the last renewal or the Server page replaced, to go back to |
| `ddt.provisional` | Only while a pair from the Server page waits for its confirmation: when DDT goes back |
| `ddt-anchor.replaced.pem` | Only after the upgrade described below: the old self-signed certificate |

The server certificate is valid for 90 days. At startup and every five minutes DDT checks it, and
issues a new one from the root when 30 days or less are left, or when `localhost`, the computer's
name or a name from `DDT:Https:SubjectAlternativeNames` is missing from it. A new certificate keeps
every name the old one had and names the computer's current addresses; a changed address alone is
never a reason to issue one. Kestrel serves the new certificate from the next connection on, with no
restart, and open connections carry on. The configured names are read at startup, so a new name
takes a restart. To drop a name, delete `ddt.pem` and `ddt-key.pem`: DDT issues a new certificate
from the same root and boot images keep working. Deleting the root as well makes a new root, and
every boot image and every browser that trusted the old one then has to be updated.

Trust `ddt-root.pem` in the browsers of the computers that manage DDT, and nowhere else (see the
security model). Anyone can download it without signing in from `/api/about/root-certificate`.
Before trusting a downloaded copy, compare its SHA-256 with the one DDT logged when it made the root,
or with that of `ddt-root.pem` in the store. `rootSha256` from `GET /api/server/certificate` is no
such check: it comes over the very connection whose certificate is in question, so it only helps
over a connection that is trusted already.

**A certificate of your own.** Put it and its key at the two configured paths. DDT serves them as
they are, loads them again within five minutes when they change, never renews or replaces them, and
logs a warning once when 30 days or less are left. Changed files that do not load, such as a new
certificate next to the old key, leave the certificate loaded before in service. Build boot images
with the root of your CA, and put its intermediate certificates after yours in the certificate file:
DDT sends them along, because the agent downloads none. `DDT:Https:GenerateSelfSignedCertificate`,
true by default, lets DDT make its root and issue from it; set to false, DDT never writes to the
folder, not even to renew its own certificate. Either way DDT writes there only to look after a
certificate of its own, so yours may sit on a read-only mount. A PFX, a key under
`Kestrel:Certificates:Default:Password`, and any setup without both paths are left to Kestrel
entirely, as before.

DDT does not start when its root cannot be used with its key, or when a certificate it cannot issue
again, such as one of your own, does not load at startup while no previous pair does either; the
message says what to do. A pair that does not load, such as a certificate next to a key that is not
its own, gives way to the previous pair at startup. DDT never makes a new root over an existing one
by itself, because that would break every boot image.

**On the Server page.** With both paths set and no password, the page shows the certificate and
changes it, with the administrator's password again:

- **Server names**, which `DDT:Https:SubjectAlternativeNames` seeds once, are the names the server is
  reached by. Generate issues for them, with `localhost` and the computer's name, and an upload has
  to name them. The configuration key keeps its own meaning above and never locks them.
- **Generate** issues a certificate from DDT's root. Without a root yet it makes one, which every
  boot image and every browser then has to learn, so it asks for that first.
- **Upload** takes a PEM certificate, its intermediates after it, and its key, or a PFX with its
  password. It has to load with its key, be valid now and name the address the page was loaded from
  and every server name. One that does not come from DDT's root asks first, because boot images pin
  that root and have to be built again with the new one.

A new pair is served at once, but provisionally: the page confirms it from a connection that was
served it, which proves that the browser accepts it, and unless that happens within 5 minutes, DDT
goes back to the pair before it, which an unreachable page after HSTS would otherwise make
impossible to fix. While a pair waits, DDT closes a connection served the old one after its answer,
so the page's next request gets the new pair. Every change is audited: `certificate.replaced`,
`certificate.confirmed` and `certificate.rolled-back`.

**Limits while DDT serves the certificate.** With both paths set and no password, DDT hands every
HTTPS endpoint the certificate it holds, its own or yours, and a certificate configured for a single
endpoint, under `Kestrel:Endpoints:<name>:Certificate`, is ignored. HTTP/3 cannot be turned on:
with `Http3` among an endpoint's `Protocols`, Kestrel refuses to start. And Kestrel does not reload
its endpoint configuration when a configuration file changes; restart DDT for that.

### Upgrading from the self-signed certificate

Versions before the root generated a self-signed certificate valid for two years and never renewed
it, and boot images pin that certificate itself. On its first start after the upgrade, DDT makes the
root, issues a new certificate with the old one's names from it, keeps the old pair as the previous
pair and a copy of the old certificate as `ddt-anchor.replaced.pem`, and logs a warning with the
root's path and SHA-256. The old certificate is no certificate authority, so nothing new can chain
to it, and this break cannot be avoided:

1. Every boot image built before cannot reach DDT any more. Build each one once again with
   `Build-BootImage.ps1 -RootCertificatePath` set to `ddt-root.pem`. After that, renewals and new
   names need no rebuild. An agent from this version says so on the console when the server's
   certificate does not come from the root it pins; the agent in an older boot image only reports
   that the TLS connection failed.
2. A browser that trusted the old certificate refuses the new one with no way to click through,
   because DDT sends HSTS. Trust the root in it first. Take `ddt-root.pem` from the store, or fetch
   it with a client that has not been to DDT before, such as
   `curl -k https://ddt.example:8443/api/about/root-certificate`, and compare its SHA-256 with the
   one in the warning.
3. Until an administrator confirms that every boot image was built again, the web UI shows
   administrators a banner with the root's path and SHA-256, and `GET /api/server/certificate`
   reports when the old certificate was replaced, in `anchorReplacedUtc`. The banner's Done button,
   or `DELETE /api/server/certificate/replaced-anchor`, confirms it, deletes the copy and is written
   to the audit table.

## Netboot

### Where DDT runs

Sites are joined by a site-to-site WireGuard VPN that terminates on each site's gateway and adds a
few milliseconds. One central DDT serves every site over that tunnel; that is the design, not a
compromise. A `pxe` instance local to one site is an option for a site with high latency, not the
norm.

Because the tunnel ends on the gateway, remote traffic reaches DDT on its ordinary LAN interface.

### What travels how

| Transport | Carries |
|---|---|
| TFTP | Only the netboot chain: `bootmgfw.efi`, `BCD`, `boot.sdi` and `boot.wim`, about 340 MB in all, about 480 MB with PowerShell in Windows PE |
| HTTPS | Everything the agent does: registration, its own updates, task sequences, images, packages, logs |
| Plain HTTP, port 8080 | The same boot files, for UEFI HTTP Boot clients, which cannot validate a private CA |

The boot manager downloads `boot.wim` over TFTP itself, so TFTP speed decides how long a netboot
takes. At the default window of 16 and a round trip time of 5 to 10 ms, expect roughly 1.5 to 3
minutes for the image without PowerShell, and about 40 percent longer with it; on a local switch it
takes seconds. The plain HTTP listener serves the
same files to firmware that offers UEFI HTTP Boot, which fetches the boot manager over HTTP. Whether
the Windows boot manager started that way then reads `BCD`, `boot.sdi` and `boot.wim` over HTTP
rather than TFTP has not been verified: no firmware with an HTTP Boot device has booted from DDT
yet, and the `BCD` that `Build-BootImage.ps1` writes carries only TFTP settings for `boot.wim`.

### Which interfaces are served

`DDT:Pxe:Interfaces` is a list of interface names or local IPv4 addresses, and it has no default.
Until it names an interface, DDT logs every candidate interface with its addresses and serves
nothing, so a host with a NIC on someone else's network never answers PXE there. The Network boot page
lists the candidates each pxe host found, from `GET /api/settings/pxe/interfaces`, and warns about an entry
that names nothing on a host.

A save of the pxe section stops the listeners and starts them again with the new values, which ends
any TFTP transfer in progress; the machine then starts it again. Interfaces and their addresses are
read again at every apply, so after an address changed, the page's Rescan, which is
`POST /api/settings/pxe/rescan`, makes every pxe host pick it up. When the new listeners do not
bind, because another service holds a port, the previous ones run again and the page shows the host
as failed, with the reason. Only when configuration names the interfaces does such a failure at
startup still stop the server, as it always did.

ProxyDHCP and TFTP ignore any datagram that did not arrive on a served interface. HTTP boot can only
check the address a connection was made to, and a Linux host accepts a connection to any of its
addresses on any interface, so a client elsewhere that deliberately routes to a served address still
reaches the boot files. They are public by design, as the security model below explains.

Name the LAN interface remote sites arrive on, never a tunnel interface.

### Reaching DDT from a remote site

There are two ways, and the first is the one to use. Either way, TFTP requests go to one of the
served interface's addresses.

**The site's DHCP server names DDT.** Set next-server (option 66) to DDT's address and the boot file
(option 67) to one of the boot manager paths below. No ProxyDHCP is involved: the client talks TFTP
to DDT straight away. This is the primary path because a UniFi network runs either a DHCP server or
a DHCP relay, never both. If every site works this way, set `DDT:Pxe:EnableProxyDhcp` to false and
DDT binds nothing on UDP 67 or 4011.

**The site relays DHCP to DDT.** The relay forwards the client's Discover with its own address in
giaddr, DDT answers as ProxyDHCP back to the relay on port 67, and the relay delivers it. List every
relay in `DDT:Pxe:AuthorisedRelayAgents`. DDT ignores relayed requests from anywhere else, because
answering one would serve a subnet it was never told about.

ProxyDHCP needs a boot target per client architecture, keyed by architecture name. Nothing is
configured by default, because a site that names DDT in options 66 and 67 needs none:

```bash
DDT__Pxe__BootTargets__X64Uefi__Method=Tftp
DDT__Pxe__BootTargets__X64Uefi__BootFile=x64/bootmgfw.efi
```

### Boot files and Secure Boot

`DDT:Pxe:BootDirectory` is the folder `boot` in `DDT:StorePath` unless set, and a relative path
is inside the store as well. Everything below it is served to anyone who asks, so DDT refuses to
start with a filesystem root, the store or a folder above it, anything in the key ring folder
`keys` of the store, or the folder of any certificate or key file configured under `Kestrel`, such
as a PFX in `Kestrel:Certificates:Default:Path`, or a folder above it.

`build/Build-BootImage.ps1` writes this layout, which is what `DDT:Pxe:BootDirectory` should hold:

| Path | Contents |
|---|---|
| `x64/bootmgfw.efi` | Boot manager signed by Microsoft Windows Production PCA 2011 |
| `x64/bootmgfw_ex.efi` | Boot manager signed by Windows UEFI CA 2023 |
| `Boot/BCD` | Boot configuration: `boot.wim` from a RAM disk over TFTP |
| `Boot/boot.sdi` | RAM disk description |
| `Boot/boot.wim` | Windows PE with `DDT.Agent`, and PowerShell unless left out |
| `Boot/ddt-boot-image.json` | What the build holds, for the boot image page, see below |
| `EFI/Microsoft/Boot/boot.stl` | Secure Boot revocation list the boot manager checks |
| `EFI/Microsoft/Boot/Fonts/` | Fonts the boot manager draws its screens with |

The script adds the Windows PE optional components PowerShell needs, WinPE-WMI, WinPE-NetFx,
WinPE-Scripting, WinPE-PowerShell, WinPE-DismCmdlets, WinPE-StorageWMI and WinPE-SecureBootCmdlets,
with their en-us language packages, so task sequence steps can run PowerShell scripts in Windows PE.
Components cannot be added to a running Windows PE, so they have to be in `boot.wim`. On the test
machine they took it from 347,838,990 to 496,342,317 bytes. The script prints the size at the end,
in megabytes of 1,048,576 bytes, which makes 331.7 MB and 473.3 MB, about 142 MB more. How much
longer the larger image takes to netboot has not been measured yet. `-SkipPowerShell` builds the
lean image for sites where netboot time matters more. On it the agent refuses a run with a
PowerShell script in Windows PE before it touches the disk, and says to build the image without
`-SkipPowerShell`. Either way the script exports `boot.wim` at the end, which drops what servicing
left behind in the file. A build needs an elevated prompt, the Windows ADK and its Windows PE
add-on.

Both boot manager paths are stable, because a site DHCP server picks one by name. Neither file is
dual signed. The 2011 one is the default: firmware ignores certificate expiry, and most machines
today, this project's Hyper-V test host included, have not taken the 2023 db update. A machine that
has taken it and revoked the 2011 certificate needs `x64/bootmgfw_ex.efi`.

Names are matched without regard to case, and a leading separator means the boot directory, because
the boot manager asks for `\Boot\BCD` whatever the host's filesystem calls it. Every TFTP read is
logged with the name the client asked for.

A normal boot also logs a dozen refused reads, and none of them is a fault. The boot manager tries
`\BCD` before `\Boot\BCD`, and it and the Windows loader look for optional Secure Boot policy files
such as `\EFI\Microsoft\Boot\SiPolicy.p7b` and `UnlockToken.pol`, then carry on without them.

#### Drivers in the boot image

Windows PE carries drivers for common network and storage controllers only. A machine whose
controller it does not know cannot reach DDT, or cannot see its disk, before any sequence runs, so
the driver has to be in `boot.wim`. There are two ways to put it there, and a build can use both:

- **From DDT.** On the Packages page an administrator flags a driver package for the boot image
  (`bootImage` in `PUT /api/packages/{id}`; only a driver package can have it). Build with
  `-ServerUrl`, `-RootCertificatePath` and `-ApiToken`, an administrator's API token, see
  [API tokens](#api-tokens). The script asks `GET /api/boot-image` for the flagged packages,
  downloads each from `GET /api/boot-image/drivers/{packageId}/content` over a connection that
  trusts the pinned root and no other, checks its SHA-256 and unpacks it. The token only authorizes
  the download; it goes into neither the image nor the file below. Pass it from an environment
  variable rather than typing it, so it stays out of the shell's history.
- **From a folder.** `-DriverPath` names a folder of drivers on the build computer.

DISM adds every `.inf` below each folder with `/Add-Driver /Recurse`, and refuses a driver that is
not signed, which Windows PE could not load with Secure Boot on anyway.

Every build writes `Boot/ddt-boot-image.json` next to `boot.wim`: when it was built, the DDT
packages it holds with their SHA-256, the ADK version, the version of the boot managers and that of
the agent. DDT reads it on each request, and `GET /api/boot-image` compares the flagged packages
with it: `stale` says the boot image must be built again to carry them, or no longer carries one
that was taken out. A missing or unreadable file counts as a build without DDT's drivers, which is
what a boot image built before this is. The file holds no secret; like everything in the boot
directory, anyone who can netboot can read it. DDT looks at it every 10 seconds and pushes a new
build to open pages, so copying a build into the boot directory is enough.

### TFTP tuning

- **Block size** is capped at 1380. The block is payload only: a data packet adds 32 octets of TFTP,
  UDP and IP headers, so 1400 would make 1432 and fragment inside WireGuard's 1420 MTU. 1380 also
  fits a tunnel over PPPoE.
- **Window size** is what the boot manager asks for in the BCD, `Build-BootImage.ps1
  -TftpWindowSize`, capped by `DDT:Pxe:TftpMaxWindowSize`. Both default to 16. Only 4 has
  Microsoft backing for the boot manager, but on the test machine 16 was reliable and loaded
  `boot.wim` about 40 percent faster than 4 (see [Status](#status)). A site whose link loses packets
  under a large window lowers `DDT:Pxe:TftpMaxWindowSize`, to 8 or 4, without building its boot
  images again. Boot images built before this default ask for 4, and keep getting 4 until they are
  built again.
- **Single port mode**, `DDT:Pxe:TftpSinglePort`, is off by default: each transfer answers from a
  fresh port, which lets the kernel drop strays and keeps a duplicated request from producing two
  interleaved streams. Turn it on when the log shows a read request arriving at DDT but the client
  never receives data. A stateful firewall on the path is dropping replies from a port it never saw.
  In this mode replies leave from whichever address the route back to the client uses, so the route
  to a site must leave through the served interface.

### Diagnosing a machine that does not boot

- A successful bind proves nothing on Windows: another process holding a specific address on the
  same port silently takes unicast traffic. Each UDP listener logs the first datagram it accepts from
  a served interface, and that line, not the bind, is the evidence DDT is reachable.
- Set the level of `DDT.Pxe` to `Debug` on the Logging tab of the Server page, which applies at
  once, to see every DHCP datagram DDT declined to answer and why, or to `Trace` to also see
  datagrams that arrived on interfaces it does not serve.
- `build/New-TestVm.ps1` creates a Generation 2 Hyper-V machine with Secure Boot on, on the Default
  Switch, whose own DHCP server makes it the same shape as a real site. It gives the machine a 64 GB
  disk, a virtual TPM and 4 GB of memory, enough to deploy Windows 11, and keeps the network first in
  its boot order. `-Remove` leaves the disk in place and says where. It prints a `pktmon` recipe for
  capturing the boot.
- Hyper-V has no HTTP boot device, so the HTTP boot listener is covered by tests that replay the
  firmware's request sequence rather than by the test machine.

## The agent

`DDT.Agent` is a single NativeAOT executable, because Windows PE has no .NET runtime. It reads the
machine's SMBIOS UUID, manufacturer, model and serial number straight from the firmware table, so it
does not depend on WMI, which an image built with `-SkipPowerShell` lacks, and it reports every MAC
address it finds. The same executable goes on with a run in the installed Windows as the temporary
`DdtSequence` service, see [In the installed Windows](#in-the-installed-windows).

```bash
.\build\Publish-Agent.ps1
```

Publishing needs the Visual C++ build tools. The result is `artifacts\agent\ddt-agent.exe`, about
11 MB. It carries wimlib's `libwim-15.dll` inside itself and writes it next to itself before it
applies an image, so the update below also updates wimlib, unless you supply your own as described
under [Using your own libwim](#using-your-own-libwim). See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

At start-up the agent prints its legal notices: DDT's attribution notice, that it comes with
absolutely no warranty, that it is free software under the GNU GPL version 3 or later with
additional terms, and that it uses wimlib, which is under the GNU LGPL version 3 or later.
`ddt-agent --licenses` prints the licence texts the agent carries and exits. A console shows them in
its code page, which usually lacks a few of their characters, such as the copyright sign.
`ddt-agent --licenses > licences.txt` writes them in UTF-8, exactly as the agent carries them.
In Windows PE the agent is `X:\DDT\ddt-agent.exe`, and the boot image puts `X:\DDT` on the path,
so at the command prompt that stays open when the agent stops, `ddt-agent --licenses | more` pages
through the texts.

### Updating the agent without a new boot image

The boot image carries an agent, but that agent first asks the server for the current one. When the
server offers a different file, the agent downloads it over the same verified connection, checks its
SHA-256 against what the server announced and runs it in its place. Put the published agent at
`DDT:Agent:BinaryPath`, by default `agent/ddt-agent.exe` in the store, and every machine runs it from
its next boot. A download that fails, including one that has not finished after 5 minutes, or an
agent that cannot start, leaves the machine on the agent from the boot image. The check happens
once, before the machine registers, and only in an agent built with `dotnet publish`, never in a
dry run; `--no-update` turns it off. What the agent from the boot image printed before it switched
stays on the console and does not reach the machine's log.

The Server page uploads the agent, as `PUT /api/settings/agent/binary` with the executable as the
body, to that default path: written next to the agent it replaces and renamed over it, so no machine
downloads half a file, and audited as `agent.uploaded` with its SHA-256. It needs a fresh password,
like the fields that grant trust, because it turns an administrator's session into code that runs as
SYSTEM on every netbooting machine. It takes only a Windows executable of at most 128 MB, and is
refused while `DDT:Agent:BinaryPath` names the file in configuration, which stays for development.
`GET /api/settings/agent` says which agent machines get, and who uploaded it when.

So a boot image only has to be built again for Windows PE itself, including its PowerShell
components, drivers, the keyboard layout, the TFTP block and window size its BCD asks for, the
server's URL or a new root. A renewed server
certificate, or one with more names, needs no rebuild, because the boot image pins DDT's root. The
upgrade to the root needs one rebuild, as
[Upgrading from the self-signed certificate](#upgrading-from-the-self-signed-certificate) describes.

### Using your own libwim

The agent can run with a libwim you built yourself, as wimlib's licence, the GNU LGPL, provides
for. It uses a `libwim-15.dll` that is already next to it instead of overwriting it. When there is
none, it writes its own copy. One identical to its own copy it simply uses. A different one it keeps
and uses, and logs a line with both SHA-256 values; delete the file to make the agent use its own
copy again. Put your DLL next to `ddt-agent.exe`, or build the boot image with
`Build-BootImage.ps1 -WimLibraryPath`, which copies it to `X:\DDT\libwim-15.dll`. An agent that
updated itself runs from the same folder, so it uses the same DLL. Alternatively, point the
`EmbeddedResource` in `src/DDT.Agent/DDT.Agent.csproj` at your DLL and publish the agent again. It
then carries your DLL and writes it out wherever no `libwim-15.dll` is next to it yet.
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) names the source and the build script the
embedded DLL comes from.

### Registration and authorization

1. `Build-BootImage.ps1` with `-AgentPath`, `-ServerUrl` and `-RootCertificatePath` puts the agent
   and an `agent.json` holding the last two values into `boot.wim`, together with the name of the
   keyboard layout. The image is set to the layout given with `-KeyboardLayout`, by default the build
   computer's own. `startnet.cmd` starts the agent.
2. The agent registers at `POST /api/agents/register`, with no credential. A machine DDT has not seen
   before appears on the Machines page as `Pending`, live. The agent then polls
   `GET /api/agents/{id}/next` every ten seconds, and every answer carries fresh tokens.
3. The technician at the machine signs in with their DDT account, local or directory, and for a
   local account with the authenticator code if it has one. An operator or administrator account
   authorizes the machine there and then. An operator or administrator can also approve it on the
   Machines page instead, which is how an account that only signs in through OpenID Connect
   authorizes a machine: it has no password to type.
4. On its next poll the agent receives a session token, the credential it needs to run a sequence.
   Only then does it send what it has printed, including the lines from before, to
   `POST /api/agents/{id}/log`. A pending machine cannot write to the log, because anyone
   can get its kind of token by registering.

After a wrong password the agent asks for the password again and keeps the user name; an empty
password goes back to the user name. The prompt names the keyboard layout, because a password typed
with the wrong one looks exactly like a wrong password, and five of those lock the account for 15
minutes, on the web as well.

Where one person at a machine is not enough, set `DDT:Machines:RequireWebApproval` to `true`. Signing
in at the machine then only records who is there, the Machines page shows that name, and an operator
or administrator also has to approve the machine on the page. Approving is refused until someone has
signed in at the machine, so in this mode a machine cannot be authorized from the page alone, and
accounts that only use OpenID Connect cannot authorize machines.

A registration is the same machine when the SMBIOS UUID matches exactly and at least one of its MAC
addresses is still present. Cloned virtual machines and boards that share a UUID stay apart, and a
machine whose firmware reports no usable UUID, all zeros or all ones, is only ever matched with
another reporting the same value.

Registering a known machine again starts it over at `Pending` and invalidates every token it held,
because anyone who reaches the server can present its UUID and MAC. The exception is the agent
already holding the machine: every answer includes a resume token, valid for 24 hours, and an agent
that has to register again after an outage presents it and keeps its approval. A machine that
restarts during a run, once its disk is partitioned, goes on with the run through the run token it
keeps on that disk, see [In Windows PE](#in-windows-pe). Any other machine that rebooted has lost
its resume token and starts over, unless zero touch applies (see
[Deploying a machine](#deploying-a-machine)). A rejected machine stays rejected, and its agent
stops.
To take a rejection back, an operator removes the machine on the Machines page, and it registers as
a new machine at its next netboot.

Registration is limited to 120 requests a minute per address, polling, logging and run reports to
60 a minute per machine, and requests for images and packages to 30 a minute per machine. The
machine limits count the machine whose token a request carries, so one machine cannot use up
another's. The server keeps the newest 50,000 log lines of each machine. At most 100 machines
nobody has approved may wait per address, `DDT:Machines:MaxWaitingPerAddress`, and 10,000 in all,
`DDT:Machines:MaxWaiting`; a new machine beyond that is refused until some are approved or removed.
An operator can remove a waiting machine nobody ever approved, or every such machine from one
address, on the Machines page. Such machines also disappear once they have not been seen for a day.
A machine that was approved once, or that has a sequence assigned, is never removed this way, so its
log survives it booting again.

The agent trusts only the root certificate in `agent.json`, which for DDT's own certificate is
`ddt-root.pem`. Revocation is not checked and missing intermediates are not downloaded, because a
provisioning network has no route to either, so the server has to send its full certificate chain.
Always pass the root, even for a public CA: Windows PE carries only a handful of Microsoft roots,
and none of the ones public web certificates chain to. When the server's certificate does not come
from the pinned root, or does not name the server, the agent says on the console what to change.

### Reaching a development host from a test machine

`dotnet run` serves `localhost` with the ASP.NET Core development certificate, and a netbooted
machine can use neither. With the `pxe` and `web` roles together, DDT refuses to start on an HTTPS
endpoint that only this computer can reach. For the Hyper-V test machine, start it with:

```powershell
.\build\Start-DevHost.ps1
```

It listens on every interface and has DDT issue a certificate from its root in the store that names
the Default Switch's DNS name, `<computer>.mshome.net`, and prints the `-ServerUrl` and the
`-RootCertificatePath`, the root's path, to build the boot image with. The switch changes its
address when Windows restarts, but the name follows it, so the boot image keeps working. The browser
warns about the certificate until it trusts the root.

### Trying the agent without a spare machine

```powershell
artifacts\agent\ddt-agent.exe --dry-run --server https://localhost:7152
```

`--dry-run` stands in for a fake machine with a stable identity per `--dry-run-id`, so several
runs with different ids look like several machines on the Machines page. It reports one fake disk and
runs the whole sequence, both phases, in this one process, without changing anything on the
computer: partitioning, `bcdboot`, `reg`, `dism`, scripts, the domain join, `shutdown` and `sc` only
log what they would do, and a restart starts the agent over within the process. Its restarts
therefore always happen, so it records none in `X:\DDT\restart-due` and only logs where Windows PE
would record one. It really downloads the images and packages, fetches the answer file and the join
account, whose password it never logs, and stages the agent for the Windows phase, all into
`%TEMP%\ddt-dry-run-{id}`, which it deletes when the run ends. Leave room there for the image. The
Windows phase starts from the `agent.json` the hand-over staged, and takes Windows setup as
finished at once. A dry run stopped with Ctrl+C goes on when it is started again with the same
`--dry-run-id`. Every setting in `agent.json` except the keyboard layout name can also be given as
an argument.

A sequence that writes a raw disk image writes it to `%TEMP%\ddt-dry-run-{id}-disk0.img`, a sparse
file as large as the fake disk, 128 GiB, which takes only what is written to it. The file outlasts
the run, so the disk can be looked at afterwards, and the next run that cleans the disk deletes it.
`--dry-run-secure-boot` makes the fake machine report that Secure Boot is on, to try the
[Secure Boot check](#secure-boot-and-raw-disk-images).

## Images

The Images page lists the library: Windows images from WIM files, and [raw disk
images](#raw-disk-images) such as a distribution's cloud image. An administrator uploads a WIM
there, for example `sources\install.wim` from a Windows ISO, or an unencrypted ESD, or a disk image.
The browser sends the file in 8 MiB chunks. If the page is reloaded or the connection drops,
selecting the same file again continues where the server left off: it recognises the file by name,
size and last change.

After the last chunk the server reads the image list from a WIM and computes its SHA-256. It
refuses a split WIM (`.swm`), a pipable WIM, a WIM whose image list is
compressed, an encrypted ESD from Windows Update, and a WIM that holds no x64 Windows image. Every x64
image in the file becomes its own entry in the library, named, versioned and sized from the file.

The file is stored once, named by its SHA-256, under `images/objects` in `DDT:StorePath`; uploads in
progress sit under `images/uploads` on the same volume. Uploading a file that is already there adds
nothing. A new upload needs free space for its size, plus what other unfinished uploads still have to
send, plus 1 GB. Unfinished uploads that nobody touched for 24 hours are removed. Deleting an entry is
refused while an assigned or running run uses it, and the stored file goes when no image or package
uses it any more. A sequence whose Apply image step names a deleted image shows a problem.

Behind a reverse proxy the chunk size matters. nginx refuses bodies over 1 MB by default, and
Traefik gives a whole request 60 seconds, so an 8 MiB chunk needs at least 140 KB/s. Packages are
uploaded through the same route. Machines download images and packages with range requests, which
the proxy must pass through without buffering the whole file. For nginx, with DDT listening on port
8443 behind it:

```
proxy_set_header Host $http_host;
proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
proxy_set_header X-Forwarded-Proto $scheme;

location /api/images/uploads {
    client_max_body_size 16m;
    proxy_request_buffering off;
    proxy_read_timeout 300s;
    proxy_pass https://127.0.0.1:8443;
}

location ~ ^/api/agents/[^/]+/runs/[^/]+/files/ {
    proxy_buffering off;
    proxy_pass https://127.0.0.1:8443;
}
```

The three headers are the ones [TLS is required](#tls-is-required) asks for. Set them in the `server`
block, so that every location inherits them: nginx drops the inherited ones in a location that sets
any header of its own. DDT reads the forwarded ones only from a listed proxy, and nginx connects from
loopback here, so list it with `DDT__ForwardedHeaders__KnownProxies=127.0.0.1`.

Checking a large file after its last chunk can take longer than a proxy waits. The server carries on
and the page asks again.

### Raw disk images

A raw disk image is a whole disk, partition table and all, which a
[Write raw disk image](#task-sequences) step writes over the machine's disk. Distributions publish
their cloud images this way, such as Ubuntu's `noble-server-cloudimg-amd64.img` or Debian's
`debian-12-genericcloud-amd64.raw`. The server tells the format from the file's first bytes, not
its name:

| Format | How the server reads it |
|---|---|
| raw | as it is |
| gzip, zstd | unpacks it itself |
| xz | with `xz`, when it is on the server's `PATH` |
| qcow2 | with `qemu-img`, when it is on the server's `PATH` or, on Windows, in `C:\Program Files\qemu` |
| VHDX, VMDK, VDI | refuses it, naming the command that converts it |

The container image has `xz` and `qemu-img`. Without them the server refuses those files and says how
to convert them by hand, for example `qemu-img convert -O raw noble-server-cloudimg-amd64.img
disk.raw`. A qcow2 image that depends on a backing file, keeps its data in an external file or is
encrypted is refused before `qemu-img` runs, because `qemu-img` would read that file from the
server's own disk.

The disk must have a GUID partition table made for disks with 512-byte sectors, which is what
distributions publish, and a file that ends before its last partition is refused as incomplete. The
server then reads the EFI system partition, and in it `\EFI\BOOT\BOOTX64.EFI`, the file the firmware
starts from a disk that has no boot entry of its own. Its signature decides the Secure Boot column:

- **Signed**: the file is signed under Microsoft's UEFI CA 2011 or 2023, as a distribution's shim
  is, and the detail names which. The machine starts the image with Secure Boot on, provided its
  firmware trusts that CA: PCs trust the 2011 CA as they come and the 2023 CA once a firmware or
  Windows update added it, and Secured-core PCs turn the CA off until it is turned on in their
  firmware setup. The agent tells which machines do not trust it, see [Secure Boot and raw disk
  images](#secure-boot-and-raw-disk-images). A Hyper-V Generation 2 machine trusts it only with the
  Microsoft UEFI Certificate Authority template, which in turn does not trust the Windows boot
  manager DDT netboots, so there Windows PE runs with Secure Boot off, as `New-TestVm.ps1
  -SecureBootOff -NoTpm` sets up.
- **Not signed**: the file has no signature, or one that does not lead to Microsoft's UEFI CA. The
  machine starts the image only with Secure Boot off, or with your own key enrolled. The page says
  why, for example that a distribution's own shim is elsewhere on the partition.
- **Unknown**: the image has no EFI system partition, no `BOOTX64.EFI`, or a file DDT cannot read.

The Architecture column comes from the same file, and an image whose boot file is for arm64 or x86
machines only cannot be written. The check follows the signature and the certificate chain as the
firmware does, but not the firmware's revocation lists, dbx and SBAT, so a file that was revoked
still shows as signed.

An upload the server cannot convert for a cause of its own, a missing `xz` or `qemu-img`, a tool
that failed or a full store volume, stays on the server. Complete it again once the cause is fixed,
or discard it.

The server stores the disk compressed with zstd, and names it by that file's SHA-256. The Size column
shows the stored file and the Installed column the disk it holds, which the machine's disk must be
able to hold. Converting needs room on the store volume for the whole disk and its compressed copy
beside the upload. The same disk uploaded again, in any of the formats, adds nothing, because the
server also keeps the SHA-256 of the disk itself. An image is named after its file without the
extensions of its formats, such as `noble-server-cloudimg-amd64`.

## Packages

A package is a zip that the steps of a task sequence unpack on the machine. An administrator uploads
it on the Packages page as one of two kinds, in resumable chunks like an image.

- A **driver package** holds the driver folder of one hardware model with its `.inf` files. Its
  targets say which machines get it: a model as the machine's firmware reports it, optionally with
  a manufacturer. A model ending in `*` matches every model that starts with what comes before it,
  such as a Lenovo machine type, and needs at least three characters there. Case and spacing are
  ignored, and the placeholders firmware leaves in unset fields, such as "To Be Filled By O.E.M.",
  are refused and never match. An Inject drivers step adds every driver package whose targets match
  the machine. A driver package can also go into the Windows PE boot image, for a controller Windows
  PE lacks, see [Drivers in the boot image](#drivers-in-the-boot-image).
- A **files package** has no targets. A Run script step that names it has it unpacked into a folder
  of its own, which becomes the script's working directory and is named in `DDT_PACKAGE`.

The agent unpacks a package as SYSTEM, so the server checks every zip before any machine gets it,
and never writes an entry anywhere itself. It refuses encrypted entries, symbolic links, a name that
would leave the folder it is unpacked to or that Windows cannot create (a leading slash or
backslash, `..`, a colon, a device name such as `CON` or `COM1`, a control character, a trailing dot
or space), a name longer than 240 characters or more than 32 folders deep, the same name twice when
case is ignored, a file and a folder of the same name, more than 200,000 entries and more than 64 GB
unpacked. It inflates every entry to prove its size and checksum, and a driver package needs at
least one `.inf`. The agent checks the names again as it unpacks, and never writes more than an
entry declared.

A file uploaded again is not stored twice: the upload names the package it already is. A package
cannot be deleted while an assigned or running run uses it, and a sequence whose Run script step
names a deleted package shows a problem.

## Task sequences

A task sequence is the list of steps a machine runs, in order. Administrators create and edit
sequences on the Sequences page. A deployment runs one sequence on one machine, as
[Deploying a machine](#deploying-a-machine) describes.

| Step | Runs in | What it does |
|---|---|---|
| Partition the disk | Windows PE | Erases the disk and partitions it with one `diskpart` script: EFI system partition, 16 MB MSR, Windows, and a recovery partition at the end. The EFI system partition is 260 to 4096 MB, 300 by default, and the recovery partition 300 to 65536 MB, 1024 by default. |
| Apply image | Windows PE | Downloads the chosen image from the library to the Windows partition, resuming after a dropped connection, checks its size and SHA-256, applies it with wimlib and deletes the download. It gives up when the download has not grown for 15 minutes. |
| Inject drivers | Windows PE | Adds the drivers of every driver package that matches the machine's model to the applied Windows with `dism /Add-Driver /Recurse`. Without such a package the step does nothing, or fails with "Fail when no driver package matches the model" on. |
| Write the answer file | Windows PE | Writes the answer file for Windows setup, see [What Windows shows at its first start](#what-windows-shows-at-its-first-start). Time zone, language and region, and keyboard left empty take the `DDT:Deployment` defaults. "Add the local administrator" adds the account configured there. |
| Join the domain | Windows | Joins the domain configured in `DDT:Deployment:Domain`, in the organizational unit the step names or else the configured one, and restarts Windows for the join to take effect, see [Joining a domain](#joining-a-domain). |
| Run script | Windows PE or Windows | Runs a cmd or PowerShell script as SYSTEM, optionally with a files package, within a timeout of 1 to 1440 minutes, 60 by default. Its exit codes decide: 0 means success and 3010 a restart unless the step lists others, and any other code fails it. |
| Restart | the phase of the step before | Restarts the machine and goes on with the next step. |
| Write raw disk image | Windows PE | Erases the disk and writes the chosen [raw disk image](#raw-disk-images) over it as it downloads, see [Deploying Linux](#deploying-linux). |
| Write the cloud-init seed | Windows PE | Adds a 64 MiB partition labelled `CIDATA` at the end of the disk with the `meta-data`, `user-data` and optionally `network-config` files the step holds, with the machine's values filled in, for cloud-init to find at the image's first start. |

Every step has a name, conditions and two switches. "Go on when this step fails" lets the run go on
after the step failed, which stays marked as failed. "Restart after this step" restarts the machine
after the step succeeded and goes on with the next one; a script asks for the same with a restart
exit code. A script must not restart the machine itself: a step that was running when the machine
restarted, lost power or the agent stopped is never run again. It fails as interrupted, and "Go on
when this step fails" applies to it as to any failure. The machine's log shows it as an error in the
step's log, which also says when the run goes on because that switch is on.

**Scripts.** The agent writes a script to a file and runs it with `cmd.exe /d /c`, or with Windows
PowerShell as `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File`, see
[Security model](#security-model) for a Group Policy that overrides the execution policy. Either way
the console is switched to UTF-8 first (`chcp 65001`, for PowerShell from a `.cmd` file next to the
script). The agent reads each line of output as UTF-8, and a line that is not valid UTF-8 in the
ANSI code page, as older console programs such as `tree` write in it into a pipe whatever the
console's code page is, so umlauts and accents reach the machine's log either way. A script
finds the phase, `WindowsPE` or `Windows`, in `DDT_PHASE`, and the run's and the step's ids in
`DDT_RUN_ID` and `DDT_STEP_ID`. With a files package, `DDT_PACKAGE` names the folder the package
is unpacked to, which is the script's working directory and is deleted when the script ends.
Without one, the working directory is the folder the agent wrote the script to. In Windows PE after
Partition the disk, `DDT_WINDOWS` is the root of the Windows partition, usually `W:\`, for a script
that changes the applied Windows offline.

**Phases.** A sequence runs in Windows PE first. When it has steps in Windows, a Join the domain or
a Run script step set to Windows, the agent [hands the run over](#the-hand-over-to-windows) to the
installed Windows once the Windows PE steps are done, and the rest runs there after Windows setup. A
sequence without such steps ends in Windows PE, and the machine restarts into Windows setup. A
sequence that writes a raw disk image runs in Windows PE only, and the machine restarts into the
image.

**Conditions.** A step with conditions runs only when all of them hold, and is skipped otherwise.
A condition compares one of the machine's values, Manufacturer, Model, Serial number, SMBIOS UUID,
MAC address, Computer name (the name assigned to the machine) or Phase (`WindowsPE` or `Windows`),
with equals, does not equal, starts with or contains, ignoring case. MAC addresses are compared
without their separators, and a machine with several holds a condition when any of its addresses
does, or for does not equal, when none equals. The machine's log names a skipped step with the
conditions that did not hold and what the machine reported, such as
`Model starts with "OptiPlex", and the machine reports "Latitude 5440"`. The step never ran, so that
line is the run's, not the step's.

**What makes a sequence runnable.** A sequence is saved with problems, as a draft, but one with a
problem cannot be assigned, chosen at a machine or run by a rule. The editor shows each problem at
its step and field. The rules:

- A sequence has 1 to 100 steps, each with a name of at most 100 characters and at most 10
  conditions. A MAC address condition needs 12 hex digits for equals and does not equal, 1 to 12
  for starts with and contains.
- It partitions the disk at most once and applies at most one image, in that order. Neither step
  may have conditions or go on when it fails, because the steps after them rely on them.
- Inject drivers and Write the answer file come after Apply image. The answer file is written at
  most once, and the domain joined at most once.
- Steps in Windows come after every step in Windows PE, and need an Apply image step without
  conditions before them.
- Windows PE restarts only after Partition the disk, because the run's state is kept on that disk.
  That holds for Restart steps, "Restart after this step" and a script's restart exit codes. A
  Windows PE script with a files package also runs only after it.
- A script has at most 64 KiB, 1 to 16 exit codes for success and at most 16 for a restart, and no
  code in both lists.
- A sequence either installs Windows or writes a raw disk image. One that writes a raw disk image has
  no Partition the disk, Apply image, Inject drivers, Write the answer file or Join the domain step,
  and no step in Windows. It writes one image, whose step may have no conditions and not go on when
  it fails, and at most one cloud-init seed after it. It keeps its state in memory, so Windows PE
  cannot restart during it, and its scripts cannot have a package: the image leaves no partition to
  keep the state on or unpack a package to. Each seed file has at most 64 KiB, and `meta-data` and
  `user-data` must be there, though they can be empty.
- The image of Apply image must be an x64 Windows image in the library, the image of Write raw disk
  image a raw disk image in the library whose boot file is not for another processor, and a script's
  package a files package in the library. A time zone, language and region, and keyboard must be
  ones Windows knows. "Add the local administrator" needs
  `DDT:Deployment:LocalAdministrator:Password`, and Join the domain needs `DDT:Deployment:Domain`.

Three findings are only warnings. A sequence that goes on in Windows without a Write the answer file
step that adds the local administrator: Windows setup then stops at its account page, and the run
waits there until someone finishes it. A raw disk image that is not signed for Secure Boot, see
[Secure Boot and raw disk images](#secure-boot-and-raw-disk-images). And a placeholder in a seed
file that DDT does not know, which stays as it is. Deleting an image or a package, or changing a
setting, can give a saved sequence a problem, which the page then shows.

**Templates.** "New from the Install Windows template" makes a sequence with Partition the disk,
Apply image, Inject drivers, and Write the answer file, which adds the local administrator when one
is configured. With a domain configured, it ends with Join the domain. The template chooses no
image, so the new sequence has a problem until you choose one in its Apply image step. "New from the
Install Linux template" makes Write raw disk image, again without an image, and Write the cloud-init
seed, whose `meta-data` names the machine and whose `user-data` is a `#cloud-config` with an empty
list of SSH keys for the image's default user. "New empty sequence" starts without steps.

**Editing.** The editor saves by itself: 700 ms after typing stops, at least every 3 seconds while
typing goes on, and at once for switches, choices and moves. Steps move with their Move buttons,
with the arrow keys, Home and End on a Move button, or with Alt and the up or down arrow within a
step. A removed step comes back with Undo for 10 seconds. Every save raises the sequence's revision,
and a save based on an older revision is refused: the editor then says who saved meanwhile and what
they changed, and offers "Use theirs", which drops your changes since your last save, or "Keep
mine", which saves yours over theirs after a confirmation. With no unsaved changes, another
administrator's save appears as it happens. Every save is written to the audit table with what
changed, and the SHA-256 of every script that changed. Names are unique, ignoring case. A sequence a
rule chooses cannot be deleted, and runs keep the copy they ran either way.

**Who may change them.** Only administrators create and change sequences, packages and rules, and
operators assign, approve and stop runs. A sequence is code that runs as SYSTEM on every machine it
goes to, see [Security model](#security-model).

## Rules

A rule chooses the task sequence for a machine by one of its MAC addresses or by its model. An
administrator adds rules on the Rules page. A rule never authorizes a machine: it only chooses what
an operator's approval runs, or what is offered first at the machine.

What counts first: a sequence assigned on the web or chosen at the machine comes before every rule.
Then a rule for one of the machine's MAC addresses, the primary one first, then a rule for its
model: an exact model before a model ending in `*`, the longest such prefix first, and a rule that
names the manufacturer before one for any manufacturer. Models are matched as for
[driver packages](#packages). The machine's page says which choice applies and why.

A rule's sequence runs only in two ways:

- **Approved on the web.** Approving a waiting machine that a rule chooses a sequence for runs it,
  after a confirmation that names the sequence and whether it erases the disk. The approval carries
  the sequence the operator saw, and the server refuses it when the rules now choose another. When
  the sequence has a problem, erases a disk on a machine that reported more than one, or joins a
  domain or names the machine in its cloud-init seed and the machine has no name yet, the approval
  only authorizes the machine. So does an approval with someone signed in at the machine, who
  chooses there; with `DDT:Machines:RequireWebApproval` on, an approval therefore never runs a
  rule's sequence. When the sequence writes a raw disk image that may not start with Secure Boot
  on, the confirmation says so, and for a machine that reported Secure Boot on it approves only once
  the operator allows the image.
- **Suggested at the machine.** The technician signed in at the machine sees the rule's sequence
  first, marked as suggested, and still chooses it, confirming with `ERASE` when it erases a disk.

A rule's run never counts as zero touch, and [Deploying a machine](#deploying-a-machine) says when
it is cancelled. A new rule starts nothing by itself.

## Deploying a machine

A deployment, or run, runs one [task sequence](#task-sequences) on one machine. It starts in one of
these ways.

- **At the machine.** Once someone signed in at it (see [Registration and
  authorization](#registration-and-authorization)), the agent lists the sequences that can run,
  those a rule suggests first, and leaves out those that erase a disk when the machine has no disk
  DDT could install on. The technician types the sequence's number, and then answers only what that
  sequence needs: the disk number when it erases a disk and there is more than one, a computer name
  when it joins a domain or names the machine in its cloud-init seed, and `ERASE` when it erases a
  disk. Anything but `ERASE` there goes back to the list. On a machine with Secure Boot on, a
  sequence that writes a raw disk image the machine would not start, one not signed for Secure Boot
  or signed under a CA its firmware does not trust, asks last for `ANYWAY`, and anything else goes
  back to the list too. A sequence with nothing more to ask starts once its number is typed.
- **On the Machines page.** An operator or administrator assigns a sequence, optionally with a
  computer name, which is required when the sequence joins a domain or names the machine in its
  cloud-init seed and the machine has no name yet.
  The dialog names the disks the machine reported and says what the assignment does. A machine
  waiting at its prompt, seen in the last 90 seconds, is authorized by the assignment, unless
  `DDT:Machines:RequireWebApproval` is on, in which case only a machine someone already signed in at
  is. Any other machine stays `Pending` with the sequence assigned, and runs it as soon as someone
  signs in at it. Assigning a sequence that erases a disk is refused for a machine that reported
  more than one disk DDT could install on: sign in at it and choose the disk there. For a raw disk
  image that may not start with Secure Boot on, the dialog warns and offers to write it anyway,
  which it requires for a machine that reported Secure Boot on.
- **By an approval** of the sequence a rule chooses, as [Rules](#rules) describes.
- **Zero touch.** `DDT:Machines:ZeroTouchNetworks` lists networks, for example `10.20.0.0/16`, and is
  empty by default. A machine with a sequence assigned on the page that netboots from one of them is
  authorized by that assignment and runs it with nobody at it. Zero touch is off while
  `DDT:Machines:RequireWebApproval` is on. It matches the address the registration comes from. Behind
  a reverse proxy that is the proxy's address, unless the proxy is listed in `DDT:ForwardedHeaders`
  (see [TLS is required](#tls-is-required)); then it is the address the proxy reports. List only
  proxies there. No network in `DDT:Machines:ZeroTouchNetworks` may contain a proxy's address or
  overlap a listed proxy network, see [Security model](#security-model).

When a sequence is assigned, chosen or approved, the run keeps a copy of it and fixes the files it
downloads: its image, the driver packages that match the machine's model at that moment, and its
files packages. Editing the sequence or uploading a package later never changes the run; assign the
sequence again for that. The `DDT:Deployment` values without passwords are taken when the run
starts, and the passwords are read when a step fetches them. A run whose sequence needs a password
that is no longer configured fails as it starts, before the disk is touched.

A run that has not started can be cancelled on the Machines page, and a running one stopped. In
Windows PE, stopping marks the run failed and makes the machine start over as `Pending`, and its
disk is left half written. In the installed Windows, the agent learns of the stop at its next
contact with the server, within about ten seconds while it runs, ends the running step, script
included, and removes itself; Windows stays as far as the run got. Rejecting a machine also ends its
run. A sequence chosen at the machine, or run by an approval of a rule's choice, is cancelled when
the machine starts again before the run began; a web assignment stays for the next sign-in or a zero
touch netboot. A machine whose run is done and that netboots again becomes `Pending`, and nothing
runs on it without a sign-in or a new assignment.

DDT installs only on internal disks: not on removable media, USB, FireWire, iSCSI, file-backed virtual
disks or Storage Spaces, and not on disks under 30 GB. When a PC shows no disk at all, its storage is
most likely set to RAID or Intel VMD/RST in the firmware setup, for which Windows PE has no driver;
switch it to AHCI.

### Watching a run

The Machines page shows how far each run is, and a machine's name opens its page. That page says
what chooses the machine's sequence and why, and shows the run: each step with its state, start and
end on the server's clock, duration, progress and error, or the conditions it was skipped for; a
timeline from the machine's registration through each restart, with how long the machine was away,
and the hand-over to the end; the machine's earlier runs, one of which `?run=` pins; and the log.
While the machine restarts, and after the hand-over until the service in the installed Windows
starts, the agent does not report, and the page shows the last contact. Once the service runs, it
reports while it waits for Windows setup to finish. A run whose agent has been silent for longer
than a run token lasts (7 days) can never go on, so the server fails it within the next hour, with
an error that says since when; until then it stays running unless someone stops it. The same data
is at `GET /api/deployments/{id}` and `GET /api/machines/{id}/deployments`. The runs of every machine
are at `GET /api/deployments`, newest first and a page at a time, filtered by state, sequence,
machine and a search over the machine's name, model, serial number and MAC addresses and the run's
title; the first page counts the runs of each state.

The log panel shows the newest 500 lines of the run, or of the machine with its registration and
sign-in lines, and adds each line the agent sends as it arrives. It follows the newest line until
you scroll up, then pauses and counts what arrives, until Jump to the newest. Load older lines reads
500 more at a time, back to the start of what the server keeps, the newest 50,000 lines of each
machine; the page holds at most 20,000. Lines can be filtered by level and text, and a step's log
button shows only that step's lines; a skipped step has none. The line for a skipped or an
interrupted step, see [Task sequences](#task-sequences), appears once, however often the run went on
after a restart or a stop. Without the live connection the panel reads new lines every 5 seconds.
`GET /api/machines/{id}/log` takes `before`, `after`, `limit`, at most 1000, and `deploymentId`.

**Clock correction.** The Windows PE clock can be hours off. Every batch of log lines carries the
time the agent sent it by its own clock, and the server moves each line in the batch by the
difference to its own clock when the batch arrives. A difference under 2 seconds counts as network
delay and is ignored, and no line is put after the moment it arrived. The page shows the corrected
time. Where the agent's own time differs from it by more than 2 seconds, the time's tooltip says
what the agent's clock said and how far it was behind or ahead of the server. Step times are always
the server's own.

### In Windows PE

Before it touches the disk, the agent checks the run: the sequence again, that the server sent every
image, that `dism.exe` is there for Inject drivers and `powershell.exe` for a PowerShell script in
Windows PE, that it can load wimlib, and that the server has each image at its size. For a sequence
that erases a disk, it checks that the disk is there and holds the partitions, each image's download
and installed size, each package twice, for the download and unpacked, and 2 GB to spare. A failure
here leaves the disk as it was. Then it runs the steps, and reports each one live.

Partition the disk creates `DDT` on the new Windows partition, `W:\DDT` in Windows PE and `C:\DDT`
in the installed Windows, which only SYSTEM can open. When `W:` is taken in Windows PE, the Windows
partition gets the highest free letter instead, which scripts find in `DDT_WINDOWS`. From then on
the run lives in `DDT`:

| Path | Contents |
|---|---|
| `run\state.json` | The run's frozen sequence, its phase, the next step, each step's state and the partitions' ids, written before and after every step. |
| `run\token` | The run token, see [Security model](#security-model). |
| `run\final-report.json` | In the installed Windows only: how the run ended, until the server has it. |
| `cache`, `packages`, `scripts`, `scratch` | Downloads, unpacked packages, scripts and DISM's scratch space. |
| `logs` | DISM's logs, and in the installed Windows the agent's `agent.log`. |
| `agent` | From the hand-over on: `ddt-agent.exe` and its `agent.json`. |

A restart in Windows PE first sets the firmware's `BootNext` to the entry this start came from, so
the machine starts from the network again whatever its boot order says, then runs `wpeutil reboot`.
When the firmware made that entry only for a one-time boot menu, the agent warns and restarts
plainly; the disk has no boot loader yet, so the firmware normally falls through to the network.
After the restart the agent finds the run's state on the disk, registers with the run token, gives
the partitions their letters again by their ids and goes on with the next step.

That state already goes on after the step that asked for the restart, so the agent records the due
restart in `X:\DDT\restart-due` as soon as the step asks for it. `X:` is Windows PE's RAM disk,
built anew from `boot.wim` at every start, so the restart clears the file, as a restart of
Windows clears the registry key the service uses
[in the installed Windows](#in-the-installed-windows). An agent that is stopped before the restart,
or started again by hand after `wpeutil` failed, finds the file at its next start and makes the
same restart, setting `BootNext` again, instead of registering, and says so once. A start that was
stopped already, for example with Ctrl+C during the update check, leaves the restart due.

When the Windows PE steps are done, the agent makes the disk bootable, in Microsoft's order after
applying an image: the applied image's own `bcdboot`, its recovery environment with its `reagentc`,
and last Windows Boot Manager as the first UEFI boot entry, writing the boot variables itself where
`bcdboot` has not already done so. The entry the previous deployment of this disk left is reused, so
re-imaging does not pile up entries. If the firmware refuses, the run goes on with a warning, and
the machine's boot order has to be set by hand. From then on `X:\DDT\restart-due` says that the
restart leads into the installed Windows, for as long as the run keeps Windows Boot Manager first,
so an agent started again before that restart restarts into Windows instead of registering. A
sequence without steps in Windows then ends: the agent deletes `W:\DDT`, token first, sends its last
log lines, reports the run done and restarts into Windows setup. A run that fails or is stopped in
Windows PE deletes the answer file if its step had started, puts the boot order back as it was and
deletes `W:\DDT\run`, token first. The rest of `W:\DDT`, such as the scripts and DISM's logs, stays
on the half-written disk until the disk is partitioned again.

A failed run, a token refused while the run hands over to Windows or before its Done report is
sent, or a boot order put back after a stop owes no restart, and `X:\DDT\restart-due` goes. A
restart step still restarts after a refused token, and so does a finished run whose Done report was
refused.

### The hand-over to Windows

A sequence with steps in Windows hands the run over before the disk is made bootable:

1. The agent copies itself, which may be a newer agent than the boot image's, to
   `W:\DDT\agent\ddt-agent.exe`, with an `agent.json` that holds only the server's URL and root
   certificate.
2. It registers the `DdtSequence` service in the applied Windows offline, through `reg load` of its
   SYSTEM hive: started automatically as LocalSystem with
   `"%SystemDrive%\DDT\agent\ddt-agent.exe" --service`, and started again 60 seconds after a crash.
   This does not depend on `SetupComplete.cmd`, which Windows skips with an OEM product key.
3. It saves the run's state as being in Windows, with the run token.

Then it makes the disk bootable as above, tells the server of the restart and restarts into Windows
setup. An agent stopped before it tells the server puts the boot order back, and its next start
hands the run over again. One stopped while or after it tells the server, or whose `wpeutil`
failed, restarts into Windows at its next start, through `X:\DDT\restart-due`, without handing over
again. When Windows PE starts again instead, because the hand-over was interrupted or the firmware
starts from the network first, the agent hands the run over again, three times at most. The next
return fails the run, and the machine's firmware has to be set to start Windows Boot Manager first.

### In the installed Windows

Windows setup runs first, with the answer file. When the service starts, it registers with the run
token, and while setup or the out-of-box experience still runs, it reports that it waits for Windows
setup, which the Machines page shows. It checks again every 15 seconds, with no time limit, because
someone may be finishing the out-of-box experience by hand, and logs a warning every 30 minutes.
Then it deletes `C:\Windows\Panther\unattend.xml` and runs the remaining steps. It logs to the
server and to `C:\DDT\logs\agent.log`, whose lines start with the date and the time in UTC, such as
`2026-09-23 14:03:12 UTC INFO  ...`. The agent's console in Windows PE shows only the time, also in
UTC, by a clock that can be hours off, see [Watching a run](#watching-a-run).

- **Restarts** use `shutdown /r`. Before it tells the server, the agent records the due restart in
  the volatile registry key `HKLM\SYSTEM\CurrentControlSet\Services\DdtSequence\RestartDue`, which a
  restart clears. A service that starts and still finds the key, because the restart never
  happened, restarts Windows before it goes on. When Windows has not restarted 5 minutes after it
  was asked to, the agent asks again.
- **Join the domain** reports the step as running, fetches the join account and joins with
  `NetJoinDomain`, creating the computer account when there is none. While no domain controller
  answers, as happens while the network comes up, it tries again for 5 minutes. Known error codes
  become sentences, the refusal to reuse a computer account under KB5020276 among them. Then it
  restarts Windows for the join to take effect. The password is never logged.
- **The end of the run.** The agent writes how the run ended to `C:\DDT\run\final-report.json`, next
  to the token, and sends it. If the server cannot be reached, the service keeps trying until it
  can; if the service is stopped first, its next start sends it. Only once the server has it are the
  run's files deleted, token first.

Then the agent removes itself: it deletes the rest of `C:\DDT` except itself and its own log, runs
`sc delete DdtSequence`, which removes the service once its process ends, and marks what is still in
use, the agent, its `agent.json`, `agent.log`, anything a step's process still holds and the
folders, for deletion when Windows next starts. After a finished run it restarts Windows once more,
without warning a signed-in user, and nothing of DDT is left. After a failed or stopped run it does
not restart: the service is gone, but `C:\DDT` with the agent stays until Windows next restarts. A
service whose run the server no longer runs changes nothing on the server and removes itself the
same way.

### What Windows shows at its first start

A Write the answer file step writes `W:\Windows\Panther\unattend.xml` from `DDT:Deployment`, which
the server checks at startup. It lists every problem at once, and a misspelled key stops it:

| Setting | Meaning |
|---|---|
| `TimeZone` | A Windows time zone id such as `W. Europe Standard Time`. Empty: Windows picks one from the locale. A step can set its own. |
| `Locale` | Formats and system locale, such as `de-DE`. Empty: the image's language. A step can set its own. |
| `Keyboard` | Input locale, such as `0407:00000407` or `de-DE`. Empty: the locale. A step can set its own. |
| `LocalAdministrator:Name`, `LocalAdministrator:Password` | The local administrator a step with "Add the local administrator" creates. The name defaults to `Admin`. |
| `Domain:Name`, `Domain:OrganizationalUnit`, `Domain:UserName`, `Domain:Password` | The Active Directory domain a Join the domain step joins, the OU as a distinguished name, which a step can override (empty for the default Computers container, which cannot be named), and the join account as `DOMAIN\user` or `user@domain`. |
| `Domain:Controller` | The domain controller DDT asks when an administrator checks the join account, as a host name or an address. Unset, DDT asks the domain's name, which works when the server's DNS knows the domain. The machines never use it. |

Without the local administrator, Windows setup skips the Microsoft account screens and asks the
person at the PC to create a local account. With it, setup creates the administrator, lifts the
maximum password age for local accounts so the password does not expire after 42 days, and skips
the account pages. Configuring `Domain:Name` therefore requires `LocalAdministrator:Password`,
because without any account setup would stop at the account page on every domain PC. A sequence
that joins the domain still has to add the administrator in its Write the answer file step: without
that, it only gets the warning described under [Task sequences](#task-sequences), and its run waits
at the account page. The computer name is the one assigned to the machine, or one Windows makes up.

The answer file holds the local administrator's password. Setup masks it after each pass, the
service deletes the file before the first step in Windows, and a line the step adds to
`SetupComplete.cmd` deletes it once setup finished. Windows does not run `SetupComplete.cmd` when
the machine uses an OEM product key, except on Enterprise editions, so after a sequence without
steps in Windows the file can stay there.

### Joining a domain

A Join the domain step joins the domain from the installed Windows, and the run shows whether it
worked; `C:\Windows\debug\NetSetup.log` has the details. Use a dedicated join account that may only
create, and to re-image also reset, computer objects in that OU, deny it interactive sign in, and
never use a domain administrator: every operator can obtain its password, see
[Security model](#security-model). Re-imaging a PC under its old name only works if that account
created the computer object, or if its owner is allowed by the policy "Domain controller: Allow
computer account re-use during domain join" (KB5020276). A plain domain user without that delegation
stops after its quota of joins, 10 by default. Home editions cannot join a domain.

**Checking the join account.** On a Join the domain step, "Check the join account" lets an
administrator ask the domain before a machine does. The server signs in to a domain controller as
the join account and reports, step by step: whether the controller accepted the account and why not
(wrong password, disabled, locked out, expired), whether it serves the configured domain, whether
the step's organizational unit exists, and whether the account may create computer objects in it.
Without that right, and only for the default Computers container, it counts the joins left in the
domain's `ms-DS-MachineAccountQuota`. It cannot see the "Add workstations to domain" user right, nor
whether the account may take over an existing computer account when a PC is re-imaged. Every check
is audited, and it reads the settings as they are at that moment.

The server keeps the password secret during the check: over LDAPS when the controller has a
certificate the server trusts, and otherwise, only on a Windows server, over LDAP signed and sealed
with Kerberos or NTLM. On Linux a controller without LDAPS cannot be checked, and the check says so;
joining does not depend on it. When the server's DNS does not know the domain, set
`DDT:Deployment:Domain:Controller`.

### Deploying Linux

A sequence that writes a [raw disk image](#raw-disk-images), such as one from the Install Linux
template, runs in Windows PE and ends there.

Before it touches the disk, the agent checks that the disk has 512-byte sectors and holds the disk
image, plus 66 MiB for a seed, and whether the image may be written with the machine's Secure Boot,
see below. A failure here leaves the disk as it was.

Write raw disk image removes the partition table with `diskpart`'s `clean`, then downloads the
compressed image and writes it straight to the physical disk as it arrives, 4 MiB at a time,
decompressing it on the way. Nothing is stored in between, so the step takes as long as the slower of
the download and the disk. A dropped connection resumes where it stopped, as for a Windows image. The
first MiB, which holds the partition table, is written last, so a disk whose write was cut off holds
no partition table. The image's backup table moves to the end of the disk, and the rest of the disk
after the image's partitions stays free. A download that does not match its SHA-256 fails the step
before the partition table is written.

Write the cloud-init seed adds a partition labelled `CIDATA` at the end of the disk, 64 MiB with
FAT16, and writes the step's files into it. cloud-init's NoCloud source finds them at the image's
first start. The seed's place leaves the free space right after the image's partitions, so
cloud-init's `growpart` still grows the root partition into it. Before writing, the agent fills in
these placeholders, whose names it matches ignoring case:

| Placeholder | Value |
|---|---|
| `{{ComputerName}}` | the name assigned to the machine, or typed at it |
| `{{Manufacturer}}`, `{{Model}}`, `{{SerialNumber}}` | as the machine's firmware reports them |
| `{{SmbiosUuid}}` | the machine's SMBIOS UUID |
| `{{MacAddress}}` | the primary MAC address, in lowercase with colons |

A value is escaped for a YAML string in double quotes, so put the placeholder in double quotes, as in
`hostname: "{{ComputerName}}"`. Anything else in double braces, such as cloud-init's own jinja
templates, stays as it is. A placeholder the machine has no value for fails the step, and a sequence
that uses `{{ComputerName}}` needs a computer name when it is assigned or chosen, as a domain join
does. The template's `instance-id` is the SMBIOS UUID, which differs from machine to machine.

At the end the agent adds a firmware boot entry for the image's `\EFI\BOOT\BOOTX64.EFI`, named after
the image, puts it first in the boot order and restarts the machine into the image. As for Windows,
it takes over an entry that pointed at the EFI system partition the run erased, rather than adding
one more at every deployment. An image without an EFI system partition gets no entry, and the log
says to set the boot order by hand. The run is done when the machine restarts: DDT does not run in
the image, so the server hears nothing from the machine after that. A run that fails adds no boot
entry and does not restart the machine. A seed step skipped by its conditions, or one that failed
with "Go on when this step fails" on, leaves the image to start without a seed.

#### Secure Boot and raw disk images

The agent reads from Windows PE whether the firmware started it with Secure Boot on, and whether the
firmware's list of trusted certificates, db, holds Microsoft's third-party UEFI CAs 2011 and 2023.
It reports both when it registers, and the Machines page shows them. The server knows which of the
two CAs each image's boot file is signed under. A machine with Secure Boot on does not start an image
that is not signed for Secure Boot, nor one signed only under CAs its firmware does not trust: a
Secured-core PC or Hyper-V's Microsoft Windows template trusts neither, and a PC that never got the
2023 CA does not start a shim signed since June 2026, when the 2011 one expired. For such an image:

- A machine that reported Secure Boot on gets such an image only when the run allows it: the operator
  ticks the box to write the image anyway when assigning or approving, or the technician types
  `ANYWAY` at the machine. The server refuses the run otherwise, and the audit row of the assignment
  says it was allowed. The machine then starts the image once Secure Boot is turned off in its
  firmware setup, your own key is enrolled, or, for an image signed under the CA, the CA is allowed
  there.
- A machine that did not say gets the run, and the agent checks the firmware again before it erases
  anything: with Secure Boot on and the image not allowed, the run fails there and leaves the disk
  as it was.
- A machine with Secure Boot off gets the image without a question.

The image's name and the reason stand in each warning, and the run's page says when it was allowed.
Where the agent cannot read the firmware's list, DDT cannot tell, writes a signed image without a
question, and says so in the machine's log.

### When a deployment goes wrong

- Everything the agent runs and everything it prints goes to the machine's log on the server.
  In Windows PE, `X:\DDT\partition.txt` is the `diskpart` script and `X:\DDT\wimlib.log` wimlib's own
  messages. In the installed Windows, `C:\DDT\logs\agent.log` holds the agent's lines until it
  removes itself. `C:\DDT` is open only to SYSTEM, so a local administrator reads the file from a
  command prompt that runs as SYSTEM, or takes ownership of the folder and grants themselves access
  first.
- When the agent cannot reach the server, its warning or error says why. Where the connection itself
  failed, it names the address it tried, so a mistyped `-ServerUrl` in the boot image shows there.
  For a server at `ddt.example:8443`:
  - "the server at ddt.example:8443 did not accept a connection within 10 s (tried 10.0.0.5, name
    lookup 0.0 s)", when no TCP connection came about: a wrong address, a firewall that drops the
    packets, or a route that loses them;
  - "the server at ddt.example:8443 accepted a connection at 10.0.0.5:8443 after 0.1 s, but the TLS
    handshake did not finish within 10 s", when something listens there that does not complete TLS,
    such as an overloaded server or a proxy that holds the connection;
  - "the name of the server at ddt.example:8443 could not be looked up within 10 s", when the DNS
    server does not answer;
  - "the server did not answer within 30 s", once the connection was made;
  - "the connection to ddt.example:8443 was refused, so nothing listens on that port";
  - "this machine has no network route to ddt.example";
  - "the name ddt.example cannot be found in DNS";
  - for a certificate the agent does not trust, what to change, see
    [Registration and authorization](#registration-and-authorization).
- During Windows setup, Shift+F10 opens a command prompt. Setup writes `C:\Windows\Panther\setupact.log`
  and `setuperr.log`, and `C:\Windows\Panther\UnattendGC\setupact.log` for the answer file.
- A run that waits for Windows setup for a long time most likely waits at the out-of-box experience
  for someone to create an account: no Write the answer file step adds the local administrator.
- A run that makes no contact after the hand-over may have its agent blocked in Windows, see
  [Security model](#security-model).
- A PC that netboots again straight after its deployment did not take the new boot entry. The machine
  log shows the firmware boot entries as `bcdedit /enum firmware` listed them after the agent wrote
  them, and any warning from writing them.

## Security model

State the trust boundary plainly, because a careful design elsewhere invites the wrong assumption
here.

**The provisioning network is inside the trust boundary.** DDT cannot stop a rogue DHCP or PXE
server on that segment from serving a different boot image to your machines, exactly as Microsoft
states for ConfigMgr. Anything inside `boot.wim` is readable by anyone who can boot it, so DDT
treats the boot path as public and puts nothing there but the server URL, the root certificate and
the name of the keyboard layout.

Registration is open to anyone who reaches the HTTPS endpoint, and deliberately worth almost nothing:
it lets a machine say it exists. A machine enters as `Pending`, and its token reaches nothing but its
own poll and the sign in. Only an operator or administrator authorizes it, by signing in at the
machine or approving it on the Machines page, before it can write a log line or read a task sequence,
an image or a secret. That gate is the control that the published attacks against SCCM operating
system deployment walk straight through, and it is the reason the rest of this design exists.

A run hands an authorized machine its sequence, the files the sequence downloads, and, while the
steps that need them run, the `DDT:Deployment` passwords. Assigning a sequence on the Machines page
is an operator's decision like an approval, with two consequences to keep in mind. A machine counts
as waiting at its prompt for 90 seconds after it was last seen, and anyone presenting its UUID and a
MAC address can register as it in that time, so the assign dialog shows where it was last seen from:
check it, as for an approval. And with `DDT:Machines:ZeroTouchNetworks` set, a registration from a
listed network that presents an assigned machine's UUID and a MAC receives the run and the
passwords. Every viewer can see both values, and
every PXE request carries them, so list only provisioning segments and cancel assignments that are
not about to be used. Behind a reverse proxy listed in `DDT:ForwardedHeaders`, the network is judged
by the address the proxy reports, and a listed proxy network that also holds clients lets them claim
a zero touch address by sending `X-Forwarded-For` to DDT themselves. A request a proxy forwards
without a client address, before `DDT:ForwardedHeaders` is set or from an nginx location that dropped
the headers, comes from the proxy's own address, so a zero touch network must not contain a proxy or
overlap a listed proxy network. DDT refuses zero touch to a request that still comes from a listed
proxy, but it cannot tell a proxy it does not list from a machine.

**Rules never authorize.** A rule only chooses a sequence. It never counts as an approval, never
makes a machine zero touch, and runs only through an operator's approval, which carries the sequence
the operator was shown. Spoofing a MAC address or a model only changes the sequence of a machine
that still has to be authorized.

Every operator can obtain the local administrator and domain join passwords by running a sequence
that needs them on a machine they control, and they sit in DDT's settings. Treat the local
administrator password as known to all operators, for example by letting Windows LAPS take the
account over after the join, and give the join account nothing but the right to create computer
objects in its OU.

**A task sequence is code that runs as SYSTEM** on every machine it goes to, in Windows PE on the
provisioning network and in the installed Windows, and the packages it uses are unpacked and their
drivers installed there as SYSTEM. A script can do anything SYSTEM can, including reading the domain
join account's password when a later Join the domain step fetches it. Only administrators change
sequences, packages and rules, and every change is audited, but every signed-in user can read the
scripts: never put a password in one. The zip checks keep a package from writing outside its folder
on the machine; they say nothing about what it contains.

**A raw disk image is code too.** It runs whatever its boot loader and its system run, with the
machine to itself and with what the seed gives it. A signature under Microsoft's UEFI CA only says
that the boot loader may start with Secure Boot on; it says nothing about the rest of the image.
Upload images from their publisher, and check their checksums. The seed's files are part of the
sequence, which every signed-in user can read, and a cloud-init seed on a disk can be read by anyone
who holds the disk: put in public SSH keys, and passwords only hashed. Allowing an image that is not
signed for Secure Boot is written to the audit table with the run.

**Secrets are handed out just in time.** The run the agent receives holds no password. The agent
fetches the answer file while its Write the answer file step runs, and the join account while its
Join the domain step runs. The server answers only for the machine's running run, only for that
step while it knows the step runs, with `Cache-Control: no-store`, and writes an audit row for every
read. The passwords are read from the settings at that moment and never stored with the run.
The join account goes only to an agent that registered as the service in the installed Windows,
which is what the agent says of itself, and only for the domain that was configured when the run
started. It lives in the agent's memory and is never written to disk or logged. The local
administrator's password stays in the answer file until Windows setup is done, as
[What Windows shows at its first start](#what-windows-shows-at-its-first-start) describes.

**The run token.** From Partition the disk on, the agent keeps a run token in `DDT\run\token` on
the Windows partition, in a folder only SYSTEM can open. It lets the agent register again after a
restart and go on with its run, and nothing else: the server takes it only at registration, only
for its machine and run, only while that run is the machine's running run in the token generation
it was issued in, and for 7 days after it was issued, with a new one in every answer to a report.
It dies with the run: when the run ends, is stopped, is failed by the server because the agent was
silent for longer than a token lasts, or the machine is rejected, and the agent deletes it first
when the run is over. Every registration that goes on with a run is audited with
its address, and so is a refused run token. Whoever reads the token during the run, a local
administrator in the installed Windows or anyone with the disk in hand, where the folder's
permissions mean nothing, is the machine for that run: they can download its files, fetch a password
whose step has not run yet, report false progress or fail it.

**In the installed Windows** the agent is an unsigned executable in `C:\DDT`. Microsoft Defender or
Smart App Control may block it, and so may WDAC or AppLocker rules in the image; the run then makes
no contact after the hand-over. Where such rules block it, allow `C:\DDT\agent\ddt-agent.exe` in
them by its path, or sign the agent you upload on the Server page and allow its signer: machines
run that agent in place of the boot image's, as
[Updating the agent without a new boot image](#updating-the-agent-without-a-new-boot-image)
describes. A Group Policy that sets the PowerShell execution policy overrides
`-ExecutionPolicy Bypass` for scripts once the machine has joined the domain. Re-imaging a PC under
its old name can fail on the domain's rules for reusing a computer account, see
[Joining a domain](#joining-a-domain).

Because anyone who registers a machine reaches the sign in at it, it is exposed exactly like the web
sign in page, and treated the same: the same accounts and lockout, and the same limit of 10
attempts every 5 minutes per address, shared between the two. An approval by signing in is bound to
the registration that asked for it, so an agent that registers the machine again while the password
is being checked does not receive it.

Every registration, re-registration, sign in at a machine, approval, rejection and removal by an
operator is written to the audit table with the actor and source address, and so is every run that
is assigned, starts, goes on after a restart, reads a password or ends, and every change to a
sequence, package, rule, account or API token. Waiting machines removed after a day unseen are only
counted in the server log. Since anyone can register, approve on the page only a machine you can tie
to a real PC, by its address or by someone signing in at it.

Administrators read the audit table at `GET /api/audit`, newest first, a page of up to 500 rows at a
time, filtered by the start of the action such as `machine.`, any part of the actor's name, the exact
subject id and a time range, `from` included and `to` not. Each row says whether a user, an API
token, a machine or DDT itself acted, and the rows a change adds reach administrators' open pages as
it is stored.

Machine tokens are opaque payloads from ASP.NET Core Data Protection rather than JWTs: the key
ring is already required, already rotates, and this needs no token library. Each purpose, poll,
session, resume and run, has its own protector, so a poll token cannot be replayed as a session
token, and a run token is never taken as a bearer token.
Every token carries the machine's token generation, so bumping one column invalidates all of that
machine's outstanding tokens at once.

Machine tokens are bound to the reported SMBIOS UUID and MAC address. That detects mistakes and
casual replay. It is **not** device identity: both values are attacker controllable. The real
controls are the authorization gate, the short token lifetime and the network segment.

**Signing in at a machine puts a credential on the boot path.** A rogue DHCP or PXE server can boot a
look-alike prompt, capture what a technician types, and use a current authenticator code on the web
sign in straight away. For a directory account the captured value is the domain password, and DDT
asks for no second factor on those. `DDT:Machines:RequireWebApproval` does not help, because it still
asks for the sign in. Where the provisioning segment cannot be trusted, leave that setting off and
approve machines on the Machines page instead of signing in at them. Everywhere else, sign in at
machines with accounts that hold the operator role and nothing more, never with a domain
administrator.

Human and machine principals are separated by authentication scheme, and every policy names its
scheme, so a machine token can never satisfy a human policy or the reverse.

DDT's root key, `ddt-root-key.pem`, can issue a certificate for any name, and every boot image and
browser that trusts the root accepts it: whoever holds the key can stand in for DDT towards machines
and administrators, and for any other site towards those browsers. Protect it like the Data
Protection key ring, which the container keeps in the same store volume, and back both up together:
without the key, a new root means building every boot image again. Trust the root only in the
browsers of the computers that manage DDT. The root carries no name constraints, because the names
the server is reached by change.

**Settings are an administrator's.** Every section needs the Administrator role; operators may
read the deployment and machines sections, which decide what an assignment does. No secret ever
comes back from it, only whether it is set, and a stored LDAP bind password or client secret goes
only to the server it was entered for, so a stolen administrator session cannot have one sent to a
server of its own. The fields that grant roles or trust, and the agent upload, need the
administrator's password again, at most 5 minutes old: the uploaded agent is code that every
machine that netboots runs as SYSTEM before anyone has authorized it, and the SHA-256 the agent
checks proves only that it received what the server announced.

The secrets of the settings are encrypted with the Data Protection key ring. That protects a copy of
the database alone, a dump or a database backup; it does not protect the store volume, where the key
ring is plain files. Keep backups of the key ring apart from those of the database, and protect them
like the volume.

Not defended, and worth saying out loud: an attacker with layer 2 control who spoofs the identity
of an already approved machine; anyone who can read the store volume together with the database;
and anyone who can read the Data Protection key ring, which can mint an administrator cookie and any
machine token, decrypts the stored secrets, or DDT's root key. Treat that volume as a secret. Anyone
who can write to it can also replace the boot files and the agent in it, which every machine that
netboots runs as SYSTEM before anyone has authorized it. And until a run ends, whoever can read the machine's disk is that machine
for the run, as the run token above describes.

## Status

M0 (the scaffold), authentication, the DHCP, ProxyDHCP and TFTP protocol layer, and the `pxe` role
are complete. A Hyper-V Generation 2 machine with Secure Boot on has netbooted from DDT into
Windows PE with the 2011 signed boot manager, fetching the 344 MB `boot.wim` in under nine seconds
at a window of 4 on the local virtual switch. The HTTP boot listener has not yet served real
firmware, because Hyper-V has no HTTP boot device.

Agent registration (M3) is complete: the NativeAOT agent registers, is authorized by a technician
signing in at the machine or by an approval on the Machines page, polls and streams its log, and the
Machines page updates live over SignalR.

The image library and deployment (M4) have run for real on a Hyper-V Generation 2 machine: two
deployments of Windows, a re-image that kept a single firmware boot entry, and the agent updating
itself in Windows PE. PostgreSQL runs in the tests through Docker, and the web UI has been used in a
browser against the server. A reverse proxy has not been tried yet.

Task sequences (M5), which replace M4's fixed list of deployment steps, are built: sequences and
their editor, packages, rules, runs in Windows PE and in the installed Windows, the machine page
with its live log and clock correction, DDT's own root certificate with renewal, and PowerShell in
the boot image. They are tested with fakes, and end to end in `DDT.E2E`, where the real host runs
whole sequences with the published agent in dry runs through both phases. On 2026-09-22 they ran,
with master at commit `8b0d080`, on the Hyper-V Generation 2 test machine against a development
host:

- At its first start the host moved the store's old self-signed certificate to DDT's own root,
  after its SQLite database had been deleted for the new schema. The boot image was built once more
  with `-RootCertificatePath` set to `ddt-root.pem`, and the agent trusted the server through the
  pinned root in Windows PE and in the installed Windows.
- `boot.wim` with the PowerShell components measured 496,342,317 bytes, against 347,838,990 bytes
  for the build before without them, or 473.3 MB against 331.7 MB in the script's megabytes of
  1,048,576 bytes: about 142 MB more, where about 120 MB had been estimated.
- The machine was authorized by a sign in at its console, and the run was assigned on the web. Its
  sequence of seven steps took six minutes and finished without a step error: Partition the
  disk; two scripts in Windows PE, the second restarting Windows PE; Apply image, in 2 minutes 46
  seconds; Inject drivers, with a driver package for the model `Virtual*` made from the inbox
  `wnetvsc.inf` driver folder; Write the answer file, with the configured local administrator and a
  time zone; the hand-over; and a PowerShell script in Windows with a restart after it.
- The restart in Windows PE netbooted the machine again through `BootNext`, and the agent found the
  run on the disk and went on without partitioning again.
- The `DdtSequence` service that the hand-over registered offline started in the installed Windows,
  so its quoted path with `%SystemDrive%` works. It reported while it waited through setup's restart
  and the out-of-box experience, ran the script, restarted Windows as the step asked, came back and
  finished the run.
- The machine's log had two warnings: a mistyped password at the sign in, and the update check
  after the Windows PE restart, which said only "The operation was canceled.". The agent now says
  which limit ran out and names the server's address, see
  [When a deployment goes wrong](#when-a-deployment-goes-wrong).

On 2026-09-24, with master at commit `b367f52`, the same machine and boot image checked more:

- With the host running, `ddt.pem` and `ddt-key.pem` were deleted. Within its five-minute check the
  host issued a new certificate from the same root and served it without a restart, and the boot
  image from 2026-09-22 netbooted, trusted it, switched to the new agent and registered.
- The power was cut at 27 % of Apply image. After the next netboot the agent found the run on the
  disk, gave the system and recovery partitions their letters again and failed the step as
  interrupted, without running it again; the machine log says so for that step. The clock of
  Windows PE was 10 hours ahead, and the log showed the corrected times.
- A second run of the seven steps took 4 minutes 42 seconds, Apply image 1 minute 50 seconds.
  After the last restart the `DdtSequence` service and `C:\DDT` were gone, and Windows Boot Manager
  came first in the firmware's boot order.
- The run's PowerShell script printed a German umlaut as a replacement character. PowerShell steps
  now start from a `.cmd` file that switches the console to UTF-8 first.
- A third run, with master at `1449f80`, added a PowerShell script that sends the queries for
  `davicloud.local` to its domain controller (`Add-DnsClientNrptRule`), and a Join the domain step.
  The machine joined the test domain within a second and restarted, and the run finished. The join
  password was in no log line, step error or audit record. Before it, "Check the join account"
  followed the domain live: can join within the quota, cannot with the quota set to 0, can with the
  right to create computer objects in the Computers container.
- `boot.wim` over TFTP on the local virtual switch, each boot to the agent's prompt, with no
  retransmission and no failed boot:

  | Window | With PowerShell, 496 MB | Without, 344 MB |
  |---|---|---|
  | 4 | 13.2 s | 8.4 s, 8.7 s, once 21.1 s |
  | 8 | 10.1 s, 10.1 s | 6.9 s, 7.1 s |
  | 16 | 7.9 s, 8.0 s | 5.3 s, 5.4 s |

  16 is therefore the default window since then, for `Build-BootImage.ps1` and for
  `DDT:Pxe:TftpMaxWindowSize`.

- A fourth run, with master at `e486d44`, stopped the host while the hand-over told it of the
  restart into Windows, and the agent was stopped with Ctrl+C while it tried again. Started again
  by hand, the agent found `X:\DDT\restart-due`, restarted into the installed Windows without
  registering, and the run went on there and finished, joining the domain again under the same
  computer name. The warning the agent printed about the due restart reaches only the console: the
  agent restarts before it registers, so the server never gets it.
- The third and fourth runs still showed the umlaut of `tree` wrongly. `tree` writes into a pipe in
  the ANSI code page whatever the console's is, so the agent now reads a line that is not valid
  UTF-8 in the ANSI code page. A fifth run, with master at `12cfbcb`, logged it correctly: "für".
- The first contact with the server after a netboot often failed with "did not accept a connection
  within 10 s", up to three times in a row, for up to 40 s. The agent now says which stage ran out:
  the name lookup took 0.0 s, and the TCP connection to the right address was not accepted. The
  server still held the connections of the machine's previous start as established, because a
  restart or reset never closes them, and Windows PE hands out the same client ports at every
  start, from 49668 on. A new connection from such a port collided with the old one until the
  server timed it out. The agent now connects from a port drawn at random from the dynamic range,
  49152 to 65535, and the agent from the boot image closes its connections before it starts a
  newer agent. In five netboots after that, the newer agent registered at once every time; the
  agent in the boot image, built before the fix, still missed its first update check twice, for
  2 s each.

On 2026-09-24 and 2026-09-25, with master at `5b04fdf`, two more runs checked the rest:

- With the Join the domain step set to `OU=Nowhere,DC=davicloud,DC=local`, "Check the join account"
  signed in over LDAP signed and sealed with Kerberos or NTLM, as the test domain controller has no
  LDAPS, and reported that the domain has no such organizational unit. The run then failed at the
  step with "davicloud.local has no organizational unit OU=Nowhere,DC=davicloud,DC=local (error
  2)". Less than a minute later Windows restarted on its own, requested by its setup experience
  (`CloudExperienceHostBroker.exe`, event 1074), which removed `C:\DDT` as the agent had marked it.
- A Run script step with the condition Model is "No such Model" was skipped, and the machine log
  said "Step Run script was skipped, because this condition did not hold: Model is "No such
  Model", and the machine reports "Virtual Machine".".
- A script step copied `C:\DDT\logs\agent.log` while the run went on. Its lines start with the date
  and time in UTC, such as `2026-09-25 07:23:00 UTC INFO`.

Not checked on a machine yet:

- a model rule;
- two administrators editing one sequence.

Letting the operator or the technician type the join credentials for a run is planned after M5.

Linux raw disk images (M6) are built: the upload and conversion of raw, gzip, zstd, xz and qcow2
images with the Secure Boot check of their boot file, the Write raw disk image and Write the
cloud-init seed steps, the boot entry, and the Secure Boot state of each machine with the allowance
for an image not signed for it. They are tested with fakes and end to end in `DDT.E2E` with a dry
run. On 2026-09-26, with branch `m6` at `b5b82f5`, they ran on Hyper-V against a development host,
with the Ubuntu 24.04 and Debian 13 cloud images:

- The server read both as signed for Secure Boot and x64. Ubuntu's disk uploaded raw and again
  compressed with zstd became one image. Ubuntu's qcow2 was kept as an unfinished upload, because the
  host has no `qemu-img`.
- A second Generation 2 machine, without a virtual TPM, netbooted Windows PE with Secure Boot off.
  Ubuntu was written with its seed, then Debian over it: each started under its assigned name with
  the seed's user and SSH key, and DDT took over the boot entry of the erased partition. Switched to
  the Microsoft UEFI Certificate Authority template with Secure Boot on, Ubuntu started through its
  shim.
- The Debian image with its grub as `BOOTX64.EFI`, which is not signed for Secure Boot, started with
  Secure Boot off. On the first machine, with Secure Boot on and the Windows template, the web and
  the console asked for the allowance, and Hyper-V then refused to start it, as the warning says. A
  signed Ubuntu image was written there without a question and did not start either: the Windows
  template does not trust Microsoft's third-party CA. The agent has read the firmware's db since, and
  asks for the allowance there too; that check has run only against fakes so far.
- A disk with 4 KiB sectors was refused before anything was erased, and Windows was installed over
  the Linux disk.
- With its seed step skipped by a condition, Ubuntu showed nothing on its screen after the early boot
  messages. The image has no network configuration of its own and sends the rest of its boot to the
  serial console, so it was most likely waiting for the network.
- A power cut during the write could not be timed: the write took seconds.

Later milestones, in order, as [docs/roadmap.md](docs/roadmap.md) details them: M6.5 the real UI, as
the web UI and the agent's console in Windows PE are concept UIs until then; M7 the task sequence
flow builder and the sequence model it shows; M8 the Linux phase, in which a run goes on in the
installed Linux; M9 applications and Windows configuration; M10 golden images and the machine
lifecycle; M11 reach beyond netboot and a single site; M12 the documentation of the whole project,
which this README stands in for until then. The roadmap also says what is not planned.

`DDT.Protocols` is pure: it binds no socket, reads no file and keeps no clock. It is a codec plus
two state machines, driven by `DDT.Pxe`. Packet fixtures live under
`tests/DDT.Protocols.Tests/Fixtures` with a provenance note beside each one; they are currently all
hand-constructed from the RFCs, and a real firmware capture always wins over one of them.

Two things about the protocol layer are deliberately not done yet:

- Option 43 sub-options are not parsed, so PXE Boot Server Discovery is not answered. DDT sets
  `PXE_DISCOVERY_CONTROL` to tell clients to skip discovery and boot the file in the reply, which is
  the normal arrangement for a single boot server, but a client that insists on discovery is not served.
- Only read requests are implemented. Netboot never writes, and a TFTP server that accepts writes on
  a provisioning network is a liability rather than a feature.

## Licence

DDT is free software under the GNU General Public License, version 3 or later, with additional
terms under section 7 of that licence. [LICENSE](LICENSE) holds the licence, and [NOTICE](NOTICE)
the attribution notice and the additional terms. In plain words, the additional terms ask everyone
who passes on DDT or a work based on it to:

- keep the attribution notice "DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026
  Davicloud." and the copyright notices intact, including in the legal notices the program shows;
- not present DDT, or a work based on it, as their own work;
- mark a modified version as modified, for example by saying in its legal notices who changed it
  and when.

For users this means: running DDT inside your organisation, changed or not, brings no duties.
Distributing DDT or a fork of it to others, as source, as a container image, as an agent or in a
boot image, means passing on its source code and its notices: LICENSE, NOTICE,
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and `licenses/`. DDT's source code is at
https://github.com/Davicloud-NET/DDT.

The web UI has an About page with DDT's legal notices. The sign-in page links to it, and so does
the navigation once signed in, so it can be read without signing in. The server publishes LICENSE,
NOTICE, THIRD-PARTY-NOTICES.md and `licenses/` into `legal/` next to itself, `/app/legal` in the
container image, and serves them without sign-in at `/api/about/legal/<path>`, where the About page
links to them. The agent prints its notices at start-up and its licence texts with `--licenses`.

DDT's built artefacts contain software by others under their own licences, among them wimlib in the
agent, under the GNU LGPL version 3 or later. [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
lists it for each artefact, and `licenses/` holds the licence texts.

The Windows PE and Windows ADK files a boot image consists of are Microsoft's. Whoever builds a boot
image supplies them from their own ADK installation. DDT does not distribute them, and DDT's licence
does not cover them.
