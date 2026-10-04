// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;

namespace DDT.Host.Helper;

// Hands what is written to it on line by line, for code that reports to a TextWriter.
internal sealed class LineWriter(Action<string> line) : TextWriter(CultureInfo.InvariantCulture)
{
    private readonly StringBuilder _pending = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (value == '\n')
        {
            line(_pending.ToString().TrimEnd('\r'));
            _pending.Clear();
        }
        else
        {
            _pending.Append(value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _pending.Length > 0)
        {
            line(_pending.ToString());
            _pending.Clear();
        }

        base.Dispose(disposing);
    }
}
