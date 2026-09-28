// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { extendTailwindMerge } from "tailwind-merge";

// Joins class names so a later class wins over an earlier one of its group, and a caller can override defaults.
// tailwind-merge needs the token names of src/DDT.Design/tokens.json to tell a size such as `text-label` from a colour.
const typeScale = [
  "wordmark",
  "display",
  "title",
  "subtitle",
  "heading",
  "label",
  "body",
  "small",
  "numeral",
  "step",
  "tag",
  "data",
];

export const cx = extendTailwindMerge<"type-scale">({
  extend: {
    theme: {
      text: typeScale,
      radius: ["tag", "key", "panel", "overlay"],
      shadow: ["panel", "overlay"],
    },
    classGroups: {
      "type-scale": [{ type: typeScale }],
    },
  },
});
