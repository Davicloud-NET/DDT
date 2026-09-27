// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { crc32, deflateSync } from "node:zlib";

// A logo of the kind the console's header wants, drawn here so no picture file is needed: a blue square and three light
// bars standing in for a name, on a transparent background, 240 by 64 pixels.
export function sampleLogo(): Buffer {
  const width = 240;
  const height = 64;
  const rows: Buffer[] = [];
  const bars: [number, number][] = [
    [84, 132],
    [140, 176],
    [184, 236],
  ];

  for (let y = 0; y < height; y++) {
    const row = Buffer.alloc(1 + width * 4);

    for (let x = 0; x < width; x++) {
      const inSquare = x < 64;
      const inBar = y >= 18 && y < 46 && bars.some(([from, to]) => x >= from && x < to);
      const [r, g, b, a] = inSquare
        ? [0x2f, 0x80, 0xed, 0xff]
        : inBar
          ? [0xf2, 0xf4, 0xf2, 0xff]
          : [0, 0, 0, 0];

      row.set([r, g, b, a], 1 + x * 4);
    }

    rows.push(row);
  }

  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header.set([8, 6, 0, 0, 0], 8);

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", header),
    chunk("IDAT", deflateSync(Buffer.concat(rows))),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

function chunk(type: string, data: Buffer): Buffer {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));

  return Buffer.concat([length, body, crc]);
}
