<picture>
  <source media="(prefers-color-scheme: dark)" srcset="src/DDT.Design/brand/readme-banner-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="src/DDT.Design/brand/readme-banner-light.png">
  <img src="src/DDT.Design/brand/readme-banner-light.png" alt="DDT logo over screens of the machine console" width="100%">
</picture>

<div align="center">

# DDT, the Davicloud Deployment Toolkit

**Bare metal in, finished Windows out. Self-hosted, open source, and friends with Secure Boot.**

[![CI](https://github.com/Davicloud-NET/DDT/actions/workflows/ci.yml/badge.svg)](https://github.com/Davicloud-NET/DDT/actions/workflows/ci.yml)
[![Licence: GPL-3.0-or-later](https://img.shields.io/badge/licence-GPL--3.0--or--later-blue)](LICENSE)

</div>

Microsoft retired the Deployment Toolkit in January 2026. DDT is here to take its place: a
self-hosted replacement for MDT, made for networks of 10 to 100 machines, even when they are spread
over a few sites and a VPN.

Plug a PC into the network and let it netboot. DDT takes it from there: it starts Windows PE, runs
a task sequence that partitions the disk and applies Windows (or writes a Linux image), carries on
inside the freshly installed Windows, and reports every step live to your browser. You get to drink
your coffee.

> [!NOTE]
> **There is no documentation yet.** Real documentation is on its way: a quick start and a guide
> for MDT users arrive with the one-command installer in M6.6, everything else with M12. Until
> then, this page is the tour. Need the nitty-gritty right now? The
> [old README](https://github.com/Davicloud-NET/DDT/blob/b8f9f1a2a686a03ec450a5d0ff82b211d57a3ab3/README.md)
> still lives in the history: 2,346 lines of very thorough, very robotic prose. You have been warned.

## What it does

- **Netboots with what you already have.** PXE with ProxyDHCP and TFTP, right next to the DHCP
  server you run today. No custom bootloader and no switching Secure Boot off: DDT serves
  Microsoft's own signed boot manager from the Windows ADK and does its magic after that.
- **Runs task sequences.** Partition, apply an image, inject drivers, write the answer file, run
  scripts, join a domain, and restart as often as it takes, in Windows PE and in the installed
  Windows. You draw them as a flow, with IF branches, loops, groups and pauses, and conditions on the
  machine's model, memory, TPM or network. Rules pick the sequence for a machine and fill in values
  such as its name or time zone, a sequence can ask its questions on the web or at the machine, and
  a step can use an account whose password its script never sees.
- **Does Linux, too.** Raw disk images (raw, gzip, zstd, xz or qcow2) with a cloud-init seed,
  written from the very same Windows PE. One agent, one boot path.
- **Has a web UI that stays live.** Changes show up the moment they happen, no reload button
  required. Light and dark, English and German, and it fits on a phone, for approving a machine
  from the couch.
- **Puts a console at the machine.** A graphical console in Windows PE lets a technician sign in,
  pick a sequence and watch it run. Shift+F10 still gets you a command prompt.
- **Is secure by default.** HTTPS only, with DDT's own root certificate. A machine gets no image,
  package or secret until it is authorized: by someone signing in at it, by an approval on the web,
  or by a zero-touch network you chose. Local accounts, LDAP and OpenID Connect, two-factor
  sign-in, roles and API tokens.
- **Keeps the boot image lean.** About 305 MB, roughly a third smaller than a typical MDT
  LiteTouchPE.
- **Ships as one container.** The web UI, the API and the netboot services in one image, with
  PostgreSQL next to it.

## How DDT is built

A lot of DDT's source code, including the tests, is written using AI, more specifically Claude Opus
and Fable. I exclusively decide what DDT actually does, how it works, what it will look like, its
security guidelines and its roadmap. I plan each milestone, review every code change and read all
documentation changes, and test every feature and system on virtual and physical hardware before
pushing it.

The repository also uses automated bots to help me manage this big project. All pull requests are
appropriately reviewed by a human before merging.

The logo and any product art included inside of this repository are fully drawn by humans or
sourced from humans. Once the CLA is in place, AI assistance in PRs is fully welcome under the rules
of [CONTRIBUTING.md](CONTRIBUTING.md)!

Who helps with what:

| Helper | What it does here |
|---|---|
| Claude Code, with Claude Opus and Claude Fable | Writes much of the code and the tests |
| Dependabot | Opens pull requests for package updates |
| GitHub code scanning and Copilot Autofix | Looks for security issues and suggests fixes |
| CodeRabbit | Reviews pull requests |

## Where it's at

DDT is young and has no release yet. It has deployed Windows and Linux to Hyper-V test machines and
netbooted real hardware, but treat it as a preview for now.

| | Milestone | |
|---|---|---|
| M0 to M4 | The foundations: sign-in, PXE and TFTP, the agent, images and deployment | ✅ Done |
| M5 | Task sequences, in Windows PE and in the installed Windows | ✅ Done |
| M6 | Linux raw disk images | ✅ Done |
| M6.5 | The real UI: a new web UI, the graphical console, settings on web pages | ✅ Done |
| M6.6 | Installing DDT with one command, on Windows Server and on Linux | 🔜 Next |
| M7 | A node-based flow builder for task sequences | Planned |
| M8 | Runs that carry on inside the installed Linux | Planned |
| M9 | Applications and Windows configuration | Planned |
| M10 | Golden images and the machine lifecycle | Planned |
| M11 | Beyond netboot and beyond a single site | Planned |
| M12 | Documentation, the proper kind | Planned |

The [roadmap](docs/roadmap.md) has the details, and what is not planned at all.

## Building it yourself

There is no installer yet (that is M6.6), so for now DDT is built from source. You will want:

- the .NET SDK 10.0.201 or a later 10.0 feature band, and Node.js 24 LTS;
- Docker, for the container and for the PostgreSQL tests;
- the Visual Studio C++ build tools, for the NativeAOT agent;
- the Windows ADK with its WinPE add-on, 10.1.26100.2454 or later, for the boot image.

Run the whole development stack, server and web UI, through Aspire. On its first start the server
writes the first administrator's password to `first-admin.txt` in its store, and logs where that is.

```bash
dotnet run --project src/DDT.AppHost
```

Or build the container and run it with PostgreSQL next to it:

```bash
docker build -f build/Dockerfile -t ddt:dev .
echo "DDT_DB_PASSWORD=$(openssl rand -hex 24)" > build/.env
docker compose -f build/compose.yaml up
```

Netbooting real machines needs a Linux host for the container, because Docker Desktop does not pass
DHCP broadcasts through, and a boot image built on Windows with `build/Build-BootImage.ps1`.
[CONTRIBUTING.md](CONTRIBUTING.md) says how to run the tests.

## Contributing

Bug reports and feature ideas are very welcome as
[issues](https://github.com/Davicloud-NET/DDT/issues). Pull requests from outside have to wait for
the contributor licence agreement, which is in the works. [CONTRIBUTING.md](CONTRIBUTING.md) has
the details. Found a security hole? Please report it privately, as [SECURITY.md](SECURITY.md)
explains. Everyone who takes part follows the [code of conduct](CODE_OF_CONDUCT.md).

## Licence

DDT is free software under the [GNU General Public License, version 3 or later](LICENSE), with a
few additional terms under its section 7 in [NOTICE](NOTICE). In short, if you pass on DDT or a
work based on it:

- keep the attribution "DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud." and
  the copyright notices intact, including in the legal notices the program shows;
- do not present it as your own work;
- mark a modified version as modified.

Running DDT inside your own organisation, changed or not, comes with no duties at all.
Distributing it, as source, as a container image, as an agent or in a boot image, means passing on
its source code and its notices: LICENSE, NOTICE, [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
and `licenses/`. NOTICE also permits conveying the console together with Avalonia, which contains
code under a licence the GPL does not otherwise get along with. The Windows PE and ADK files in a
boot image are Microsoft's: you bring them from your own ADK, and DDT's licence does not cover them.
