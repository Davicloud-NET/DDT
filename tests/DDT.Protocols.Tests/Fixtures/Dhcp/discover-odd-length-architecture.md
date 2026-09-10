# discover-odd-length-architecture.bin

**Source**: hand-constructed. RFC 4578 section 2.1 requires the option 93 length to be an even
number greater than zero.

**What it is**: a 300 octet DHCPDISCOVER whose option 93 carries three octets. The malformed option
sits mid-packet with a real End marker after it, so the rest of the option block still parses.

**Why it is here**: a malformed architecture must be distinguishable from a missing one. Both lead
to silence, but an operator debugging a machine that will not boot needs to know which.
