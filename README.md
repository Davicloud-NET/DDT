# DDT, the Davicloud Deployment Toolkit

DDT is a self-hostable, open-source replacement for the Microsoft Deployment Toolkit, aimed at small
networks of 10 to 100 machines that may span several sites over a VPN. It netboots machines into
Windows PE and runs an agent there that executes a task sequence: it applies a Windows image, goes
on in the installed Windows where the sequence asks for it, and reports progress live to a web UI.
Windows PE is the only deployment environment: Linux, a later milestone, is to be deployed from
inside WinPE by writing a raw disk image and a cloud-init seed partition, so there is a single agent
and a single boot path. DDT never ships its own EFI
bootloader. It serves the Microsoft-signed `bootmgfw.efi` from the Windows ADK and does everything
interesting after the boot manager has loaded, which is what lets it work on stock PCs with UEFI
Secure Boot enabled.

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
   `boot.wim` (about 340 MB) over TFTP as well. That is everything TFTP carries.
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
docs/settings.md           plan for moving admin settings from configuration to a settings page
src/
  DDT.Core/                domain model, image library, hashing, task sequences. No ASP.NET, no EF
  DDT.Protocols/           DHCP/PXE codec and TFTP state machine. Pure, no sockets
  DDT.Contracts/           DTOs shared with the agent, source-generated JSON
  DDT.Pxe/                 hosted services that bind the UDP sockets and drive DDT.Protocols
  DDT.Server/              EF Core, image storage, minimal API endpoints, SignalR hubs
  DDT.Host/                ASP.NET Core entry point. Registers roles, serves the API, hubs and SPA
  DDT.Web/                 Vite + React + TypeScript SPA, SCSS modules
  DDT.Agent/               NativeAOT agent for Windows PE, and its temporary service in Windows
  DDT.AppHost/             Aspire orchestration, development only
  DDT.ServiceDefaults/     OpenTelemetry, health checks, service discovery
tests/
  DDT.Core.Tests/
  DDT.Protocols.Tests/
  DDT.Pxe.Tests/
  DDT.Server.Tests/
  DDT.Agent.Tests/
  DDT.E2E/                 end-to-end checks, trait Category=E2E, not in the default test run
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

What a deployed Windows is set up with comes from `DDT:Deployment`, described under
[What Windows shows at its first start](#what-windows-shows-at-its-first-start) and
[Joining a domain](#joining-a-domain). Task sequences, packages and rules are not configuration:
they live in the database and are managed on their pages.

### What stays in configuration

Every admin setting is configuration today. With M6.5 the settings an admin changes in normal
operation move to a settings page in the web UI, where a change is checked when it is saved, applies
without a restart and leaves an audit row; [docs/settings.md](docs/settings.md) is the plan.
Configuration then keeps only what the server needs before it can serve that page, and stays
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
the certificate.

### Rules for new settings

From M5 on, a new setting follows these rules, so that moving it to the page changes only where it
is read from:

1. It is designed for a section of the settings page and lives in configuration until M6.5. Only a
   setting that passes the test above stays in configuration after that, and joins the table.
2. Task sequences, drivers per model and assignment by MAC address or model are database entities
   with an API and a page, never configuration.
3. It has its own section class with defaults, a known-key check and a pure validator that returns
   `SettingProblem` values. It is read where it is used, never while the services are registered
   and never through an `IOptions<T>` a singleton keeps. Secret fields say so in a comment, and the
   setting is documented here as a future page field.
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
- **LDAP.** Set `DDT:Ldap:Enabled`. Accounts are keyed on the directory's immutable identifier
  (`objectGUID` on Active Directory, `entryUUID` on OpenLDAP), never on the user name or the
  distinguished name, because both change when someone is renamed or moved. Group membership maps
  onto DDT roles through `DDT:Ldap:GroupRoleMap` and the directory stays authoritative: a role
  removed there is removed here on the next sign in.
- **OpenID Connect.** Set `DDT:Oidc:Enabled` to point DDT at Entra ID, Keycloak, Authentik or any
  other provider. DDT never links an external identity to an existing local account by email
  address, because a provider that does not verify addresses could then take over any account.
  Link from an authenticated session, or turn on `DDT:Oidc:AutoProvision` to create new accounts
  keyed on issuer and subject. They get the role in `DDT:Oidc:AutoProvisionRole`, `Viewer` by
  default. A role that does not exist stops the server at startup, and `Administrator` is logged as
  a warning at every start. When linking the identity or granting the role fails, the new account is
  deleted again and the sign in fails. A linked account with an authenticator still enters its code
  after the provider's sign in, and a disabled or locked out account is refused as with a password.

Two factor authentication is TOTP with recovery codes. Passkeys are not enabled, but the schema
carries the passkey table from the first migration so turning them on later needs no migration.

Roles are Administrator, Operator and Viewer. Endpoints deny by default: a new endpoint is closed
until it explicitly opts out.

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
| `ddt.previous.pem`, `ddt-key.previous.pem` | The pair the last renewal replaced, to go back to by hand |
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
again, such as one of your own, does not load at startup; the message says what to do. It never
makes a new root over an existing one by itself, because that would break every boot image.

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
| TFTP | Only the netboot chain: `bootmgfw.efi`, `BCD`, `boot.sdi` and `boot.wim`, about 340 MB in all, about 460 MB with PowerShell in Windows PE |
| HTTPS | Everything the agent does: registration, its own updates, task sequences, images, packages, logs |
| Plain HTTP, port 8080 | The same boot files, for UEFI HTTP Boot clients, which cannot validate a private CA |

The boot manager downloads `boot.wim` over TFTP itself, so TFTP speed decides how long a netboot
takes. At a window of 4 and a round trip time of 5 to 10 ms, expect roughly 5 to 10 minutes for the
image without PowerShell, and about a third longer with it. The plain HTTP listener serves the same
files to firmware that offers UEFI HTTP Boot, which fetches the boot manager over HTTP. Whether the
Windows boot manager started that way then reads `BCD`, `boot.sdi` and `boot.wim` over HTTP rather
than TFTP has not been verified: no firmware with an HTTP Boot device has booted from DDT yet, and
the `BCD` that `Build-BootImage.ps1` writes carries only TFTP settings for `boot.wim`.

### Which interfaces are served

`DDT:Pxe:Interfaces` is a comma separated list of interface names or local IPv4 addresses, and it
has no default. Until it names an interface, DDT logs every candidate interface with its addresses
and serves nothing, so a host with a NIC on someone else's network never answers PXE there.
Interfaces and their addresses are read at startup; restart DDT after changing them.

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
| `EFI/Microsoft/Boot/boot.stl` | Secure Boot revocation list the boot manager checks |
| `EFI/Microsoft/Boot/Fonts/` | Fonts the boot manager draws its screens with |

The script adds the Windows PE optional components PowerShell needs, WinPE-WMI, WinPE-NetFx,
WinPE-Scripting, WinPE-PowerShell, WinPE-DismCmdlets, WinPE-StorageWMI and WinPE-SecureBootCmdlets,
with their en-us language packages, so task sequence steps can run PowerShell scripts in Windows PE.
Components cannot be added to a running Windows PE, so they have to be in `boot.wim`: by the size of
their packages they take it from about 330 MB to about 450 MB. The script prints the real size at
the end; the sizes and netboot times of both images are to be measured on the test machine.
`-SkipPowerShell` builds the lean image for sites where netboot time matters more. On it the agent
refuses a run with a PowerShell script in Windows PE before it touches the disk, and says to build
the image without `-SkipPowerShell`. Either way the script exports `boot.wim`
at the end, which drops what servicing left behind in the file. A build needs an elevated prompt,
the Windows ADK and its Windows PE add-on.

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

### TFTP tuning

- **Block size** is capped at 1380. The block is payload only: a data packet adds 32 octets of TFTP,
  UDP and IP headers, so 1400 would make 1432 and fragment inside WireGuard's 1420 MTU. 1380 also
  fits a tunnel over PPPoE.
- **Window size** is capped by `DDT:Pxe:TftpMaxWindowSize`, default 4, the only value with Microsoft
  backing for the boot manager. DDT writes the BCD, so 8 or 16 can be measured by raising this and
  `Build-BootImage.ps1 -TftpWindowSize` together. A window of 16 would cut `boot.wim` to about a
  quarter of the time.
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
- Set `Logging:LogLevel:DDT.Pxe` to `Debug` to see every DHCP datagram DDT declined to answer and
  why, or to `Trace` to also see datagrams that arrived on interfaces it does not serve.
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
its next boot. A download that fails, or an agent that cannot start, leaves the machine on the agent
from the boot image. The check happens once, before the machine registers, and only in an agent
built with `dotnet publish`, never in a dry run; `--no-update` turns it off. What the agent from the
boot image printed before it switched stays on the console and does not reach the machine's log.

So a boot image only has to be built again for Windows PE itself, including its PowerShell
components, drivers, the keyboard layout, the server's URL or a new root. A renewed server
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
log what they would do, and a restart starts the agent over within the process. It really downloads
the images and packages, fetches the answer file and the join account, whose password it never
logs, and stages the agent for the Windows phase, all into `%TEMP%\ddt-dry-run-{id}`, which it
deletes when the run ends. Leave room there for the image. The Windows phase starts from the
`agent.json` the hand-over staged, and takes Windows setup as finished at once. A dry run stopped
with Ctrl+C goes on when it is started again with the same `--dry-run-id`. Every setting in
`agent.json` except the keyboard layout name can also be given as an argument.

## Images

The Images page lists the library. An administrator uploads a WIM there, for example
`sources\install.wim` from a Windows ISO, or an unencrypted ESD. The browser sends the file in 8 MiB
chunks. If the page is reloaded or the connection drops, selecting the same file again continues where
the server left off: it recognises the file by name, size and last change.

After the last chunk the server reads the image list from the file and computes its SHA-256. It
refuses a file that is not a WIM, a split WIM (`.swm`), a pipable WIM, a WIM whose image list is
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

## Packages

A package is a zip that the steps of a task sequence unpack on the machine. An administrator uploads
it on the Packages page as one of two kinds, in resumable chunks like an image.

- A **driver package** holds the driver folder of one hardware model with its `.inf` files. Its
  targets say which machines get it: a model as the machine's firmware reports it, optionally with
  a manufacturer. A model ending in `*` matches every model that starts with what comes before it,
  such as a Lenovo machine type, and needs at least three characters there. Case and spacing are
  ignored, and the placeholders firmware leaves in unset fields, such as "To Be Filled By O.E.M.",
  are refused and never match. An Inject drivers step adds every driver package whose targets match
  the machine.
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

Every step has a name, conditions and two switches. "Go on when this step fails" lets the run go on
after the step failed, which stays marked as failed. "Restart after this step" restarts the machine
after the step succeeded and goes on with the next one; a script asks for the same with a restart
exit code. A script must not restart the machine itself: a step that was running when the machine
restarted, lost power or the agent stopped is never run again. It fails as interrupted, and "Go on
when this step fails" applies to it as to any failure.

**Scripts.** The agent writes a script to a file and runs it with `cmd.exe /d /c`, or with Windows
PowerShell as `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File`, see
[Security model](#security-model) for a Group Policy that overrides the execution policy. A script
finds the phase, `WindowsPE` or `Windows`, in `DDT_PHASE`, and the run's and the step's ids in
`DDT_RUN_ID` and `DDT_STEP_ID`. With a files package, `DDT_PACKAGE` names the folder the package
is unpacked to, which is the script's working directory and is deleted when the script ends.
Without one, the working directory is the folder the agent wrote the script to. In Windows PE after
Partition the disk, `DDT_WINDOWS` is the root of the Windows partition, usually `W:\`, for a script
that changes the applied Windows offline.

**Phases.** A sequence runs in Windows PE first. When it has steps in Windows, a Join the domain or
a Run script step set to Windows, the agent [hands the run over](#the-hand-over-to-windows) to the
installed Windows once the Windows PE steps are done, and the rest runs there after Windows setup. A
sequence without such steps ends in Windows PE, and the machine restarts into Windows setup.

**Conditions.** A step with conditions runs only when all of them hold, and is skipped otherwise.
A condition compares one of the machine's values, Manufacturer, Model, Serial number, SMBIOS UUID,
MAC address, Computer name (the name assigned to the machine) or Phase (`WindowsPE` or `Windows`),
with equals, does not equal, starts with or contains, ignoring case. MAC addresses are compared
without their separators, and a machine with several holds a condition when any of its addresses
does, or for does not equal, when none equals.

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
- The image must be an x64 image in the library, and a script's package a files package in the
  library. A time zone, language and region, and keyboard must be ones Windows knows. "Add the local
  administrator" needs `DDT:Deployment:LocalAdministrator:Password`, and Join the domain needs
  `DDT:Deployment:Domain`.

One finding is only a warning: a sequence that goes on in Windows without a Write the answer file
step that adds the local administrator. Windows setup then stops at its account page, and the run
waits there until someone finishes it. Deleting an image or a package, or changing a setting, can
give a saved sequence a problem, which the page then shows.

**Templates.** "New from the Install Windows template" makes a sequence with Partition the disk,
Apply image, Inject drivers, and Write the answer file, which adds the local administrator when one
is configured. With a domain configured, it ends with Join the domain. The template chooses no
image, so the new sequence has a problem until you choose one in its Apply image step. "New empty
sequence" starts without steps.

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
  domain and the machine has no name yet, the approval only authorizes the machine. So does an
  approval with someone signed in at the machine, who chooses there; with
  `DDT:Machines:RequireWebApproval` on, an approval therefore never runs a rule's sequence.
- **Suggested at the machine.** The technician signed in at the machine sees the rule's sequence
  first, marked as suggested, and still chooses it, confirming with `ERASE` when it erases a disk.

A rule's run never counts as zero touch, and [Deploying a machine](#deploying-a-machine) says when
it is cancelled. A new rule starts nothing by itself.

## Deploying a machine

A deployment, or run, runs one [task sequence](#task-sequences) on one machine. It starts in one of
these ways.

- **At the machine.** Once someone signed in at it (see
  [Registration and authorization](#registration-and-authorization)), the agent lists the sequences
  that can run, those a rule suggests first, and leaves out those that erase a disk when the machine
  has no disk DDT could install on. The technician types the sequence's number, and then answers
  only what that sequence needs: the disk number when it erases a disk and there is more than one,
  a computer name when it joins a domain, and last `ERASE` when it erases a disk. Anything but
  `ERASE` there goes back to the list. A sequence with nothing more to ask starts once its number is
  typed.
- **On the Machines page.** An operator or administrator assigns a sequence, optionally with a
  computer name, which is required when the sequence joins a domain and the machine has no name yet.
  The dialog names the disks the machine reported and says what the assignment does. A machine
  waiting at its prompt, seen in the last 90 seconds, is authorized by the assignment, unless
  `DDT:Machines:RequireWebApproval` is on, in which case only a machine someone already signed in at
  is. Any other machine stays `Pending` with the sequence assigned, and runs it as soon as someone
  signs in at it. Assigning a sequence that erases a disk is refused for a machine that reported
  more than one disk DDT could install on: sign in at it and choose the disk there.
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
is at `GET /api/deployments/{id}` and `GET /api/machines/{id}/deployments`.

The log panel shows the newest 500 lines of the run, or of the machine with its registration and
sign-in lines, and adds each line the agent sends as it arrives. It follows the newest line until
you scroll up, then pauses and counts what arrives, until Jump to the newest. Load older lines reads
500 more at a time, back to the start of what the server keeps, the newest 50,000 lines of each
machine; the page holds at most 20,000. Lines can be filtered by level and text, and a step's log
button shows only that step's lines. Without the live connection the panel reads new lines every 5
seconds. `GET /api/machines/{id}/log` takes `before`, `after`, `limit`, at most 1000, and
`deploymentId`.

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

When the Windows PE steps are done, the agent makes the disk bootable, in Microsoft's order after
applying an image: the applied image's own `bcdboot`, its recovery environment with its `reagentc`,
and last Windows Boot Manager as the first UEFI boot entry, writing the boot variables itself where
`bcdboot` has not already done so. The entry the previous deployment of this disk left is reused, so
re-imaging does not pile up entries. If the firmware refuses, the run goes on with a warning, and
the machine's boot order has to be set by hand. A sequence without steps in Windows then ends: the
agent deletes `W:\DDT`, token first, sends its last log lines, reports the run done and restarts
into Windows setup. A run that fails or is stopped in Windows PE deletes the answer file if its step
had started, puts the boot order back as it was and deletes `W:\DDT\run`, token first. The rest of
`W:\DDT`, such as the scripts and DISM's logs, stays on the half-written disk until the disk is
partitioned again.

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

Then it makes the disk bootable as above and restarts into Windows setup. When Windows PE starts
again instead, because the hand-over was interrupted or the firmware starts from the network first,
the agent hands the run over again, three times at most. The next return fails the run, and the
machine's firmware has to be set to start Windows Boot Manager first.

### In the installed Windows

Windows setup runs first, with the answer file. When the service starts, it registers with the run
token, and while setup or the out-of-box experience still runs, it reports that it waits for Windows
setup, which the Machines page shows. It checks again every 15 seconds, with no time limit, because
someone may be finishing the out-of-box experience by hand, and logs a warning every 30 minutes.
Then it deletes `C:\Windows\Panther\unattend.xml` and runs the remaining steps. It logs to the
server and to `C:\DDT\logs\agent.log`, whose lines carry the time in UTC, without the date.

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
| `Domain:Name`, `Domain:OrganizationalUnit`, `Domain:UserName`, `Domain:Password` | The Active Directory domain a Join the domain step joins, the OU as a distinguished name, which a step can override, and the join account as `DOMAIN\user` or `user@domain`. |

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

### When a deployment goes wrong

- Everything the agent runs and everything it prints goes to the machine's log on the server.
  In Windows PE, `X:\DDT\partition.txt` is the `diskpart` script and `X:\DDT\wimlib.log` wimlib's own
  messages. In the installed Windows, `C:\DDT\logs\agent.log` holds the agent's lines until it
  removes itself. `C:\DDT` is open only to SYSTEM, so a local administrator reads the file from a
  command prompt that runs as SYSTEM, or takes ownership of the folder and grants themselves access
  first.
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
that needs them on a machine they control, and they sit in DDT's configuration. Treat the local
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

**Secrets are handed out just in time.** The run the agent receives holds no password. The agent
fetches the answer file while its Write the answer file step runs, and the join account while its
Join the domain step runs. The server answers only for the machine's running run, only for that
step while it knows the step runs, with `Cache-Control: no-store`, and writes an audit row for every
read. The passwords are read from the configuration at that moment and never stored with the run.
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
them by its path, or sign the agent you put at `DDT:Agent:BinaryPath` and allow its signer: machines
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
sequence, package or rule. Waiting machines removed after a day unseen are only counted in the
server log. Since anyone can register, approve on the page only a machine you can tie to a real PC,
by its address or by someone signing in at it.

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

Not defended, and worth saying out loud: an attacker with layer 2 control who spoofs the identity
of an already approved machine; anyone who can read the store volume or the database; and anyone
who can read the Data Protection key ring, which can mint an administrator cookie and any machine
token, or DDT's root key. Treat that volume as a secret. Anyone who can write to it can also replace
the boot files and the agent at `DDT:Agent:BinaryPath`, which every machine that netboots runs as
SYSTEM before anyone has authorized it: the SHA-256 the agent checks proves only that it received
what the server announced. And until a run ends, whoever can read the machine's disk is that machine
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
the boot image. They are tested with fakes and with the dry run through both phases, not yet on a
machine. The maintainer's run on the Hyper-V test machine is still to come, and checks:

- the certificate switch with its one boot image rebuild, with the size of `boot.wim` and the
  netboot time before and after PowerShell and at TFTP windows of 4, 8 and 16, and a forced renewal
  that needs no rebuild;
- a sequence with Partition the disk, a PowerShell and a cmd script in Windows PE, a restart in
  Windows PE and the resume after it, Apply image, a driver package made from one inbox driver
  folder, the answer file, the hand-over, a PowerShell script in Windows, a restart in Windows, and
  the end of the run, after which there is no `DdtSequence` service and no `C:\DDT`, and Windows
  Boot Manager comes first;
- an edit conflict between two administrators, a model rule, and power lost during Apply image.

The domain join is covered only by unit tests and the dry run, because the test network has no
Active Directory.

Later milestones, in order: M6 Linux raw disk images; M6.5 the real UI, as the web UI and the
agent's console in Windows PE are concept UIs until then; M7 the task sequence flow builder.

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
https://github.com/Davide244/DDT.

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
