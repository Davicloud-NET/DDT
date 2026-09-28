// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import "@testing-library/jest-dom/vitest";
import { i18n } from "@lingui/core";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

// Tests read the English text: with no catalog, the macros fall back to the message in the code.
i18n.loadAndActivate({ locale: "en", messages: {} });

// jsdom has no Web Animations, and React Aria's shared elements, such as the tabs' underline, call getAnimations
// without checking. No animation ever runs here, so entrances and exits end at once. A test that needs one stubs
// this.
if (typeof Element !== "undefined" && !Reflect.has(Element.prototype, "getAnimations")) {
  Element.prototype.getAnimations = () => [];
}

afterEach(() => {
  cleanup();
});
