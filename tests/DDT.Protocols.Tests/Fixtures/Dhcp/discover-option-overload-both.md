# discover-option-overload-both.bin

**Source**: hand-constructed from RFC 2132 section 9.3 (option 52) and RFC 2131 section 4.1 (the
mandatory parse order).

**What it is**: a 300 octet DHCPDISCOVER with option 52 = 3. Option 60 lives in the `file` field at
offset 108 and option 55 lives in the `sname` field at offset 44; both are terminated by End.

**Why it is here**: RFC 2131 requires the options field to be read first so option 52 is discovered,
then `file`, then `sname`. A parser that decodes `sname` and `file` as null-terminated strings
instead produces garbage here, and a parser that ignores option 52 never finds the vendor class and
wrongly stays silent.
