# Security

DDT decides which machines receive task sequences that run as SYSTEM, the passwords of a local
administrator and of a domain join account, and the images that end up on the disks of a network's
computers. A flaw in it can reach every machine it deploys, so please report one privately.

## Reporting a vulnerability

Do not open a public issue, discussion or pull request for a vulnerability. Report it instead
through GitHub's private vulnerability reporting: on the repository's **Security** tab choose
**Report a vulnerability**, or go straight to
https://github.com/Davicloud-NET/DDT/security/advisories/new. Only the maintainer and you can read
the report. If that does not work for you, write to contact@davicloud.net.

A useful report says:

- what an attacker can do, and from where: the provisioning network, the web UI signed in with
  which role, a machine being deployed, or a package or image someone uploads;
- the commit of DDT it was found in, and which role or program it concerns: the `web` or `pxe`
  role, the agent, the console at the machine, or `build/Build-BootImage.ps1`;
- the steps to reproduce it, with a proof of concept if there is one.

You will get an answer to say that the report arrived, and then word of what is being done about
it. Once a fix is on `master`, the report is published as a GitHub security advisory, crediting
you unless you ask not to be named. Please keep the details to yourself until then.

## What counts

[The security model](README.md#security-model) in the README states DDT's trust boundary. What it
names as accepted is not a vulnerability by itself, for example that the provisioning network is
inside the trust boundary, that anything in `boot.wim` can be read by whoever boots it, or that
every operator can obtain the deployment passwords by running a sequence that needs them. A way
around what it promises is one, for example:

- a machine that reads a task sequence, an image, a package or a secret before an operator or
  administrator authorized it;
- a rule, a zero touch network or a forwarded header that authorizes a machine where the security
  model says it must not;
- a signed-in user doing what their role does not allow, or reaching another user's API tokens;
- a password or a token in a log line, an audit record, the hub's traffic or a response that
  should not carry it;
- a package or an upload that writes outside its folder on the machine or on the server.

A vulnerability in a library DDT uses belongs with that library's maintainers first. Tell DDT as
well when DDT is affected in a way its users need to know about.

## Supported versions

DDT has no releases yet. Fixes go to `master`, and only the latest commit there is supported.
