// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import "@testing-library/jest-dom/vitest";
import { i18n } from "@lingui/core";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

// Tests read the English text: with no catalog, the macros fall back to the message in the code.
i18n.loadAndActivate({ locale: "en", messages: {} });

// jsdom has no Web Animations. React Aria's shared elements, such as the underline of tabs, ask an element for its
// animations without checking first; here no element ever runs one, so every entrance and exit ends at once, as in a
// browser with "reduce motion". A test that needs a running animation stubs this. Tests that run in Node have no
// elements at all.
if (typeof Element !== "undefined" && !Reflect.has(Element.prototype, "getAnimations")) {
  Element.prototype.getAnimations = () => [];
}

afterEach(() => {
  cleanup();
});
