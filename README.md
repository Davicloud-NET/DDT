# DDT, the Davicloud Deployment Toolkit

DDT is a self-hostable, open-source replacement for the Microsoft Deployment Toolkit, aimed at small
networks of 10 to 100 machines that may span several sites over a VPN. It netboots machines into
Windows PE, runs an agent there that applies a Windows image or writes a raw Linux disk image,
executes a task sequence, and reports progress live to a web UI. Windows PE is the only deployment
environment: Linux is deployed from inside WinPE by writing a raw disk image and a cloud-init seed
partition, so there is a single agent and a single boot path. DDT never ships its own EFI
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
                         |           task sequence engine, database         |
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
4. Machines already running Windows skip PXE entirely: the agent stages WinPE on a local partition,
   adds a one-time BCD entry and reboots. This is the default re-imaging path. PXE is for bare
   metal.

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
  DDT.Agent/               NativeAOT console app for Windows PE
  DDT.AppHost/             Aspire orchestration, development only
  DDT.ServiceDefaults/     OpenTelemetry, health checks, service discovery
tests/
  DDT.Core.Tests/
  DDT.Protocols.Tests/
  DDT.Pxe.Tests/
  DDT.Server.Tests/
  DDT.Agent.Tests/
  DDT.E2E/                 Hyper-V driven, excluded from the default test run
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
PostgreSQL migration test runs only while Docker is running, and is skipped otherwise.

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
[Deploying a machine](#deploying-a-machine).

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
| `DDT:Https:GenerateSelfSignedCertificate`, `DDT:Https:SubjectAlternativeNames` | They give a fresh install a certificate to serve the page with. |
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
  deleted again and the sign in fails.

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
| `ddt-root-key.pem` | The root's private key. On Linux only its owner can read it, as with `ddt-key.pem`; on Windows the folder's permissions apply |
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
3. Until an administrator confirms that every boot image was built again,
   `GET /api/server/certificate` reports when the old certificate was replaced, in
   `anchorReplacedUtc`. `DELETE /api/server/certificate/replaced-anchor` confirms it, deletes the
   copy and is written to the audit table.

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
| HTTPS | Everything the agent does: registration, its own updates, task sequences, images, logs |
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
their packages they take it from about 330 MB to about 450 MB, and the script prints the real size
at the end. `-SkipPowerShell` builds the lean image for sites where netboot time matters more; a
step that runs PowerShell in Windows PE cannot run on it. Either way the script exports `boot.wim`
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
address it finds.

```bash
.\build\Publish-Agent.ps1
```

Publishing needs the Visual C++ build tools. The result is `artifacts\agent\ddt-agent.exe`, about
9 MB. It carries wimlib's `libwim-15.dll` inside itself and writes it next to itself before it
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
4. On its next poll the agent receives a session token, the credential it needs to deploy an image.
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
rebooted has lost that token and starts over, unless zero touch applies (see
[Deploying a machine](#deploying-a-machine)). A rejected machine stays rejected, and its agent stops.
To take a rejection back, an operator removes the machine on the Machines page, and it registers as
a new machine at its next netboot.

Registration is limited to 120 requests a minute per address, polling, logging and deployment
reports to 60 a minute per machine, and image requests to 30 a minute per machine. The machine limits
count the machine whose token a request carries, so one machine cannot use up another's. The server
keeps the newest 10,000 log lines of each machine. At most 100 machines
nobody has approved may wait per address, `DDT:Machines:MaxWaitingPerAddress`, and 10,000 in all,
`DDT:Machines:MaxWaiting`; a new machine beyond that is refused until some are approved or removed.
An operator can remove a waiting machine nobody ever approved, or every such machine from one
address, on the Machines page. Such machines also disappear once they have not been seen for a day.
A machine that was approved once, or that has an image assigned, is never removed this way, so its
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
runs a deployment without partitioning, applying or restarting anything: it logs the commands it
would run, but it really downloads the image and fetches the answer file, into
`%TEMP%\ddt-dry-run-{id}`, which it deletes when the run ends. Leave room there for the image. Every
setting in `agent.json` except the keyboard layout name can also be given as an argument.

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
refused while an assigned or running deployment uses it, and the stored file goes when no entry uses
it any more.

Behind a reverse proxy the chunk size matters. nginx refuses bodies over 1 MB by default, and
Traefik gives a whole request 60 seconds, so an 8 MiB chunk needs at least 140 KB/s. Machines download
images with range requests, which the proxy must pass through without buffering the whole file. For
nginx, with DDT listening on port 8443 behind it:

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

location ~ ^/api/agents/[^/]+/images/ {
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

## Deploying a machine

A deployment installs one image from the library on one machine. It starts in one of three ways.

- **At the machine.** Once someone signed in at it (see
  [Registration and authorization](#registration-and-authorization)), the agent lists the x64 images.
  The technician types the image number, the disk number when there is more than one disk, a computer
  name when a domain is configured and the machine has none, and then `ERASE`. Anything else goes back
  to the list.
- **On the Machines page.** An operator or administrator assigns an image, optionally with a computer
  name, which is required when a domain is configured. The dialog names the disks the machine reported
  and says what the assignment does. A machine waiting at its prompt, seen in the last 90 seconds, is
  authorized by the assignment, unless `DDT:Machines:RequireWebApproval` is on, in which case only a
  machine someone already signed in at is. Any other machine stays `Pending` with the image assigned,
  and deploys as soon as someone signs in at it. Assigning is refused for a machine that reported more
  than one disk DDT could install on: sign in at it and choose the disk there.
- **Zero touch.** `DDT:Machines:ZeroTouchNetworks` lists networks, for example `10.20.0.0/16`, and is
  empty by default. A machine with an image assigned on the page that netboots from one of them is
  authorized by that assignment and deploys with nobody at it. Zero touch is off while
  `DDT:Machines:RequireWebApproval` is on. It matches the address the registration comes from. Behind
  a reverse proxy that is the proxy's address, unless the proxy is listed in `DDT:ForwardedHeaders`
  (see [TLS is required](#tls-is-required)); then it is the address the proxy reports. List only
  proxies there. No network in `DDT:Machines:ZeroTouchNetworks` may contain a proxy's address or
  overlap a listed proxy network, see [Security model](#security-model).

A deployment that has not started can be cancelled, and a running one stopped. Stopping marks it
failed and makes the machine start over as `Pending`; its disk is left half written. Rejecting a
machine also ends its deployment. An image chosen at a machine is dropped if the machine netboots again
before it started.

What the agent does, with progress and each step's duration shown live on the Machines page:

1. It checks, before it touches the disk, that the disk is there, that it holds 1.5 GB of partitions
   plus the download plus the installed size plus 2 GB, that it can load wimlib and that the server
   has the image. A failure here leaves the disk as it was.
2. It erases the disk and partitions it with one `diskpart` script: EFI 300 MB, MSR 16 MB, Windows,
   and a 1 GB recovery partition at the end.
3. It downloads the image to `W:\DDT`, resuming after a dropped connection, and checks its size and
   SHA-256. It gives up when the download has not grown for 15 minutes.
4. It applies the image with wimlib.
5. It makes the disk bootable with the applied image's own `bcdboot` and sets up the recovery
   environment with its `reagentc`.
6. It writes `W:\Windows\Panther\unattend.xml`, adds a line to `SetupComplete.cmd` that deletes it
   once setup finished, and makes sure Windows Boot Manager on the new disk is the first UEFI boot
   entry, writing the boot variables itself where `bcdboot` has not already done so. A machine that
   starts from the network first then starts Windows next. The
   entry the previous deployment of this disk left is reused, so re-imaging does not pile up entries.
   If the firmware refuses, the deployment still finishes with a warning, and the machine's boot
   order has to be set by hand. A deployment that fails or is stopped after this step deletes the
   answer file and puts the boot order back as it was.
7. It sends its last log lines, reports the deployment done and restarts.

A deployment interrupted by a restart is not resumed: it fails, and the image is assigned or picked
again. A machine whose deployment is done and that netboots again becomes `Pending`, and nothing is
installed on it without a sign in or a new assignment.

DDT installs only on internal disks: not on removable media, USB, FireWire, iSCSI, file-backed virtual
disks or Storage Spaces, and not on disks under 30 GB. When a PC shows no disk at all, its storage is
most likely set to RAID or Intel VMD/RST in the firmware setup, for which Windows PE has no driver;
switch it to AHCI.

### What Windows shows at its first start

The answer file comes from `DDT:Deployment`, which the server checks at startup. It lists every
problem at once, and a misspelled key stops it:

| Setting | Meaning |
|---|---|
| `TimeZone` | A Windows time zone id such as `W. Europe Standard Time`. Empty: Windows picks one from the locale. |
| `Locale` | Formats and system locale, such as `de-DE`. Empty: the image's language. |
| `Keyboard` | Input locale, such as `0407:00000407` or `de-DE`. Empty: the locale. |
| `LocalAdministrator:Name`, `LocalAdministrator:Password` | A local administrator created on every machine. The name defaults to `Admin`. |
| `Domain:Name`, `Domain:OrganizationalUnit`, `Domain:UserName`, `Domain:Password` | An Active Directory domain to join, the OU as a distinguished name, and the join account as `DOMAIN\user` or `user@domain`. |

Without a local administrator password, Windows setup skips the Microsoft account screens and asks the
person at the PC to create a local account. With one, setup creates the administrator, lifts the
maximum password age for local accounts so the password does not expire after 42 days, and skips
the account pages. A domain requires the local
administrator, because without any account setup would stop at the account page on every domain PC.
The computer name is the one assigned to the machine, or one Windows makes up.

The domain join happens at the first start of Windows, after DDT has shown the deployment as done, and
DDT does not see whether it worked. When a PC ends up in a workgroup, look at
`C:\Windows\debug\NetSetup.log` and `C:\Windows\Panther\UnattendGC\setupact.log`. Use a dedicated join
account that may only create, and to re-image also reset, computer objects in that OU, deny it
interactive sign in, and never use a domain administrator: every machine DDT deploys can read its
password. Re-imaging a PC under its old name only works if that account created the computer object,
or if its owner is allowed by the policy "Domain controller: Allow computer account re-use during
domain join" (KB5020276). A plain domain user without that delegation stops after its quota of joins,
10 by default. Home editions cannot join a domain.

The answer file holds these passwords. Setup masks them in it after each pass and `SetupComplete.cmd`
deletes it, but Windows does not run `SetupComplete.cmd` when the machine uses an OEM product key,
except on Enterprise editions.

### When a deployment goes wrong

- Everything the agent runs and everything it prints goes to the machine's log on the server.
  In Windows PE, `X:\DDT\partition.txt` is the `diskpart` script and `X:\DDT\wimlib.log` wimlib's own
  messages.
- During Windows setup, Shift+F10 opens a command prompt. Setup writes `C:\Windows\Panther\setupact.log`
  and `setuperr.log`, and `C:\Windows\Panther\UnattendGC\setupact.log` for the answer file.
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

A deployment hands an authorized machine its image and an answer file with the `DDT:Deployment`
passwords. Assigning an image on the Machines page is an operator's decision like an approval, with
two consequences to keep in mind. A machine counts as waiting at its prompt for 90 seconds after it
was last seen, and anyone presenting its UUID and a MAC address can register as it in that time, so
the assign dialog shows where it was last seen from: check it, as for an approval. And with
`DDT:Machines:ZeroTouchNetworks` set, a registration from a listed network that presents an assigned
machine's UUID and a MAC receives the image and the passwords. Every viewer can see both values, and
every PXE request carries them, so list only provisioning segments and cancel assignments that are
not about to be used. Behind a reverse proxy listed in `DDT:ForwardedHeaders`, the network is judged
by the address the proxy reports, and a listed proxy network that also holds clients lets them claim
a zero touch address by sending `X-Forwarded-For` to DDT themselves. A request a proxy forwards
without a client address, before `DDT:ForwardedHeaders` is set or from an nginx location that dropped
the headers, comes from the proxy's own address, so a zero touch network must not contain a proxy or
overlap a listed proxy network. DDT refuses zero touch to a request that still comes from a listed
proxy, but it cannot tell a proxy it does not list from a machine.

Every operator can obtain the local administrator and domain join passwords by deploying a machine
they control, and they sit in DDT's configuration. Treat the local administrator password as known to
all operators, for example by letting Windows LAPS take the account over after the join, and give the
join account nothing but the right to create computer objects in its OU.

Because anyone who registers a machine reaches the sign in at it, it is exposed exactly like the web
sign in page, and treated the same: the same accounts and lockout, and the same limit of 10
attempts every 5 minutes per address, shared between the two. An approval by signing in is bound to
the registration that asked for it, so an agent that registers the machine again while the password
is being checked does not receive it.

Every registration, re-registration, sign in at a machine, approval, rejection and removal by an
operator is written to the audit table with the actor and source address. Waiting machines removed
after a day unseen are only counted in the server log. Since anyone can register, approve on the page
only a machine you can tie to a real PC, by its address or by someone signing in at it.

Machine tokens are opaque payloads from ASP.NET Core Data Protection rather than JWTs: the key
ring is already required, already rotates, and this needs no token library. Each purpose, poll,
session and resume, has its own protector, so a poll token cannot be replayed as a session token.
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
what the server announced.

## Status

M0 (the scaffold), authentication, the DHCP, ProxyDHCP and TFTP protocol layer, and the `pxe` role
are complete. A Hyper-V Generation 2 machine with Secure Boot on has netbooted from DDT into
Windows PE with the 2011 signed boot manager, fetching the 344 MB `boot.wim` in under nine seconds
at a window of 4 on the local virtual switch. The HTTP boot listener has not yet served real
firmware, because Hyper-V has no HTTP boot device.

Agent registration (M3) is complete: the NativeAOT agent registers, is authorized by a technician
signing in at the machine or by an approval on the Machines page, polls and streams its log, and the
Machines page updates live over SignalR.

The image library and deployment (M4) are built: resumable uploads, range downloads, the Images page,
assignment on the page and at the machine, zero touch, and an agent that partitions, downloads,
applies with wimlib, makes the disk bootable, writes the answer file and restarts into Windows. They
have run end to end on one PC with the published agent in dry-run mode against a real host. Not yet
run: a real deployment in Windows PE, PostgreSQL (its test needs Docker), a reverse proxy, and the web
UI in a browser against the server. Later milestones, in order: task sequences, Linux raw disk images,
and the task sequence flow builder.

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
