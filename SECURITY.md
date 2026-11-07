# Security

DDT decides which machines receive task sequences that run as SYSTEM, the passwords of a local
administrator and of a domain join account, and the images that end up on the disks of a network's
computers. A flaw in it can reach every machine it deploys, so please report one privately.

## Reporting a vulnerability

Do not open a public issue, discussion or pull request for a vulnerability. Report it instead
through GitHub's private vulnerability reporting: on the repository's **Security** tab choose
**Report a vulnerability**, or go straight to
https://github.com/Davicloud-NET/DDT/security/advisories/new. The report is private, but repository
owners, organization owners, security managers, and users with the admin role may also have access
to the security advisory. If that does not work for you, write to contact@davicloud.net.

A useful report says:

- what an attacker can do, and from where: the provisioning network, the web UI signed in with
  which role, a machine being deployed, or a package or image someone uploads;
- the commit of DDT it was found in, and which role or program it concerns: the `web` or `pxe`
  role, the agent for Windows PE or for Linux, the console at the machine,
  `build/Build-BootImage.ps1`, or the Linux boot environment and its build in `build/LinuxBoot`;
- the steps to reproduce it, with a proof of concept if there is one.

You will get an answer to say that the report arrived, and then word of what is being done about
it. Once a fix is on `master`, the report is published as a GitHub security advisory, crediting
you unless you ask not to be named. Please keep the details to yourself until then.

## What counts

[The security model](https://github.com/Davicloud-NET/DDT/blob/b8f9f1a2a686a03ec450a5d0ff82b211d57a3ab3/README.md#security-model)
in the old README states DDT's trust boundary, until the documentation takes it over. What it
names as accepted is not a vulnerability by itself, for example that the provisioning network is
inside the trust boundary, that anything in `boot.wim` can be read by whoever boots it, or that
every operator can obtain the deployment passwords by running a sequence that needs them.

The Linux boot environment came after that text and stands inside the same boundary.
[docs/install.md](docs/install.md), section 4.9, says what netboot trusts. Accepted there too, and
no vulnerability by itself:

- Secure Boot covers shim, GRUB and the kernel, which Microsoft and Debian signed. It does not
  cover the initramfs files, `grub.cfg` or `agent.json`. That is the trust `boot.wim` has: whoever
  answers a machine's netboot in DDT's place decides what the machine runs.
- The start scripts take the server's time and its name and address from the kernel command line
  on that same trust.
- The menu that the agent leaves when it has ended offers a root shell at the machine, as Windows
  PE leaves a command prompt.
- An agent for Linux that an administrator uploads runs as root on every machine that netboots, as
  the agent for Windows PE runs as SYSTEM. The server can only check that the file is a Linux
  program for x64.
- Someone on the provisioning network can read every boot file. They can register in a machine's
  name, which costs it its approval and sets what the server believes of its firmware, and so
  whether it is named the Linux boot environment. With forged source addresses they can fill the
  TFTP transfers or have the server reset a machine's HTTP boot downloads, which makes that
  machine's start slower or fail once.

A way around what the security model promises is a vulnerability, for example:

- a machine that reads a task sequence, an image, a package or a secret before an operator or
  administrator authorized it;
- a rule, a zero touch network or a forwarded header that authorizes a machine where the security
  model says it must not;
- a signed-in user doing what their role does not allow, or reaching another user's API tokens;
- a password or a token in a log line, an audit record, the hub's traffic or a response that
  should not carry it;
- a package or an upload that writes outside its folder on the machine or on the server;
- a boot file request that reads a file outside the boot directory and the files the Linux boot
  environment lists, or a value that reaches the generated `grub.cfg` as more than a word of the
  kernel command line;
- a machine somebody registered under an identity of their own that changes which boot environment
  another, approved machine is named, or a datagram that resets another machine's downloads
  without a forged source address.

A vulnerability in a library DDT uses belongs with that library's maintainers first. Tell DDT as
well when DDT is affected in a way its users need to know about.

## Supported versions

DDT has no releases yet. Fixes go to `master`, and only the latest commit there is supported.
