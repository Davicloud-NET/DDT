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
