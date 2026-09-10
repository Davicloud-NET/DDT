# rrq-wdsmgfw-no-options.bin

**Source**: hand-constructed from RFC 1350 section 5.

**What it is**: a plain read request with no options at all.

**Why it is here**: RFC 2347 says an OACK is sent only when at least one option is accepted. This
pins the rule that a bare request gets no OACK and goes straight to block 1 at 512 octets, which is
what old BIOS ROMs do.
