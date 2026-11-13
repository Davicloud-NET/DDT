# Installer fixtures

- `properties.msi.gz`: an MSI that Windows Installer itself wrote, gzipped. It pins down how DDT
  reads the Property table without msi.dll, from a file the real writer made.
  - **Source**: `New-MsiFixture.ps1` beside it, run on Windows 11 with msi.dll 5.0. Our own content,
    no third-party binaries.
  - **What it is**: code page 1252, a Property table with ProductCode, ProductName (`DDT Prüfpaket
    Größe`, not ASCII), ProductVersion, Manufacturer, UpgradeCode, ProductLanguage and ALLUSERS, and
    summary information. A version 3 compound file with 512-byte sectors, its streams in the mini
    stream.
  - SHA-256 of the uncompressed file:
    `40a3ab212afee815dcd8096a793b9c0b2df5d05cd27ed5f2e174647d0d793bb7`. Each run of the script
    gives another hash.
