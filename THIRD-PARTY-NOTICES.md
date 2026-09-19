# Third-party notices

DDT ships the following software that is not its own. NuGet and npm packages that are only referenced,
and not redistributed inside DDT's own files, carry their licences in their packages.

## wimlib (libwim)

The agent, `ddt-agent.exe`, carries `libwim-15.dll` from wimlib 1.14.4 inside the executable and
writes it next to itself before it applies an image. DDT loads it as a separate library and does not
link it statically.

- Project: https://wimlib.net, Copyright Eric Biggers and the wimlib contributors
- Licence: DDT uses libwim under the GNU Lesser General Public License, version 3 or later, which
  wimlib offers for Windows builds of the library. See [licenses/wimlib/COPYING](licenses/wimlib/COPYING),
  [COPYING.LGPLv3](licenses/wimlib/COPYING.LGPLv3) and [COPYING.GPLv3](licenses/wimlib/COPYING.GPLv3),
  which the LGPL incorporates.
- It contains libdivsufsort-lite, under the MIT licence in
  [licenses/wimlib/COPYING.libdivsufsort-lite](licenses/wimlib/COPYING.libdivsufsort-lite).
- The binary comes from the native assets of the ManagedWimLib 2.6.0 NuGet package by Hajin Jang,
  built from wimlib 1.14.4 without NTFS-3G. DDT uses none of ManagedWimLib's own code.
- Source: https://wimlib.net/downloads/wimlib-1.14.4.tar.gz and
  https://github.com/ebiggers/wimlib/tree/v1.14.4. The build scripts for the binary are at
  https://github.com/ied206/ManagedWimLib in the `native` folder.
- To run the agent with a modified libwim, build `libwim-15.dll` from that source and change the
  `EmbeddedResource` in `src/DDT.Agent/DDT.Agent.csproj` to point at it, then publish the agent again.
