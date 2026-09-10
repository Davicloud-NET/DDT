# discover-httpclient-x64.bin

**Source**: hand-constructed. Option 93 value 0x0010 is "x64 uefi boot from http" in the IANA
Processor Architecture Types registry; the option 60 `HTTPClient` prefix is the UEFI HTTP Boot
convention that firmware uses in place of `PXEClient`.

**What it is**: a 300 octet DHCPDISCOVER from a UEFI HTTP Boot client.

**Why it is here**: HTTP Boot must be answered with a URL, and a PXE client must never be handed
one. This fixture is the positive half of that pair; `discover-uefi-x64-pxeclient.bin` is the
negative half when the configured target is HTTP.
