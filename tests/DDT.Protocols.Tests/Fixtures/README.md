# Packet fixtures

One `.bin` file per packet, holding the raw payload with no framing: for DHCP that is the UDP
payload starting at the BOOTP `op` field, for TFTP the UDP payload starting at the opcode.

Every `.bin` has a sibling `.md` recording where it came from, because a fixture whose provenance
is unknown cannot be trusted when firmware disagrees with it. The note states:

- **Source**: `hand-constructed` with the RFC or specification section it was built from, or
  `capture` with the hardware, firmware version and date.
- **What it is**: the message type and the notable options.
- **Why it is here**: the behaviour it pins down.

Hand-constructed fixtures are a starting point. Real firmware captures replace them as they are
collected, and a capture always wins over a hand-constructed packet when the two disagree.
