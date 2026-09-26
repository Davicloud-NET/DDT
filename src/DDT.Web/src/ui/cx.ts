// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { extendTailwindMerge } from "tailwind-merge";

// Joins class names and lets a later class win over an earlier one of the same kind, so a component's caller can
// override its defaults. tailwind-merge has to know the token names to tell `text-label` (a size) from `text-ink`
// (a colour); they come from src/DDT.Design/tokens.json.
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
