# Source code of the DDT Linux boot environment

DDT puts its Linux boot environment together from files of Debian packages and compiles none of them
itself. These are the source packages of those packages, and of what Debian built them with, as they
were in the Debian archive at its snapshot 20261006T203214Z. Every DDT release publishes these
files as `ddt-linux-boot-sources.tar`, beside its other files. Each is also at the address given with it.

## busybox 1:1.37.0-6

The source of `busybox-static` 1:1.37.0-6+b9.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/main/b/busybox/

| File | Bytes | SHA-256 |
|---|---|---|
| `busybox_1.37.0-6.dsc` | 2279 | `3ba2c096eef0c391f744f093edbe0df7b21ab118652487cc076818e92cb57e8e` |
| `busybox_1.37.0.orig.tar.bz2` | 2565764 | `3311dff32e746499f4df0d5df04d7eb396382d7e108bb9250e7b519b837043a4` |
| `busybox_1.37.0-6.debian.tar.xz` | 65032 | `10b2cd64b41c26ae134e1cd6135b68575bbe79356c20985f35e32f32a1746c34` |

## console-data 2:1.12-9

The source of `console-data` 2:1.12-9.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/main/c/console-data/

| File | Bytes | SHA-256 |
|---|---|---|
| `console-data_1.12-9.dsc` | 2651 | `2c8000950bf3a213f1f24d06658e7572549b7eb8741e11571ec6d93741373747` |
| `console-data_1.12.orig.tar.bz2` | 494941 | `f1a604f0254a2479e060d6d3d53c586e7a725e3c3c9d6f5711f053a6d8da600e` |
| `console-data_1.12-9.debian.tar.xz` | 85924 | `71876bb81c7d2387298fffd48d50c142062f7b1c2de1f19b4592bd6a07a06e7c` |

## firmware-nonfree 20260810-1~bpo13+1

The source of `firmware-realtek` 20260810-1~bpo13+1, `firmware-bnx2` 20260810-1~bpo13+1, `firmware-bnx2x` 20260810-1~bpo13+1, `firmware-misc-nonfree` 20260810-1~bpo13+1.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/non-free-firmware/f/firmware-nonfree/

| File | Bytes | SHA-256 |
|---|---|---|
| `firmware-nonfree_20260810-1~bpo13+1.dsc` | 4644 | `5bd20c1c09f7d2048af403a7836bafcbfeef09423037e6b7bf77df8de33c5a0f` |
| `firmware-nonfree_20260810-1~bpo13+1.debian.tar.xz` | 826184 | `59c705af8147c7dce0c439699cf42dd6bd7a89ab28e72e16b7453bc2c6401092` |

DDT does not publish the next file, which is at the same address: it is the firmware as its vendors
pass it on, without source code. The licence texts are in `firmware-nonfree.copyright`.

| File | Bytes | SHA-256 |
|---|---|---|
| `firmware-nonfree_20260810.orig.tar.xz` | 358531136 | `c2ae16d4f22f3aed547146ae486f48b14dd70e9cf86f08f640193326db5bc5ec` |

## glibc 2.41-12+deb13u4

The source of `libc6` 2.41-12+deb13u4.
Built into `busybox-static` 1:1.37.0-6+b9.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/main/g/glibc/

| File | Bytes | SHA-256 |
|---|---|---|
| `glibc_2.41-12+deb13u4.dsc` | 7576 | `0915324aa646bb99abfaa9aa6ecb734b30babd28454a3e51f3152912370fbd3e` |
| `glibc_2.41.orig.tar.xz` | 20323540 | `f24aa441021121a79266f0d75242706cab8843a47901fefe74527491807f1998` |
| `glibc_2.41-12+deb13u4.debian.tar.xz` | 499524 | `dda4153511bfd543502d18e5bc9323110996fe9c531f14ae3fad6eb17f027c7d` |

## grub-efi-amd64-signed 1+2.12+9+deb13u2

The source of `grub-efi-amd64-signed` 1+2.12+9+deb13u2.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/main/g/grub-efi-amd64-signed/

| File | Bytes | SHA-256 |
|---|---|---|
| `grub-efi-amd64-signed_1+2.12+9+deb13u2.dsc` | 1218 | `db42753007d3df83a3b2489883c612f20f14ceb387d76c076f2213fb03c37f09` |
| `grub-efi-amd64-signed_1+2.12+9+deb13u2.tar.xz` | 4796 | `93f0131a788f3977ab866632449e3f2ae3e59757adf0fd93c96630a4ebef497e` |

## grub2 2.12-9+deb13u2

Built into `grub-efi-amd64-signed` 1+2.12+9+deb13u2.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/main/g/grub2/

| File | Bytes | SHA-256 |
|---|---|---|
| `grub2_2.12-9+deb13u2.dsc` | 8103 | `5d1cda92727de39518a94ce23e7e239077acdfa9a4694e71eba736dbd354391a` |
| `grub2_2.12.orig.tar.xz` | 6675608 | `f3c97391f7c4eaa677a78e090c7e97e6dc47b16f655f04683ebd37bef7fe0faa` |
| `grub2_2.12-9+deb13u2.debian.tar.xz` | 1123180 | `4d3c4689ee100466a36b6a5caa2477ef525fbc5b6ec27cb0319c83db7d9faf89` |

## linux 7.2.6-1~bpo13+1

The source of `linux-modules-7.2.6+deb13-amd64` 7.2.6-1~bpo13+1, `linux-base-7.2.6+deb13-amd64` 7.2.6-1~bpo13+1.
Built into `linux-binary-7.2.6+deb13-amd64` 7.2.6-1~bpo13+1.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/main/l/linux/

| File | Bytes | SHA-256 |
|---|---|---|
| `linux_7.2.6-1~bpo13+1.dsc` | 143262 | `69128cc1b1620d8fba889c860137efb52e6676fb16097f0079fbfaddf8c96e86` |
| `linux_7.2.6.orig.tar.xz` | 163554268 | `1819c494eeda2be08fdca1b4d4d8d669e5a4215395595503502aeb78d019882a` |
| `linux_7.2.6-1~bpo13+1.debian.tar.xz` | 1458812 | `e9ededaf5d4f29ced0cfe828a710be861a19e76ef137e985954bee4b52a7d77c` |

## linux-signed-amd64 7.2.6+1~bpo13+1

The source of `linux-binary-7.2.6+deb13-amd64` 7.2.6-1~bpo13+1.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/main/l/linux-signed-amd64/

| File | Bytes | SHA-256 |
|---|---|---|
| `linux-signed-amd64_7.2.6+1~bpo13+1.dsc` | 5520 | `2a503f8ba987a16c2574d03d47464abd1edbe51482858f3f99cbccc25dde2112` |
| `linux-signed-amd64_7.2.6+1~bpo13+1.tar.xz` | 690384 | `20256dc1c601f9e498739693131fe24a7bbbc3717ef1bce7a38f54c8cccc5e85` |

## ncurses 6.5+20250216-2

The source of `ncurses-base` 6.5+20250216-2.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/main/n/ncurses/

| File | Bytes | SHA-256 |
|---|---|---|
| `ncurses_6.5+20250216-2.dsc` | 3890 | `00539cc2bc6bb84be1b8a8a5decf606682e14b6f39b76b9aa090e65d2c2d4580` |
| `ncurses_6.5+20250216.orig.tar.gz` | 3774714 | `b37baafa436e7133bb12a185cb8f60e1599b1947e9f0181c76f3190acf28b6eb` |
| `ncurses_6.5+20250216.orig.tar.gz.asc` | 729 | `64f4d17923322176c44079f18f170e2164a59d551d7e4e9c1a6e4eebedc5dd6f` |
| `ncurses_6.5+20250216-2.debian.tar.xz` | 50420 | `546930a8992ae3325d8c38378f290265d969cf6333cfae9648ac6d69e1e8a8a6` |

## shim 16.1-2~deb13u1

Built into `shim-signed` 1.51~1+deb13u1+16.1-2~deb13u1.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/main/s/shim/

| File | Bytes | SHA-256 |
|---|---|---|
| `shim_16.1-2~deb13u1.dsc` | 2713 | `d5748139a9a7fdd8e4c1ecd89b0156c3d226b21d393be495c2a28575817a0b90` |
| `shim_16.1.orig.tar.bz2` | 2348998 | `46319cd228d8f2c06c744241c0f342412329a7c630436fce7f82cf6936b1d603` |
| `shim_16.1.orig.tar.bz2.asc` | 833 | `05086f18c1f44a779efc20898434398812a90163a33dbba9105af0e64f46bb97` |
| `shim_16.1-2~deb13u1.debian.tar.xz` | 58992 | `2d33fb67f05793f48e5d7696c3cd64229a2275abd3a9d18aed9db2f137f0e641` |

## shim-signed 1.51~1+deb13u1

The source of `shim-signed` 1.51~1+deb13u1+16.1-2~deb13u1.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/main/s/shim-signed/

| File | Bytes | SHA-256 |
|---|---|---|
| `shim-signed_1.51~1+deb13u1.dsc` | 1963 | `29b8f65c870cb2a735536355c092bb07348342c6a9b16165092435d3faf940d0` |
| `shim-signed_1.51~1+deb13u1.tar.xz` | 771352 | `a9c614e6d69caf950359ac800c6df7125169f95b44073e378943b58935d4e400` |

## unicode-data 14.0.0-1

Built into `console-data` 2:1.12-9.
From https://snapshot.debian.org/archive/debian/20261006T203214Z/pool/main/u/unicode-data/

| File | Bytes | SHA-256 |
|---|---|---|
| `unicode-data_14.0.0-1.dsc` | 1794 | `750465ab3867cfb764f9d1d5b2831372f700abdd4c9ef31f14ccfaa44988d695` |
| `unicode-data_14.0.0.orig.tar.xz` | 13804440 | `379b760b7ce8a9a5644388c504f85ed8466eabe8a85b45bdfc3b585cf3ac9e4e` |
| `unicode-data_14.0.0-1.debian.tar.xz` | 3796 | `4128633acd024773c147e545d30363e99722ad7d0197fa3bc80222b503491cd1` |
