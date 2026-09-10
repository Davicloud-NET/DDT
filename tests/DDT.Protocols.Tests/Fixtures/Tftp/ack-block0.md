# ack-block0.bin

**Source**: hand-constructed from RFC 1350 section 5 and RFC 2347.

**What it is**: an acknowledgement for block zero.

**Why it is here**: block zero is how a client confirms an OACK, and it is also what block 65536
looks like on the wire once the 16 bit counter wraps. Which one it means is decided by session
state, never by the value, and both readings are asserted.
