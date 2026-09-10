# error-8-option-negotiation.bin

**Source**: hand-constructed. The message text is the one the EDK2 MTFTP client sends when it
rejects an OACK.

**What it is**: an ERROR packet with code 8, option negotiation failed.

**Why it is here**: an ERROR from the client ends the transfer immediately and is not acknowledged.
This is what a server sees when its OACK broke one of the negotiation rules.
