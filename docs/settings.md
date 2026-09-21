# Settings

This is the plan for moving admin settings from configuration files to a settings page. It was
written against commit 11acba4, and every file:line reference points to that commit. "Framework
behavior" marks ASP.NET Core behavior that has not been tested in this repository. M6.5 has to prove
each such behavior with a test before relying on it.

## 1. The rule

The maintainer's rule is: "Settings admins have to shut down the server for and edit files should
be as low as possible, it should be in a settings page." For DDT this means that every setting an
admin changes during normal operation lives on a settings page in the web UI. A change there is
validated when it is saved, applies without a server restart, and leaves an audit row.

Configuration (environment variables, appsettings.json, the command line) keeps only what the
server needs before it can serve that page (section 2). It also stays available as an override,
both for recovery and for installs managed as code. Values baked into the boot image are the only
other exception (section 4).

The move happens with the real UI in M6.5. From M5 on, every new setting is built so that the move
is only a change of source (section 7).

## 2. What stays in configuration (class A)

A setting stays in configuration only if at least one of these holds:

- The server needs it before it can open the database or decrypt stored secrets.
- It decides what the process registers or binds before `Build()` (Program.cs:28-68).
- A wrong value would make the page unreachable, so the page could never be the fix.

The page shows these settings read-only in a Server panel (section 6).

| Setting | Why it stays |
|---|---|
| ConnectionStrings:ddtdb | The settings store is in this database (DataServiceCollectionExtensions.cs:23-37). |
| DDT:StorePath | Holds the key ring that decrypts stored secrets, and the SQLite file (DataServiceCollectionExtensions.cs:25-30, DdtAuthenticationExtensions.cs:42-44). |
| DDT:Roles | Set per process, and decides what is registered before Build (Program.cs:28-29,64). One database value cannot describe two processes with different roles. |
| DDT:RequireHttps | Sets the cookie names and Secure policy at composition (Program.cs:43-47, DdtAuthenticationExtensions.cs:79,84-86), and refuses to start without HTTPS (HttpsConfigurationCheck.cs:46-55). A change renames both cookies and signs everyone out. |
| DDT:Https:GenerateSelfSignedCertificate | Runs before Build, so that a fresh install can serve the page at all (Program.cs:32, CertificateBootstrap.cs:17). Since M5 it means that DDT may make its own root and issue, renew and reissue the server certificate from it. |
| DDT:Https:SubjectAlternativeNames | The names the server certificate must carry, read before Build. Since M5 DDT issues the certificate from its root again whenever one of them is missing, not only at first start. It also seeds the page's server names (section 3), and it never locks them. |
| Kestrel:Endpoints:* | The listener that serves the page. |
| Kestrel:Certificates:Default:Path, KeyPath, Password | Where the certificate is stored. A wrong path stops startup. The page manages the content (5.2). |
| DDT:Pxe:HttpBootPort | A Kestrel endpoint injected before Build (PxeHostingExtensions.cs:30-34). A port that is already in use stops the whole host. |
| DDT:Pxe:BootDirectory | Everything below it is served anonymously over TFTP and HTTP (BootFileResolver.cs:9-10,54-79, BootHttpEndpoints.cs:57-63). As a page field, one edit or one stolen admin session could publish the key ring, the TLS key and the database. |
| Kestrel:Endpoints:Boot:* | Generated from HttpBootPort after every other source, so admins cannot set it (PxeHostingExtensions.cs:27-34). |
| ASPNETCORE_URLS, Urls, ASPNETCORE_HTTP_PORTS, ASPNETCORE_HTTPS_PORTS | Framework listener settings, ignored once any endpoint is declared (HttpsConfigurationCheck.cs:25-32,64-69). |
| AllowedHosts | Host filtering runs before any page, so a wrong value locks every browser out. |
| OTEL_* | The exporters are built once at startup (DDT.ServiceDefaults Extensions.cs:91-96), and OTEL_EXPORTER_OTLP_HEADERS may carry credentials. |
| ASPNETCORE_ENVIRONMENT | Development switches such as HSTS (Program.cs:90-93) and /health (Extensions.cs:121-131). Production leaves it unset. |

Two changes shorten this list in practice:

- **Boot directory default.** DDT:Pxe:BootDirectory defaults to `<StorePath>/boot` instead of the
  fixed /var/lib/ddt/boot (PxeOptions.cs:16). For the default store this is the same path.
  PxeSetup refuses a boot directory that is:
  - the filesystem root
  - the store path or one of its parents
  - a directory inside `<StorePath>/keys`
  - the folder of any certificate or key file configured under Kestrel, or a folder above it
- **Container defaults move into the Dockerfile.** They leave build/compose.yaml:19,28-30 and become
  ENV lines in the final stage of build/Dockerfile, next to `EXPOSE 8443` (Dockerfile:39):
  - DDT__StorePath=/var/lib/ddt
  - Kestrel__Endpoints__Https__Url=https://0.0.0.0:8443
  - both certificate paths under /var/lib/ddt/certs

  These must not become defaults in code, for two reasons. Once any Kestrel endpoint is declared,
  Kestrel ignores the URLs that Aspire and launchSettings.json assign (HttpsConfigurationCheck.cs:25-32,
  AppHost.cs:11-14, launchSettings.json:8,17). And CertificateBootstrap writes files whenever both
  certificate paths are set (CertificateBootstrap.cs:22-33).

After M6.5, a standard container install configures only three things:

- ConnectionStrings__ddtdb, pointing at the PostgreSQL service that compose then includes, with that
  service's password
- DDT__Roles, through the compose variable DDT_ROLES
- the names for the first certificate, through DDT_HOSTNAMES into DDT__Https__SubjectAlternativeNames

Some installs also need:

- DDT__RequireHttps=false behind a TLS terminating proxy
- OTEL_* for telemetry
- DDT__Pxe__HttpBootPort when port 8080 is taken

Everything else is on the page.

Some keys are not settings at all and never appear on the page:

- **Developer and container tooling:** DDT_DESIGN_TIME_CONNECTION, launchSettings.json, the AppHost
  resources, the Vite variables, compose DDT_TAG and the Dockerfile build arguments.
- **Build and test script parameters:** Start-DevHost, Publish-Agent, New-TestVm, and Build-BootImage
  -Destination and -WorkDirectory.
- **Agent command-line flags.** startnet.cmd never passes any (Build-BootImage.ps1:358).
- **ASPNETCORE_FORWARDEDHEADERS_ENABLED,** which is inert by design (DdtForwardedHeadersExtensions.cs:31-33).
- **Constants that stay constants:**
  - UDP 67, 4011 and 69 (PxeHost.cs:17-19)
  - the /boot/ route (BootHttpEndpoints.cs:59)
  - the TFTP retransmit timings (TftpLimits.cs:20-24)
  - the first administrator (IdentityBootstrap.cs:22-24)
  - values the agent and server must agree on, such as MaxLinesPerBatch (AgentLimits.cs:9-10,
    MachineLogLimits.cs:9)

## 3. The settings page

**Who may read and write.**

- Every read and write of a settings section needs the Administrator role (the policy is at
  DdtAuthorizationExtensions.cs:23-26). Every section does at least one of these things:
  - holds a credential
  - decides who signs in, and with which role
  - decides which machines deploy unattended
  - runs code as SYSTEM on netbooting machines
- Viewers keep GET /api/deployments/options (DeploymentEndpoints.cs:26). It returns three booleans
  (DeploymentEndpoints.cs:33-39) and must stay limited to them once it reads the snapshot.

**Fields marked "Admin, re-auth".** These also need a fresh proof of identity, a re-auth token from
section 6.

- The token needs the admin's password, and the TOTP code when the account has one.
- An account without a local or directory password cannot change these fields.
- The main reason is the agent upload. It turns an admin session into code that runs as SYSTEM on
  every netbooting machine, and the agent checks only the size and hash announced by the same server
  (AgentUpdate.cs:128-135, README.md:759-762). README's security model has to say this.

**How a change applies.**

- "Live" means the next use reads the new value.
- "Subsystem restart" means the named component is rebuilt inside the running process on save (5.2).
- No setting on the page needs a server restart.
- The defaults shown are today's defaults.

### Deployment (section `deployment`, keys under DDT:Deployment)

A change applies to deployments that start after the save. Once groundwork (d) from section 7 is
done, a deployment keeps the non-secret values it started with, and the passwords are read when the
agent fetches them. Until then, the answer file is rendered when the agent fetches it
(AgentDeploymentEndpoints.cs:300).

| Key | Type | Default | Secret | Applies | Who |
|---|---|---|---|---|---|
| TimeZone | Windows time zone id | unset: Windows picks from the locale | | live | Admin |
| Locale | culture name | unset: image language, else en-US | | live | Admin |
| Keyboard | input locale | unset: the locale | | live | Admin |
| LocalAdministrator:Name | 1 to 20 characters | Admin | | live | Admin |
| LocalAdministrator:Password | text | unset: no account, Windows asks | yes | live | Admin |
| Domain:Name | DNS name | unset: workgroup | | live | Admin |
| Domain:OrganizationalUnit | DN | unset | | live | Admin |
| Domain:UserName | DOMAIN\user or user@domain | unset | | live | Admin |
| Domain:Password | text | unset | yes | live | Admin |

### Machines and zero touch (section `machines`, keys under DDT:Machines)

| Key | Type | Default | Secret | Applies | Who |
|---|---|---|---|---|---|
| RequireWebApproval | bool | false | | live | Admin |
| MaxWaitingPerAddress | integer, 1 to MaxWaiting | 100 | | live | Admin |
| MaxWaiting | integer, at least 1 | 10000 | | live | Admin |
| ZeroTouchNetworks | CIDR list | empty: zero touch off | | live | Admin, re-auth |

Machines that are already approved are not re-evaluated.

### Network boot (section `pxe`, keys under DDT:Pxe)

Every key here applies through a subsystem restart of the PXE listeners. That ends any TFTP transfer
in progress (TftpListener.cs:72-80), and the page says so.

| Key | Type | Default | Secret | Applies | Who |
|---|---|---|---|---|---|
| Interfaces | interface names or local IPv4 addresses | empty: serve nothing | | restart: PXE | Admin |
| EnableProxyDhcp | bool | true | | restart: PXE | Admin |
| EnableTftp | bool | true | | restart: PXE | Admin |
| TftpSinglePort | bool | false | | restart: PXE | Admin |
| TftpMaxWindowSize | integer, 1 to 64 | 4 | | restart: PXE | Admin |
| MaxConcurrentTftpTransfers | integer, at least 1 | 128 | | restart: PXE | Admin |
| AuthorisedRelayAgents | IPv4 addresses | empty | | restart: PXE | Admin |
| BootTargets:{arch}:Method | Tftp or Http | no targets | | restart: PXE | Admin |
| BootTargets:{arch}:BootFile | path, or http(s) URL for Http | none | | restart: PXE | Admin |
| BootTargets:{arch}:ServerAddress | IPv4 | address of the arrival interface | | restart: PXE | Admin |
| BootTargets:{arch}:ServerHostName | ASCII, at most 63 | the server address | | restart: PXE | Admin |
| BootTargets:{arch}:AdvertiseBootServerDiscovery | bool | false | | restart: PXE | Admin |

How the page fills in these fields:

- {arch} is picked from the ClientArchitecture member names (ClientArchitecture.cs:10-29), not typed.
- For BootFile, the page offers the two boot managers in the boot image layout (Build-BootImage.ps1:19-20):
  - x64/bootmgfw.efi (2011 CA)
  - x64/bootmgfw_ex.efi (2023 CA)
- The page builds Http boot URLs from a server name, HttpBootPort and the /boot/ route.
- HttpBootPort and BootDirectory are shown read-only (section 2).

### Sign-in and directories (sections `ldap` and `oidc`, keys under DDT:Ldap and DDT:Oidc)

LDAP changes apply at the next sign-in, because the scoped services copy their options for each
instance (LdapAuthenticator.cs:15, DirectorySignInService.cs:20).

| Key | Type | Default | Secret | Applies | Who |
|---|---|---|---|---|---|
| Ldap:Enabled | bool | false | | live | Admin |
| Ldap:Host | host name | empty | | live | Admin, re-auth |
| Ldap:Port | 1 to 65535 | 636 | | live | Admin, re-auth |
| Ldap:Transport | Ldaps, StartTls, UnencryptedDangerous | Ldaps | | live | Admin, re-auth |
| Ldap:BaseDn | DN | empty | | live | Admin |
| Ldap:BindDn | DN | empty | | live | Admin |
| Ldap:BindPassword | text | empty | yes | live | Admin |
| Ldap:UserFilter | LDAP filter containing {0} | (&(objectClass=user)(sAMAccountName={0})) | | live | Admin |
| Ldap:ImmutableIdAttribute | attribute name | objectGUID | | live | Admin |
| Ldap:DisplayNameAttribute | attribute name | displayName | | live | Admin |
| Ldap:EmailAttribute | attribute name | mail | | live | Admin |
| Ldap:ResolveNestedGroups | bool | true | | live | Admin |
| Ldap:GroupRoleMap | group DN to role | empty: roles untouched | | live | Admin, re-auth |
| Ldap:Timeout | duration | 00:00:10 | | live | Admin |
| Oidc:Enabled | bool | false | | restart: scheme | Admin |
| Oidc:Authority | https URL | empty | | restart: scheme | Admin, re-auth |
| Oidc:ClientId | text | empty | | restart: scheme | Admin |
| Oidc:ClientSecret | text | empty | yes | restart: scheme | Admin |
| Oidc:DisplayName | text | Single sign on | | live | Admin |
| Oidc:Scopes | list | openid, profile, email | | restart: scheme | Admin |
| Oidc:AutoProvision | bool | false | | live | Admin, re-auth |
| Oidc:AutoProvisionRole | Viewer or Operator | Viewer | | live | Admin, re-auth |

Notes on these sections:

- GroupRoleMap keys are compared case-insensitively. Today the lookup is exact
  (DirectorySignInService.cs:160).
- Scopes can remove defaults. Today the list is get-only and configuration can only add to it
  (OidcOptions.cs:23).
- The page shows the redirect URI to register at the provider: `{origin}/api/auth/external/callback`
  (DdtAuthenticationExtensions.cs:125).
- DisplayName has no reader today. The sign-in page gets it from an anonymous providers endpoint
  (section 6).

### Certificates and proxies (sections `certificate` and `proxies`)

| Key | Type | Default | Secret | Applies | Who |
|---|---|---|---|---|---|
| Server names | host names and IP addresses | seeded from DDT:Https:SubjectAlternativeNames | | used by Generate and by the name check | Admin, re-auth |
| Server certificate and key | upload of a PEM chain and key or a PFX, or Generate | generated at first start | key: yes | restart: TLS certificate, provisional | Admin, re-auth |
| ForwardedHeaders:KnownProxies | IP addresses | empty | | restart: forwarded headers | Admin, re-auth |
| ForwardedHeaders:KnownNetworks | CIDR list | empty | | restart: forwarded headers | Admin, re-auth |

Certificate actions have these limits:

- They need both Kestrel certificate paths to be set.
- They stay locked when a PFX password is configured. Such an install keeps managing its file by hand.
- They act on the process that serves the page.

### Agent

| Key | Type | Default | Secret | Applies | Who |
|---|---|---|---|---|---|
| Agent binary (replaces DDT:Agent:BinaryPath) | upload of ddt-agent.exe | none: machines keep the boot image agent | | live: next netboot | Admin, re-auth |

- The upload goes to the default path `<StorePath>/agent/ddt-agent.exe` (AgentReleaseStore.cs:19-21).
  That file is hashed again whenever it changes (AgentReleaseStore.cs:12-13,34).
- DDT:Agent:BinaryPath stays only as a configuration override for development (Start-DevHost.ps1:71).
  While it is set, uploads answer 409.

### Logging (section `logging`)

| Key | Type | Default | Secret | Applies | Who |
|---|---|---|---|---|---|
| Logging:LogLevel:{category} | log level per category | Default Information; Microsoft.AspNetCore and two EF Core categories Warning | | live | Admin |

- The defaults move from appsettings.json:2-9 into code (5.6).
- README.md:376 recommends DDT.Pxe at Debug or Trace for netboot problems.

### Later, when someone needs them

These constants could join a section later. They are not in M6.5 scope.

- TftpLimits.MaxBlockSize, together with the -TftpBlockSize range (TftpLimits.cs:24,
  Build-BootImage.ps1:81-82)
- MachineTokenLifetimes.Resume (MachineTokenLifetimes.cs:15)
- MaxStoredLinesPerMachine (MachineLogLimits.cs:13)
- WaitingMachineLifetime (MachineLogLimits.cs:26)
- the per-address agent and sign-in rate limits (RateLimitingExtensions.cs:25-63)
- the Identity password and lockout policy (DdtAuthenticationExtensions.cs:50-54). This one needs a
  validator that reads the snapshot, because UserManager caches `IOptions<IdentityOptions>`.
- the cookie lifetime (DdtAuthenticationExtensions.cs:88-89)
- the upload chunk size, which the server already announces (ImageUploadEndpoints.cs:37)
- ProtectYourPC (UnattendWriter.cs:85-96)
- PollAfterSeconds (MachineRegistrar.cs:27). Other limits assume its value (MachineLogLimits.cs:18-20,
  DeploymentLimits.cs:14-16).

## 4. Boot image and agent values

| Value | Lives in | Can the server hand it out instead? |
|---|---|---|
| serverUrl (agent.json, -ServerUrl, --server) | boot.wim (Build-BootImage.ps1:337-347) | No: it is how the agent finds the server (AgentOptions.cs:93-127). |
| rootCertificate (agent.json, -RootCertificatePath, --root-certificate) | boot.wim | No: a trust anchor cannot come over the channel it protects (HttpAgentServer.cs:41-53). A private CA (question 5) would make it change rarely. |
| keyboardLayout (agent.json, -KeyboardLayout) | boot.wim, twice: the input locale and the name shown at the prompt (Build-BootImage.ps1:340,367-371) | Not yet (see below). |
| -AgentPath | boot.wim, as the fallback agent (Build-BootImage.ps1:328-330) | Already done: the agent updates itself from the server at every boot (AgentUpdate.cs:36-62). The page uploads it. |
| -WimLibraryPath | boot.wim (Build-BootImage.ps1:287-295,333-335) | It could ship with the agent release. That has little value, so it stays as is. |
| -TftpBlockSize, -TftpWindowSize | the BCD (Build-BootImage.ps1:215-216) | No: the boot manager reads them before any DDT code runs. The server-side caps apply (TftpMaxWindowSize on the page). |
| startnet.cmd, scratch space, BCD layout, amd64 | Build-BootImage.ps1:212-233,350-373 | No, and nothing there needs steering. |
| Agent constants: heartbeat, HTTP timeouts, retries, download, stall and give-up timeouts, log queue | ddt-agent.exe | Changed by publishing a new agent, with no new boot image. If one ever needs tuning, send it additively in AgentNextResult (AgentNextResult.cs:10-19). The contracts are frozen (AgentRelease.cs:7). |
| Disk eligibility, partition sizes, free space rule | ddt-agent.exe (DiskEligibility.cs:9-31, DiskpartScript.cs:10-21, DeploymentRunner.cs:33-36) | Yes: in M5 they become task sequence data that the server sends. |
| PollAfterSeconds | a server constant (MachineRegistrar.cs:27) | Already sent in every response. |

**Keyboard layout.** The server could send a layout as a new additive field on
AgentRegistrationResult or AgentNextResult, with the baked layout kept as the fallback. But
Build-BootImage.ps1:367-368 records that `wpeutil SetKeyboardLayout` reportedly reaches only consoles
opened after it, and the agent runs in the first one. So this needs a Windows PE test that finds a way
to switch the agent's own console (question 12).

**Rebuilding a boot image.** The page shows what the boot images should carry: the server names and
the root certificate. It also generates the Build-BootImage.ps1 command line.

- A rebuild needs Windows, the ADK and elevation (Build-BootImage.ps1:5-6).
- The output goes into the boot directory, which is read on every request (BootFileResolver.cs:45-78),
  so the server needs no restart.
- README.md:423-424 should also list the BCD TFTP block and window size among the things that need a
  new boot image.

## 5. How it works

### 5.1 Storage

**The settings table.** `ddt."SettingsSections"` (entity SettingsSection, a DbSet on DdtDbContext)
holds one row per section.

| Column | Contents |
|---|---|
| Section varchar(32), primary key | deployment, machines, ldap, oidc, proxies, pxe, logging, certificate (the server names only), and the reserved row keyring (5.4). |
| SchemaVersion int | The shape of Values. Each bump comes with a C# upgrade step that runs on load, so renames happen in code. |
| Values text | JSON of the fields that have been written. An absent field was never written (5.7). Unknown members are skipped, so an older build still starts. |
| Secrets text | A JSON object mapping each field path to its Data Protection ciphertext. |
| Version bigint | Concurrency token, incremented on every save, like Machine.TokenGeneration (DdtDbContext.cs:59-63). |
| UpdatedUtc, UpdatedByUserId (FK, SetNull as at DdtDbContext.cs:93), UpdatedByName | The last save. |

**The host state table.** `ddt."SettingsHostStates"` has the key (Host, Section). Its other columns
are AppliedVersion, State (Applied or Failed), Message, Detail and UpdatedUtc. For pxe, Detail holds
the host's candidate interfaces as JSON.

- Only the process on that host writes its rows, and only for the sections it applies with a subsystem
  restart: pxe, oidc and proxies.
- The process refreshes its rows every 5 minutes.
- Rows that have not been refreshed for a day are deleted.
- The page shows a host as Pending while its AppliedVersion is older than the section's Version.

**Why one row per section.** A section is one form on the page and one unit of validation. The
existing cross-field rules already stay inside a section:

- the domain needs its passwords (DeploymentOptionsValidation.cs:50-78)
- a TFTP target needs a ServerAddress when TFTP is off (PxeSetup.cs:78-89)

Two admins can edit different sections without conflicting. A key/value table would need the
reflection binder to turn rows back into objects.

**Stored types.** The stored types are the existing option classes, serialized through a
source-generated SettingsJsonContext in DDT.Server. The existing validators therefore keep working.

- Secret properties are marked `[JsonIgnore]` and stored in the Secrets column.
- Collections become settable and are replaced on read, never populated:
  - LdapOptions.GroupRoleMap (LdapOptions.cs:35) gains an OrdinalIgnoreCase comparer.
  - OidcOptions.Scopes (OidcOptions.cs:23) uses its defaults only when the field is absent.
  - PxeOptions.BootTargets (PxeOptions.cs:40) keeps its OrdinalIgnoreCase comparer.
- The stored pxe document leaves out HttpBootPort and BootDirectory. The snapshot fills them in from
  configuration.
- DDT.Server gets a reference to DDT.Pxe, so that the server can validate the pxe section.
  - DDT.Server does not reference it today (DDT.Server.csproj:20-21).
  - DDT.Pxe depends only on Core and Protocols (DDT.Pxe.csproj:8-9), and it stays free of EF.

**Database or files.** A store of JSON files under `<StorePath>/settings` was considered. It would
need no table and no poll, and it would survive the SQLite wipe. The database store wins for three
reasons:

- The pxe section is written by the web process and read by the pxe process. DDT:Roles allows those to
  be separate processes on different hosts against one database (DatabaseInitializer.cs:16-17).
- The audit rows are saved in the same transaction as the setting.
- Compose needs PostgreSQL for M5's schema changes anyway (groundwork (a) in section 7).

If running separate processes is not a goal, the file store is simpler (question 1).

**PostgreSQL and SQLite.**

- On PostgreSQL, one migration adds both tables. MigrateAsync serializes concurrent starts
  (DatabaseInitializer.cs:16-17,27-33). Plain text columns are enough.
- SQLite stays for development only. The new tables change the schema fingerprint, so development
  databases are recreated once (DatabaseInitializer.cs:55-63). After that, every schema change drops
  the page settings together with the file. Values held in configuration come back through the import.

### 5.2 Delivery to consumers

There are no new interfaces. The pieces are:

- **SettingsSnapshot.** Immutable. It holds every section already validated, decrypted and parsed,
  including ZeroTouchNetworks, the ForwardedHeadersOptions that `TrustOnly` produces, and the
  PxeOptions. It also records each section's problems and locks. Exactly one function builds it, from
  the stored rows plus the configuration overrides.
- **DdtSettings** (singleton). It has three members:
  - `Current`, a volatile reference to the snapshot
  - `GetChangeToken()`, a CancellationChangeToken that is replaced on every publish
  - `Publish()`, which has no database access

  `Current` starts as the code defaults, so logging has its levels before the store is loaded.
- **SettingsStore** (scoped). It uses DdtDbContext, IDataProtectionProvider and TimeProvider.
  - `LoadAsync` reads the rows.
  - `SaveAsync(section, update, actor, CancellationToken)` writes the row and its audit rows in one
    SaveChanges, then publishes.
- **SettingsService** (a BackgroundService registered after DatabaseInitializer).
  - `StartAsync` runs the import (5.7), loads and publishes. Hosted services finish starting before
    Kestrel binds (framework behavior), so the first request sees the loaded snapshot.
  - `ExecuteAsync` polls (Section, Version) every 15 s with a PeriodicTimer on TimeProvider. It reloads
    every section whose version differs from the snapshot's and refreshes this host's state rows.

**Publishing and reading.**

- Publish runs behind one lock and rebuilds the snapshot from the rows while it holds that lock, so two
  saves of different sections cannot drop each other's change.
- Consumers read `settings.Current` once per request or decision. For example, KeepsApprovalOnNetboot
  (DeploymentService.cs:381-385) reads zero touch and the proxies from one snapshot.

**Consumers that change.**

- UnattendRenderer.cs:12,30
- DeploymentService.cs:28-30,41,44,381-385
- MachineRegistrar.cs:198-199
- AgentEndpoints, MachineEndpoints, and DeploymentEndpoints.cs:31-39
- LdapAuthenticator and DirectorySignInService, which take the LDAP section once per scope
- ExternalLoginEndpoints.cs:43,66,91
- AgentReleaseStore.cs:19-21
- ListedProxies.cs:23

**Framework-owned options.** Options that DDT does not own (`OpenIdConnectOptions` named "oidc", and
`LoggerFilterOptions`) get a bridge: an IConfigureNamedOptions or IConfigureOptions that reads the
snapshot, plus an IOptionsChangeTokenSource that returns `GetChangeToken()`. OptionsMonitor then drops
its cached value when the token fires (framework behavior).

**Why not a configuration provider over the database.**

- It would load before DatabaseInitializer has migrated the schema, and before DI exists, so it could
  not use the key ring to decrypt secrets.
- The reads at composition time (DdtAuthenticationExtensions.cs:33, PxeHostingExtensions.cs:21-25,
  DeploymentServiceCollectionExtensions.cs:26-44) would need rewriting anyway.
- Decrypted secrets would sit in IConfiguration, where a configuration dump prints them.

**Why not `IOptionsMonitor<T>` for DDT's own sections.** The option classes use set, because the
binding generator skips init accessors, so the one instance a monitor caches could be changed by any
consumer for all the others. Parsed objects would still need their own holders, which would leave
two ways to reach the same value.

**The stored document is the desired state, and it is never reverted.** When applying a subsystem
restart fails, that host keeps a safe running state and reports Failed in SettingsHostStates and in an
audit row. What "safe" means is defined per subsystem below.

**PXE.**

- `PxeSetup.Create` is split into `FindProblems` and `Create` (it throws at PxeSetup.cs:111-115).
- PxeHost gets `ApplyAsync(PxeSetup next, CancellationToken)`, serialized by a SemaphoreSlim. It stops
  the listeners (PxeHost.cs:114-122) and starts them with `next`.
  - If a bind fails, it stops whatever did start, starts the previous setup again, and returns the
    failure.
  - With no previous setup (at startup), the listeners stay stopped.
  - Sockets bind the wildcard address (PxeHost.cs:76,84,94), so Kestrel is not touched.
- The desired setup and the change token reach PxeHost as two delegates wired in DDT.Host, which
  references both projects (DDT.Host.csproj). There is no separate applier service, and DDT.Pxe does
  not reference DDT.Server. DDT.Host records each result.
- The HTTP boot gate reads the setup that PxeHost applied last, instead of values captured at startup
  (PxeHostingExtensions.cs:49, BootHttpEndpoints.cs:25-48).
- Interfaces are enumerated again on every apply; today this happens once (NetworkInterfaceMap.cs:17).
  The page gets a Rescan action, and README.md:295 ("restart DDT after changing them") no longer holds.
- A bind failure caused by a configuration value still stops the host, as PxeHost.cs:39-40 intends.
  A bind failure caused by a stored value is reported instead (question 3).

**OIDC.**

- The `AddOpenIdConnect` registration stays, so the handler services exist
  (DdtAuthenticationExtensions.cs:106-133). Its options come from the snapshot through the bridge, and
  the scheme is added and removed with `IAuthenticationSchemeProvider.AddScheme` and `RemoveScheme`
  (framework behavior).
- While OIDC is off, the scheme is not registered. The authentication middleware builds the options of
  every registered remote scheme on every request, and an empty ClientId fails validation (framework
  behavior).
- Before a save, the candidate options are built off to the side, in this order:
  1. the same configure code
  2. the framework post-configure step
  3. `Validate`
  4. the SignInScheme check

  ExternalSignInSchemeGuard cannot check a candidate, because it reads the published options
  (ExternalSignInSchemeGuard.cs:20). It keeps running at startup.
- If applying still fails after publish, the scheme is removed, the host reports Failed, and local
  sign-in keeps working. A test publishes a broken candidate and checks that POST /api/auth/login still
  answers.
- `/api/auth/external/start` answers 404 while OIDC is off. Today it challenges an unregistered scheme
  (ExternalLoginEndpoints.cs:24,31-37).

**Proxies.**

- `UseDdtForwardedHeaders` always adds a small middleware and keeps the RemoteIpAddress check
  (DdtForwardedHeadersExtensions.cs:48-52).
- That middleware calls `ApplyForwarders` on a ForwardedHeadersMiddleware built for each snapshot from
  the `TrustOnly` output (DdtForwardedHeadersExtensions.cs:57). That this method is public is framework
  behavior to verify.

**Certificate.**

- Kestrel serves the certificate through a ServerCertificateSelector configured in code, which returns
  the pair held in memory. That this takes precedence over Kestrel:Certificates:Default is framework
  behavior to prove. The configured paths are where the pair is stored and read at startup.
- An upload or a Generate is checked before anything changes:
  - the key matches the certificate
  - the certificate is valid now
  - its names cover the Host of the saving request and every server name
- Generate used to create a new self-signed root every time (ServerCertificateFile.cs:31-41). Since
  M5 DDT keeps one root and issues from it, so Generate needs no confirmation: whatever trusts the
  root accepts the new certificate. A new root, and an upload with a new root, need the confirmation
  `certificate.newRoot`. The page says that two things must then be updated:
  - every boot image, because boot images pin the root (HttpAgentServer.cs:41-53)
  - every browser that trusted the old certificate, as README.md:232-235 advises
- A new pair is provisional. The selector records on each connection which pair it served. Unless an
  admin confirms within 5 minutes, from a connection that was served the new pair, DDT switches back.
  This rollback keeps the page reachable: outside Development the host sends HSTS (Program.cs:90-93),
  so browsers that trusted the old self-signed certificate refuse a new one with no way to click
  through.
- All certificate writes go through one lock:
  - The new files are written next to the old ones and renamed into place.
  - The previous pair stays as ddt.previous.pem and ddt-key.previous.pem.
  - At startup, a certificate and key that do not match fall back to the previous pair.
  - Key files are created owner-only from the start, as M5 does. Before M5,
    ServerCertificateFile.cs:43-45 changed the mode after writing.

**Several processes.**

- A save applies in its own process at once. Other processes pick it up within 15 s.
- Every process on one database must share the key ring (5.4).
- Interfaces is one global value, which matches the single central PXE instance of README.md:28-29
  (question 9).

### 5.3 Validation

**One problem shape.** Every validator returns `SettingProblem(string Field, string Message)`, where
Field is a path relative to the section.

- Validators are pure and return every problem at once, like `DeploymentOptionsValidation.FindProblems`
  (DeploymentOptionsValidation.cs:29-86).
- At startup, each message is prefixed with its configuration key.
- On a PUT, the problems become a ValidationProblem keyed by field, as AgentDeploymentEndpoints.cs:151
  does.
- Three validators that throw today are split into FindProblems plus a builder:
  - PxeSetup.Create (PxeSetup.cs:111-115)
  - TrustOnly (DdtForwardedHeadersExtensions.cs:92-96)
  - ZeroTouchNetworks.Parse (ZeroTouchNetworks.cs:40-43)

**Rules per section.**

- **deployment:**
  - FindProblems, with messages worded relative to the field
  - culture names must exist
- **machines:**
  - MaxWaiting is at least 1
  - MaxWaitingPerAddress is between 1 and MaxWaiting
- **machines and proxies:**
  - Networks must parse, and /0 is refused. Both parsers accept /0 today (ZeroTouchNetworks.cs:50-59,
    DdtForwardedHeadersExtensions.cs:119-129).
  - A network wider than /16 (IPv4) or /48 (IPv6) needs the confirmation `network.wide`.
  - A zero touch network that contains a listed proxy or overlaps a listed proxy network is an error
    (README.md:713-716). The check runs on a save of either section and at load.
- **ldap:**
  - When enabled, Host is required.
  - Port is between 1 and 65535.
  - UserFilter contains {0} and survives a test `string.Format` (LdapAuthenticator.cs:105).
  - Timeout is positive.
  - Every GroupRoleMap role is in `DdtRoleNames.All` (DdtRoleNames.cs:13).
  - ResolveNestedGroups=false together with a non-empty GroupRoleMap is refused. With that combination
    no groups are read at all (LdapAuthenticator.cs:179-182), so every directory user would lose all
    roles (DirectorySignInService.cs:149-178).
  - When the saving admin is a directory account, a change to GroupRoleMap or to the connection fields
    needs a successful test sign-in with the candidate that keeps them Administrator.
- **oidc:**
  - When enabled, Authority is an absolute https URL and ClientId is set.
  - Scopes contain openid.
  - The candidate options pass the checks in 5.2.
  - AutoProvisionRole is in `DdtRoleNames.All` and is not Administrator.
- **pxe:**
  - `PxeSetup.FindProblems`.
  - An interface name that matches nothing on a host is a warning. Today it is only logged
    (PxeHost.cs:45-48).
- **logging:** level names must parse.

**Warnings that need a confirmation code.** A save that triggers one of these is refused until the
update lists the code in `confirm`. The page shows them as a dialog.

| Code | When |
|---|---|
| ldap.unencrypted | Transport is UnencryptedDangerous. |
| ldap.rekey | ImmutableIdAttribute changes. This re-keys every directory account (DirectorySignInService.cs:113-116). |
| ldap.noAdministrator | No group maps to Administrator. |
| oidc.operatorRole | AutoProvisionRole is Operator. Operators can read deployment passwords by deploying (LocalAdministratorOptions.cs:7-8). |
| auth.noLocalAdministrator | An ldap or oidc save while no enabled local Administrator exists. |
| network.wide | See the machines and proxies rules above. |
| pxe.bootUrl | An Http BootFile does not use HttpBootPort and /boot/. Nothing checks this today (PxeSetup.cs:181-184). |
| certificate.newRoot | See 5.2. |

### 5.4 Secrets

The secrets are:

- Deployment LocalAdministrator:Password
- Deployment Domain:Password
- Ldap:BindPassword
- Oidc:ClientSecret

**Encryption.** They are encrypted with the existing key ring (DdtAuthenticationExtensions.cs:42-44).
The protector purpose is "DDT.Settings" plus the section plus the field, so a ciphertext cannot be
moved into another field.

**Write-only.**

- GET returns `{ isSet, unreadable, updatedUtc }`.
- PUT takes keep (the default when the field is omitted), set, or clear.
- No endpoint ever returns plaintext. Logs and audit rows name only the field.

**Bound to their destination.** keep is refused in two cases:

- for Ldap:BindPassword, when Host, Port or Transport differ from the stored values
- for Oidc:ClientSecret, when Authority differs

The answer is 400 on the secret field ("Enter it again for the new server"), plus a
`settings.refused` audit row. Without this rule, an admin or a stolen admin session could obtain a
stored secret in two ways:

- Point Host at their own server and receive the bind password in a simple bind
  (LdapAuthenticator.cs:36,73,83-98).
- Point Authority at their own provider and receive the client secret in the code exchange
  (DdtAuthenticationExtensions.cs:114-119).

The same rule applies to the test endpoints: they use a stored secret only against the stored
destination. The deployment passwords are outside the rule, because Operators can already read them
from answer files (LocalAdministratorOptions.cs:7-8). Every future secret with a configurable
destination gets the same rule.

**A secret that no longer decrypts** is reported as `isSet: false, unreadable: true`. keep is refused
until the secret is set again, and the section fails closed (5.8).

**Key ring canary.** The reserved row keyring holds a known value in protected form. A process whose
key ring cannot read it refuses settings writes and logs a warning. Every process on one database
must share the key ring. Today each process reads the ring from its own store path, so this is a
stated requirement.

**What the encryption protects.**

- It protects copies that hold only the database: a dump, or a database backup.
- It does not protect the store volume, where the key ring is plain files.
- Keep key ring backups apart from database backups, and protect them like the volume. The key ring
  can also mint an administrator cookie (README.md:755-758).

### 5.5 Audit and live push

**New AuditActions** (next to AuditActions.cs:9-21):

- settings.imported, settings.changed, settings.refused, settings.reset
- settings.applied, settings.apply-failed
- certificate.replaced, certificate.rolled-back
- agent.uploaded
- administrator.created

**Row contents.**

- SubjectId is the section.
- Detail lists the changed fields:
  - `timeZone: 'A' to 'B'` for plain fields
  - `domain.password set` or `cleared` for secrets
  - for maps and lists, only the keys added, removed or changed
- Detail is limited to 2048 characters (DdtDbContext.cs:126). A longer diff is split across several
  rows.
- Rows are written in the same SaveChanges as the settings row, the pattern described at
  DeploymentService.cs:21-24.
- Apply rows carry the host name.

**Live push.** A new `LiveNotifier.SettingsChanged(section)` sends the event settingsChanged with only
the section name, like LiveNotifier.cs:25-27. The hub admits Viewers (Program.cs:121-124), so the
payload stays that small.

### 5.7 First-start import

(This comes before 5.6 in reading order because locking depends on it; the numbering follows the
outline.)

At every start, before the snapshot is published:

- Each field that has never been written in its stored row takes the configuration value, if the key
  is present. Code defaults are never imported.
- This runs per field, also for rows that already exist.
- Secrets are encrypted on the way in.
- A `settings.imported` row names the imported fields, never secret values.

A process that starts first therefore cannot freeze its defaults over another process's configured
values. For example, a pxe host without LDAP keys writes nothing for LDAP, and the web host's LDAP keys
are imported when the web host starts.

Races and migration:

- Two processes that create the same row conflict on the primary key. The loser reads again and
  retries. Updates use Version.
- To migrate, an admin starts the M6.5 build once with the old configuration. The values are imported,
  and locked while the keys remain. After the admin removes the keys, the stored values apply and can
  be edited on the page.

### 5.6 Precedence and locking

**What locks a field.** A field whose key is present in configuration is locked. That holds for any
source and even for an empty value.

- The configuration value applies.
- The page shows the source, both spellings of the key (`DDT:Deployment:Domain:Name` and
  `DDT__Deployment__Domain__Name`), and `storedDiffers` when the stored value is different.
- A PUT keeps the stored value of a locked field.
- The stored value applies again once the key is removed.

An empty value locks so that configuration can force the safe value: no zero touch networks, no
trusted proxies, no domain.

**Collections and seeds.**

- Collections (GroupRoleMap, BootTargets, Scopes, log levels) lock as a whole.
- A collection's configured entries are read on their own, never merged with the defaults.
- DDT:Https:SubjectAlternativeNames never locks.

**Nothing may lock by accident.**

- The Logging defaults move from appsettings.json:2-9 into code.
- compose.yaml drops DDT__Pxe__Interfaces and DDT__Pxe__EnableProxyDhcp (compose.yaml:25,27), which
  pass empty or "true" defaults.

**Unknown keys.** They are refused against an explicit list of known keys per section.

- The DDT root allows its own four keys (DdtOptions.cs:11-17) and the known subsection names.
  `ErrorOnUnknownConfiguration` on the root would fail on every subsection.
- The pxe section also allows the bootstrap keys HttpBootPort and BootDirectory.
- For logging, only keys under Logging:LogLevel are checked, because logging providers add their own
  subsections.
- Since M5, every DDT section refuses unknown keys at startup (DdtConfigurationCheck), with an
  explicit list for the root.

**Other rules.**

- Configuration is read through the configuration binding source generator
  (EnableConfigurationBindingGenerator), so binding uses no reflection.
- A configuration value that fails validation stops startup, as it does today
  (DeploymentServiceCollectionExtensions.cs:14-16,37).
- Configuration overrides stay supported permanently.

### 5.8 Recovery from a bad change

**Fail closed per section.** A problem caused only by stored values does not stop startup, because the
page is where it gets fixed. Examples are a stricter rule in a newer build, a secret that no longer
decrypts, or an overlap between sections. The section fails closed instead, and the page lists the
problems.

| Section | Fails closed as |
|---|---|
| deployment | New assignments, image picks and the first Running report are refused, with the problems listed. The agent sends that report before it partitions (DeploymentRunner.cs:111,139,260). Deployments already running keep the values captured when they started. Refusing the answer file instead would hit machines whose disks are already wiped, because the agent fetches it after partitioning, applying the image and writing the boot entry (DeploymentRunner.cs:260-307). |
| machines | Zero touch is off. RequireWebApproval is true if the stored or the configured value is true. The caps take their defaults. |
| ldap | Directory sign-in is off. Local accounts keep working. |
| oidc | The scheme is not registered. |
| proxies | Nothing is trusted, and zero touch is off as well. |
| pxe | The listeners stay stopped. |
| logging | The code defaults apply. |

**Configuration override.** A present key locks its field (5.6). Examples:

- `DDT__Ldap__Enabled=false`
- `DDT__Oidc__Enabled=false`
- `DDT__Machines__ZeroTouchNetworks=` (empty)

An override needs the container to be recreated. It is meant for when the page is unreachable.

**Console commands.** These need no file edit and no restart. DDT.Host gets two verbs that run next to
the running server, for example `docker exec ddt ./DDT.Host settings reset ldap`.

- `settings reset <section>` writes the code defaults and clears the section's secrets.
- `settings create-admin` does one of two things, and prints a one-time password once:
  - creates a local Administrator
  - re-enables and resets an existing one

  This matters because IdentityBootstrap creates an administrator only while no user exists
  (IdentityBootstrap.cs:42-45).

Both commands write through the store with audit rows (actor "console"), and both need only the
database and the key ring, which the container has. Running processes apply the change within 15 s.

This is the only recovery for GroupRoleMap. Group DNs contain '=' and ',', so the map cannot be set
through environment variables, and the container's appsettings.json is in the read-only image
(compose.yaml:15).

**Certificate.** The automatic rollback (5.2) covers a certificate that browsers or agents refuse.

## 6. The API for the page

**Placement and access.**

- All routes live under `/api/settings`, inside the `/api` group, so the same-origin and antiforgery
  filters apply (Program.cs:106-109).
- They require the Administrator policy.

**Contracts.**

- The contracts live in DDT.Contracts/Settings: one small record per section, mapped explicitly in the
  endpoints and registered in DdtJsonContext (DdtJsonContext.cs:14).
- DDT.Contracts references no other project, so the option classes cannot be the API shapes.
- Explicit mapping keeps secret and bootstrap fields out by construction, and keeps the stored JSON
  independent of the API.

**Shared types.**

- `SettingsSectionView<T>`:
  - `version`, `updatedUtc`, `updatedBy`
  - `values`
  - `secrets`: field to `{ isSet, unreadable, updatedUtc }`
  - `locked`: a list of `{ field, configurationKey, environmentVariable, source, storedDiffers }`
  - `problems` and `warnings`: `{ field, message, code }`
  - `apply`, for subsystem restart sections only: per host
    `{ host, version, state: Applied | Failed | Pending, message, updatedUtc }`
- `SettingsSectionUpdate<T>`:
  - `version` and `values`
  - `secrets`: field to `{ action: keep | set | clear, value }`
  - `confirm`: the warning codes the admin accepted
- `SettingsOverview`:
  - `sections`: name, kind (live or restart), version, updatedUtc, updatedBy, lockedCount,
    problemCount, apply
  - `server`: the class A values (see the display rules below)

**Class A display rules.** Values are shown only from an allowlist:

- DDT:Roles, DDT:StorePath, DDT:RequireHttps
- Kestrel endpoint URLs and certificate paths
- DDT:Pxe:HttpBootPort and BootDirectory
- AllowedHosts and ASPNETCORE_ENVIRONMENT
- OTEL_EXPORTER_OTLP_ENDPOINT, without user info
- the provider, host and database of the connection string

Every other key shows as `{ key, isSet, source }`. Any key whose last segment is Password, Secret,
Key or Headers, and every connection string, is secret by rule.

**Re-auth.** A request that changes a re-auth field carries a token in the `X-DDT-Reauthentication`
header. Without it, the request answers 403 and names the fields. The token is protected with the key
ring, so it also works for the streamed agent upload.

**Endpoints.**

| Endpoint | Behavior |
|---|---|
| POST /api/settings/reauthenticate | Body `{ password, code }`, checked like a sign-in under the SignIn rate limiter (RateLimitingExtensions.cs:25-32). Returns a token that is valid for 5 minutes and bound to the user and their security stamp. |
| GET /api/settings | SettingsOverview. |
| GET /api/settings/{section} | The section view. The section is deployment, machines, ldap, oidc, proxies, pxe or logging. |
| PUT /api/settings/{section} | 200 with the view, including apply. 400 is a ValidationProblem keyed by field, and nothing is saved; an unaccepted warning is reported under the field "confirm". 409 answers "Someone saved {section} since you loaded it. Load it again." when the version is stale, as MachineEndpoints.cs:365-369 does. A failed apply is not an error response: it shows in apply (5.2). |
| POST /api/settings/ldap/test | Body: candidate values and secrets, and optionally a user name and password. Returns the bind result, whether the user was found, the groups, the mapped roles, and a message. Stored secrets are used only against the stored destination. The user part goes through the lockout checks of directory sign-in (DirectorySignInService.cs:62-81) without issuing a cookie, under the SignIn rate limiter. |
| POST /api/settings/oidc/test | Fetches the candidate Authority's discovery document. Returns the issuer and the redirect URI to register. |
| GET /api/settings/pxe/interfaces | The candidate interfaces of every host that runs the pxe role, from SettingsHostStates. |
| POST /api/settings/pxe/rescan | Saves the pxe section unchanged with a new version, so every pxe process applies it again. |
| GET /api/settings/certificate | Subject, SANs, issuer, root thumbprint, notAfter, and whether the pair is provisional. |
| POST /api/settings/certificate | Upload. Needs re-auth; a new root also needs `certificate.newRoot`. |
| POST /api/settings/certificate/generate | Uses the stored server names. Needs re-auth and `certificate.newRoot`. |
| POST /api/settings/certificate/confirm | Makes a provisional pair permanent. Accepted only from a connection that was served the new pair. |
| GET /api/settings/agent | `{ sha256, size, uploadedUtc, uploadedBy, source }`. |
| PUT /api/settings/agent/binary | Streamed application/octet-stream with a size limit and an MZ header check. Written to a temporary file, then renamed. Returns `{ sha256, size }` and audits `agent.uploaded` with the hash. Answers 409 while DDT:Agent:BinaryPath is set in configuration. Needs re-auth. |
| GET /api/auth/external/providers | Anonymous. The enabled providers with their DisplayName, for the sign-in page. |

Example values for the deployment section:

```json
{ "timeZone": "W. Europe Standard Time", "locale": "de-DE", "keyboard": null,
  "localAdministrator": { "name": "Admin" },
  "domain": { "name": "corp.example", "organizationalUnit": "OU=Workstations,DC=corp,DC=example",
              "userName": "CORP\\join" } }
```

The matching secrets are
`{ "localAdministrator.password": { "isSet": true }, "domain.password": { "isSet": true } }`.

## 7. Rules for new settings from M5 on, and timing

### Rules

1. Every new setting is designed for a settings section, and it lives in configuration until M6.5.
   Only a setting that passes the test in section 2 stays in configuration after that, and it is then
   added to the class A list.
2. Prefer entities to settings. Task sequences, drivers per model, and assignment by MAC or model are
   database entities with an API and a concept UI, never configuration.
3. Until the store exists, a new setting already has its final shape:
   - its own section class with defaults
   - a known-key check
   - a pure validator that returns SettingProblem
   - it is read where it is used, never at composition time, and never through `IOptions<T>` held in
     a singleton
   - secret fields are marked in a comment
   - it is documented in README as a future page field
4. Agent tunables go into server responses, additively (AgentNextResult.cs:10-19; the contracts are
   frozen, AgentRelease.cs:7). They never go into agent.json, which the server cannot rewrite
   (AgentConfiguration.cs:7-10).
5. Anything baked into the boot image states why it cannot come from the server.
6. A secret whose destination is configurable is bound to that destination (5.4). A setting that
   grants roles or trust needs re-auth.

### Timing

Do not build the store, the API or the page before M6.5.

- The maintainer decided that the move comes with the UI.
- A store without a page is worse than files: admins would need curl, or would keep using
  configuration, which wins anyway.
- The store's design also benefits from the real UI.

M6.5 then proceeds in three steps:

1. The store, snapshot, SettingsService, import, validators, API, console commands and consumer
   changes, plus tests for every framework behavior named in 5.2.
2. The page.
3. Clean-up:
   - compose drops its PXE lines
   - README is updated: its fixed numbers that became settings, the class A list, and the security
     model

### Groundwork during M5

Each item here is useful on its own. Items marked done are in the M5 groundwork commits.

- **(a) PostgreSQL in compose,** with ConnectionStrings__ddtdb. Compose sets no connection string today
  (compose.yaml:17-30), so it runs on SQLite. SQLite refuses to start after any schema change
  (DatabaseInitializer.cs:55-63), and M5 changes the schema. Done: a `db` service, with the
  password required in build/.env.
- **(b) Known-key checks for every DDT section,** with an explicit list for the root (5.6). Done:
  DdtConfigurationCheck in DDT.Host, with the binding generator on. The generator skips a property
  with an init accessor without a warning, so the option classes use set.
- **(c) SettingProblem,** used by the first M5 validator, and the three throwing validators converted
  (5.3). Done: SettingProblem is in DDT.Core, and the startup check lists the problems of every
  section in one message, each after its configuration key.
- **(d) Deployment settings:**
  - Decide for each DDT:Deployment field whether it is a global default or task sequence data.
  - Capture the non-secret inputs on the Deployment row when the deployment starts, and render the
    answer file from them. The secrets are read from configuration when the agent fetches them, so
    none is copied into a row.
  - Refuse at start when the values are invalid.
  - Register the validated DeploymentOptions instance instead of binding a second one
    (DeploymentServiceCollectionExtensions.cs:26-42). Done.

  This also closes a gap: a domain switched on between the name check (DeploymentService.cs:559-569)
  and the answer file fetch.
- **(e) The BootDirectory default and the refused roots** (section 2). Done. A relative
  BootDirectory is inside the store as well.
- **(f) OIDC provisioning fixes:**
  - Check the results of AddLoginAsync and AddToRoleAsync, and delete the new account when either
    fails (ExternalLoginEndpoints.cs:83-91).
  - Validate AutoProvisionRole at startup.

  Done. A role that does not exist stops startup. Administrator is logged as warning 880 until
  question 7 is answered.
- **(g) Container defaults** in build/Dockerfile (section 2). Done.
- **(h) Wording:**
  - The comment at DeploymentOptions.cs:7-8 becomes "configuration until the M6.5 settings page".
  - README records the class A list and these rules.

  Done.

## 8. Open questions for the maintainer

1. **Separate processes.** Is running the web and pxe roles as separate processes on different hosts
   a goal? The plan uses the database store and per-host apply states for it. If it is not a goal, a
   file store under `<StorePath>/settings` is simpler (5.1).
2. **Fail closed.** While the stored deployment section is invalid, new deployments are refused.
   Is that acceptable?
3. **PXE bind failures.** A bind failure caused by stored PXE values is reported instead of stopping
   the host. This reverses the choice at PxeHost.cs:39-40 for stored values only. Confirm.
4. **Cookie SameSite.** It is Strict or Lax depending on Oidc:Enabled at startup
   (DdtAuthenticationExtensions.cs:81-83). Either fix it at Lax (the response mode is already Query,
   line 120), or feed the cookie options from the snapshot.
5. **Certificate expiry.** Answered in M5, as proposed. The generated certificate was valid for 2
   years and never renewed (ServerCertificateFile.cs:41), so every boot image would have broken when
   it expired. DDT now makes a private root valid for 20 years, which boot images pin, and issues a
   90-day server certificate from it that it renews when 30 days are left, without a restart.
   Renewals and new names need no new boot image and no browser update.
   `DDT:Https:GenerateSelfSignedCertificate` now means that DDT may make that root and issue from it.
6. **Re-auth scope.** Confirm the list of re-auth fields, and that accounts without a password cannot
   change them. OIDC step-up with max_age=0 could follow later.
7. **Auto-provisioned roles.** AutoProvisionRole refuses Administrator and needs a confirmation for
   Operator. Confirm.
8. **Operators and settings.** Should Operators be able to read (not write) the deployment and
   machines sections? The plan says no.
9. **Several PXE hosts.** Interfaces is one global value. If several PXE hosts ever share a database,
   the pxe section needs one entry per host.
10. **Offline domain join.** Offline domain join (djoin) in M5 would take Domain:Password out of
    answer files, where Operators can read it today.
11. **The web role.** DDT:Roles cannot turn the web role off (Program.cs:50-57,106-126), so every
    process serves the settings API. Fix this in M5?
12. **Keyboard layout from the server.** It needs a Windows PE test that switches the agent's own
    console layout (Build-BootImage.ps1:367-368). Until then it stays in the boot image.
13. **Values this plan chose.** The 15 s poll, the 5 minute re-auth token, the 5 minute certificate
    confirmation, and the /16 and /48 threshold for wide networks. Change any of them?
