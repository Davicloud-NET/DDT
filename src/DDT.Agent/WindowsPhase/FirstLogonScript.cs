// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;
using DDT.Agent.Applications;
using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// The first-logon commands as one cmd file, which RunOnce starts. A RunOnce value holds 260 characters at most, and
// Windows runs the values in no fixed order.
public static class FirstLogonScript
{
    public const string FileName = "FirstLogon.cmd";

    // %ProgramData%, because the agent's removal empties C:\DDT.
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DDT", "FirstLogon");

    public static string RunOnceCommand(string script) => $"\"{WindowsScriptLauncher.CmdPath}\" /d /c \"{script}\"";

    // Each command in a subroutine of its own, so its exit /b or setlocal ends there and a failure goes on to the next.
    // (goto) ends the batch before del removes the file, so cmd never reads past it.
    public static byte[] File(IReadOnlyList<string> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        StringBuilder text = new();

        for (int index = 0; index < commands.Count; index++)
        {
            text.Append(CultureInfo.InvariantCulture, $"@call :command{index + 1}\r\n");
        }

        text.Append("@(goto) 2>nul & del \"%~f0\" & rd \"%~dp0\" 2>nul\r\n");

        for (int index = 0; index < commands.Count; index++)
        {
            text.Append(CultureInfo.InvariantCulture, $":command{index + 1}\r\n");
            text.Append(CommandPercents.ForBatchFile(commands[index]).ReplaceLineEndings("\r\n")).Append("\r\n");
            text.Append("@exit /b\r\n");
        }

        return WindowsScriptLauncher.CmdFile(text.ToString());
    }
}
