# discover-bios-x86.bin

**Source**: hand-constructed. Option 93 value 0x0000 is "x86 BIOS" in the IANA registry.

**What it is**: a 300 octet DHCPDISCOVER from a legacy BIOS PXE client.

**Why it is here**: BIOS is the only architecture DDT sends option 43 to. UEFI firmware classifies
an offer carrying option 43 differently and can refuse it, so the presence of that option is
asserted for this fixture and its absence is asserted for the UEFI ones.
