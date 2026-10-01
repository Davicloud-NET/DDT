// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;

namespace DDT.Host.Startup;

// A verb's words go to the console as it runs, and are kept for the event log
internal sealed class EchoWriter(TextWriter console, TextWriter kept) : TextWriter(CultureInfo.InvariantCulture)
{
    public override Encoding Encoding => console.Encoding;

    public override void Write(char value)
    {
        console.Write(value);
        kept.Write(value);
    }
}
