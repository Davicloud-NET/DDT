# Roadmap

This is the order of DDT's milestones after M6 and what each one holds. The README's
[Where it's at](../README.md#where-its-at) says what is built. Most features below come from a comparison with the Microsoft Deployment Toolkit,
which Microsoft retired on 2026-01-06: they are what someone moving from MDT would miss, and the MDT
feature each one answers is named in brackets.

## M6.5 The real UI

A graphical console in Windows PE, a new web UI, and the settings page that
[settings.md](settings.md) plans. With them:

- **Drivers in the boot image.** Network and storage drivers from driver packages marked for Windows
  PE go into the boot image, so machines with Intel VMD or RST storage, or a network card Windows PE
  has no driver for, netboot without a change in their firmware setup. The graphical console changes
  the boot image anyway. This blocks deployments on many current laptops, so it comes first.
  [Boot image drivers from selection profiles]
- **API tokens**, so scripts use the API the web UI uses without a browser sign-in. [The MDT
  PowerShell provider]
- **A smaller boot image.** The build removes what DDT's Windows PE never uses, 37 % of `boot.wim`
  with PowerShell, which a netboot no longer has to fetch. [LiteTouchPE_x64.wim]

## M6.6 Installation

One command puts DDT on a new server, and a fresh install netboots its first machine without a
compiler, a clone of this repository, a file copied by hand or a configuration file edited. MDT shops
are the measure: MDT was an installer on a Windows Server that already ran WDS and the ADK, so
Windows Server becomes a host for DDT beside the Linux container. It comes before M7 because nobody
outside can try DDT until it installs this way. [install.md](install.md) is the plan.

- **Releases.** Tagged releases carry the server for Windows and a container image for Linux, each
  with the agent and the graphical console built in, so nobody needs the Visual C++ build tools and a
  new server serves an agent of its own version. [MicrosoftDeploymentToolkit_x64.msi]
- **DDT on Windows Server** as a Windows service, installed with `winget install` or one line of
  PowerShell, which also sets up the firewall, the certificate, the database and the first
  administrator.
- **DDT on Linux with one line**: the script sets up the container and its database, names the
  certificate after the host, starts it, and prints the address and the first password.
- **The boot image built by the server.** On Windows the Boot image page builds it, with the ADK the
  server found or installed, the server's own address and root, the flagged drivers and the agent the
  server serves, and it says when the image has to be built again. A Linux server gets its boot image
  from any Windows PC with the ADK: the page hands out a builder that uploads the result.
  [Update Deployment Share]
- **Netboot set up at the first start.** DDT finds a Microsoft DHCP server and WDS on its host and
  offers what fits: options 66 and 67 pointing at DDT, DDT in place of WDS, or DDT's boot image added
  to WDS next to LiteTouch, so a site tries DDT without changing its network. ProxyDHCP gets a
  default boot target. [WDS with MDT]
- **A checklist at the first start** in the web UI, from trusting the root certificate to the first
  deployment.
- **Import from MDT.** Windows images from an ISO or a folder on the server instead of a browser
  upload, and a deployment share's operating systems and out-of-box drivers taken over as they are.
  Its rules, applications and task sequences follow once M7 and M9 give them a place. [Import
  Operating System, Import Drivers]
- **A quick start** and a guide for MDT users: the part of M12's documentation an install needs.

## M7 The flow builder and the sequence model

The task sequence flow builder is a node editor, in the manner of Blender's shader nodes or Unreal's
Blueprints, with the elements of a flow chart such as decisions. It shows the sequence model, so the
model grows with it:

- **Groups** of steps with conditions of their own, and conditions that hold when all, any or none
  of their parts hold. [Groups, If statements]
- **More machine facts** for conditions and rules: laptop, desktop or virtual machine, memory,
  processor, TPM and Secure Boot, IP address, subnet and default gateway. [Gather]
- **Variables**, set by a Set variable step, by a rule or by an input, and used in scripts, names and
  settings. [Task sequence variables, CustomSettings.ini properties]
- **Computer names from patterns**, such as `PC-{{SerialNumber}}`. [OSDComputerName]
- **Rules that set values**, not only choose a sequence: per MAC address, model, site, by subnet or
  default gateway, or role, such as the time zone of a site. [CustomSettings.ini sections, the MDT
  database]
- **Inputs asked at the machine**, in the graphical console of M6.5, or on the web when a sequence
  is assigned: applications, language, domain and organizational unit, as the sequence declares
  them. [The Deployment Wizard]
- **Secrets for any step**, handed out while it runs, such as an account for a script that reads a
  share. [Run this step as the following account]
- **A Pause step** that waits until someone at the machine or on the web lets the run go on.
  [LTISuspend]

## M8 Linux

Built, in three parts, each a pull request that works by itself.

1. **The Linux boot environment and the agent for Linux.** Built. A netbooted Linux environment
   writes Linux images, and Windows PE is used for Windows only: the ADK's licence allows Windows
   PE for installing and recovering Windows and names no other purpose
   ([install.md](install.md), section 8, question 5).
   - The environment comes with every release, so a server starts machines in it without the ADK
     and without a build: Debian's signed shim, GRUB and kernel from packages pinned by version and
     hash, DDT's start scripts, and the agent for Linux as one static file. Every release publishes
     the packages' source code.
   - The server serves it over TFTP and HTTP beside Windows PE, and names each 64-bit UEFI machine
     the boot file of the environment its task sequence needs. A setting says what machines without
     a run start.
   - A sequence that writes a raw disk image is a Linux sequence. It runs in this environment, with
     shell scripts before and after the image. Without steps in the installed Linux it ends when
     the disk is written and first in the boot order.
   - A machine that started the other environment restarts into the right one. Its sign-in goes
     along in a UEFI variable, and where the firmware does not keep that, someone signs in again.
2. **The run goes on in the installed Linux**, as it goes on in the installed Windows. Built.
   [install.md](install.md), section 4.9, has the details.
   - The Linux boot environment hands the run over on the cloud-init seed: the agent, the run's
     state and its token go onto the seed, and cloud-init installs the agent as a systemd unit at
     the first start. So the image has to have cloud-init and systemd, and its user-data must
     leave vendor data on. The sequence editor warns of user-data that does not.
   - The agent there waits for cloud-init, then runs the sequence's steps as root: shell scripts,
     file packages, variables, pauses, restarts and shares, which sign in with NTLM and not with
     Kerberos. It reports each step with its log and ends the run only when those steps are done.
     Then it removes itself, and overwrites what held the run's token before it deletes it.
   - The server cannot see into an image, so a machine's page says since when its run waits for
     the installed Linux to call, and after ten minutes what may keep it.
   - A machine that starts from the network while its run is in the installed Linux is sent back
     to its disk. The server counts such starts and ends the run when they do not stop.
   - A sequence with such steps is of version 5. Agents take the server's agent at their next
     netboot, so no boot image has to be built for it.
   - Tried in QEMU on the cloud images of Debian 13 and Ubuntu 24.04, and on Debian 13 on a
     Lenovo ThinkCentre M72e, with Secure Boot on. Not tried on Hyper-V yet.
3. **The graphical console on Linux**, in the boot environment and in the installed Linux. Built.
   - The same console as in Windows PE, drawn on the firmware's framebuffer, with the keyboard
     read directly and typed by the layout the server sets. Shift+F10 opens a shell in the boot
     environment.
   - In the installed Linux the agent starts it as a unit of its own, as a user of its own, on a
     free terminal, and only that unit is let into its socket. It shows nothing where a display
     manager runs, where there is no framebuffer, or on a system without glibc.
   - The server offers it like the agent, from the Server page or the release.
   - Tried in QEMU and on a Lenovo ThinkCentre M72e, with Secure Boot on. Not tried on Hyper-V
     yet.

Decided for all three parts:

- **No "run as" for Linux scripts.** They run as root, and a script that wants another user
  switches to it itself.
- **No domain join for Linux.**
- **Shares only in the installed Linux**, so with part 2. The Linux boot environment connects none.
- **One boot environment per site** where the DHCP server names the boot file itself in options 66
  and 67, or where machines boot through WDS: DDT chooses per machine only in its own ProxyDHCP
  answer.
- **The source code of the Linux boot environment goes out with each release**, not on request.
- **The kernel moves only with the lock**: a release gets a newer kernel when someone changes the
  pinned packages and the environment passes its checks again.

## M9 Applications and Windows configuration

Three parts, each a pull request that works by itself. All three are built.

1. **Applications.** [Applications]
   - The library, under Library > Applications: an entry holds files and a silent command, with
     success and restart codes. An upload suggests the command for MSI, WiX Burn, Inno Setup, NSIS
     and InstallShield. An MSIX or Appx package is provisioned for all users.
   - Detection an upload suggests: an MSI's product code; the uninstall key of a Burn bundle, of an
     Inno Setup installer (`<AppId>_is1`) and of an NSIS installer that writes one literal key.
   - Detection, optional per entry: an MSI product code, an uninstall key, or a file or a package
     family, each with a minimum version. Without one the application is always installed.
   - Dependencies, installed first, and bundles: ordered lists of applications.
   - An Install applications step installs a list of applications and bundles, and the names a
     value holds. Where an installer asks for a restart, the step restarts and goes on. A failed
     application stops the step, unless its entry lets the run go on. After a power loss detection
     decides; without it the application is installed once more.
   - An Applications input: a tick list on the web and in the console, with a search on long
     lists. Rules name applications.
   - The MDT import brings a share's applications and bundles
     ([coming-from-mdt.md](coming-from-mdt.md)).
   - winget packages, by ID and an optional version, installed for the machine:
     - need a Write answer file step, because winget runs in DDT's session;
     - run as its account DDTDeploy, an administrator only while winget runs;
     - need internet on the machine;
     - Windows 11 and Windows Server 2025, which bring winget.
   - Tried on a Hyper-V virtual machine with Windows 11 25H2. Not on Windows Server or a physical
     machine yet.
2. **Windows in the run.** The two new steps, and first-logon commands or an automatic logon, make
   a sequence version 7.
   - A Windows Update step, in the installed Windows. [Windows Update steps]
     - Microsoft Update only, through Windows Update's COM interface. WSUS is not built.
     - Installs what Windows Update offers, except: never a feature upgrade; drivers, and optional
       updates and previews, only with their switches (off); no software update that may ask for
       input; no KB number on the step's list.
     - Updates for other Microsoft products: a switch, on. It registers Microsoft Update, which
       stays registered.
     - Searches, downloads and installs until nothing is left, and restarts when an update or
       Windows asks. At most 5 searches between two restarts.
     - Restart limit: 1 to 20, default 8. Over it, the step fails and names the open updates.
     - Each update's own result counts. A failed update is tried once more after a restart; a second
       failure fails the step, with the update's name and HRESULT.
     - Waits up to 30 minutes while Windows Update installs for someone else. A download or an
       installation that takes over 6 hours fails the step.
     - A stop ends a download at once. An installation cannot be stopped mid-way: the run stops
       when it ends, and until then the console says the run was stopped and Windows finishes
       installing first.
   - An Enable roles and features step, in the installed Windows. [Install Roles and Features]
     - Feature names as DISM lists them (`NetFx3`, `Microsoft-Hyper-V-All`). The web offers common
       client and server names and takes any other.
     - Enabled with their parent features. One that asks for a restart restarts the machine after
       the last, and the step goes on after it, at most 3 times.
     - A name DISM does not know fails the step and is named.
   - The Write the answer file step writes more. [ZTIConfigure]
     - Product key: a write-only secret on Deployment defaults. A `ProductKey` value from a rule,
       a machine role or a variable overrides it, and can be read like every run value.
     - The key is in clear text in `Windows\Panther\unattend.xml` on the machine until Windows
       setup ends. It never goes into a log.
     - Organization and owner: Deployment defaults, or `Organization` and `Owner` values,
       overridden by the step.
     - UI language: a step setting. Unset is the image's language.
     - The licence, OEM registration, online account and wireless pages: a switch each, all
       skipped by default.
     - Static network from three values: `IPAddress` (IPv4 with prefix length, several joined by
       `;`), `Gateway` and `DnsServers` (`;`). For the adapter the machine registered with. Without
       `IPAddress`, DHCP.
     - A product key or an address Windows setup would refuse keeps the run from starting.
     - Windows Server: with the local administrator ticked, the built-in Administrator gets the
       same password, so Windows setup does not stop to ask for one.
   - First-logon commands and automatic logon, on the Write the answer file step.
     - Up to 20 commands of 1,000 characters, run in order at the first sign-in of an
       administrator. A failing command does not stop the next.
     - Automatic logon as the local administrator of Deployment defaults or as an account
       (library or Account input), 1 to 99 times.
     - A sequence without a step in the installed Windows: in the answer file, from the first
       sign-in after Windows setup.
     - A sequence with one: kept out of the answer file. As the run ends in Windows, the agent
       writes one script to `%ProgramData%\DDT\FirstLogon` (SYSTEM and administrators only) and one
       RunOnce value that starts it, and sets the automatic logon with the password as an LSA
       secret. Both take effect from the next start of Windows, after the agent has removed itself.
     - Known leftovers: the LSA secret stays after the last automatic logon, readable by SYSTEM
       and administrators only. An automatic logon as an account that is no administrator never
       runs the commands, and their script and RunOnce value stay.
   - A deployment record. [Tattoo]
     - `HKLM\SOFTWARE\Davicloud\DDT\Deployment`, strings: SequenceName, SequenceId,
       SequenceRevision, RunId, Result, DeployedUtc, ImageName, DdtVersion, ServerName.
     - Written as a run ends in the installed Windows: Result Done, Failed or Stopped (stopped from
       the web). A run that ends in Windows PE leaves none.
     - Leave a deployment record in Windows, on Deployment defaults: on by default.
   - A detail line under the running step, on the console at the machine and on the run's page:
     the application being installed, the feature being enabled, "Downloading 5 updates".
   - While a run goes on in the installed Windows, the machine does not go to sleep and the
     console keeps the display on.
   - Tried on a Hyper-V virtual machine with Windows 11 25H2 and Windows Server 2025, automatic
     logon also as a domain account. Not on a physical PC.
3. **Servicing and security.** The four new steps make a sequence version 8.
   - Windows packages, under Library > Windows packages. [Packages]
     - A `.msu` or `.cab` file, or a zip of them at its top. Anything else in the zip is refused.
     - What a package holds is read from its file names: Update, Language, Features on demand, or
       Updates and features, with its languages, KB numbers and architecture.
     - Windows builds, typed by an administrator (26100, 26200), mark a package for images of those
       builds. No models: a package goes where a sequence names it.
     - The Languages and Optional Features ISO, uploaded or copied into an import folder: tick
       languages, and each becomes one package `<ll-CC> language pack (<build>)` with its language
       pack, language features and the fonts its script needs, marked for the ISO's build. Other
       features on demand: upload the `.cab`.
   - A Service packages step, in Windows PE after Apply image. [Install Updates Offline, Install
     Language Packs Offline]
     - DISM on the applied Windows: each language pack, then the feature cabinets together, then
       each update package, lowest KB first. An update package's newest `.msu` is the target, and
       DISM finds the checkpoints beside it.
     - Matching updates, a switch: every update package marked for the build of an image the
       sequence applies, for its architecture or none. Frozen with the run like the listed ones.
     - Up to 64 packages. A listed package that does not fit this Windows fails the step; a matched
       one is left out with a warning.
     - An update needs 3.5 times its size free on the Windows partition, or the step fails before
       DISM starts. 2 hours per DISM call.
     - Clean up after the updates, a switch, on: the component store cleanup. Its failure is a
       warning.
     - Scratch, packages and DISM's logs go to the target disk and are deleted when the step ends;
       on a failure the log's last 40 lines go into the machine log.
   - A Pre-provision BitLocker step, in Windows PE between Partition and Apply image, once per
     sequence. [Pre-provision BitLocker]
     - `manage-bde -on` on the empty Windows partition, used space only, with a clear key. It does
       not wait.
     - Without manage-bde in the boot image or without a TPM: skipped with a warning (default), or
       failed. Set to fail, a boot image without manage-bde fails the run before a disk is touched.
   - An Enable BitLocker step, in the installed Windows, once per sequence. [Enable BitLocker]
     - TPM protector and a recovery password on the Windows volume, the password handed to the
       server, and only then protection on. Used space only.
     - The checkpoint keeps the protector ID, never the password. A recovery password the server
       never confirmed is deleted and made anew.
     - Back up the recovery password to Active Directory, a switch, on: only on a joined machine.
       The FVE policy values the backup needs are set for the call and removed after it. A failed
       backup fails the step.
     - No TPM: fails (default), or skipped with a warning.
     - Windows Server: the BitLocker feature first, with one restart.
     - Wait until the disk is encrypted, a switch, off: 1 to 1440 minutes, 120 by default.
   - Recovery keys, on the machine's page.
     - Key ID, volume, date and Replaced. Everyone sees them; only administrators reveal a password
       or delete a replaced key. Each reveal and deletion is an audit row with the key ID, never
       the password.
     - The Machines page's search finds a machine by a key ID, whole or its first 8 hex digits.
     - Removing a machine with keys asks, naming how many go with it.
     - Encrypted with the DDT key ring (`<store>\keys`). A lost or moved key ring loses every key,
       so Active Directory is the first copy and the keys folder needs a backup.
   - A Register with Autopilot step, in the installed Windows.
     - Reads the hardware hash and serial number in one PowerShell call, never logged. Windows
       Server fails the step.
     - Group tag, or the default of the Autopilot settings. Wait until Autopilot has imported the
       hash, a switch, off: 1 to 120 minutes, 30 by default.
     - Upload through Microsoft Graph by the server, with an app registration under Machines >
       Autopilot (application permission `DeviceManagementServiceConfig.ReadWrite.All`). The
       server follows each import every 30 seconds and gives up after 30 minutes.
     - The upload was tested against a stand-in only, never a real tenant.
     - Without the app registration the hash stays on the server: Intune's CSV for one machine on
       its page, or for the machines the Machines page shows, at most 500.
   - The boot image gets WinPE-EnhancedStorage and WinPE-SecureStartup, for manage-bde, with the
     PowerShell components. Only Pre-provision BitLocker needs them: the Boot image page asks for a
     rebuild while a sequence has that step and the served image lacks them.
   - Detail lines: the package being added, the cleanup, the percent encrypted.
   - Tried on a Hyper-V virtual machine with Windows 11 25H2 and Windows Server 2025, the backup
     against a Windows Server 2025 domain controller. Not on a physical PC, and the Autopilot
     upload not against a real tenant.

## M10 Golden images and the machine lifecycle

- **The image builder.** One screen: choose a base image, list the steps, build. DDT netboots a
  machine or a virtual machine, runs the steps, generalizes Windows with Sysprep, captures it with
  wimlib in Windows PE, and adds the image to the library as a new version. A recipe uses the same
  steps as a deployment, so a step moves between the image and the deployment, and an image is as
  thick or as thin as it needs to be. It has to feel familiar to MDT users and be a lot easier than
  MDT's build and capture. Its design is still open. [Sysprep and Capture]
- **Image versions.** A sequence uses a chosen version or the newest tested one, and rebuilding with
  the month's updates is the same button.
- **Linux images** built the same way once M8 exists: from a cloud image, generalized, captured as a
  raw disk image.
- **User state migration** with USMT, to refresh or replace a machine and keep its users' data.
  [Refresh and Replace scenarios]
- **In-place upgrades** to a newer Windows release. [Standard Client Upgrade Task Sequence]

## M11 Reach

- **Offline media**: a USB stick or an ISO with Windows PE, the agent and a sequence's files, for
  sites without netboot. The run reports to the server once it can reach it. [Deployment media]
- **Site caches**: a DDT role at a remote site that keeps images and packages, so machines there
  download them over the local network, and multicast for many machines at once. [Linked deployment
  shares, WDS multicast]
- **Remote view** of the console in Windows PE from the web UI. [DaRT remote control]

## M12 Documentation

The whole project documented, for the people who run DDT and for those who work on it. Until then,
the [old README](https://github.com/Davicloud-NET/DDT/blob/b8f9f1a2a686a03ec450a5d0ff82b211d57a3ab3/README.md)
is the most complete description.

## Ideas for later

- **Custom input pages** in the graphical console in Windows PE, designed with the sequence, whose
  fields write to variables. They would build on M7's inputs and variables and M6.5's console. Not
  planned yet.
- **Certificates from Active Directory Certificate Services.** DDT requests its server certificate
  from the shop's own CA and renews it by itself, the whole way: browsers, agents and the boot
  image all work with it, so nobody in the domain has to trust DDT's root. Until then IIS in front
  of DDT serves the shop's certificate. Planned, in no milestone yet.
- **Windows from the Linux boot environment.** M8's Linux boot environment stands beside Windows
  PE and writes Linux images only. Installing Windows from it, so that a server needs no ADK and
  no boot image build at all, is not planned.

## Not planned

- **BIOS and 32-bit machines.** Windows 11 needs UEFI and x64, and Windows 10 is out of support.
- **Deploying to a VHD** for dual boot. [Deploy to VHD]
