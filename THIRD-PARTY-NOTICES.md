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
file, `licenses/dotnet`, `licenses/wimlib` and `licenses/zstd`, and prints them with
`ddt-agent --licenses`. The console in Windows PE carries LICENSE, NOTICE and the licence texts of
what it contains, and shows them on its licences view. The web bundle alone holds no licence files;
its licences are in `licenses/web`, which the About page links to. Pass LICENSE, NOTICE, this file
and `licenses/` on with any artefact you distribute.

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
its copyright line, followed by the build tools whose own code the bundle contains. `npm run
licences` in `src/DDT.Web` writes it. The build leaves many of these packages out of the bundle,
such as the type declarations, the Node.js dependencies of `@microsoft/signalr`, and the Babel and
Jest packages that Lingui's runtime declares for its macros and its configuration, so the file
covers more than the bundle holds. `@microsoft/signalr`, `client-only` and `tr46` carry no licence
file, and their entries say where their text comes from.

Two of the web UI's components, the table and the drawer, are adapted from Untitled UI React,
Copyright (c) 2025 Untitled UI, under the MIT licence in
[licenses/untitledui/LICENSE](licenses/untitledui/LICENSE). Each adapted file says so under its
licence header. Untitled UI's icon package is not used: its licence file forbids distributing the
icons.

- Meta Platforms, Inc. and affiliates, MIT: `react`, `react-dom`, `scheduler` and
  `use-sync-external-store`; with Facebook, Inc. and its affiliates, `react-is`. Their Jest
  packages, MIT, reach the closure through Lingui's configuration and not the bundle:
  `@jest/schemas`, `@jest/types`, `jest-get-type`, `jest-validate` and `pretty-format`.
- Adobe, from the React Spectrum project, Apache License 2.0: `react-aria`, `react-aria-components`, `react-stately`,
  `@internationalized/date`, `@internationalized/number`, `@internationalized/string` and
  `@react-types/shared`.
- Tanner Linsley, MIT: `@tanstack/history`, `@tanstack/query-core`, `@tanstack/react-query`,
  `@tanstack/react-router`, `@tanstack/react-store`, `@tanstack/router-core` and
  `@tanstack/store`.
- Tomáš Ehrlich and Crowdin, MIT: `@lingui/core`, `@lingui/react`, `@lingui/message-utils`,
  `@lingui/conf` and `@lingui/babel-plugin-lingui-macro`. OpenJS Foundation and contributors,
  MIT: `@messageformat/date-skeleton` and `@messageformat/parser`. Tim Radvan, BSD 3-Clause:
  `moo`.
- Paweł Kuna, MIT: `@tabler/icons` and `@tabler/icons-react`, the icons.
- Dany Castillo, MIT: `tailwind-merge`.
- Project Nayuki and Anthony Fu, MIT: `uqr`, which draws the QR code for setting up an authenticator.
- Fonts under the SIL Open Font License 1.1: `@fontsource-variable/archivo` (The Archivo Project
  Authors) and `@fontsource-variable/martian-mono` (The Martian Mono Project Authors).
- .NET Foundation and Contributors, MIT: `@microsoft/signalr`.
- Microsoft Corporation: `tslib` under the BSD Zero Clause License; and under MIT the type
  declarations `@types/istanbul-lib-coverage`, `@types/istanbul-lib-report`,
  `@types/istanbul-reports`, `@types/node`, `@types/yargs` and `@types/yargs-parser`.
- Babel, MIT (Sebastian McKenzie and other contributors, the parser by various contributors),
  which Lingui's runtime declares for its macros: `@babel/code-frame`, `@babel/compat-data`,
  `@babel/core`, `@babel/generator`, `@babel/helper-compilation-targets`, `@babel/helper-globals`,
  `@babel/helper-module-imports`, `@babel/helper-module-transforms`,
  `@babel/helper-string-parser`, `@babel/helper-validator-identifier`,
  `@babel/helper-validator-option`, `@babel/helpers`, `@babel/parser`, `@babel/template`,
  `@babel/traverse` and `@babel/types`. With them, under MIT unless named: `@jridgewell/gen-mapping`,
  `@jridgewell/remapping`, `@jridgewell/resolve-uri`, `@jridgewell/sourcemap-codec` and
  `@jridgewell/trace-mapping` (Justin Ridgewell); `browserslist` and `update-browserslist-db`
  (Andrey Sitnik); `baseline-browser-mapping` (Apache License 2.0, the web-platform-dx project);
  `caniuse-lite` (Creative Commons Attribution 4.0, Ben Briggs, with the data of caniuse.com); `electron-to-chromium` (ISC,
  Kilian Valkhof); `node-releases` (Sergey Rubanov); `lru-cache`, `semver` and `yallist` (ISC, Isaac
  Z. Schlueter and contributors); `gensync` (Logan Smyth); `json5` (Aseem Kishore and others);
  `jsesc` (Mathias Bynens); `js-tokens` (Simon Lydell); `convert-source-map` (Thorsten Lorenz);
  `debug` (TJ Holowaychuk); `ms` (Vercel, Inc.); `escalade` (Luke Edwards); `picocolors` (ISC,
  Oleksii Raspopov, Kostiantyn Denysov, Anton Verinov); `jiti` (Pooya Parsa); `lilconfig` (Anton
  Kastritskiy); `normalize-path` (Jon Schlinkert); `camelcase`, `ansi-styles`, `chalk`, `has-flag`,
  `leven` and `supports-color` (Sindre Sorhus); `color-convert` (Heather Arthur); `color-name`
  (Dmitry Ivanov); `@sinclair/typebox` (Haydn Paterson); and `@swc/helpers` (Apache License 2.0,
  the SWC project).
- Alexis Munsayac, MIT: `seroval` and `seroval-plugins`.
- Unshift.io, Arnout Kazemier and contributors, MIT: `querystringify`, `requires-port` and
  `url-parse`.
- Toru Nagashima, MIT: `abort-controller` and `event-target-shim`.
- Sebastian Mayr, MIT: `tr46` and `whatwg-url`.
- MIT, one package each: `aria-hidden` (Anton Korzunov), `client-only` (no holder named),
  `clsx` (Luke Edwards), `cookie-es` (Pooya Parsa), `eventsource` (EventSource GitHub
  organisation), `js-sha256` (Chen, Yi-Cyuan), `node-fetch` (David Frank), `psl` (Lupo Montero),
  `punycode` (Mathias Bynens), `set-cookie-parser` (Nathan Friedly), `undici-types` (Matteo Collina
  and Undici contributors), `universalify` (Ryan Zimmerman) and `ws` (Einar Otto Stangvik).
- Other licences: `tough-cookie` (BSD 3-Clause, Salesforce.com, Inc.), `webidl-conversions`
  (BSD 2-Clause, Domenic Denicola), and `fetch-cookie` and `isbot`, which their authors dedicate
  to the public domain under the Unlicense.
- Build tools, MIT: `rolldown` (VoidZero Inc. and Contributors, with parts derived from Rollup and
  from esbuild by Evan Wallace) adds its helpers for CommonJS interoperability and the module
  preload polyfill, `vite` (VoidZero Inc. and Vite contributors) asks for that polyfill, and
  `tailwindcss` (Tailwind Labs, Inc.) writes its base styles and the utility classes the web UI
  uses into the stylesheet.

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
- `ZstdSharp.Port` 0.8.8 and the Zstandard 1.5.7 code it is ported from, as described for the server
  above, which the agent unpacks raw disk images with.
- libwim, described next.

The agent also carries LICENSE, NOTICE, this file and the texts in `licenses/dotnet`,
`licenses/wimlib` and `licenses/zstd`. It prints its legal notices at start-up, and
`ddt-agent --licenses` prints these texts.

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

## The console in Windows PE, `ddt-console.exe`

`build/Publish-Console.ps1` publishes the graphical console the agent starts in Windows PE as one
NativeAOT executable, `ddt-console.exe`, with the two native libraries it draws with,
`libSkiaSharp.dll` and `libHarfBuzzSharp.dll`, next to it. Besides DDT's own code, from
`DDT.MachineConsole` and `DDT.ConsoleProtocol`, they contain:

- The .NET runtime and libraries and the Visual C++ startup code, as described for the agent above.
- Avalonia, the UI framework: `Avalonia`, `Avalonia.HarfBuzz`, `Avalonia.Remote.Protocol`,
  `Avalonia.Skia`, `Avalonia.Themes.Simple` and `Avalonia.Win32`, version 12.1.3, Copyright (c)
  AvaloniaUI OÜ, under the MIT licence in [licenses/avalonia/LICENSE.md](licenses/avalonia/LICENSE.md).
  Code by others in Avalonia, from WPF, WinUI, Mono, the Silverlight Toolkit (under the Microsoft
  Public License), RichTextKit (under the Apache License, version 2.0) and others, is listed in
  [licenses/avalonia/NOTICE.md](licenses/avalonia/NOTICE.md). Both files are taken from the
  Avalonia repository at the commit 12.1.3 was built from,
  `8eeda4f6f546165b3f72e63c9f42247abb306905`, because the packages carry none. The Microsoft
  Public License is incompatible with the GPL, so [NOTICE](NOTICE) grants, under section 7 of the
  GPL, the additional permission to convey DDT's console combined with Avalonia.
- `MicroCom.Runtime` 0.11.6, which Avalonia calls COM with, Copyright (c) 2021 Nikita Tsukanov,
  under the MIT licence in [licenses/microcom/LICENSE](licenses/microcom/LICENSE), taken from the
  MicroCom repository because the package carries none.
- `SkiaSharp` 3.119.4 and `HarfBuzzSharp` 8.3.1.3, the .NET bindings, and
  `SkiaSharp.NativeAssets.Win32` and `HarfBuzzSharp.NativeAssets.Win32`, whose
  `libSkiaSharp.dll` and `libHarfBuzzSharp.dll` are Skia and HarfBuzz built for Windows. The
  bindings are Copyright (c) 2015-2016 Xamarin, Inc. and Copyright (c) 2017-2018 Microsoft
  Corporation, under the MIT licence in [licenses/skiasharp/LICENSE.txt](licenses/skiasharp/LICENSE.txt).
  Skia is Copyright (c) 2011 Google Inc. under a BSD licence, HarfBuzz is under the "Old MIT"
  licence of its many authors, and the code by others that the native libraries contain, such as
  FreeType, libpng, zlib, libjpeg-turbo and libwebp, is listed with its licences in
  [licenses/skiasharp/THIRD-PARTY-NOTICES.txt](licenses/skiasharp/THIRD-PARTY-NOTICES.txt), the
  notices file both native packages carry.
- The fonts Archivo, Copyright 2020 The Archivo Project Authors, and Martian Mono, Copyright 2021
  The Martian Mono Project Authors, under the SIL Open Font License 1.1 in
  [licenses/fonts/Archivo-OFL.txt](licenses/fonts/Archivo-OFL.txt) and
  [licenses/fonts/MartianMono-OFL.txt](licenses/fonts/MartianMono-OFL.txt). The console carries
  static instances, one per weight and width it uses, cut from the variable fonts of Google Fonts by
  `src/DDT.MachineConsole/Assets/Fonts/cut_fonts.py`, which names the source commits and their
  SHA-256. They are Modified Versions in the terms of the licence and carry names of their own,
  such as "Archivo 750 62"; neither font declares a Reserved Font Name.

The console carries LICENSE, NOTICE and the texts in `licenses/avalonia`, `licenses/microcom`,
`licenses/skiasharp`, `licenses/dotnet` and `licenses/fonts`, and its licences view, on F3, shows
DDT's attribution notice, the list above and each of those texts.

## Windows PE and the Windows ADK

A boot image built by `build/Build-BootImage.ps1` consists mostly of files from Windows PE and the
Windows ADK: `boot.wim` with Windows PE, the boot managers, `boot.sdi`, `boot.stl` and the boot
fonts. Those files are Microsoft's. Whoever builds the boot image supplies them from their own ADK
installation under Microsoft's licence terms. DDT does not distribute them, and DDT's licence does
not cover them. DDT adds `ddt-agent.exe`, `agent.json` and `startnet.cmd`, with `-WimLibraryPath` a
`libwim-15.dll`, and with `-ConsolePath` the console's `ddt-console.exe`, `libSkiaSharp.dll` and
`libHarfBuzzSharp.dll`.

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
