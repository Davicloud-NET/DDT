// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { lingui, linguiTransformerBabelPreset } from "@lingui/vite-plugin";
import babel from "@rolldown/plugin-babel";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";

// Aspire injects the host's endpoint as services__<resource>__<endpoint>__<index>.
// The resource name contains a hyphen, so it can only be read by index.
const backend = process.env["services__ddt-host__http__0"] ?? "http://localhost:5254";

export default defineConfig({
  // Lingui's macros turn the English text in the code into message ids; its plugin compiles the catalogs.
  plugins: [react(), lingui(), babel({ presets: [linguiTransformerBabelPreset()] }), tailwindcss()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  build: {
    outDir: fileURLToPath(new URL("../DDT.Host/wwwroot", import.meta.url)),
    emptyOutDir: true,
    sourcemap: true,
  },
  server: {
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    proxy: {
      "/api": { target: backend, changeOrigin: true, secure: false },
      // The hub checks Origin against Host, and changeOrigin rewrites only Host.
      "/hubs": { target: backend, changeOrigin: false, secure: false, ws: true },
    },
  },
});
