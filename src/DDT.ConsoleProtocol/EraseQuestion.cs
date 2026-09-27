// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The last word before the sequence erases the disk. The person types Word, which is ERASE, and the console sends what
// was typed as Text, never the word on its own, such as from a button: a word typed on purpose keeps a stray key or
// click from erasing a disk. Anything but the word, or Back, goes back to the list of sequences and erases nothing.
public sealed record EraseQuestion(string SequenceName, ConsoleDisk Disk, string Word) : ConsoleQuestion;
