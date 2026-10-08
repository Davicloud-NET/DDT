# Installation

This is the plan for M6.6: installing DDT on a new server with one command, on Windows Server and on
Linux, so that a fresh install netboots its first machine without anyone building DDT. It was
written against commit d95105a, and every file:line reference points to that commit. Section 8 holds
the questions only the maintainer can answer; where this plan needs an answer, it names the
recommended one and marks it.

## 1. The goal

The maintainer's words: run a command and it is online, and DDT has to be extremely easy to adopt
for MDT shops.

**The measure.** From a freshly installed Windows Server to a netbooted machine deploying Windows 11
in under 30 minutes, of which the administrator spends a few on decisions. No compiler, no clone of
this repository, no file copied by hand, no configuration file edited. What only the administrator
can decide is asked on screen, at most five questions, each with an answer already chosen. The same
holds on a Linux host with Docker, except that the boot image needs a Windows PC with the ADK.

**Before a boot image is built.** Since M8 a release brings a Linux boot environment (sections 4.2
and 4.9), so a fresh server netboots machines without the ADK, without a build, and on a Linux host
without a Windows PC. Once a network card is chosen, such a server can:

- start a 64-bit UEFI machine from the network with PXE, also with Secure Boot on, where the
  firmware trusts one of Microsoft's third-party UEFI CAs, 2011 or 2023;
- register the machine, show it on the Machines page and let a technician sign in at its console,
  which is text in this environment;
- run a Linux sequence on it: write a raw disk image with its cloud-init seed and start the
  installed Linux.

It cannot:

- install Windows. Windows sequences run in Windows PE alone, and its boot image is built with the
  shop's own ADK (sections 4.7, 4.8 and 6);
- start a machine with Secure Boot on whose firmware trusts neither of them, a BIOS or 32-bit
  machine, or one that uses UEFI HTTP Boot;
- start any machine while a DHCP server names another boot file, or while the server's certificate
  is an administrator's own and not from DDT's root (section 4.9).

The measure above is about Windows and stands as it is.

**Who it is measured against.** An MDT shop runs a Windows Server with the Deployment Workbench, a
deployment share, the Windows ADK with its Windows PE add-on, and WDS for PXE. The Microsoft DHCP
server often runs on the same server or the one next to it, in an Active Directory domain, sometimes
with an enterprise CA. Its administrators know PowerShell, Group Policy and the Workbench. Many have
no Linux host and no Docker, and MDT itself was an MSI.

**What it is compared with.** DeployR Community from 2Pint Software is free, though not open
source, built by people who built MDT, and aimed at the same shops. Before its installer runs it
needs Windows Server, the ADK, PowerShell 7, three .NET runtimes and a TLS certificate; its database
is SQLite by default. DDT can do better on the rest of that list: a self-contained server that needs
no runtime, a certificate authority of its own (section 4.6), and the ADK installed for the
administrator where it is missing (section 4.7).

## 2. What a new install takes today

| # | Step | Why |
|---|---|---|
| 1 | A Linux host with Docker Engine | PXE needs host networking, which Docker Desktop does not deliver broadcasts to (README, Building and running) |
| 2 | Clone the repository and build the image | No release and no published image: CI builds the image and pushes nothing (.github/workflows/ci.yml:200) |
| 3 | Write `build/.env` with a database password, set `DDT_HOSTNAMES`, and `DDT_ROLES=web,pxe` | Compose refuses to start without the password, and runs only the web role unless told (build/compose.yaml:18,20,23) |
| 4 | Find the first administrator's password in the container log | It is logged once at warning level (src/DDT.Server/Authentication/IdentityBootstrap.cs:116-117) |
| 5 | Copy `ddt-root.pem` out of the store volume and trust it in each browser | DDT's own root (README, The server certificate) |
| 6 | On a Windows PC: the ADK, the Windows PE add-on, the .NET SDK, the Visual C++ build tools and a clone | The agent and the console are NativeAOT, published from source (build/Publish-Agent.ps1) |
| 7 | Run `Publish-Agent.ps1` and `Publish-Console.ps1` | The image carries no agent: build/Dockerfile:33,41 publishes only DDT.Host |
| 8 | Run `Build-BootImage.ps1`, elevated, with the server URL, the root, the agent and the console | build/Build-BootImage.ps1:6 needs elevation for DISM and bcdedit |
| 9 | Copy `artifacts\boot` into the boot directory inside the Linux host's store volume | There is no upload: `/api/boot-image` only reads (src/DDT.Server/Endpoints/BootImageEndpoints.cs:30-36) |
| 10 | Choose the interface, and set a boot target for ProxyDHCP or options 66 and 67 on the DHCP server | No interface and no boot target by default (src/DDT.Pxe/PxeOptions.cs:11-14,41-43); DDT only warns (src/DDT.Pxe/PxeHost.cs:299) |
| 11 | Extract `install.wim` from an ISO and upload it through the browser | The Images page takes WIM, ESD and disk images only (README, Images) |
| 12 | Write a task sequence and approve the machine | Nothing to start from |

Twelve steps on two operating systems, one of them a C++ toolchain. Steps 1 to 9 are what this
milestone removes; 10 to 12 become questions on a checklist (section 5).

## 3. The two commands

### 3.1 Windows Server

```powershell
winget install Davicloud.DDT
```

winget is part of Windows Server 2025. Where it is missing, the same MSI comes from one line:

```powershell
irm https://github.com/Davicloud-NET/DDT/releases/latest/download/install.ps1 | iex
```

The MSI does only what has to be in place before the web UI can serve, which is what configuration
keeps today (settings.md section 2):

1. It checks for 64-bit Windows Server 2019 or Windows 10 1809 or later, build 17763 (section 8,
   question 9), and for an elevated prompt.
2. It puts the server in `%ProgramFiles%\DDT` and the store in `%ProgramData%\DDT`.
3. It writes the bootstrap file of section 4.3: the store, the roles `web,pxe`, the HTTPS endpoint on
   8443, the two certificate paths, and the database of section 4.4.
4. It registers the `DDT` service, started automatically and restarted after a failure, running as
   the virtual account `NT SERVICE\DDT`. The store belongs to SYSTEM, Administrators and that account
   alone (section 4.3). It registers the `DDT Helper` service of section 4.7 as well.
5. It adds inbound firewall rules for `DDT.Host.exe` alone, for the Domain and Private profiles:
   TCP 8443 and 8080, UDP 67, 4011 and 69, and the ports of TFTP transfers, which a rule for the
   program covers without naming them. When the server's own network is public, setup says so and
   offers to cover public networks too (`FIREWALLPUBLIC=1`, `-AllowPublicNetworks`); `install.ps1`
   warns. Upgrades keep the profiles.
6. It starts the service and waits until `/api/about` answers.
7. It prints the address by the computer's DNS name, the root's SHA-256, and where the first password
   is (section 4.5).

Everything else is a setting or an action, so it happens on the checklist in the web UI (section 5).
An unattended install passes the port, the store and the database to the MSI as properties, and
`install.ps1` passes its own parameters on.

**What setup fetches.** Where something is missing, setup shows a Dependencies page with a box for
each: the Windows ADK with its Windows PE add-on (section 4.7), ticked, and URL Rewrite with
Application Request Routing where IIS is installed without them (section 4.6). Setup downloads what
is ticked from Microsoft and installs it quietly, which accepts Microsoft's licence terms, and the
page says so; DDT ships none of it. Each download has to match the SHA-256 the release names.
Windows runs one installer at a time, and all of these are installers, so none can run inside DDT's
own:

- The IIS modules install when the page's Next is clicked, before DDT, so the IIS page can follow.
  Windows Installer asks for elevation itself and names Microsoft as the publisher. Unattended,
  `install.ps1 -InstallIisModules` does the same before it starts the MSI; the MSI alone cannot.
- The ADK installs once DDT's install is done, in the background: `INSTALLADK=1` starts
  `DDT.Host setup adk`, which waits for Windows Installer, and the Application event log says how it
  went. `install.ps1 -InstallAdk` runs the same verb itself and shows what it says.

A server without internet access installs as before, and the ADK's failure is in the event log.
Installing IIS itself is left to the administrator: it is a server role.

### 3.2 Linux

```bash
curl -fsSL https://github.com/Davicloud-NET/DDT/releases/latest/download/install.sh | sudo sh
```

1. It checks for Docker Engine with the compose plugin, and without it stops and says how to install
   them. It installs nothing of its own accord.
2. It writes `/opt/ddt/compose.yaml`, the one from the release with the image pinned to its version,
   and `/opt/ddt/.env` with `DDT_ROLES=web,pxe`, `DDT_PORT` and `DDT_HOSTNAMES` from `hostname -f`.
3. Where ufw or firewalld is active, it asks, then opens the same ports as 3.1.
4. It runs `docker compose up -d` and waits until `/api/about` answers.
5. It prints the address, the root's SHA-256 and the command that shows the first password
   (section 4.5).

The database is the SQLite file in the store volume (section 4.4), so the container needs no
database service, and the whole install is also one line without the script:

```bash
docker run -d --name ddt --network host --restart unless-stopped -e DDT__Roles=web,pxe -v ddt:/var/lib/ddt ghcr.io/davicloud-net/ddt
```

Done: build/install.sh and build/compose.release.yaml, which the release workflow publishes as
`install.sh` and `compose.yaml`, the latter naming the release's image. The script takes
`--version`, `--port`, `--names`, `--firewall yes|no` and `--directory`, as in
`... | sudo sh -s -- --port 443`. Piped into `sh` it asks about the firewall on the terminal, and
without one it leaves the firewall alone and says which ports to open. It checks `compose.yaml`
against the release's `SHA256SUMS`. An upgrade is the script again: it replaces `compose.yaml` and
keeps `.env`, and changes of one's own, such as a PostgreSQL connection string, go into
`compose.override.yaml`, which it never touches. CI runs it on the Linux runner against the image
it just built.

Tried on a fresh Ubuntu Server 26.04.1 VM, with Docker Engine from Docker's repository and the
image built there from this source. Piped into `sudo sh` on a terminal, the script asked about ufw,
opened the ports, and DDT answered 8 seconds later; run again, it kept the store and its root. The
network card for netboot was chosen through the settings' API, since nothing chooses it on Linux
yet (section 5). The builder zip, run on a Windows VM with the ADK, built and uploaded in 123
seconds (section 4.8). A Windows 11 ISO copied into the volume's `import` folder was in the library
10 seconds later, and a machine netbooted from the container, through ufw, and got its Windows 11.

Found there and fixed: the container listened on IPv4 alone, `https://0.0.0.0`, while DDT's
certificate names the host's IPv6 addresses as well. A Windows PC that looked the server's name up
and got its IPv6 address was refused, the builder among them. The image and `compose.yaml` listen
on `https://*` now.

### 3.3 Upgrading and removing

The same command upgrades: `winget upgrade Davicloud.DDT`, the MSI again, or the script again, which
pulls the new image. The database is migrated at start, as PostgreSQL is today
(src/DDT.Server/Data/DatabaseInitializer.cs:27-31). The agent and the console come with the server
(section 4.2), so machines run the new agent from their next boot. A boot image is built again only
when the release notes say that Windows PE changed, and the Boot image page says so too (section
4.7). The Linux boot environment comes with the server as well, so an upgrade replaces it and there
is nothing to build. A server that has a boot image keeps starting machines without a run in
Windows PE after the upgrade (section 4.9).

Removing DDT keeps `%ProgramData%\DDT` unless the removal is asked to delete it: the store holds the
root key, and a new root means building every boot image again. What setup fetched from Microsoft
stays as well, as programs of their own in Apps and features.

Tried on the Windows Server 2025 VM: `msiexec /x` with `REMOVESTORE=1` took 6 seconds and left no
service, file, firewall rule, trusted root, registry key or event source. The helper's work folder,
`%ProgramData%\DDT Helper`, stayed behind at first; the MSI removes it now, with what a build that
broke off left in it.

## 4. What it takes

### 4.1 Releases

A workflow that a maintainer starts by hand:

- A Windows job publishes the agent and the console with NativeAOT, as Publish-Agent.ps1 and
  Publish-Console.ps1 do, and the server self-contained for win-x64, so the host needs no .NET
  runtime. It builds the MSI with WiX, and the programs and the MSI are signed (Signing, below).
- A Linux job publishes the agent for Linux and builds the Linux boot environment around it
  (section 4.2), each in a container and without a cache: `build/Dockerfile.agent-linux` and
  `build/LinuxBoot/Dockerfile`. It stops unless `base.img` has the SHA-256 the lock names
  (`build/LinuxBoot/packages.lock.json`), every file is the one `ddt-linux-boot.json` lists, and
  the agent in `ddt.img` is the file the server will offer, with the release's version. Then it
  collects the Debian source packages of everything in the environment, each checked against the
  lock, as `ddt-linux-boot-sources.tar`. The job has no secret.
- The MSI takes the Linux files beside the Windows ones: `ddt-agent-linux-x64` next to
  `DDT.Host.exe`, and the folder `linux-boot` with `shimx64.efi`, `grubx64.efi`, `vmlinuz`,
  `base.img`, `ddt.img` and `ddt-linux-boot.json`.
- Another Linux job builds the image with the agents, the console and the Linux boot environment
  as the MSI holds them, so both carry the same files. NativeAOT does not compile for Windows on Linux,
  so build/Dockerfile takes them as files rather than building them. It pushes to
  `ghcr.io/davicloud-net/ddt` with the version and `latest`, x64 first, with a provenance
  attestation. Before that it pushes `ghcr.io/davicloud-net/ddt-sources` under the same tags, an
  image that holds `ddt-linux-boot-sources.tar` and nothing that runs.
- The release carries `DDT.msi`, `install.ps1`, `install.sh`, `compose.yaml`,
  `ddt-linux-boot-sources.tar` and `SHA256SUMS`, which lists the other five, and a winget manifest
  goes to winget-pkgs.

**The source code of the Linux boot environment.** The environment is made of Debian packages under
the GPL and other licences, so every release publishes their source at both places it publishes
the programs: `ddt-linux-boot-sources.tar` beside the MSI, and the `ddt-sources` image beside the
server's image. `licenses/linux-boot/SOURCES.md`, which the server also serves from its `legal`
folder, lists each file with its SHA-256 and its address in Debian's archive. The network card
firmware has no source code: its makers publish it in binary form only, and of it the archive
holds Debian's packaging alone. [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) names every
package and licence.

**Versions.** Decided 2026-10-02: a version is Year.Major.Build, as 26.1.412.

- Year is the year's last two digits and Major counts the releases with new features in that year,
  starting at 1 each January. Both are in `Directory.Build.props` and raised by hand. Windows
  Installer compares exactly three numbers and takes at most 255.255.65535, so the year cannot be
  2026.
- Build is the number of commits up to the one built. Nobody types it: `Directory.Build.targets`
  counts it for a plain `dotnet build`, and `build/Get-Version.ps1` prints the whole version. Without
  git, as in a container build, Build is 0 unless the version is passed in.
- The server, the agent, the console, the MSI, the winget manifest and the image's tag carry the
  same version. The About page reads the server's (src/DDT.Host/Program.cs:155).
- A pre-release has no number of its own, because the MSI takes none. GitHub marks it, the About
  page shows `26.1.412-pre`, the image's `latest` stays where it is, and winget gets nothing.

**Cutting a release.** The Release workflow is run by hand from master, with a box for a
pre-release and one for a dry run, which builds everything from any branch and publishes nothing.
It counts the version, refuses one that is released already, builds the MSI and the image, and only
then makes the tag `v26.1.412` and the GitHub release. Releases come from master alone: a branch
that is squashed into master has more commits than master gets from it, so a release from the
branch would be newer than the next one from master, and Windows Installer would not upgrade to it.

Each release's `install.ps1`, `install.sh` and `compose.yaml` name that release, so a copy installs
the release it came with. `releases/latest/download/` skips pre-releases, so a pre-release is
installed from its own address, `releases/download/v26.1.412/install.ps1`.

**The release's text.** It starts with `build/release-notes.md`, which gives the two install lines
with the release's own address, so a pre-release has them too. GitHub's list of the pull requests
merged since the last release follows, with package updates in a group of their own
(`.github/release.yml`). Words about a particular release are added on GitHub once it is out.

**Signing.** A release signs `DDT.Host.exe`, the agent, the console and the MSI with the
maintainer's certificate, through SSL.com's eSigner and its GitHub action. Whoever holds that
account's secrets can sign anything in the maintainer's name, and the repository is public, so:

- The secrets are secrets of the environment `signing` and of nothing else: `ES_USERNAME`,
  `ES_PASSWORD`, `ES_TOTP_SECRET`, and `ES_CREDENTIAL_ID` where the account has more than one
  certificate. The environment allows the branch `master` alone and has the maintainer as required
  reviewer, so no pull request, fork or other branch reaches them, and each signing job waits for
  an approval: two per release, one for the programs and one for the MSI that holds them.
- Only the two signing jobs name the environment. They fetch the files, run SSL.com's action and
  hand the files on. They check out nothing and build nothing, so no package that a build restores
  runs in a job that has the secrets. `Build-Installer.ps1` builds in two halves for that:
  `-StageOnly` up to the MSI, and `-Staged` for the MSI.
- The jobs after check every signature with `build/Test-Signature.ps1`, timestamp included, before
  the file goes into the MSI or the release. The sums and the winget manifest are written from the
  signed MSI, and the image is pushed only after that.
- A dry run signs nothing and needs no approval.

This does not help against a build that is wrong to begin with: what the run built from master is
what gets signed. Not signed: DDT's managed libraries next to `DDT.Host.exe`, the custom action
library inside the MSI, and `install.ps1`. The Linux files are not code-signed by DDT either:
`ddt-agent-linux-x64`, `base.img` and `ddt.img` carry no signature, and `shimx64.efi`,
`grubx64.efi` and `vmlinuz` carry Microsoft's and Debian's, as Debian's packages bring them. What
vouches for them is `SHA256SUMS` for the MSI that holds them, the hashes in `ddt-linux-boot.json`,
which the server checks the folder's files against, and the lock that pins every Debian package
(section 4.9, What netboot trusts). Not run yet: the signing jobs need the environment and a
release from master.

### 4.2 The agent and the console come with the server

Today a fresh server has no agent to offer until an administrator uploads one on the Server page
(README, Updating the agent without a new boot image), and a boot image built from an older clone
runs its own agent.

The release puts `ddt-agent.exe` and `ddt-console.zip` into the server's own folder, next to
`DDT.Host`. The server offers what an administrator uploaded, as today, and otherwise the files it
came with, so a new server offers an agent of its own version from its first start. The Server page
says which it offers, and once an upgrade brings an agent newer than the uploaded one, it says that
too and offers to drop the upload. `Build-BootImage.ps1` takes the bundled agent and console when
it runs from a release and `-AgentPath` is not given.

Done: `Build-Installer.ps1` compiles the agent and the console with the server's version and puts
them into the MSI, with `Build-BootImage.ps1` and its module; the release workflow hands both to the
container image, since only Windows compiles them. The server reads the version out of each
executable to tell which is newer. Run from the server's folder, `Build-BootImage.ps1` also names
the server by its DNS name and port, trusts its root and writes into its boot directory.

**The Linux files.** A release puts two more things next to `DDT.Host`:

- `ddt-agent-linux-x64`, the agent for Linux: one static program for x64 that needs no library of
  the system it runs on. The server offers it by the same rule as the Windows agent: what an
  administrator uploaded on the Server page, which goes to `<StorePath>/agent/ddt-agent-linux-x64`,
  and otherwise this file. An ELF file has no version resource, so the build writes
  `DDT-AGENT-VERSION:` and the version into the program, and the server reads that.
- `linux-boot/`, the Linux boot environment: `shimx64.efi`, `grubx64.efi`, `vmlinuz`, `base.img`
  and `ddt.img`, with their description `ddt-linux-boot.json`. The server looks for it in that
  folder, next to its own program files. It needs no build, and nothing of a particular server is
  in it: the server's address and root certificate reach a machine in a small archive the server
  generates at each netboot (section 4.9).

The description lists each file with its size and SHA-256. The server starts machines from the
folder only while the description can be read and lists the five files, each is there in its size
and is no link, and the shim carries the signatures the description says. It compares each file's
SHA-256 with the description in the background and stops serving when one differs. The Boot image
page shows the environment with its kernel, shim and GRUB versions and its keyboard layouts, or
says why no machine can be started in it.

**An agent for each system.** An agent asks for the one built for its system:
`GET /api/agents/releases/{platform}` and `.../binary`, with `windows-x64` or `linux-x64` as the
platform. The routes without a platform, `api/agents/release` and `/binary`, stay the Windows PE
agent's, which the agent in a boot image asks.

**The agent updates itself in the Linux boot environment too.** `ddt.img` holds the agent of the
release it was built in. Before it registers, that agent asks the server which agent it offers
for `linux-x64`. When that is another file, it downloads it, compares its size and SHA-256 with
what the server announced, and runs it in its own place. So an uploaded agent reaches machines at
their next netboot and needs no new boot environment. When the download or the start fails, the
agent from `ddt.img` goes on. In a release both are the same file, and nothing is downloaded.

**From source.** Two configuration keys stand in for what a release brings.
`DDT:Agent:LinuxBinaryPath` names the agent for Linux, and while it is set, uploads answer 409
and the bundled agent is not offered, as with `DDT:Agent:BinaryPath`. `DDT:LinuxBoot:Path` names
the folder of the Linux boot environment. `build/Start-DevHost.ps1` points the first at
`artifacts\agent-linux\ddt-agent` and, when `Build-LinuxBoot.ps1` has built one, the second at
`artifacts\linux-boot`. The Server page lists both keys with whether they are set.

### 4.3 The server as a service

**Hosting.** DDT.Host builds its host with `WebApplication.CreateBuilder(args)` and runs it with
`app.Run()` (src/DDT.Host/Program.cs:33,163), so the service control manager would give up waiting for
it to start. It calls `AddWindowsService()` and `AddSystemd()`, from
Microsoft.Extensions.Hosting.WindowsServices and .Systemd, which do nothing outside a service, so a
container, `dotnet run` and the console verbs behave as today. As a service it also sets its content
root to `AppContext.BaseDirectory`: a service starts in System32, and the content root is the current
directory today, where `UseStaticFiles` and the fallback to `index.html` look for `wwwroot`
(Program.cs:120-121,161). The `legal` folder already comes from `AppContext.BaseDirectory`
(Program.cs:85).

**The bootstrap file.** The service and the console verbs read `%ProgramData%\DDT\ddt.ini` on
Windows, after `appsettings.json` and before environment variables and the command line, so both
still override it. It's an INI file because Windows Installer writes those itself, with rollback,
where JSON would need a custom action; .NET reads it with `AddIniFile`. The MSI only writes keys
that are missing, so an upgrade keeps what an administrator changed. It is what ENV lines are in the
image (build/Dockerfile:44-47): the defaults of an install, not of the code, for the reasons
settings.md section 2 gives. So the store's code default stays `/var/lib/ddt`
(src/DDT.Server/Configuration/DdtOptions.cs:13) and the file names `%ProgramData%\DDT`. It holds
only keys of settings.md section 2: a key of a page field there would lock that field (README,
Configuration wins, and locks). `DDT.Host settings create-admin`, which SettingsConsole.cs:16 runs
with `docker exec` today, then finds the same database on Windows from any elevated prompt.

**What differs on Windows.** The server's tests run on Windows in CI (.github/workflows/ci.yml:25-55),
and the PXE sockets and the key files have Windows code of their own (src/DDT.Pxe/PxeSocket.cs:15-31,
66-82, src/DDT.Server/Certificates/PemFiles.cs:29-66). What is left:

- **The store is readable by every user.** The Data Protection key ring is plain XML in `keys` of the
  store (src/DDT.Server/Authentication/DdtAuthenticationExtensions.cs:40-42), and whoever reads it can
  mint an administrator's cookie and every machine token. Below `%ProgramData%` the Users group
  inherits read access, to the database and the images as well. The installer gives the store to
  SYSTEM, Administrators and `NT SERVICE\DDT` alone, and the key ring is encrypted with DPAPI for the
  machine as well, which a copied store does not open elsewhere. Done. For the machine and not an
  account, because the service, setup as SYSTEM and an administrator's recovery verb share the ring.
  So a store moved to another server loses its secrets (settings.md, What the encryption protects).
- **Key files belong to whoever wrote them first.** The ACL of a PEM key names the account that made
  it (PemFiles.cs:48-66). A key made by an administrator's `dotnet run` is unreadable to the
  service, and the error then speaks of restoring a backup
  (src/DDT.Server/Certificates/ServerCertificates.cs:285-292). The installer lets the service make
  them, and the error names the account that can read the file. Done: a key DDT may not read says
  which account DDT runs as and that it needs read access, not to restore a backup.
- **Relative paths.** A relative store or boot directory resolves against the current directory
  (for example src/DDT.Server/Images/ImageStore.cs:18,20, src/DDT.Pxe/PxeSetup.cs:61-62), which is
  System32 for a service. The bootstrap file names absolute paths, and DDT resolves relative ones
  against the program's folder. Done: as a service DDT makes the program's folder its current one.
- **Files in use cannot be replaced.** Linux renames over a file that is being read; Windows refuses.
  TFTP held boot files open, HTTP boot and the agent download served them with `PhysicalFile`, and
  the agent upload and the console's logo renamed over theirs. So an upload during a machine's
  download failed, and so did copying a boot image in while a machine netboots. Done, without new
  file names: every reader opens its file so that it may be deleted (`FileShare.Delete`), and a
  writer that cannot rename over the file moves the old one aside under another name first, which
  Windows allows then; the old file goes when its last reader closes it
  (src/DDT.Server/Machines/FileReplacement.cs, `Copy-BootFile` in build/BootImage/Private/Publish.ps1).
  A boot image copied in by hand with Explorer still waits for a netboot to finish. Section 4.7
  builds on this.
- **Free space.** `DriveInfo` measures the store's volume on Linux, but only a drive root on Windows,
  and throws for a UNC path (ImageStore.cs:39-40), which fails every upload with a 500. On Windows the
  server asks `GetDiskFreeSpaceEx` for the store's folder. Done (src/DDT.Core/Disks/VolumeSpace.cs).
- **Time zones on Windows Server 2019.** A time zone is checked through ICU
  (src/DDT.Core/Unattend/WindowsTimeZones.cs:11-16). Windows carries ICU since Windows 10 1703,
  Server 2019 included, as `icuuc.dll` and `icuin.dll`, and .NET 7 and later loads those. Tried on
  Server 2019, whose ICU is version 61: the deployment settings take `W. Europe Standard Time` and
  refuse a name that is no Windows time zone.
- **Converting disk images.** The only programs the server starts are `qemu-img` and `xz`, for qcow2
  and `.xz` uploads, found on the PATH (src/DDT.Server/Images/ConversionTools.cs:15-16,95-107). A
  missing one already refuses the upload with a message that says which to install, and keeps the
  upload. On Windows DDT also looks where QEMU's installer puts `qemu-img`. The install carries
  neither; this only concerns Linux images.
- **Logs.** Without a console, Information and below go nowhere, and warnings go to the Application
  event log: there is no file sink. The Windows install writes a rolling log file in the store, which
  the Server page offers to download. The first password leaves the log entirely (section 4.5).
  Done: as a service DDT writes `logs/ddt-<date>.log` in the store, a file a day, a new one above
  20 MB, the newest 20 kept (src/DDT.Host/Logging/FileLoggerProvider.cs). The Log levels settings
  apply to it, and the Logging tab lists the files (`GET /api/server/logs`, administrators only).
- **The advice for a failed bind.** It says to grant `NET_BIND_SERVICE`
  (src/DDT.Contracts/Messages/ServerMessages.cs:1808-1814). Windows has no privileged ports: there,
  access denied means that WDS or the DHCP server holds the port, which section 4.9 names. Done: on
  Windows the message says that another service holds the port for itself, usually WDS or the DHCP
  server.
- **The recovery commands** are documented as `docker exec` (src/DDT.Host/Startup/SettingsConsole.cs:16).
  Without the bootstrap file, `DDT.Host.exe settings reset` on Windows would open a SQLite file of its
  own and reset the wrong database; with it, it finds the service's. Done, and settings.md names
  both forms.

### 4.4 The database on one server

Today the server uses PostgreSQL when `ConnectionStrings:ddtdb` is set and otherwise a SQLite file
that is created from the model and never migrated
(src/DDT.Server/Data/DataServiceCollectionExtensions.cs:29-45,
src/DDT.Server/Data/DatabaseInitializer.cs:35-65), with the warning that SQLite is not a supported
production store (DatabaseInitializer.cs:119). So one command needs a database server as well.

- **(a) SQLite as the store of a single server, with migrations.** Recommended, and what DeployR
  Community does by default. A second set of migrations for SQLite, applied at start like
  PostgreSQL's; WAL mode and a busy timeout; a `DDT.Host backup` verb that writes a consistent copy
  with `VACUUM INTO`. The load of 100 machines writing log lines is small for SQLite. It serves one
  process, which is the standard install with both roles. A second process on the same database,
  such as a pxe host at another site, needs PostgreSQL, because it cannot open a file on this
  server. The fingerprint check of today's development file goes, since the file is migrated.
- **(b) The installer installs PostgreSQL**, with winget or EDB's installer unattended: a second
  product on the server with its own superuser password, updates and major upgrades.
- **(c) DDT carries PostgreSQL itself**, which makes DDT responsible for PostgreSQL's major upgrades.

With (a), PostgreSQL stays for installs that want it, and the compose file keeps it as an option. An
install does not move between the two; an export and import can come later.

**SQL Server** too, for shops that run it already (ConfigMgr, WSUS, MDT's database). Needs:

- `NO ACTION` on the foreign keys SQL Server calls multiple cascade paths (the user references on
  `Machine`, `ApiToken`, `Deployment`), with the code clearing them before a user is deleted;
- a filter on the unique `DdtUser.DirectoryObjectId` index, which allows one NULL otherwise;
- a binary collation on hash and token columns, since the default one ignores case;
- `sp_getapplock` around the migration at start, like PostgreSQL's lock;
- tests against a SQL Server container in CI.

**One migration per provider.** At the end of 4.4 the migrations are reset: one initial migration
each for SQLite, PostgreSQL and SQL Server.

Done, with (a):

- **Which database.** `DDT:Database` is `Sqlite`, `PostgreSql` or `SqlServer`. Unset, a connection
  string means PostgreSQL, as before, and none means SQLite. The installer takes
  `DATABASEPROVIDER` next to `CONNECTIONSTRING`, and `install.ps1` `-DatabaseProvider` next to
  `-Database`. The Server page names the database in use.
- **One model, three sets of migrations.** EF Core keeps a context's migrations per type, so each
  database has a context of its own below `DdtDbContext` (src/DDT.Server/Data), with its first
  migration in src/DDT.Server/Migrations. A new one is added three times:
  `dotnet ef migrations add <Name> --context SqliteDdtDbContext --output-dir Migrations/Sqlite --project src/DDT.Server --startup-project src/DDT.Server`,
  and the same for `PostgreSqlDdtDbContext` and `SqlServerDdtDbContext`. A test fails while one of
  the three lacks a migration for a model change.
- **SQLite.** The file is `ddt.db` in the store, migrated at start. EF Core creates it with the
  write-ahead log, and a writer waits up to 30 seconds for another. `DDT.Host backup <file>` copies
  it with `VACUUM INTO` next to the running server. A file from the last build before the
  migrations, `ddt-dev.db` with that build's fingerprint, is renamed and goes on with what it holds;
  one from an older build is refused, as before.
- **PostgreSQL.** The twelve migrations became one, which creates the same tables. A database at
  the last of the twelve has its history rewritten at the next start; one that stopped earlier is
  refused and says so.
- **SQL Server.** Its model differs in two places. Every text column has the collation
  `Latin1_General_100_BIN2`, so text is compared exactly as on the other two, not only hashes and
  tokens. And the thirteen references that name a user are not cleared by the database, which
  refuses `SET NULL` where two paths lead from a user to a table: DDT clears them in the
  transaction that deletes the user (src/DDT.Server/Data/UserReferences.cs). The directory id's
  unique index has its filter, and EF Core's own lock (`sp_getapplock`) serializes migrations.
- **What ships for SQL Server.** `Microsoft.Data.SqlClient` brings two native libraries under
  Microsoft licences that are not free software licences. Its network library for Windows ships
  with the MSI: Microsoft allows passing it on inside an application, and NOTICE grants an
  additional permission under GPL section 7 for combining DDT with it, so those who pass DDT on
  may keep it in (section 8, question 10). The Entra ID broker stays out for now: nothing needs it
  for Windows or SQL sign-in. Should brokered Entra ID sign-in to the database be wanted later, it
  takes a permission of its own in NOTICE and its licence text, like the network library. The
  container has neither: on Linux the client uses its managed networking.
- **Tests.** The tests that ran on PostgreSQL run on both servers. SQL Server comes from
  `DDT_TEST_SQLSERVER`, a connection string without a database, then from LocalDB on Windows, then
  from a container.

### 4.5 The first administrator

Today the password is logged once (IdentityBootstrap.cs:116-117). In a container `docker logs` shows
it. A Windows service logs warnings to the Application event log, where the password would stay
readable to every administrator of the server for as long as the log keeps it.

DDT writes the password to `first-admin.txt` in the store instead, readable only as `ddt-key.pem` is,
and deletes the file when the password is first changed. The log line names the file, not the
password. Both install commands print it, and the sign-in page of a server whose first password is
unchanged says where it is. No bootstrap credential is read from configuration, as before (README,
The first administrator), and `settings create-admin` stays the way back in.

### 4.6 Names and trust

**Names.** The certificate names `localhost`, the short host name and the addresses, loopback
included, for a browser or IIS on the server itself (src/DDT.Server/Certificates/ServerNames.cs). It
names the DNS name as well, such as `deploy01.contoso.local`, which the installers print and the
boot image uses: Windows PE resolves a short name only if DHCP hands it the right suffix. The DNS
name is the host name with the computer's domain, where that makes a host name; a certificate from
DDT's root that lacks it is issued again at the next start, as for any missing name.

**Trusting the root.** The checklist (section 5) links `ddt-root.pem` with its SHA-256 and a Group
Policy recipe for the computers of those who manage DDT, and nowhere else, as the security model
asks: the root has no name constraints (README, Security model).

**IIS in front.** Where IIS has URL Rewrite and ARR, or setup just added them (section 3.1), setup
offers a page for an IIS site of DDT's own:
a host name with its own DNS record and a certificate from the machine store (`IISHOSTNAME`,
`IISCERTIFICATE`; `install.ps1 -IisHostName -IisCertificate`). `DDT.Host setup iis` makes the site,
binds the certificate by SNI and forwards to Kestrel with `X-Forwarded-Proto`;
`settings trust-local-proxy` lists loopback as a proxy. Browsers get the shop's certificate, and IIS
Manager's automatic rebind follows AD CS renewals. Agents keep DDT's port and root. Uninstall removes
the site, and only a site in DDT's own folder.

ARR's proxy switch and `preserveHostHeader` exist only for the whole server, and DDT needs both:
without the host name it takes itself for `localhost:8443`. So `setup iis` sets them only where
ARR's proxy was off. Where ARR already proxies without the host name, it stops and names the
command, instead of changing how the other sites are proxied. They stay set after an uninstall.
Live updates use WebSockets where IIS has that feature, and server-sent events otherwise. Tried
on Windows Server 2025 with IIS 10, URL Rewrite 2.1 and ARR 3.0.

**A certificate from the shop's own CA.** Many MDT shops run AD CS, whose root every domain member
trusts already. A certificate of one's own works today, but DDT does not renew it (README, A
certificate of your own). Requesting and renewing one from AD CS is not in this milestone (section
8, question 8).

### 4.7 The boot image built on the server

Today `Build-BootImage.ps1` runs on a PC with the ADK, elevated (Build-BootImage.ps1:6), and its
output is copied into the boot directory by hand. The server picks up a new build by itself, because
it reads `Boot/ddt-boot-image.json` every 10 seconds (src/DDT.Server/BootImage/BootImageWatcher.cs:23).

**The Build button.** The Boot image page builds the boot image on a Windows server:

- DISM and bcdedit need elevation, and the web server, which parses uploads, should not have it.
  So the `DDT Helper` service runs as LocalSystem and does what needs an administrator on the
  server: builds, and the DHCP and WDS changes of section 4.9. It listens on a named pipe that only
  `NT SERVICE\DDT` may open and takes a few kinds of request, each with values and never a path
  outside the store or a command. A build's values are the keyboard layout, with or without
  PowerShell, the TFTP window, and folders inside the store that the server filled. It runs the
  `Build-BootImage.ps1` in `%ProgramFiles%\DDT`, with its `BootImage` module folder (including the
  two C# files it compiles), all of which only administrators can change, and returns its output
  line by line.
- The server fills in what the script's parameters take today: its URL by DNS name, its root, the
  agent and console it offers (section 4.2), and the flagged driver packages, which it unpacks
  itself, so the build needs no API token.
- The page shows the build's output as it runs, pushed over the hub. One build runs at a time.
- Each build goes into a folder of its own below the boot directory, and TFTP and HTTP boot serve
  the build marked current. A build that succeeded becomes current, a netboot in progress finishes
  from the build it started with, since Windows cannot replace a file that TFTP holds open (section
  4.3), and the previous build stays to go back to.
- The page says when the boot image has to be built again. `GET /api/boot-image` already says so
  for the flagged drivers; it also says so when the server's URL or root changed, the keyboard layout
  changed, the ADK changed, or the release says Windows PE changed.

**The ADK.** The builder finds it as the script does, by `KitsRoot10`
(Build-BootImage.ps1:219-226), and MDT servers have it already. The page shows its version and
whether DDT supports it: the add-on has to be 10.1.26100.2454 or later
(Build-BootImage.ps1:1147-1150). Without one, the page offers to install it: the builder downloads
Microsoft's `adksetup.exe` and `adkwinpesetup.exe` for the versions the release names and runs them
quietly with the deployment tools and Windows PE only. The release names a tested pair and never the
newest: winget offers the ADK 10.1.28000.1 with the add-on 10.1.26100.2454, which do not match, and
2Pint recommends against 10.1.28000.1 for adding drivers.

Done, ahead of the page: `DDT.Host setup adk` installs the pair 10.1.26100.9457 that way
(src/DDT.Host/Startup/AdkSetup.cs), with each setup checked against its SHA-256, and setup and
`install.ps1` offer it (section 3.1). It adds a missing add-on only to the ADK of the same version.
An elevated prompt runs it by hand, after an upgrade for instance, which shows no Dependencies page.

Done, the helper and the button:

- `DDT.Host helper` is the `DDTHelper` service (src/DDT.Host/Helper), which the MSI registers next
  to `DDT`. Its pipe `DDT.Helper` lets `NT SERVICE\DDT` read and write, refuses the network, and
  takes one request at a time as a line of JSON: `build` or `adk`. A build's name, address, keyboard
  layout and TFTP window are checked again in the helper, and the address is put together again
  from its parts, so nothing from the request reaches PowerShell's command line as it came.
- The helper works in `%ProgramData%\DDT Helper`, which only SYSTEM and administrators can open. It
  unpacks the flagged driver packages there from the store, each checked against the SHA-256 in its
  name, runs the script, and copies the result into `boot\builds\<name>`. The image carries the
  agent and console the release came with, not an uploaded one, which machines get at netboot
  anyway (section 4.2).
- The helper reads the store and the boot directory from `ddt.ini` once, as it starts
  (src/DDT.Host/Helper/HelperPaths.cs), and so does the server. After a change of `DDT:StorePath`
  there, restart the service `DDTHelper` together with `DDT`: a helper left running builds the next
  boot image into the store it started with.
- A build's name is its start in UTC, `20261001-100000`. The file `boot\current` names the build
  that TFTP and HTTP boot serve (src/DDT.Pxe/BootBuilds.cs); without it they serve the boot
  directory itself, as before, so a build copied there by hand keeps working and shows in the list
  as one more build to serve. After a build the server keeps it and the one served before it, and
  removes the rest.
- `POST /api/boot-image/build`, `/adk` and `/current` are an administrator's, and each is audited.
  `GET /api/boot-image` names why a build is due (`drivers`, `serverAddress`, `root`, `adk`), the
  ADK, the builds and the job; `GET /api/boot-image/job` has the job's output, and the hub pushes
  it as `bootImageJobOutput`. The job is kept in memory, so a restart of the server forgets its
  output, and the build it left says the rest.
- The page offers the keyboard layout and PowerShell, starting from what the served build has. The
  TFTP window is in the API alone.

Left: the page does not say that a release changed Windows PE, since no release says so yet. The
keyboard layout is chosen at each build and not a setting, so it has nothing to differ from.

Tried on a fresh Windows Server 2025 VM, through the API the page uses: the ADK and its add-on
installed in 85 seconds, and a build took 125 to 155 seconds. A driver package flagged for Windows
PE made the served build out of date, and the next build had the driver in `boot.wim`'s driver
store. A build that finished while a machine was still loading the one before, over TFTP with a
window of 1, left that machine alone: it got the rest of the old `boot.wim` from the old folder
and started.

`Build-BootImage.ps1` stays the one implementation, so a build by hand on a PC gives the same image.
Run next to an installed server without `-Destination`, it builds into `boot\builds` and marks its
build current, as the button does.

**The Linux boot environment on the same page.** The boot image is for Windows sequences. The
page's second panel shows the Linux boot environment, which comes with the release and has nothing
to build (section 4.2): its kernel, shim and GRUB, the day its Debian packages are from, the boot
file machines load, its keyboard layouts, which certificate authorities its shim is signed under,
and a link to the list of its source packages. `GET /api/boot-image` carries the same as `linux`.
A server that deploys Linux alone never builds a boot image.

### 4.8 The boot image from another PC

Until it has a boot image, a server starts machines in the Linux boot environment, which runs Linux
sequences only (section 4.9). A Linux server, or a Windows server without the ADK, gets its boot
image from any Windows PC that has one:

- The Boot image page hands out a builder: a zip with `Build-BootImage.ps1` and its `BootImage`
  module folder, the trim list, the
  agent, the console, `ddt-root.pem` and a `Build.cmd` holding the server's URL and a token that can
  upload one boot image, once, within a day. The administrator downloads it after entering the
  password again, since a boot image runs as SYSTEM on every machine that netboots, like the agent
  upload.
- `Build.cmd` runs the script elevated and uploads the result to `PUT /api/boot-image`, which checks
  the layout and the manifest, and makes it the current build as a build on the server does.

Tokens are an administrator's today (README, API tokens). The upload token is the first with a
single purpose.

Done:

- `POST /api/boot-image/builder`, an administrator's after the password again, answers the zip:
  the script, its module and the trim list as the release brought them, the agent, the console,
  `ddt-root.pem`, the drivers flagged for Windows PE, unpacked, a `Build.cmd` that elevates itself,
  and `builder.json` with the server's address and the token. The token is in that file and not in
  `Build.cmd`, so nothing has to be quoted for cmd. The container image carries the script too.
- Run in a folder with `builder.json`, `Build-BootImage.ps1` builds for that server with what the
  folder holds, asks the server first whether the token still uploads, and sends the build as a zip
  of its `Boot`, `EFI` and `x64` folders, trusting only the root it came with.
- `PUT /api/boot-image` takes the token in `X-DDT-Builder-Token` and nothing else: it is no API
  token, and no other call takes it. The server refuses a zip with a file outside the three
  folders, without one of the files a build has, or whose `boot.wim` or description cannot be read,
  and only then moves it into `bootuilds` and serves it. The page shows the upload as a job.
- The token is good for a day and for one boot image that the server accepted. Whether it was used
  is in the audit log, so every host of the server knows.
- The page offers the builder where the server cannot build right now: without the helper, or
  without a supported ADK.

### 4.9 Netboot at the first start

What is served stays the administrator's choice: `DDT:Pxe:Interfaces` keeps no default
(PxeOptions.cs:11-14). The checklist asks, with the interface that holds the default route already
chosen, from the list `GET /api/settings/pxe/interfaces` gives today.

**A default boot target.** With ProxyDHCP on and no boot target, DDT answers nothing and only warns
(PxeHost.cs:299). Without a configured one, `X64Uefi` boots `x64/bootmgfw.efi` over TFTP, the 2011
boot manager that is already the default. Done: with ProxyDHCP and TFTP on and no boot target
configured, that is the one target (src/DDT.Pxe/PxeSetup.cs), and the Network boot page says so.

**Two boot environments.** The server starts a machine in one of two. Windows PE, from the boot
image of sections 4.7 and 4.8, runs Windows sequences. The Linux boot environment, from
`linux-boot/` (section 4.2), runs Linux sequences, which are those that write a raw disk image.
Neither runs the other's: the server hands a run only to the environment its sequence starts in,
and an agent refuses a run of the other kind before anything is erased.

**Which one a machine starts.** ProxyDHCP names the boot file in its answer, so the server chooses
for each request of a 64-bit UEFI machine that starts with PXE, in this order:

1. A machine with a run starts the environment the run's sequence needs.
2. A machine that was asked to restart into the other environment starts that one, for 15 minutes
   from the asking.
3. Every other machine starts what `DefaultBootEnvironment` on the Network boot page says: `Auto`,
   `WindowsPE` or `Linux`. `Auto` is the default: Windows PE once the boot directory holds the
   boot file of the 64-bit UEFI boot target, so once a boot image is built, and the Linux boot
   environment until then. A new server so starts machines in Linux until someone builds a boot
   image, and an upgraded server that has one goes on starting Windows PE.

The Linux boot environment is named only while the server can start it: its files are as their
description says, TFTP is on, and the server has a root certificate of DDT's own to hand out,
which a server with an administrator's certificate has not. A machine with Secure Boot on whose
firmware trusts none of the certificate authorities the shim is signed under is named the Windows
PE boot file where Windows PE can start, and its page says why. The server knows a machine's
firmware from its last registration and believes it only of a machine that was approved once. A
Hyper-V virtual machine of generation 2 is such a machine with the Secure Boot template Microsoft
Windows: its firmware refuses the shim and goes on to its next boot entry. With the template
Microsoft UEFI Certificate Authority it starts it.

Where the environment a run needs cannot start, the machine is named the other one, whose agent
says why the run does not start, and where neither can start it gets no answer. Other
architectures and UEFI HTTP Boot keep the boot targets of the settings. The server answers from a
list in memory, which it reads again every five seconds and when a machine changes.

**What a machine loads for Linux.** ProxyDHCP names `linux/shimx64.efi`, which the machine loads
over TFTP from DDT, whatever the boot target for 64-bit UEFI says. shim loads `grubx64.efi` from
the same folder. GRUB reads `grub/grub.cfg`, which the server writes for each request, and that
loads `vmlinuz` and three initramfs files: `base.img` with Debian's kernel modules, firmware and
BusyBox and DDT's start scripts, `ddt.img` with the agent, and `site.cpio`, which the server
generates around `agent.json`: its address, its root certificate and the console's keyboard
layout. Together about 36 MB. The kernel command line names the adapter GRUB booted from, the
server's time, name and address, the keyboard layout, and the words of `LinuxKernelArguments`.
The start scripts load the drivers, set the keyboard layout and a clock that is more than ten
minutes off, ask DHCP on that adapter and start the agent on the first screen. When the agent ends
without restarting the machine, a menu offers to start it again, a shell, a restart or power off.
Left alone, the menu starts the agent again after 60 seconds, three times, and then stays.

**HTTP first, TFTP second.** shim, GRUB and `grub.cfg` come over TFTP. The kernel and the
initramfs files come over HTTP from the boot port, 8080 unless `DDT:Pxe:HttpBootPort` names
another, and over TFTP where HTTP does not open: `grub.cfg` tries the one and then the other, and
starts the kernel only when the kernel and all three files opened. A blocked boot port costs about
16 seconds before GRUB falls back. Where neither works, GRUB says so, waits ten seconds and hands
the machine back to its firmware.

A machine that is reset while it loads over HTTP leaves its connections open on the server, and
GRUB takes the same source ports at its next start, which then waits on them. So when a machine's
netboot starts again, with its request on UDP 4011 or its TFTP read of the shim or of `grub.cfg`,
the server resets the connections from that address that carried the Linux files. And for three
minutes after an HTTP read that did not end, or while the server's own system still lists a
connection between the machine and the boot port, it writes `grub.cfg` for TFTP alone. That is why
a machine that was reset during a load takes TFTP for a few minutes, and the log line of its
`grub.cfg` says so.

The server's system also keeps a connection in its list for a while after it was closed in good
order: Windows for two minutes, Linux for one. So a machine that starts again within that time
after a start that loaded over HTTP is given TFTP for that one start, with nothing wrong. It costs
one to three seconds, as measured on Hyper-V.

**From one environment into the other.** A machine that started one environment can run a sequence
of the other. At the machine, the list shows such a sequence with a note that the machine restarts
first, where the server can start the other environment for it. Picking it records the wish on the
server, through `POST api/agents/{id}/environment`, which takes only an operator or administrator
signed in at that machine. For a run assigned on the web or by a rule, the server orders the
restart when the agent next asks. Either way the agent writes its resume token into a UEFI
variable, `DdtResume`, has the firmware start once more from the entry this start came from, and
restarts. The agent of the other environment reads the variable, deletes it and registers with the
token, so the approval, the sign-in and an assigned run carry over. Disk, name and inputs are
asked after the restart.

Its limits:

- Both environments have to be there, the machine's agent has to be one that knows the restart,
  and for Linux its firmware must not be known to refuse the shim. Otherwise the run stays
  assigned, and the machine's page and its console say why.
- Where the firmware does not keep the variable, the machine registers anew after the restart and
  someone signs in again.
- The wish holds for 15 minutes.
- The server orders at most two restarts for one run. A machine that still did not start the
  other environment, because it does not start from the network or a DHCP server names another
  boot file, is not restarted again. The run stays assigned and starts once the machine starts
  that environment.
- Where every machine is named the same boot file (below), no restart changes the environment.

**Who else holds the ports.** On Windows the Microsoft DHCP server holds UDP 67, and WDS holds 69,
4011 and, where it answers PXE itself, 67. A bind can succeed on Windows while another process takes
the traffic (README, Diagnosing a machine that does not boot), so the page names the process and
service that holds each port, and offers what fits:

- **Options 66 and 67 on the DHCP server**, pointing at DDT and `x64/bootmgfw.efi`, with ProxyDHCP
  off. It is the primary path today (README, Reaching DDT from a remote site). On a local Microsoft
  DHCP server the helper service sets them for the scopes the administrator picks; for another one
  the page shows the two values and the `Set-DhcpServerv4OptionValue` line.
- **DDT in place of WDS.** The helper service stops and disables WDS, and DDT binds its ports. MDT's
  LiteTouch no longer netboots from this server, which the page says before it asks.
- **DDT beside WDS.** The helper service adds DDT's `boot.wim` to WDS with `Import-WdsBootImage`,
  and replaces it there after every build. The WDS boot menu then offers LiteTouch and DDT, DDT runs
  without ProxyDHCP and TFTP, and the agent reaches DDT over HTTPS as always. The window size DDT's
  BCD asks for does not apply, since WDS writes its own. This lets an MDT shop try DDT without
  touching its network, and has to be tried on a real WDS before it is offered.
- **A DHCP server on a gateway**, such as UniFi, pfSense or a FortiGate: options 66 and 67 there, as
  the README describes, or ProxyDHCP on the same segment.

Done, on the Network boot page under "Next to DHCP and WDS" (`GET /api/netboot`):

- **The ports.** On Windows the page names the processes that hold UDP 67, 69 and 4011, read from
  the system's own table (src/DDT.Core/Windows/UdpPortOwners.cs), and says which of them is DDT,
  Microsoft's DHCP server or WDS. Windows hides the DHCP server's state from the web server's
  account, so the helper reports both services where there is one.
- **A DHCP Server on the same computer keeps UDP 67.** It takes its address on that port for itself
  alone, which fails once DDT holds the port for all addresses, and it then serves no one. So where
  that service is installed and may start (src/DDT.Core/Windows/DhcpServerRole.cs), DDT does not
  listen on 67 and answers on 4011 alone. The page says so, tells whether the DHCP server sends
  option 60, `PXEClient`, which sends machines to 4011, as WDS has it on a DHCP server, and the
  helper sets that option for the whole server or takes it off again.
- **Options 66 and 67.** The page always gives the two values and a PowerShell line for a Microsoft
  DHCP server elsewhere. Where one runs on the same computer, the helper lists its scopes with what
  they say now and sets both options for the scopes the administrator ticks.
- **In place of WDS.** After asking, the helper stops WDS and keeps it from starting, and DDT's
  listeners start again and take the ports. The page starts WDS again later.
- **Beside WDS.** The helper puts the served `boot.wim` into the WDS boot menu as `DDT` with
  `Import-WdsBootImage`. After every build and every upload from a builder it replaces that image,
  and only where WDS had one from DDT.
- Each change takes the password again, since it decides what every machine that netboots loads,
  and is audited. The helper runs fixed PowerShell: what a request names reaches it in environment
  variables, after the helper has checked each value itself.

Tried on the test VM with the DHCP Server and WDS roles: the options arrived in the scope, WDS
listed the image, stopped and started again. Tried again on a fresh Windows Server 2025 VM with a
machine that netboots next to it:

- **Next to the DHCP server, nothing set.** The DHCP server and DDT's ProxyDHCP both held UDP 67,
  the one gave the address and the other the boot file, and the machine started Windows PE. But
  after a restart of the server DDT bound the port first, and the DHCP Server served no one, with
  "unable to bind to UDP port number 67" in its log. That is why DDT leaves the port alone now.
- **Option 60.** With that change the server came up again with the DHCP Server serving its card
  and DDT on UDP 4011 and 69. Option 60 set through the page's API, the machine asked 4011, got its
  boot file and started Windows PE. Taken off again, the DHCP server no longer had the option.
- **Options 66 and 67.** Set from the page for the scope, with ProxyDHCP off, the machine starts
  Windows PE too.
- **Beside WDS**, with DDT's ProxyDHCP and TFTP off. WDS answers, loads DDT's `boot.wim`, and the
  agent reaches DDT. On that VM WDS's own `Boot\x64` folder had no `wdsmgfw.efi`, after
  `wdsutil /Initialize-Server` had timed out next to the DHCP server. WDS did not take it from
  DDT's image, with or without `\Windows\Boot\PXE` in it, and it had to be copied from
  `System32\RemInst\boot\x64` by hand. A shop whose WDS already netboots has that file.

Left: the checklist's netboot step with the default route's interface chosen (section 5).

**DHCP servers, WDS and the Linux boot environment.** The server chooses per machine only in its
own ProxyDHCP answer, and shim looks for GRUB where the DHCP answer's boot file points. So what
the ways above give is:

- **A DHCP server that names no boot file, with DDT's ProxyDHCP beside it.** Both environments,
  chosen per machine. This is a default install.
- **A DHCP server on DDT's own computer.** It keeps UDP 67, and DDT answers on UDP 4011 alone,
  where machines come once that DHCP server sends option 60, `PXEClient`. Both environments,
  chosen per machine. Next to Microsoft's DHCP Server DDT leaves the port by itself, as above. Next
  to another one, such as dnsmasq on a Linux host, `LeaveDhcpPort` on the Network boot page does
  it. Without it DDT's bind of UDP 67 fails there. The page then says which setting helps, and
  UDP 4011 and TFTP serve meanwhile; a server whose interfaces are named in configuration does
  not start at all.
- **Options 66 and 67 pointing at DDT, ProxyDHCP off.** Every machine loads the file option 67
  names, so the site has one boot environment: `x64/bootmgfw.efi` for Windows PE, or
  `linux/shimx64.efi` for Linux. A DHCP server that sends the name in option 67 alone and leaves
  the boot file field of its answer empty makes shim ask for `grubx64.efi` at the TFTP root, and
  DDT answers it there too.
- **A DHCP server that names another server's boot file.** The Linux boot environment does not
  start, with DDT's ProxyDHCP beside it or without: shim follows the DHCP server's answer and looks
  for GRUB there. Windows PE is not affected. The Network boot page says so next to the setting.
- **Beside WDS.** WDS loads DDT's `boot.wim`, so machines start Windows PE and never Linux.

**What netboot trusts.** DDT signs nothing a machine loads from the network, in either
environment. With Secure Boot on, the firmware checks shim by Microsoft's signature, and shim and
GRUB check GRUB and the kernel by Debian's. Nothing checks `base.img`, `ddt.img`, `grub.cfg` or
`site.cpio` with its `agent.json`, as nothing checks `boot.wim`. So whoever can answer a machine's
DHCP or TFTP requests in DDT's place decides what it runs, and the provisioning network is inside
the trust boundary, as it was. What follows from that for the Linux boot environment:

- The agent trusts the server through the root certificate in `agent.json`, which arrives on that
  same unchecked way.
- The start scripts take the server's time and its name and address from the kernel command line
  in `grub.cfg` as they come: the time sets a clock that is far off, and the name goes into the
  hosts file.
- Whoever sits at a netbooted machine is root on it. Ctrl+C ends the agent, and the menu it leaves
  opens a root shell, as Shift+F10 opens a command prompt in Windows PE. The shell on the second
  screen and on the serial port opens only once the agent has failed, or with `ddt.shell` among
  `LinuxKernelArguments`.
- An uploaded agent for Linux runs as root on every machine that netboots from then on, before
  anyone authorized the machine, as the agent for Windows PE runs as SYSTEM. The server checks only
  that the file is a 64-bit Linux program for x86-64, not where it comes from or what it does. The
  administrator's password, asked again for the upload, and the audit row stand in front of it.

Someone on the same network who does not answer in DDT's place can read every boot file, the
generated ones too: `site.cpio` holds the server's address, its root certificate, which is
public, and the keyboard layout, and no token or password. They can register in a machine's name,
which costs that machine its approval, as before, and replaces what the server knows of its
firmware: described as not trusting the shim, an approved machine is named Windows PE until it
registers again itself. With forged source addresses they can make the server reset a machine's
HTTP downloads through a TFTP read in its name, which sends that machine to TFTP for three minutes
and can fail a start that was loading, and they can fill the TFTP transfers, as before. They
cannot read a file outside the boot directory and the files the environment's description lists,
put a word into `grub.cfg`, reset another machine's downloads with a request on UDP 4011 that
only names its address, or change what an approved machine is named by registering a row of their
own with its adapter.

Tried in QEMU with OVMF and Secure Boot on, with the server in its container on a Linux host.
From an image that holds the Linux files as a release does, a machine the server knew nothing of
was offered `linux/shimx64.efi` and loaded shim and GRUB over TFTP and the rest over HTTP, and its
agent registered 11 to 15 seconds after power-on. A Debian 13 cloud image, assigned through the API, was written with its seed, and
Debian showed its login prompt with Secure Boot on within a minute of power-on. With the boot port
blocked, everything came over TFTP after GRUB's 16 seconds. With DDT's ProxyDHCP off and dnsmasq
naming the shim in option 67 alone, the machine started. With dnsmasq holding UDP 67 on DDT's own
host and `LeaveDhcpPort` on, DDT answered on UDP 4011 alone and the machine started. Reset five
times half a second into the HTTP read of `base.img` and started again at once, a machine reached
the agent each time after about 14 seconds, over TFTP. An agent in the Linux boot environment
wrote `DdtResume`, and the firmware started the network entry again. The lab had no real Windows
PE, so the Linux agent started once more: it read and deleted the variable and was still signed
in.

Tried on Hyper-V with the server and the agent as they ship, installed from the MSI on Windows
Server 2025 with Microsoft's DHCP Server on the same computer, so DDT answered on UDP 4011 alone.
A generation 2 machine with Secure Boot on and the template Microsoft UEFI Certificate Authority
reached the agent 13 seconds after power-on. A Debian 13 cloud image was written in 5 seconds, and
Debian started with Secure Boot on and applied its seed. An earlier trial there, with a `grub.cfg`
made by hand and a test program in the agent's place, had found that a reset during the HTTP read
made the next starts fail for one to two and a half minutes. The resets and the three minutes of
TFTP come from that, and they cure it: reset seven times, six of them in the HTTP read of the
initrds and one in Linux's first second, the machine reached the agent each time 14 to 16 seconds
later, over TFTP.

The change of boot environment was tried there too. A Linux sequence picked in Windows PE
restarted the machine into the Linux boot environment in 14 seconds, which went on without a
second sign-in, and a Windows sequence picked there went back the same way. `DdtResume` was read
and deleted each time. A run assigned on the web did the same with nobody at the machine. A
machine with the template Microsoft Windows was named Windows PE, and a Linux run assigned to it
stayed assigned, with the reason on its page and on its console. With options 66 and 67 naming the
Windows boot manager, DDT stopped restarting the machine and said why. With them naming the shim,
the Linux boot environment started without ProxyDHCP.

No Secure Boot setting of Hyper-V starts both the Windows boot manager and the shim: the template
Microsoft UEFI Certificate Authority refuses the first, the template Microsoft Windows the second.
So the change of boot environment ran with Secure Boot off. With Secure Boot on it is shown in
halves only: the Linux boot environment wrote the variable under one template, and Windows PE read
and deleted it under the other, the template changed while the machine was off.

Not tried yet: a physical PC, a firmware that trusts only Microsoft's 2023 CA, and the change of
boot environment on one firmware with Secure Boot on, which needs a PC that trusts both.

Hyper-V's Default Switch does not stand in for a network here. Its DHCP is Windows' connection
sharing, and on the test host it answered no machine on the switch any more from the moment DDT,
in a VM, sent its first ProxyDHCP offer, and stayed silent with DDT stopped. Connection sharing is
known to switch its DHCP off when it sees another DHCP server, which fits; it was seen once and not
tried again. DDT on the host itself shares the switch's address and never set that off. The test
went on with the DHCP Server role in the server VM. The quick start has to warn of it (section
4.11).

### 4.10 Windows images from an ISO, a folder or MDT

- **An ISO.** The Images page takes a Windows ISO and reads `sources\install.wim` or `install.esd`
  from it, with the refusals of a WIM upload today.
- **A folder on the server.** A 5 GB upload through the browser is the slowest part of a first
  deployment. The page imports a WIM, ESD or ISO from a path on the server, which is stored by its
  SHA-256 like an upload. Only paths below folders that configuration names are allowed, because an
  administrator's session must not make the server read any file it can reach.
- **An MDT deployment share.** From its path, local or UNC, the import reads
  `Control\OperatingSystems.xml` and imports each operating system's image file, and reads
  `Control\DriverGroups.xml` and `Control\Drivers.xml` and makes a driver package of each driver
  group from `Out-of-Box Drivers`, matched to its model when the group's path follows the common
  `Make\Model` layout and left for the administrator otherwise. It lists what it cannot import yet,
  from `Control\CustomSettings.ini`, `Applications.xml` and `TaskSequences.xml`, with what each holds,
  and those follow once M7 and M9 give them a place. On a UNC path the server reads as its computer
  account, which the share has to allow.

Done:

- **An ISO.** An upload that is an ISO is read for `sources\install.wim` or `install.esd`, which
  takes its place, and goes on as that WIM (src/DDT.Server/Images/IsoImages.cs). An ISO without one
  is refused. Windows media are UDF, which DiscUtils reads; it is MIT, and
  THIRD-PARTY-NOTICES names it.
- **Folders.** The import folders are `import` in the store, which every server has, and what
  `DDT:ImportFolders` names, in `ddt.ini` as `[DDT:ImportFolders]` with `0 = D:\DeploymentShare`.
  The Images page lists the WIM, ESD and ISO files below them, up to four folders deep, and imports
  one at a press. A path that is not below an import folder is refused, and so is one that leaves
  it through a link.
- **The import.** `POST /api/images/import` starts it and answers at once. One runs at a time. Each
  file is copied next to the uploads and then goes the way of an upload whose bytes have arrived,
  so the same checks decide and the same rows and audit entries result. The page follows it over
  the hub (`importChanged`), with what became of each file. The source stays where it is.
- **An MDT deployment share** is an import folder, or a folder right below one, with
  `Control\OperatingSystems.xml`. The page shows its image files with the operating systems MDT
  lists in each, and the folders of Out-of-Box Drivers that hold drivers, each of which becomes a
  driver package named after its path. Where the path ends in a maker DDT knows by name and a
  model, such as `Dell Inc.\Latitude 7440`, the package is for that model; otherwise it is for
  none until someone sets it. The dialog names the applications, the task sequences and the
  sections of `CustomSettings.ini`, which are not imported.

Left: an import folder is named in configuration and not on a page, so an MDT shop edits `ddt.ini`
once. A command at an elevated prompt would spare that, as `settings netboot-interface` does for
the network card.

### 4.11 Documentation

A quick start at the top of the README, a page long, and a guide for MDT users that maps what they
know to DDT: the deployment share to the library, `LiteTouchPE_x64.wim` to the boot image, Update
Deployment Share to Build, selection profiles to the boot image flag on driver packages,
`CustomSettings.ini` to rules and the deployment defaults, `Bootstrap.ini` to nothing because the
boot image carries the server's URL, the Deployment Wizard to the console at the machine, and
monitoring to the machine's page. The rest of the documentation stays M12.

Done: the quick start is in the README, and the guide is [coming-from-mdt.md](coming-from-mdt.md).
The guide also says how to try DDT next to MDT in the WDS boot menu, and what DDT does not do yet.
Its pictures are in `docs/images`, taken from the web UI with sample data, and the console's from
the test VM.

## 5. The checklist at the first start

A card for administrators on the first page, whose items tick themselves when the server sees them
done, pushed over the hub like every other change:

1. Change the first password (section 4.5).
2. Trust DDT's root in the browsers of those who manage it (section 4.6).
3. Netboot: the interface and the way machines reach DDT (section 4.9).
4. The boot image: Build, or the builder for another PC (sections 4.7 and 4.8).
5. A Windows image: from an ISO, a folder, an MDT share or an upload (section 4.10).
6. A task sequence from a template that erases the disk, applies the image and sets up Windows from
   the deployment defaults. [Standard Client Task Sequence]
7. Netboot a machine: what it shows, and its page, which appears as it registers.

It can be dismissed, and the Server page keeps it.

Done: `GET /api/server/checklist` says for six of the steps whether the server sees them done: the
first password's file is gone, a network card answers netboot, the boot directory holds a build, the
library holds an image, a task sequence exists, a machine has registered. It also says whether the
Linux boot environment can be started (`linuxBoot`), and the boot image step counts as done with
either: on a server without a boot image the step then reads that machines netboot into the Linux
boot environment, and that installing Windows takes the boot image as well. The card
(src/DDT.Web/src/setup) is on the Machines page and on the Server page for administrators, links
each step to its page, and goes once all six are done. A step ticks itself from the hub's pushes
for machines, images, sequences, the boot image and the network boot settings, without the list
being read again. Trusting the root is a step without a mark, since the server cannot see a
browser's trust. Dismissing is kept in the browser, so the card on the Machines page stays away
there and the Server page still shows it. The starting sequence is the Install Windows template
that exists already.

Left: the netboot step does not choose the default route's interface for the administrator, who
picks it on the Network boot page, or in setup, which offers it (section 3.1).

## 6. Not planned

- **A boot image or Windows PE in a release.** The ADK's licence does not allow passing Windows PE
  on, so every path builds it on the shop's own machine (section 8, question 5).
- **Linux images written from Windows PE.** The same licence allows Windows PE for installing and
  recovering Windows. The Linux boot environment writes Linux images, and the agent in Windows PE
  refuses a sequence that writes one (section 8, question 5).
- **Windows installed from the Linux boot environment.** Windows sequences run in Windows PE, so
  installing Windows takes the ADK and a boot image.
- **Building the boot image on Linux with wimlib.** wimlib can add files to `boot.wim` but not the
  PowerShell components or drivers, which need DISM.
- **Docker Desktop or WSL on Windows**, which do not deliver broadcasts to the container.
- **A virtual machine appliance.** The Windows service and the Linux line cover the same need.

## 7. Order of work

1. Releases, the bundled agent and console, the default boot target and the DNS name in the
   certificate. They are small and let testers run DDT without building it, so they can ship first.
2. The service hosting, the bootstrap file, the first password in a file, the database of section
   4.4, and what section 4.3 lists as different on Windows.
3. The MSI, `install.ps1` and the winget manifest; `install.sh`.
4. The helper service, the Build button and the ADK install; the boot image upload and the builder
   for another PC.
5. Netboot at the first start: the ports' owners, DHCP options, and DDT in place of or beside WDS.
6. The checklist, the imports from an ISO, a folder and an MDT share, and the starting sequence.
7. The quick start and the guide for MDT users.

All seven steps are built, with what each section lists as left. The measure below holds as far as
machines go, timed to the first question of Windows' own setup.

The milestone is done when section 1's measure holds on a fresh Windows Server 2025 VM on the Hyper-V
test host, timed from the command to a Windows 11 desktop, and on a fresh Ubuntu VM with the boot
image from the builder.

Measured on the Windows Server 2025 VM, with the MSI from a folder instead of a download, every
step started by a script through the API, and a second VM as the machine: 16 seconds for the
command, 85 for the ADK, 2 minutes 16 for the boot image, 27 seconds for a Windows 11 ISO from the
import folder and a second for the sequence, so 4 and a half minutes for the server. The machine
took 17 seconds from power-on to DDT's console, 3 minutes for the run and 3 more for Windows'
own setup, which then asks whom the PC is for: 6 minutes 12. Together under 11 minutes, of the 30.
The administrator's own minutes, in the browser, are not in that.

On the Ubuntu VM (section 3.2), with Docker there and the image built locally instead of pulled:
8 seconds for the script, 123 for the builder on a Windows PC that has the ADK, 39 for the ISO over
the network and its import, and 5 minutes 49 for the machine, so under 9 minutes.

The same chain ran on a fresh Windows Server 2019 VM, the floor: the command in 13 seconds, the
ADK in 130, the boot image in 135, and the machine netbooted from it and got its Windows 11. There
the DHCP server was another computer, and DDT answered next to it as ProxyDHCP.

## 8. Open questions for the maintainer

1. **Windows Server as a production host.** Answered: yes, and first. Section 3.1 is built, and the
   measure of section 7 was taken there.
2. **The database on one server.** Answered 2026-09-29: (a) of section 4.4, SQLite, with SQL Server
   beside PostgreSQL for a shop that wants a database server.
3. **The place in the roadmap.** Answered: M6.6. M7 was merged first, on 2026-09-28.
4. **Code signing.** Answered 2026-10-04: the maintainer has a code signing certificate in his own
   name, with SSL.com's signing service, and signs the installers with it, and perhaps other parts
   that run elevated. The release workflow signs through SSL.com's GitHub action (section 4.1).
   Left: the environment `signing` with its secrets, which the maintainer sets up, and the first
   signed release, which is the first time the signing jobs run.
5. **The ADK licence.** Confirmed 2026-10-04 from the licence of ADK 10.1.26100 (`Docs\Eula` in the
   kit's folder): only code marked as sample may be passed on, and its section 6 forbids
   publishing or distributing the software. So Windows PE stays out of every release. The same
   licence allows Windows PE for installing and recovering Windows, and for no purpose it does not
   name, and writing a Linux image is not named. Answered 2026-10-07: Linux images are written
   from the Linux boot environment, which every release brings (sections 4.2 and 4.9), and Windows
   PE is used for Windows only.
6. **An MSI.** Answered 2026-09-29: an MSI, built with WiX, and since 2026-09-30 without a
   `setup.exe` around it.
7. **The port.** Answered 2026-10-04: 8443 on every server. It collides with nothing a server
   usually runs, and a reverse proxy finds DDT on the same port everywhere. 443 stays a parameter.
8. **Certificates from AD CS.** Answered 2026-10-04: later, and in full then. It is in the roadmap
   under Ideas for later.
9. **Windows Server versions.** Answered 2026-09-29: Server 2019 and Windows 10 1809 and later, build
   17763, which the MSI and `install.ps1` check. The whole chain and the time zone check ran on
   Server 2019 (sections 4.3 and 7).
10. **SQL Server's native network library.** Answered 2026-10-01: it ships with the MSI, with an
    additional permission in NOTICE. Microsoft documents the client's managed networking on Windows
    as meant for testing, though it worked on the test VM, Windows authentication included.
