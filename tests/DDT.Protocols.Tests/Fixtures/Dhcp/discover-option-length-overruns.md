# discover-option-length-overruns.bin

**Source**: hand-constructed hostile input. Not padded to 300 and deliberately not terminated.

**What it is**: a DHCPDISCOVER whose final option declares a length of 200 octets with only two
octets left in the datagram.

**Why it is here**: anyone on the segment can send this. The parser must reject it rather than read
past the buffer or hand a half-parsed message to the responder, and it must never throw, because the
socket loop has no way to recover from an exception raised by a stranger.
