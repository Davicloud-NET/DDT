# Contributing to DDT

Thank you for your interest in DDT! It is built by Davicloud, and this file says how to report a
bug, ask for a feature, and what a change has to pass. The [README](README.md) gives the tour and
says how DDT is built, [docs/roadmap.md](docs/roadmap.md) says what comes next and what is not
planned, and proper documentation arrives with M12. Everyone who takes part follows the
[code of conduct](CODE_OF_CONDUCT.md).

## Reporting a bug

Open an issue with the bug report form. A report that DDT can act on says:

- the commit you built DDT from, and where the server runs: the container image, or from source;
- the machine, if one is involved: its make and model, and whether Secure Boot is on;
- what happened and what you expected, and the steps that lead there;
- the machine's log from the web UI, and the host's log around the time. Until the documentation
  exists,
  [When a deployment goes wrong](https://github.com/Davicloud-NET/DDT/blob/b8f9f1a2a686a03ec450a5d0ff82b211d57a3ab3/README.md#when-a-deployment-goes-wrong)
  in the old README says where else to look.

DDT keeps passwords out of its logs, but a log still names servers, networks, machines and people.
Read it before you paste it, and replace what you do not want to publish.

A vulnerability is not a bug for the issue tracker. [SECURITY.md](SECURITY.md) says how to report
one privately.

## Asking for a feature

Open an issue with the feature request form, and say what you want to do rather than how DDT should
do it. Check [docs/roadmap.md](docs/roadmap.md) first: it may be planned already, or listed as not
planned, with the reason. If you are moving from the Microsoft Deployment Toolkit, name the MDT
feature you miss; the roadmap is ordered by what someone moving from MDT would miss.

## Pull requests

DDT takes no pull requests from outside contributors until its contributor licence agreement is in
place, which Davicloud is preparing. Until then a pull request from outside cannot be merged, so
please open an issue instead, describing the bug or the change you have in mind. This section will
say how to sign the agreement once it exists.

### AI assistance

Once the agreement is in place, pull requests written with the help of AI are welcome too, on the
same terms as any other:

- say which tool helped and with which parts in the template's AI assistance section, and leave
  out the footer a tool adds to the end of a pull request, such as "🤖 Generated with …";
- understand every line you submit, and be ready to explain it in the review;
- expect the same review and the same tests as any other change.

DDT itself is built this way, as the [README](README.md#how-ddt-is-built) says.

## Working on DDT

The rest of this file is what every change has to pass. The
[README](README.md#building-it-yourself) says how to build and run DDT, and
[docs/web-ui.md](docs/web-ui.md) how the web UI is built.

**Code follows [docs/code-style.md](docs/code-style.md).** It's the part no tool checks yet:
short comments that say why, one type per file, small classes and methods, and names that say
what things are. A pull request that doesn't follow it gets sent back for a tidy-up, however it
was written.

### The checks

The CI workflow in [.github/workflows/ci.yml](.github/workflows/ci.yml) runs on every push to
`master` and on every pull request. Each job runs what you can run on your own PC:

| Job | What it runs |
|---|---|
| Windows | `dotnet build DDT.slnx`, `dotnet format whitespace DDT.slnx --verify-no-changes`, `Invoke-ScriptAnalyzer -Path build -Recurse` from the PSScriptAnalyzer module, and `dotnet test --solution DDT.slnx -- --filter-not-trait "Category=E2E" --ignore-exit-code 8` |
| Linux | the tests of `DDT.Core`, `DDT.Protocols`, `DDT.Pxe` and `DDT.Server`, with PostgreSQL and SQL Server in Docker |
| End to end | `dotnet test --project tests/DDT.E2E`, which needs the Visual C++ build tools |
| Web client | from `src/DDT.Web`: `npm ci`, `npm run lint`, `npm run format`, `npm run build` and `npm test` |
| Web screenshots | from `src/DDT.Web`: `npm run screens`, in Microsoft Edge on Windows |
| Container image | `docker build -f build/Dockerfile .`, then the server started in the image |

The server's tests run on Linux as well as on Windows because the server runs on Linux in its
container. A few of them run only there, and the PostgreSQL tests run only where Docker runs Linux
containers. The SQL Server tests also run against LocalDB on Windows, or against the server that
`DDT_TEST_SQLSERVER` names with a connection string. To run the server's tests on Linux from a Windows PC, hand the last commit to the .NET
SDK container, which builds it in a folder of its own, so the `bin` and `obj` folders of the build
on Windows stay as they are:

```bash
git archive HEAD | docker run -i --rm mcr.microsoft.com/dotnet/sdk:10.0-noble bash -c "mkdir /src && cd /src && tar -x && dotnet build DDT.slnx -p:EnableWindowsTargeting=true && dotnet test --project tests/DDT.Server.Tests"
```

### What the tests hold you to

Several rules are enforced by tests rather than by review. When one of these tests fails, this is
what it asks for:

- **The licence header.** Every source file starts with the three lines of
  [NOTICE](NOTICE)'s attribution, as a comment in the file's language:

  ```
  // Copyright (C) 2026 Davicloud
  // SPDX-License-Identifier: GPL-3.0-or-later
  // Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.
  ```

  The build enforces it for C#, and `SourceHeaderTests` for TypeScript, JavaScript, CSS, XAML,
  PowerShell and Python.
- **Third-party notices.** A package whose code ends up in the server, the agent, the console or
  the web bundle is named in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), and its licence text
  is in [licenses/](licenses). For the web bundle, `npm run licences` writes
  `licenses/web/THIRD-PARTY-LICENSES.txt` from the installed packages. NuGet versions are set only
  in [Directory.Packages.props](Directory.Packages.props).
- **Generated files.** The web theme and the console's resources come from
  `src/DDT.Design/tokens.json` through `npm run tokens`. The web's catalog of server messages comes
  from the server's through its tests and `npm run messages`. Tests on both sides fail while a
  generated file is stale.
- **Both languages.** The web UI and the console speak English and German. After changing a text,
  run `npm run i18n` and translate the new German messages;
  [docs/web-ui.md](docs/web-ui.md#translations) has the terms to use.
- **Screenshots.** After a wanted change to a page that `npm run screens` checks, take the
  screenshots again with `npm run screens:update` on Windows, and commit them with the change.
- **Formatting.** `.editorconfig` sets the style, and warnings are errors. Files use LF line
  endings and UTF-8. Prettier formats the web client, `npm run format:write` applies it.

These rules are not tested, but hold just as firmly:

- **The code style.** [docs/code-style.md](docs/code-style.md), see above.
- **Settings belong in the web UI.** Configuration keeps only what the server needs before it can
  serve the web UI. Every other setting:
  1. is a field of a settings section, on the page of what it configures. Task sequences, drivers
     per model and assignment by MAC address or model are database entities with an API and a
     page, never settings;
  2. is a property of its section's option class with its default, a field of the section's
     definition in `DDT.Server/Settings`, a member of the section's record in
     `DDT.Contracts/Settings`, and covered by the section's validator. It is read from
     `DdtSettings.Current` where it is used, once per request or decision, never while the services
     are registered and never through an `IOptions<T>`. A secret is `[JsonIgnore]` in the option
     class and a secret field of the definition;
  3. reaches the agent as a new member of a server response, never through `agent.json`, which the
     server cannot rewrite;
  4. says why, when it is built into the boot image, it cannot come from the server;
  5. is bound to its destination, when it is a secret whose destination is configurable. A setting
     that grants roles or trust needs the administrator to prove who they are again before it
     changes.
- **Pages stay live.** An action patches the cached data with the API's answer, and changes made
  elsewhere arrive through the hub, without reloading the page or fetching everything again. See
  [docs/web-ui.md](docs/web-ui.md#live-data).

### Tests

A change in behaviour comes with a test of it. Tests that need an elevated prompt or real hardware
carry the trait `Category=E2E`, which keeps them out of the default run. The protocol tests use
packet fixtures in `tests/DDT.Protocols.Tests/Fixtures`, each with a note on where it came from;
a capture from real firmware always wins over a hand-made fixture.

### Commits

A commit makes one change, and its subject says in a plain sentence what the change does, in the
imperative and without a prefix, for example "Keep a disk's number on one line in the console's
machine panel". A body, where one is needed, says why.

## Licence

DDT is licensed under the GNU General Public License, version 3 or later, with additional terms
under its section 7 in [NOTICE](NOTICE). The README's [Licence](README.md#licence) section says
what that means for you.
