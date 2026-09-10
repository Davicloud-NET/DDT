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
   firmware ---UDP--->   |  pxe      ProxyDHCP 67 + 4011, TFTP 69,          |
            <--TFTP--    |           boot file cache. Host networking.      |
                         |           One instance per L2 segment.           |
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
   Boot. The site's own DHCP server hands out the IP. The `pxe` role answers as ProxyDHCP with
   option 66 and 67 for TFTP, or a URL in option 67 for HTTP boot. Option 93 carries the client
   architecture and selects the boot file variant.
2. The firmware loads `bootmgfw.efi`, which reads `BCD`, `boot.sdi` and `boot.wim` over TFTP or
   HTTP.
3. WinPE starts, `startnet.cmd` launches `DDT.Agent`, which registers with the `web` role over
   HTTPS using its MAC addresses and SMBIOS UUID, receives a task sequence, executes it, streams
   log lines and progress, and survives reboots by persisting state to the local disk.
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
  DDT.Server.Tests/
  DDT.Agent.Tests/
  DDT.E2E/                 Hyper-V driven, excluded from the default test run
build/
  Dockerfile
  compose.yaml
```

`DDT.Pxe` is a class library of hosted services, not a separate executable. There is one image with
one entry point, and `DDT:Roles` decides what runs inside it.

## Prerequisites

- .NET SDK 10.0.201 or a later 10.0 feature band
- Node.js 24 LTS (see `src/DDT.Web/.nvmrc`). Node 25 works but is outside the engine ranges
  declared by Vitest and jsdom, and that line is no longer maintained
- Docker, for the container build and for the Postgres integration tests
- Visual Studio C++ build tools, to publish `DDT.Agent` with NativeAOT
- The Windows ADK with the WinPE add-on, for the `builder` role from M2 onwards

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

## Status

M0, the scaffold, is complete. Later milestones, in order: the DHCP and TFTP protocol layer, the
PXE role and boot image builder, agent registration, the image library and apply, task sequences,
Linux raw disk images, and the task sequence flow builder.

Authentication is not implemented yet and arrives with the UI milestone. It will cover local
accounts and LDAP against a directory such as Active Directory, with third-party logins as a
possible later addition. Until then the API is unauthenticated and DDT must not be exposed to an
untrusted network.

Two open questions are already known and are recorded here so they are not rediscovered:

- Whether a stock WinPE image contains `ucrtbase.dll`. The published agent imports
  `api-ms-win-crt-*`, which resolve to the UCRT. This has not been verified against a real WinPE
  image because the ADK is not installed on the development machine. If it turns out to be absent,
  the options are statically linking the CRT or adding a WinPE optional component.
- Whether `wimgapi.dll`, which would let the agent apply images without shipping wimlib, is present
  in the WinPE base image and can apply a solid-compressed ESD.
