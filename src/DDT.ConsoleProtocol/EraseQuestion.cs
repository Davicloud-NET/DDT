// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The last word before the sequence erases the disk. The console sends what was typed as Text, never Word on its own
// from a button, so a stray key or click cannot erase a disk. Anything else, or Back, erases nothing.
public sealed record EraseQuestion(string SequenceName, ConsoleDisk Disk, string Word) : ConsoleQuestion;
