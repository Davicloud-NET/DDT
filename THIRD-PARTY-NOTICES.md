# Third-party notices

## DDT itself

DDT, the Davicloud Deployment Toolkit, is Copyright (C) 2026 Davicloud. It is free software under
the GNU General Public License, version 3 or later, with additional terms under section 7 of that
licence. [LICENSE](LICENSE) holds the licence text. [NOTICE](NOTICE) holds the attribution notice,
the warranty disclaimer and the additional terms.

DDT's built artefacts also contain software by others under their own licences. This file lists it
for each artefact, with its copyright holders and licence, and [licenses/](licenses) holds the
licence texts. The container image carries LICENSE, NOTICE, this file and `licenses/` in
`/app/legal`, and the web UI's About page links to them. The agent carries LICENSE, NOTICE, this
file, `licenses/dotnet` and `licenses/wimlib`, and prints them with `ddt-agent --licenses`. The web
bundle alone holds no licence files; its licences are in `licenses/web`, which the About page links
to. Pass LICENSE, NOTICE, this file and `licenses/` on with any artefact you distribute.

`src/DDT.ServiceDefaults/Extensions.cs` is code from the .NET Aspire ServiceDefaults project
template, Copyright (c) .NET Foundation and Contributors, under the MIT licence in
[licenses/dotnet/LICENSE.TXT](licenses/dotnet/LICENSE.TXT). It is compiled into the server.

## The server: the container image, `/app`

`build/Dockerfile` publishes the server into `/app`. Besides DDT's own assemblies and the web UI
bundle described in the next section, `/app` holds the NuGet packages of the server's runtime
closure, grouped here by the project they come from.

`/app/legal` holds LICENSE, NOTICE, this file and `licenses/`. The server serves them without
sign-in at `/api/about/legal/<path>`, for example `/api/about/legal/LICENSE`.

### Microsoft libraries

MIT licence, text in [licenses/dotnet/LICENSE.TXT](licenses/dotnet/LICENSE.TXT). The packages give
their copyright as Microsoft Corporation, and that licence file as the .NET Foundation and
Contributors, with the same permission text.

- ASP.NET Core: `Microsoft.AspNetCore.Authentication.OpenIdConnect` and
  `Microsoft.AspNetCore.Identity.EntityFrameworkCore`. The code by others in them is listed in
  [licenses/aspnetcore/THIRD-PARTY-NOTICES.TXT](licenses/aspnetcore/THIRD-PARTY-NOTICES.TXT),
  the notices file both packages carry, taken from version 10.0.12.
- .NET runtime libraries: `System.DirectoryServices.Protocols`,
  `System.Security.Cryptography.Pkcs`, `Microsoft.Bcl.Cryptography` and
  `Microsoft.Extensions.DependencyModel`. The code by others in them is listed in
  [licenses/dotnet/THIRD-PARTY-NOTICES.TXT](licenses/dotnet/THIRD-PARTY-NOTICES.TXT), the
  notices file of the .NET runtime, which all four packages carry. For
  `System.DirectoryServices.Protocols` it includes the notice for ldap4net.
- Entity Framework Core: `Microsoft.EntityFrameworkCore`,
  `Microsoft.EntityFrameworkCore.Abstractions`, `Microsoft.EntityFrameworkCore.Relational`,
  `Microsoft.EntityFrameworkCore.Sqlite.Core` and `Microsoft.Data.Sqlite.Core`.
- .NET extensions: `Microsoft.Extensions.AmbientMetadata.Application`,
  `Microsoft.Extensions.Compliance.Abstractions`,
  `Microsoft.Extensions.DependencyInjection.AutoActivation`,
  `Microsoft.Extensions.Diagnostics.ExceptionSummarization`,
  `Microsoft.Extensions.Http.Diagnostics`, `Microsoft.Extensions.Http.Resilience`,
  `Microsoft.Extensions.Resilience`, `Microsoft.Extensions.ServiceDiscovery`,
  `Microsoft.Extensions.ServiceDiscovery.Abstractions`, `Microsoft.Extensions.Telemetry` and
  `Microsoft.Extensions.Telemetry.Abstractions`.
- IdentityModel: `Microsoft.IdentityModel.Abstractions`, `Microsoft.IdentityModel.JsonWebTokens`,
  `Microsoft.IdentityModel.Logging`, `Microsoft.IdentityModel.Protocols`,
  `Microsoft.IdentityModel.Protocols.OpenIdConnect`, `Microsoft.IdentityModel.Tokens` and
  `System.IdentityModel.Tokens.Jwt`.

### Npgsql

`Npgsql` and `Npgsql.EntityFrameworkCore.PostgreSQL`, Copyright The Npgsql Development Team, under
the PostgreSQL License. Text: [licenses/npgsql/LICENSE](licenses/npgsql/LICENSE), the Npgsql
project's licence file. The packages carry none.

### OpenTelemetry

`OpenTelemetry`, `OpenTelemetry.Api`, `OpenTelemetry.Api.ProviderBuilderExtensions`,
`OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Extensions.Hosting`,
`OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http` and
`OpenTelemetry.Instrumentation.Runtime`, Copyright The OpenTelemetry Authors, under the Apache
License, version 2.0. Text:
[licenses/opentelemetry/LICENSE.TXT](licenses/opentelemetry/LICENSE.TXT), which every one of these
packages carries. The code by others in them, from .NET and from gRPC for
.NET, is listed in
[licenses/opentelemetry/THIRD-PARTY-NOTICES.TXT](licenses/opentelemetry/THIRD-PARTY-NOTICES.TXT).
Both files are taken from the OpenTelemetry 1.18.0 package. None of the packages has a NOTICE file.

### Polly

`Polly.Core`, `Polly.Extensions` and `Polly.RateLimiting`, Copyright App vNext, under the BSD
3-Clause License. Text: [licenses/polly/LICENSE](licenses/polly/LICENSE). The 8.4.2 packages the
server uses carry no licence file and declare BSD-3-Clause with "Copyright (c) 2024, App vNext";
the text is Polly's own licence file as Polly.Core 8.6.6 carries it.

### ZstdSharp and Zstandard

`ZstdSharp.Port`, Copyright (c) 2021 Oleg Stepanischev, under the MIT licence, text in
[licenses/zstd/ZstdSharp-LICENSE](licenses/zstd/ZstdSharp-LICENSE), taken from the ZstdSharp
repository at the commit version 0.8.8 was built from, because the package carries none. It is a
port of Zstandard 1.5.7 to C#, Copyright (c) Meta Platforms, Inc. and affiliates, which Meta offers
under the BSD licence or the GNU General Public License, version 2; DDT uses it under the BSD
licence in [licenses/zstd/LICENSE](licenses/zstd/LICENSE). The server compresses raw disk images
with it.

### SQLitePCLRaw and SQLite

`SQLitePCLRaw.bundle_e_sqlite3`, `SQLitePCLRaw.core`, `SQLitePCLRaw.lib.e_sqlite3` and
`SQLitePCLRaw.provider.e_sqlite3`, Copyright SourceGear, LLC, under the Apache License, version
2.0. Text: [licenses/sqlitepclraw/LICENSE.TXT](licenses/sqlitepclraw/LICENSE.TXT) and
[licenses/sqlitepclraw/NOTICE.TXT](licenses/sqlitepclraw/NOTICE.TXT), both from the SQLitePCL.raw
project, because the packages carry none. `SQLitePCLRaw.lib.e_sqlite3` contains the native library
`libe_sqlite3.so`, which is SQLite. The NOTICE file carries SQLite's public domain dedication and
the notice for code by Microsoft Open Technologies.

## The web UI bundle, `/app/wwwroot`

Vite builds `src/DDT.Web` into the static files the server serves.
[licenses/web/THIRD-PARTY-LICENSES.txt](licenses/web/THIRD-PARTY-LICENSES.txt) holds, for every
package in the non-development closure of `src/DDT.Web/package-lock.json`, its licence text with
its copyright line, followed by the two build tools whose own code the bundle contains. The build
leaves some of these packages out of the bundle, such as the type declarations and the Node.js
dependencies of `@microsoft/signalr`, so the file covers more than the bundle holds.
`@microsoft/signalr`, `react-remove-scroll-bar` and `tr46` carry no licence file, and their
entries say where their text comes from.

- Meta Platforms, Inc. and affiliates, MIT: `react`, `react-dom`, `scheduler` and
  `use-sync-external-store`.
- WorkOS, MIT: `@radix-ui/primitive`, `@radix-ui/react-compose-refs`, `@radix-ui/react-context`,
  `@radix-ui/react-dialog`, `@radix-ui/react-dismissable-layer`, `@radix-ui/react-focus-guards`,
  `@radix-ui/react-focus-scope`, `@radix-ui/react-id`, `@radix-ui/react-portal`,
  `@radix-ui/react-presence`, `@radix-ui/react-primitive`, `@radix-ui/react-slot`,
  `@radix-ui/react-use-callback-ref`, `@radix-ui/react-use-controllable-state`,
  `@radix-ui/react-use-effect-event` and `@radix-ui/react-use-layout-effect`.
- Tanner Linsley, MIT: `@tanstack/history`, `@tanstack/query-core`, `@tanstack/react-query`,
  `@tanstack/react-router`, `@tanstack/react-store`, `@tanstack/router-core` and
  `@tanstack/store`.
- Anton Korzunov, MIT: `aria-hidden`, `get-nonce`, `react-remove-scroll`,
  `react-remove-scroll-bar`, `react-style-singleton`, `use-callback-ref` and `use-sidecar`.
- .NET Foundation and Contributors, MIT: `@microsoft/signalr`.
- Microsoft Corporation: `tslib` under the BSD Zero Clause License, and `@types/react` and
  `@types/react-dom` under MIT.
- Alexis Munsayac, MIT: `seroval` and `seroval-plugins`.
- Unshift.io, Arnout Kazemier and contributors, MIT: `querystringify`, `requires-port` and
  `url-parse`.
- Toru Nagashima, MIT: `abort-controller` and `event-target-shim`.
- Sebastian Mayr, MIT: `tr46` and `whatwg-url`.
- MIT, one package each: `cookie-es` (Pooya Parsa, Roman Shtylman, Douglas Christopher Wilson),
  `csstype` (Fredrik Nicol), `detect-node-es` (Ilya Kantor), `eventsource` (EventSource GitHub
  organisation), `node-fetch` (David Frank), `psl` (Lupo Montero), `punycode` (Mathias Bynens),
  `set-cookie-parser` (Nathan Friedly), `universalify` (Ryan Zimmerman) and `ws` (Einar Otto
  Stangvik).
- Other licences: `tough-cookie` (BSD 3-Clause, Salesforce.com, Inc.), `webidl-conversions`
  (BSD 2-Clause, Domenic Denicola), and `fetch-cookie` and `isbot`, which their authors dedicate
  to the public domain under the Unlicense.
- Build tools, MIT: `rolldown` (VoidZero Inc. and Contributors, with parts derived from Rollup and
  from esbuild by Evan Wallace) adds its helpers for CommonJS interoperability and the module
  preload polyfill, and `vite` (VoidZero Inc. and Vite contributors) asks for that polyfill.

## The agent, `ddt-agent.exe`

`build/Publish-Agent.ps1` publishes the agent as one NativeAOT executable. Besides DDT's own code,
from `DDT.Agent`, `DDT.Core` and `DDT.Contracts`, it contains:

- The .NET runtime and libraries, compiled ahead of time by
  `runtime.win-x64.Microsoft.DotNet.ILCompiler` from
  `Microsoft.NETCore.App.Runtime.NativeAOT.win-x64`. Copyright (c) .NET Foundation and
  Contributors, MIT, text in [licenses/dotnet/LICENSE.TXT](licenses/dotnet/LICENSE.TXT). The code
  by others in the runtime is listed in
  [licenses/dotnet/THIRD-PARTY-NOTICES.TXT](licenses/dotnet/THIRD-PARTY-NOTICES.TXT), the notices
  file of runtime.win-x64.Microsoft.DotNet.ILCompiler 10.0.12, the version the agent is compiled
  with.
- Startup code from the Microsoft Visual C++ runtime, which the linker adds. It is Microsoft's and
  comes with the Visual C++ build tools the agent is published with.
- libwim, described next.

The agent also carries LICENSE, NOTICE, this file and the texts in `licenses/dotnet` and
`licenses/wimlib`. It prints its legal notices at start-up, and `ddt-agent --licenses` prints these
texts.

### wimlib (libwim)

The agent carries `libwim-15.dll` from wimlib 1.14.4 inside the executable. It loads the library
at run time from a file next to itself, as a separate library; DDT does not link it statically.

- Copyright 2012-2023 Eric Biggers, https://wimlib.net.
- Licence: wimlib offers libwim under the GNU Lesser General Public License, version 3 or later,
  for Windows builds, and DDT uses it under that licence. See
  [licenses/wimlib/COPYING](licenses/wimlib/COPYING),
  [licenses/wimlib/COPYING.LGPLv3](licenses/wimlib/COPYING.LGPLv3) and
  [licenses/wimlib/COPYING.GPLv3](licenses/wimlib/COPYING.GPLv3), which the LGPL incorporates.
- Parts under other licences: the 20 source files of libwim whose headers put them under the MIT
  licence are listed with their copyright lines in
  [licenses/wimlib/COPYING.MIT](licenses/wimlib/COPYING.MIT). One of them, `src/divsufsort.c`, is
  libdivsufsort-lite, Copyright (c) 2003-2008 Yuta Mori, whose notice is also in
  [licenses/wimlib/COPYING.libdivsufsort-lite](licenses/wimlib/COPYING.libdivsufsort-lite). The
  DLL is built with MinGW-w64 and statically linked against its runtime, whose notices are in
  [licenses/wimlib/COPYING.MinGW-w64-runtime.txt](licenses/wimlib/COPYING.MinGW-w64-runtime.txt).
- The binary is the native asset of the `ManagedWimLib` NuGet package, version 2.6.0, by Hajin
  Jang. The agent takes only that DLL from the package and none of ManagedWimLib's own code. It was
  built from wimlib 1.14.4 without NTFS-3G by `native/windows/wimlib-msys2-v1.14.x.sh` at
  ManagedWimLib commit `b2e719d478b148607778d791bf6561e1dab0b2f4`:
  https://github.com/ied206/ManagedWimLib/blob/b2e719d478b148607778d791bf6561e1dab0b2f4/native/windows/wimlib-msys2-v1.14.x.sh
- SHA-256 of `libwim-15.dll`:
  `592431ef475a2093f0d8fb60db532e6b5cc31760084f4fc1f47abb0a58502328`.
- Source: https://wimlib.net/downloads/wimlib-1.14.4.tar.gz, SHA-256
  `3633db2b6c8b255eb86d3bf3df3059796bd1f08e50b8c9728c7eb66662e51300`, also tagged `v1.14.4` at
  https://github.com/ebiggers/wimlib.

To run the agent with a modified libwim, build `libwim-15.dll` from that source with your
changes, then use it in one of two ways:

1. Put it next to the agent. The agent uses a `libwim-15.dll` that is already in its folder
   instead of overwriting it. When there is none, it writes its own copy there. When the file is
   identical to its own copy, it uses it. When the file is different, it keeps and uses it, and
   logs one line that names both SHA-256 values and says that deleting the file makes the agent
   use its own copy. For a boot image, pass the DLL to `build/Build-BootImage.ps1` with
   `-WimLibraryPath`, which copies it to `X:\DDT\libwim-15.dll`, next to the agent. An agent that
   updates itself writes the new agent into the same folder, so the updated agent uses the same
   DLL. That works across wimlib releases as long as the library is still `libwim-15`, because
   wimlib changes that number when it breaks compatibility.
2. Rebuild the agent around it. Point the `EmbeddedResource` in `src/DDT.Agent/DDT.Agent.csproj`
   at your DLL and publish the agent again with `build/Publish-Agent.ps1`. The agent then carries
   your DLL and, as under 1, writes it out wherever no `libwim-15.dll` is next to it yet.

## Windows PE and the Windows ADK

A boot image built by `build/Build-BootImage.ps1` consists mostly of files from Windows PE and the
Windows ADK: `boot.wim` with Windows PE, the boot managers, `boot.sdi`, `boot.stl` and the boot
fonts. Those files are Microsoft's. Whoever builds the boot image supplies them from their own ADK
installation under Microsoft's licence terms. DDT does not distribute them, and DDT's licence does
not cover them. DDT adds `ddt-agent.exe`, `agent.json` and `startnet.cmd`, and with
`-WimLibraryPath` a `libwim-15.dll`.

## The container base image

`build/Dockerfile` builds on `mcr.microsoft.com/dotnet/aspnet:10.0-noble`, which is Ubuntu 24.04
with the .NET and ASP.NET Core runtimes, and adds `libldap2`, `qemu-utils` and `xz-utils` from
Ubuntu's archive together with the packages they depend on, such as the Cyrus SASL library and
GLib. None of this is DDT's. Each Ubuntu
package carries its copyright and licence in `/usr/share/doc/<package>/copyright`, and the .NET
runtimes carry theirs in `/usr/share/dotnet`, in `LICENSE.txt` and `ThirdPartyNotices.txt`. Their
source comes from Ubuntu (`apt-get source <package>`, or https://launchpad.net/ubuntu) and from
Microsoft (https://github.com/dotnet/dotnet, with the image definition at
https://github.com/dotnet/dotnet-docker). Distributing the image distributes these packages too,
under their own licences.
