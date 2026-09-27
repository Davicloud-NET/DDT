# DDT.Design

The design tokens of DDT's look, Switchgear, and the script that turns them into themes.

- `tokens.json` holds the colours of the light and the dark theme, the type scale, the corner radii and the
  motion values. Colour names say what a colour is for (`panel`, `ink`, `run`, `fail`), never what it looks like.
  It also lists the text and background pairs whose contrast has to hold.
- `generate.mjs` checks those pairs against WCAG and writes two files from the tokens:
  `src/DDT.Web/src/styles/theme.css`, the Tailwind theme of the web UI, and
  `src/DDT.MachineConsole/Theme/Tokens.axaml`, the Avalonia resources of the console in Windows PE: every colour as a
  `Color` and a brush in a light and a dark theme dictionary, the radii, and each type's face, size, line height and
  letter spacing in pixels. Run it from `src/DDT.Web` with `npm run tokens` after changing the tokens; a web test
  fails while either written file is out of date, and a test of the console compares its resources with the tokens
  from the .NET side.
- `brand` holds DDT's logo as it was drawn: `ddt-logo.svg`, the source, `ddt-logo.png` at 1024 pixels, and
  `ddt-logo.ico`, the Windows icon from 16 to 256 pixels that the agent, the console and the server carry. The web
  draws the SVG's paths cropped to the mark (`src/DDT.Web/src/ui/Logo.tsx`) and serves the SVG and the icon as its
  favicon; the console shows `src/DDT.MachineConsole/Assets/ddt-logo.png`, cut from the PNG to the mark and 256
  pixels high, because Avalonia draws no SVG without another package. The logo keeps its own reds. It is under
  DDT's licence like everything else here, so a work based on DDT may use it; section 7(c) of NOTICE still asks
  a modified version to say that it is modified.
  `readme-banner-dark.png` and `readme-banner-light.png` head the repository's README, one for each GitHub theme:
  the logo on a tile over the console's screens, as `tests/DDT.MachineConsole.Tests/ScreenshotTests.cs` saves them,
  at 1920 by 600 pixels.
- The console cannot vary a font's width, so it carries one static face of Archivo or Martian Mono for every weight
  and width a type uses, named after both, such as "Archivo 750 62". A new weight or width in `type` needs its face:
  run `src/DDT.MachineConsole/Assets/Fonts/cut_fonts.py`, which cuts them all from Google Fonts' variable fonts.

The rules the tokens encode:

- Surfaces get lighter as they come forward, in both themes: page, panel, overlay. A well is one shade below its
  panel. Only overlays, such as menus, drawers and dialogs, cast a shadow.
- The signal colours follow the RAL signal colours and mark state only: blue for running, yellow for someone has to
  act, red for failed, stopped or erased, green for done. Brand red appears in the logo; everywhere else red means
  something failed.
- Corners grow with the part: tags 2 px, keys and fields 4, panels 6, overlays 8.
- Archivo carries every width, from 62 % for big numbers to 100 % for body text. Martian Mono is only for
  identifiers such as MAC addresses and for logs. Capitals appear only on state tags.

Motion, from `motion` in the tokens:

- Motion answers something: a person's action, or a change the server pushed. Nothing moves to decorate, and
  nothing moves on its own except what is running: the stripes of the running step, a progress bar, a spinner that
  stands for work in progress.
- It is short and mechanical, never springy. Keys go down in `press` (70 ms) and colours change on hover in `fast`
  (120 ms); overlays and new content enter in `normal` (160 ms), decelerating (`enter`), and leave faster,
  accelerating (`exit`); the drawer and a step's fill take `slow` (240 ms). Nothing overshoots or bounces.
- Things travel `distance` (6 px) at most, and only opacity and position change, never size or blur: the console
  draws in software, and cheap motion stays smooth there too.
- Hover changes colour only: no lift, no growing shadow. A pressed key sinks by a pixel and darkens, as a switch
  does. Focus rings appear at once, without motion.
- A value the server changed while it is on screen, such as a machine's state, flashes its row in the state's
  colour at low strength and fades over `flash` (1.4 s), so an operator sees what just changed; values on a page's
  first load do not flash.
- The web honours "reduce motion": then only colour changes remain. The console runs where no such setting exists
  and keeps its motion as short as the web's.
