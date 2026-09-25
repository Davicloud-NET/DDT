# Secure Boot fixtures

Real EFI programs, compressed with `gzip -9 -n`, for the Authenticode check that decides whether a
raw disk image starts with Secure Boot on. They pin down how DDT reads signatures that real signing
tools made, which the tests' own synthetic signatures cannot.

- **Source**: Ubuntu's `shim-signed` package, version `1.58+15.8-0ubuntu1`, amd64, from the Ubuntu
  24.04 archive (`apt-get download shim-signed`), files under `/usr/lib/shim/`. shim is under the
  BSD 2-Clause licence; the fixtures are only read by the tests and never shipped.
- `shimx64.efi.dualsigned.gz`: shim 15.8 with two WIN_CERTIFICATE entries, the first signed by
  "Canonical Ltd. Secure Boot Signing (2022 v1)", the second by "Microsoft Windows UEFI Driver
  Publisher" under Microsoft Corporation UEFI CA 2011. SHA-256 of the uncompressed file:
  `3ed8aa3591f793de2e0886d077aae950810843caac5eaf96e1436a439bf5e150`. It shows that a signature
  under Microsoft's UEFI CA is found after one that is not, in PKCS #7 as Microsoft signs it.
- `fbx64.efi.gz`: shim's fallback program, signed by Canonical only. SHA-256 of the uncompressed
  file: `8f57751703470403ff7377c26a90a810eba9f6db36f262ac6ad94d132ddc5a60`. It is what a stock PC
  refuses: signed, but not under a certificate in its db.
