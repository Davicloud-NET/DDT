# Roadmap

This is the order of DDT's milestones after M6 and what each one holds. The README's Status says
what is built. Most features below come from a comparison with the Microsoft Deployment Toolkit,
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

## M8 The Linux phase

A run goes on in the installed Linux as it goes on in the installed Windows. The cloud-init seed
starts a Linux build of the agent, which runs the sequence's steps there, bash scripts, files
packages and restarts included, fetches secrets when a step needs them, reports each step with its
log, and ends the run only when those steps are done. Until then a run that writes a raw disk image
ends when the disk is written, and DDT does not watch what cloud-init does at the first start.

## M9 Applications and Windows configuration

- **Applications.** A library entry holds an application's files, its silent install command, its
  success and restart codes, what it depends on and the bundles it is in. An Install applications
  step installs a list, a bundle, or what a rule or an input chose, and restarts between them where
  they ask. [Applications]
- **Windows Update** during a run, from Microsoft Update or WSUS, before and after the applications,
  restarting until nothing is left to install. [Windows Update steps]
- **Offline servicing** in Windows PE: updates, language packs and features on demand added to the
  applied image. [Packages, Install Updates Offline, Install Language Packs]
- **A fuller answer file**: product key, organization and owner, UI language, first-logon commands,
  automatic logon, the out-of-box pages to skip, and a static network. [ZTIConfigure]
- **BitLocker**, prepared in Windows PE and turned on in Windows, with the recovery key stored in
  Active Directory and in DDT. [Pre-provision BitLocker, Enable BitLocker]
- **Roles and features**, for Windows Server. [Install Roles and Features]
- **A deployment record** in the machine's registry: the sequence, its revision, the run and the
  date. [Tattoo]
- **Autopilot registration**: the run uploads the machine's hardware hash, so DDT hands machines
  over to Intune. Not in MDT, but Microsoft points MDT users to Autopilot.

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

The whole project documented, for the people who run DDT and for those who work on it. The README
stands in until then.

## Ideas for later

- **Custom input pages** in the graphical console in Windows PE, designed with the sequence, whose
  fields write to variables. They would build on M7's inputs and variables and M6.5's console. Not
  planned yet.

## Not planned

- **BIOS and 32-bit machines.** Windows 11 needs UEFI and x64, and Windows 10 is out of support.
- **Deploying to a VHD** for dual boot. [Deploy to VHD]
