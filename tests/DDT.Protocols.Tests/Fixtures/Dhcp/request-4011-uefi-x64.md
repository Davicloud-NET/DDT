# request-4011-uefi-x64.bin

**Source**: hand-constructed, following the Intel PXE specification 2.1 section 2.2.1: once the
client has an address it returns to the boot server on UDP 4011 to ask for the boot file.

**What it is**: a 300 octet DHCPREQUEST with ciaddr 192.0.2.55 and the broadcast flag clear, as it
would arrive on UDP 4011.

**Why it is here**: the reply to this is a DHCPACK unicast back to the client's source port, not a
broadcast, and the broadcast flag must not be forced on. That addressing rule is where the
network-breaking bugs live and it is asserted separately from the reply bytes.
