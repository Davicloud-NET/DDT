# rrq-timeout-out-of-range.bin

**Source**: hand-constructed. RFC 2349 restricts `timeout` to 1 through 255 seconds.

**What it is**: a read request asking for a 900 second timeout.

**Why it is here**: RFC 2349 requires the value in the OACK to equal the value requested. A server
that clamps an out-of-range timeout and acknowledges the clamped value has sent a malformed OACK,
and the EDK2 client answers that with ERROR 4 and abandons the boot with no other diagnostic. The
option must be omitted from the OACK instead.
