# rrq-wdsmgfw-blksize1456-window4.bin

**Source**: hand-constructed from RFC 1350 section 5 (the RRQ layout), RFC 2347 (option pairs
appended to the request), RFC 2348 (`blksize`), RFC 2349 (`tsize`) and RFC 7440 (`windowsize`).

**What it is**: a read request for `ddt/x64/wdsmgfw.efi` in `octet` mode asking for a 1456 octet
block size, a transfer size (sent as 0, which the server replaces with the real length) and a
window of 4.

**Why it is here**: 1456 is above DDT's default cap of 1380 and 4 is exactly at the window cap, so
this one fixture exercises negotiating a block size down while leaving a window untouched. RFC 2348
and RFC 7440 both allow a server to negotiate only downwards.
