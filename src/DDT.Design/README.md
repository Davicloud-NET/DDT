# DDT.Design

The design tokens of DDT's look, Switchgear, and the script that turns them into themes.

- `tokens.json` holds the colours of the light and the dark theme, the type scale, the corner radii and the
  motion values. Colour names say what a colour is for (`panel`, `ink`, `run`, `fail`), never what it looks like.
  It also lists the text and background pairs whose contrast has to hold.
- `generate.mjs` checks those pairs against WCAG and writes `src/DDT.Web/src/styles/theme.css`, the Tailwind theme
  of the web UI. Run it from `src/DDT.Web` with `npm run tokens` after changing the tokens; a web test fails while
  the written theme is out of date. The console in Windows PE will get its own output from the same tokens.

The rules the tokens encode:

- Surfaces get lighter as they come forward, in both themes: page, panel, overlay. A well is one shade below its
  panel. Only overlays, such as menus, drawers and dialogs, cast a shadow.
- The signal colours follow the RAL signal colours and mark state only: blue for running, yellow for someone has to
  act, red for failed, stopped or erased, green for done. Brand red appears in the logo; everywhere else red means
  something failed.
- Corners grow with the part: tags 2 px, keys and fields 4, panels 6, overlays 8.
- Archivo carries every width, from 62 % for big numbers to 100 % for body text. Martian Mono is only for
  identifiers such as MAC addresses and for logs. Capitals appear only on state tags.
