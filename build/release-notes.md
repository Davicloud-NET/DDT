## Installing

**On Windows**, in an elevated PowerShell:

```powershell
irm https://github.com/Davicloud-NET/DDT/releases/download/v{version}/install.ps1 | iex
```

Or take `DDT.msi` from the files below and double-click it.

**On Linux**, with Docker Engine and its compose plugin:

```bash
curl -fsSL https://github.com/Davicloud-NET/DDT/releases/download/v{version}/install.sh | sudo sh
```

The same line upgrades a server that runs an older DDT, and keeps its store. Both scripts check
what they download against `SHA256SUMS`. New to DDT? The
[quick start](https://github.com/Davicloud-NET/DDT/blob/v{version}/README.md#quick-start) takes it
from there.
