# discover-no-vendor-class.bin

**Source**: hand-constructed. An ordinary non-PXE DHCP client looks like this.

**What it is**: a 300 octet DHCPDISCOVER carrying no option 60.

**Why it is here**: PXE 2.1 section 2.5.1.1 says a redirection service must only respond to messages
carrying option 60 with the value `PXEClient`. This is the fixture that proves DDT stays silent for
every ordinary workstation on the segment, which is the single most important thing it does.
