# discover-uefi-x64-pxeclient.bin

**Source**: hand-constructed from RFC 2131 section 2 (header), RFC 2132 section 2 (option encoding),
RFC 4578 sections 2.1 and 2.3 (options 93 and 94) and the Intel PXE specification 2.1 Table 2-2
(the option 60 format). Not a capture.

**What it is**: a 300 octet DHCPDISCOVER from a UEFI x64 client, as one would arrive on UDP 67.
Options: 53 (Discover), 57 (max message size 1472), 93 (architecture 0x0007, x64 UEFI), 94
(UNDI 3.16), 97 (client machine identifier), 60 (`PXEClient:Arch:00007:UNDI:003001`, 32 octets),
55 (parameter request list), End, then zero padding to 300.

**Why it is here**: the primary happy path. It pins the header offsets, the option walk, the
architecture read and the vendor class prefix match all at once.

**On option 97**: the 17 octets are `00` followed by `44454c4c570010388036b7c04f5a344a`. DDT stores
and echoes these verbatim and never converts them to a Guid, because SMBIOS, Windows and RFC 4122
disagree about the byte order of the first three fields. Which orientation real firmware puts on the
wire is not settled by this fixture and must be confirmed against a capture before DDT renders a
UUID anywhere an operator can see it.
