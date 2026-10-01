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
   and `/opt/ddt/.env` with a generated database password, `DDT_ROLES=web,pxe` and `DDT_HOSTNAMES`
   from `hostname -f`.
3. Where ufw or firewalld is active, it asks, then opens the same ports as 3.1.
4. It runs `docker compose up -d` and waits until `/api/about` answers.
5. It prints the address, the root's SHA-256 and the first password (section 4.5).

With the database of section 4.4 answered as recommended, the container needs no database service,
and the whole install is also one line without the script:

```bash
docker run -d --name ddt --network host --restart unless-stopped -e DDT__Roles=web,pxe -v ddt:/var/lib/ddt ghcr.io/davicloud-net/ddt
```

### 3.3 Upgrading and removing

The same command upgrades: `winget upgrade Davicloud.DDT`, the MSI again, or the script again, which
pulls the new image. The database is migrated at start, as PostgreSQL is today
(src/DDT.Server/Data/DatabaseInitializer.cs:27-31). The agent and the console come with the server
(section 4.2), so machines run the new agent from their next boot. A boot image is built again only
when the release notes say that Windows PE changed, and the Boot image page says so too (section
4.7).

Removing DDT keeps `%ProgramData%\DDT` unless the removal is asked to delete it: the store holds the
root key, and a new root means building every boot image again. What setup fetched from Microsoft
stays as well, as programs of their own in Apps and features.

## 4. What it takes

### 4.1 Releases

A workflow on every tag `v*`:

- A Windows job publishes the agent and the console with NativeAOT, as Publish-Agent.ps1 and
  Publish-Console.ps1 do, and the server self-contained for win-x64, so the host needs no .NET
  runtime. It builds the MSI with WiX and signs what it can (section 8, question 4).
- A Linux job builds the image with the agent and the console the Windows job published. NativeAOT
  does not compile for Windows on Linux, so build/Dockerfile takes them as files rather than building
  them. It pushes to `ghcr.io/davicloud-net/ddt` with the version and `latest`, x64 first, with a
  provenance attestation.
- The release carries the MSI, a zip of the same files, `install.ps1`, `install.sh`, `compose.yaml`
  and `SHA256SUMS`, and a winget manifest goes to winget-pkgs.

The version comes from the tag into the assemblies' informational version, which the About page
already reads (src/DDT.Host/Program.cs:84). Versions start at 0.x.

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
  Server 2019 included, as `icuuc.dll` and `icuin.dll`, and .NET 7 and later loads those. Not yet
  tried on 2019; if it falls back to NLS there, every time zone is refused and DDT carries its own ICU.
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

`Build-BootImage.ps1` stays the one implementation, so a build by hand on a PC gives the same image.

### 4.8 The boot image from another PC

A Linux server, or a Windows server without the ADK, gets its boot image from any Windows PC that has
one:

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

### 4.9 Netboot at the first start

What is served stays the administrator's choice: `DDT:Pxe:Interfaces` keeps no default
(PxeOptions.cs:11-14). The checklist asks, with the interface that holds the default route already
chosen, from the list `GET /api/settings/pxe/interfaces` gives today.

**A default boot target.** With ProxyDHCP on and no boot target, DDT answers nothing and only warns
(PxeHost.cs:299). Without a configured one, `X64Uefi` boots `x64/bootmgfw.efi` over TFTP, the 2011
boot manager that is already the default. Done: with ProxyDHCP and TFTP on and no boot target
configured, that is the one target (src/DDT.Pxe/PxeSetup.cs), and the Network boot page says so.

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

### 4.11 Documentation

A quick start at the top of the README, a page long, and a guide for MDT users that maps what they
know to DDT: the deployment share to the library, `LiteTouchPE_x64.wim` to the boot image, Update
Deployment Share to Build, selection profiles to the boot image flag on driver packages,
`CustomSettings.ini` to rules and the deployment defaults, `Bootstrap.ini` to nothing because the
boot image carries the server's URL, the Deployment Wizard to the console at the machine, and
monitoring to the machine's page. The rest of the documentation stays M12.

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

## 6. Not planned

- **A boot image or Windows PE in a release.** As far as known, the ADK's licence does not allow
  passing Windows PE on, so every path builds it on the shop's own machine (section 8, question 5).
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

The milestone is done when section 1's measure holds on a fresh Windows Server 2025 VM on the Hyper-V
test host, timed from the command to a Windows 11 desktop, and on a fresh Ubuntu VM with the boot
image from the builder.

## 8. Open questions for the maintainer

1. **Windows Server as a production host.** This reverses the rule that production is the Linux
   container with host networking. Recommended: yes, and first, because that is where MDT shops are.
2. **The database on one server.** (a), (b) or (c) of section 4.4. Recommended: (a).
3. **The place in the roadmap.** Written in as M6.6, before M7. The `m7` branch exists but has no
   commits yet.
4. **Code signing.** An unsigned MSI and `DDT.Host.exe` meet SmartScreen. Which certificate, and
   whether Azure Trusted Signing takes Davicloud e.U.?
5. **The ADK licence.** Confirm that Windows PE cannot be shipped in a release.
6. **An MSI.** Recommended over a script alone: winget installs a service through one, and MDT was one.
7. **The port.** 8443, as the container uses, or 443 when it is free?
8. **Certificates from AD CS.** Later, or part of this milestone?
9. **Windows Server versions.** Answered 2026-09-29: Server 2019 and Windows 10 1809 and later, build
   17763, which the MSI and `install.ps1` check. The time zone check on 2019 still needs a try
   (section 4.3).
10. **SQL Server's native network library.** Answered 2026-10-01: it ships with the MSI, with an
    additional permission in NOTICE. Microsoft documents the client's managed networking on Windows
    as meant for testing, though it worked on the test VM, Windows authentication included.
