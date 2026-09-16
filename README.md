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
  New-TestVm.ps1           Hyper-V Generation 2 test machine with Secure Boot on
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
`DDT.E2E` makes that assembly report "zero tests ran", which is otherwise a failure.

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
docker compose -f build/compose.yaml up
```

The compose file uses host networking, because the `pxe` role has to see DHCP broadcasts and bind
UDP 67, 4011 and 69. Host networking is a Linux host feature. On Docker Desktop the container
starts but broadcast traffic is not delivered to it, so PXE cannot be tested there.

## Configuration

`DDT:Roles` is a single comma separated string, not a list:

```bash
DDT__Roles=web,pxe
```

It is deliberately not a bound array. Indexed environment variables such as `DDT__Roles__0` merge
with a configured array rather than replacing it, so a shipped default plus an override would leave
unwanted roles running. Unknown role names fail at startup rather than being ignored.

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
  keyed on issuer and subject.

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

### TLS is required

`DDT:RequireHttps` defaults to true and the host refuses to start without an HTTPS endpoint. This
is deliberate rather than cautious: `Secure` cookies are silently dropped over plain HTTP, so an
auth stack on an HTTP listener appears to work while every request after sign in is anonymous.

A self signed certificate is generated on first run so a fresh deployment starts at all. Replace
it, or distribute it as a trusted root. Every name and address DDT is reached by has to be in the
certificate, because the agent validates the hostname against the chain it pins. List them in
`DDT:Https:SubjectAlternativeNames`.

Set `DDT:RequireHttps` to false only when a reverse proxy terminates TLS in front of DDT.

With the `pxe` role active, DDT adds a plain HTTP Kestrel endpoint named `Boot` for boot files.
Kestrel then ignores `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS` and launch profile URLs, so the
application endpoint has to be declared under `Kestrel:Endpoints` as well: `Https` as
`build/compose.yaml` does, or a plain HTTP one behind a reverse proxy. The host refuses to start
without it, whatever `DDT:RequireHttps` says.

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
| TFTP | Only the netboot chain: `bootmgfw.efi`, `BCD`, `boot.sdi` and `boot.wim`, about 340 MB in all |
| HTTPS | Everything the agent does: registration, task sequences, images, logs |
| Plain HTTP, port 8080 | The same boot files, for UEFI HTTP Boot clients, which cannot validate a private CA |

The boot manager downloads `boot.wim` over TFTP itself, so TFTP speed decides how long a netboot
takes. At a window of 4 and a round trip time of 5 to 10 ms, expect roughly 5 to 10 minutes.

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

`build/Build-BootImage.ps1` writes this layout, which is what `DDT:Pxe:BootDirectory` should hold:

| Path | Contents |
|---|---|
| `x64/bootmgfw.efi` | Boot manager signed by Microsoft Windows Production PCA 2011 |
| `x64/bootmgfw_ex.efi` | Boot manager signed by Windows UEFI CA 2023 |
| `Boot/BCD` | Boot configuration: `boot.wim` from a RAM disk over TFTP |
| `Boot/boot.sdi` | RAM disk description |
| `Boot/boot.wim` | Windows PE with `DDT.Agent` |
| `EFI/Microsoft/Boot/boot.stl` | Secure Boot revocation list the boot manager checks |
| `EFI/Microsoft/Boot/Fonts/` | Fonts the boot manager draws its screens with |

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
  Switch, whose own DHCP server makes it the same shape as a real site. It prints a `pktmon` recipe
  for capturing the boot.
- Hyper-V has no HTTP boot device, so the HTTP boot listener is covered by tests that replay the
  firmware's request sequence rather than by the test machine.

## Security model

State the trust boundary plainly, because a careful design elsewhere invites the wrong assumption
here.

**The provisioning network is inside the trust boundary.** DDT cannot stop a rogue DHCP or PXE
server on that segment from serving a different boot image to your machines, exactly as Microsoft
states for ConfigMgr. Anything inside `boot.wim` is readable by anyone who can boot it, so DDT
treats the boot path as public and puts nothing there but the server URL, the root certificate and
a rotatable enrollment token.

What that token can do is deliberately almost nothing: it lets a machine say it exists. A machine
enters as `Pending` and holds no grants at all. An administrator approves it before it can read a
task sequence, an image or a secret. That approval gate is the control that the published attacks
against SCCM operating system deployment walk straight through, and it is the reason the rest of
this design exists.

Machine tokens are opaque payloads from ASP.NET Core Data Protection rather than JWTs: the key
ring is already required, already rotates, and this needs no token library. Each purpose has its
own protector, so a poll token cannot be replayed as a session token. Every token carries the
machine's token generation, so bumping one column invalidates all of that machine's outstanding
tokens at once.

Machine tokens are bound to the reported SMBIOS UUID and MAC address. That detects mistakes and
casual replay. It is **not** device identity: both values are attacker controllable. The real
controls are the approval gate, the short token lifetime and the network segment.

Human and machine principals are separated by authentication scheme, and every policy names its
scheme, so a machine token can never satisfy a human policy or the reverse.

Not defended, and worth saying out loud: an attacker with layer 2 control who spoofs the identity
of an already approved machine; anyone who can read the store volume or the database; and anyone
who can read the Data Protection key ring, which can mint an administrator cookie and any machine
token. Treat that volume as a secret.

## Status

M0 (the scaffold), authentication, the DHCP, ProxyDHCP and TFTP protocol layer, and the `pxe` role
are complete. A Hyper-V Generation 2 machine with Secure Boot on has netbooted from DDT into
Windows PE with the 2011 signed boot manager, fetching the 344 MB `boot.wim` in under nine seconds
at a window of 4 on the local virtual switch. The HTTP boot listener has not yet served real
firmware, because Hyper-V has no HTTP boot device.

Later milestones, in order: agent registration, the image library and apply, task sequences, Linux
raw disk images, and the task sequence flow builder.

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

Agent enrollment endpoints arrive with the agent itself. The machine state model, the machine
token infrastructure and the approval gate they plug into are already in place.

One open question is already known and is recorded here so it is not rediscovered:

- Whether `wimgapi.dll`, which would let the agent apply images without shipping wimlib, is present
  in the WinPE base image and can apply a solid-compressed ESD.
