// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// DDT's session in the installed Windows, so the machine shows the run on DDT's console rather than Windows' sign-in
// screen: Windows signs in by itself as DDTDeploy, a local standard account that exists only for the run, whose shell
// is the console. The steps still run in the service, as SYSTEM; the session only shows them.
//
// The hand-over in Windows PE plans it, where the console came along and there is an answer file: a password nobody is
// told, in session.json, and the answer file's AutoLogon, so Setup itself signs in as DDTDeploy once, at the very end of
// the out-of-box experience. Setup signs in its own first user between its restarts, and nothing outside it can time
// that sign-in, so the machine's sign-in settings are left to Setup until it has finished.
//
// PrepareAsync runs at every start of the service, from the first one during setup: the account with that password;
// its profile, made before its first sign-in so its own settings can name the console as its shell and take away what
// Ctrl+Alt+Del offers, Task Manager, locking, changing the password and signing out; no switching users and no first
// sign-in animation. Once setup has finished, SetupFinished takes over the sign-in settings for the restarts the run
// still has: auto-logon that signs in again after a sign-out, with the password as the LSA secret Winlogon reads. The
// password changes once the session is up after every start of Windows. EndAsync puts the machine's settings back,
// disables the account, ends the session, and deletes the account and its profile.
public sealed class DeploySession(
    string windowsRoot,
    ISessionAccounts accounts,
    RegistryKey machine,
    RegistryKey users,
    IToolRunner tools,
    AgentLog log,
    TimeProvider timeProvider,
    Action<SecurityIdentifier, string>? startConsole = null) : IDeploySession
{
    public const string AccountName = "DDTDeploy";

    // Where the account's registry is loaded while it is not signed in.
    public const string HiveName = "DDT_DEPLOY";

    public const string WinlogonPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    public const string SystemPoliciesPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string LogonUIPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI";

    public const string UserWinlogonPath = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
    public const string UserSystemPoliciesPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string UserExplorerPoliciesPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    // How long the console shows a finished run before the session is signed out.
    public static readonly TimeSpan SignOutGrace = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan SignOutPollInterval = TimeSpan.FromSeconds(5);

    // A profile stays in use for a moment after its session ended.
    public static readonly TimeSpan ProfileRetryInterval = TimeSpan.FromSeconds(5);
    public const int ProfileAttempts = 6;

    private readonly Lock _lock = new();
    private bool _renewed;

    public static string FilePathIn(string windowsRoot) => Path.Combine(windowsRoot, "DDT", "session.json");

    public static string ConsolePathIn(string windowsRoot) =>
        Path.Combine(windowsRoot, "DDT", WindowsHandOver.ConsoleDirectory, ConsolePipe.FileName);

    // In Windows PE, once the console is staged into the Windows at windowsRoot. False when there is no answer file to
    // sign in with, as when someone at the machine is to finish setup; then the run shows only on the server.
    public static async Task<bool> PlanAsync(string windowsRoot, AgentLog log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);

        string password = NewPassword();

        if (!await UnattendFile.AddAutoLogonAsync(windowsRoot, AccountName, password, cancellationToken).ConfigureAwait(false))
        {
            log.Information("The run has no answer file to sign in with, so the machine shows Windows' own screens while the run goes on.");

            return false;
        }

        await SaveAsync(windowsRoot, new DeploySessionFile(ConsolePipe.NewName(), password, null), cancellationToken).ConfigureAwait(false);
        log.Information($"Once Windows setup has finished, Windows signs in as {AccountName}, a standard account only this run uses, and shows DDT's console.");

        return true;
    }

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        string console = ConsolePathIn(windowsRoot);

        if (!File.Exists(console) || await LoadAsync(windowsRoot, log, cancellationToken).ConfigureAwait(false) is not { } file)
        {
            return;
        }

        try
        {
            if (file.Saved is null)
            {
                file = file with
                {
                    Saved = [Saved(SystemPoliciesPath, "HideFastUserSwitching"), Saved(SystemPoliciesPath, "EnableFirstLogonAnimation")],
                };
                await SaveAsync(windowsRoot, file, cancellationToken).ConfigureAwait(false);
            }

            if (!accounts.Exists(AccountName))
            {
                // Created with the password the answer file signs in with, or, once that was replaced, with another
                // that SetupFinished makes the auto-logon password.
                string password = file.Password ?? NewPassword();

                if (file.Password is null)
                {
                    file = file with { Password = password };
                    await SaveAsync(windowsRoot, file, cancellationToken).ConfigureAwait(false);
                }

                accounts.Create(AccountName, password);
                log.Information($"Created {AccountName}, the standard account Windows signs in as to show DDT's console.");
            }

            SecurityIdentifier sid = accounts.Sid(AccountName);
            string profile = accounts.CreateProfile(sid, AccountName);
            await WriteAccountSettingsAsync(sid, profile, console, file.PipeName, cancellationToken).ConfigureAwait(false);

            using (RegistryKey policies = machine.CreateSubKey(SystemPoliciesPath))
            {
                policies.SetValue("HideFastUserSwitching", 1, RegistryValueKind.DWord);
                policies.SetValue("EnableFirstLogonAnimation", 0, RegistryValueKind.DWord);
            }

            startConsole?.Invoke(sid, file.PipeName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.Warning($"DDT's session could not be prepared ({LogText.OneLine(exception)}). The run goes on, but the machine shows Windows' own screens.");
        }
    }

    public void SetupFinished()
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(ConsolePathIn(windowsRoot)) || Load(windowsRoot, log) is not { } file || !accounts.Exists(AccountName))
                {
                    return;
                }

                // The account still has the password it was made with until the session is up, and the LSA secret
                // must hold it before the key stops holding one.
                if (file.Password is { } password)
                {
                    accounts.SetAutoLogonPassword(password);
                }
                else if (accounts.AutoLogonPassword() is null)
                {
                    Renew(file);
                }

                using RegistryKey winlogon = machine.CreateSubKey(WinlogonPath);
                winlogon.SetValue("AutoAdminLogon", "1");
                winlogon.SetValue("ForceAutoLogon", "1");
                winlogon.SetValue("DefaultUserName", AccountName);
                winlogon.SetValue("DefaultDomainName", Environment.MachineName);

                // A password in the key would win over the LSA secret, and every user can read the key.
                winlogon.DeleteValue("DefaultPassword", throwOnMissingValue: false);
                winlogon.DeleteValue("AutoLogonCount", throwOnMissingValue: false);
            }

            log.Information($"Windows signs in as {AccountName} again whenever it restarts for the run.");
        }
        catch (Exception exception)
        {
            log.Warning($"Windows could not be set to sign in as {AccountName} after a restart ({LogText.OneLine(exception)}).");
        }
    }

    // Once per start of the service, when the session's console connects: Windows has signed in with the password, so
    // the next start of Windows gets another.
    public void SessionIsUp()
    {
        try
        {
            lock (_lock)
            {
                if (_renewed || Load(windowsRoot, log) is not { } file)
                {
                    return;
                }

                Renew(file);
                _renewed = true;
            }

            log.Information($"DDT's session is up. The password {AccountName} signs in with next time is a new one.");
        }
        catch (Exception exception)
        {
            log.Warning($"The password of {AccountName} could not be changed ({LogText.OneLine(exception)}). The one it has stays.");
        }
    }

    public async Task<bool> EndAsync(bool signOut, CancellationToken cancellationToken)
    {
        DeploySessionFile? file = await LoadAsync(windowsRoot, log, cancellationToken).ConfigureAwait(false);

        if (file is null && !accounts.Exists(AccountName))
        {
            return true;
        }

        Attempt("put back the machine's sign-in settings", () => RestoreMachineSettings(file));
        Attempt("delete the auto-logon password", () => accounts.SetAutoLogonPassword(null));

        if (accounts.Exists(AccountName))
        {
            SecurityIdentifier sid = accounts.Sid(AccountName);
            Attempt($"disable {AccountName}", () => accounts.Disable(AccountName, NewPassword()));

            if (!await EndSessionsAsync(signOut, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            Attempt("forget the last user of the sign-in screen", () => ForgetLastUser(sid));
            await DeleteProfileAsync(sid, AccountName).ConfigureAwait(false);
            Attempt($"delete {AccountName}", () => accounts.Delete(AccountName));
            await DeleteSetupUserAsync().ConfigureAwait(false);
        }

        Leftovers.Delete(FilePathIn(windowsRoot), log);
        log.Information($"DDT's session is gone: {AccountName} and its profile are deleted, and Windows' sign-in settings are as they were.");

        return true;
    }

    // A new password for the account and the LSA secret, in that order, which the file no longer needs to know.
    private void Renew(DeploySessionFile file)
    {
        string password = NewPassword();
        accounts.SetPassword(AccountName, password);
        accounts.SetAutoLogonPassword(password);

        if (file.Password is not null)
        {
            Save(windowsRoot, file with { Password = null });
        }
    }

    // False when the stop token ended the wait for someone to sign out.
    private async Task<bool> EndSessionsAsync(bool signOut, CancellationToken cancellationToken)
    {
        IReadOnlyList<int> sessions;

        try
        {
            sessions = accounts.Sessions(AccountName);
        }
        catch (Exception exception)
        {
            log.Warning($"The sessions of {AccountName} could not be listed ({LogText.OneLine(exception)}).");

            return true;
        }

        if (sessions.Count == 0)
        {
            return true;
        }

        try
        {
            if (signOut)
            {
                await Task.Delay(SignOutGrace, timeProvider, cancellationToken).ConfigureAwait(false);

                foreach (int session in sessions)
                {
                    Attempt($"sign out session {session}", () => accounts.SignOut(session));
                }

                return true;
            }

            log.Information("The console shows how the run ended until someone at the machine signs out with F9.");

            while (accounts.Sessions(AccountName).Count > 0)
            {
                await Task.Delay(SignOutPollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // Setup deletes its first user, defaultuser0, when that user's part of the out-of-box experience ends. Signing in
    // as DDTDeploy, whose shell is the console, keeps it from getting there, so the account stays behind, and the
    // sign-in screen offers it. It goes as setup would have deleted it, unless someone is signed in to it.
    private async Task DeleteSetupUserAsync()
    {
        const string name = RegistrySetupProbe.SetupUser;

        try
        {
            if (!accounts.Exists(name) || accounts.Sessions(name).Count > 0)
            {
                return;
            }

            await DeleteProfileAsync(accounts.Sid(name), name).ConfigureAwait(false);
            accounts.Delete(name);
            log.Information($"Deleted {name}, the temporary account Windows setup left behind.");
        }
        catch (Exception exception)
        {
            log.Warning($"Could not delete {name}, the temporary account Windows setup left behind ({LogText.OneLine(exception)}).");
        }
    }

    private async Task DeleteProfileAsync(SecurityIdentifier sid, string name)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                accounts.DeleteProfile(sid);

                return;
            }
            catch (Exception exception) when (attempt < ProfileAttempts)
            {
                log.Information($"The profile of {name} is still in use ({LogText.OneLine(exception)}). Trying again.");
                await Task.Delay(ProfileRetryInterval, timeProvider, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                log.Warning($"The profile of {name} could not be deleted ({LogText.OneLine(exception)}). It stays in C:\\Users.");

                return;
            }
        }
    }

    private async Task WriteAccountSettingsAsync(SecurityIdentifier sid, string profile, string console, string pipeName, CancellationToken cancellationToken)
    {
        // Signed in, its registry is loaded under its SID; otherwise it is loaded here for the moment.
        string root = sid.Value;
        bool load = false;

        using (RegistryKey? signedIn = users.OpenSubKey(root))
        using (RegistryKey? loaded = users.OpenSubKey(HiveName))
        {
            if (signedIn is null)
            {
                root = HiveName;
                load = loaded is null;
            }
        }

        if (load)
        {
            await tools.RunAsync(OfflineServiceRegistration.RegPath, ["load", $@"HKU\{HiveName}", Path.Combine(profile, "NTUSER.DAT")], cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            using (RegistryKey winlogon = users.CreateSubKey($@"{root}\{UserWinlogonPath}"))
            {
                winlogon.SetValue("Shell", $"\"{console}\" {ConsolePipe.PipeArgument} {pipeName} {ConsolePipe.SessionArgument}");
            }

            using (RegistryKey policies = users.CreateSubKey($@"{root}\{UserSystemPoliciesPath}"))
            {
                policies.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                policies.SetValue("DisableLockWorkstation", 1, RegistryValueKind.DWord);
                policies.SetValue("DisableChangePassword", 1, RegistryValueKind.DWord);
            }

            using (RegistryKey explorer = users.CreateSubKey($@"{root}\{UserExplorerPoliciesPath}"))
            {
                explorer.SetValue("NoLogoff", 1, RegistryValueKind.DWord);
            }
        }
        finally
        {
            if (root == HiveName)
            {
                await tools.RunAsync(OfflineServiceRegistration.RegPath, ["unload", $@"HKU\{HiveName}"], CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    // Auto-logon off, and the values only DDT set gone. The policies are put back as the file says they were, or taken
    // out when it never said, as after a failure before the service read them.
    private void RestoreMachineSettings(DeploySessionFile? file)
    {
        using (RegistryKey winlogon = machine.CreateSubKey(WinlogonPath))
        {
            winlogon.SetValue("AutoAdminLogon", "0");
            winlogon.DeleteValue("ForceAutoLogon", throwOnMissingValue: false);
            winlogon.DeleteValue("DefaultPassword", throwOnMissingValue: false);
            winlogon.DeleteValue("AutoLogonCount", throwOnMissingValue: false);

            if (winlogon.GetValue("DefaultUserName") as string == AccountName)
            {
                winlogon.DeleteValue("DefaultUserName");
            }
        }

        IReadOnlyList<SavedSetting> saved = file?.Saved
            ?? [new SavedSetting(SystemPoliciesPath, "HideFastUserSwitching", null, null), new SavedSetting(SystemPoliciesPath, "EnableFirstLogonAnimation", null, null)];

        foreach (SavedSetting setting in saved)
        {
            using RegistryKey key = machine.CreateSubKey(setting.Key);

            if (setting.Text is { } text)
            {
                key.SetValue(setting.Name, text);
            }
            else if (setting.Number is { } number)
            {
                key.SetValue(setting.Name, number, RegistryValueKind.DWord);
            }
            else
            {
                key.DeleteValue(setting.Name, throwOnMissingValue: false);
            }
        }
    }

    // The sign-in screen would offer the deleted account as the last one signed in.
    private void ForgetLastUser(SecurityIdentifier sid)
    {
        using RegistryKey? logonUI = machine.OpenSubKey(LogonUIPath, writable: true);

        if (logonUI is null)
        {
            return;
        }

        if (logonUI.GetValue("LastLoggedOnUserSID") as string == sid.Value)
        {
            foreach (string name in new[] { "LastLoggedOnUser", "LastLoggedOnSAMUser", "LastLoggedOnDisplayName", "LastLoggedOnUserSID" })
            {
                logonUI.DeleteValue(name, throwOnMissingValue: false);
            }
        }

        if (logonUI.GetValue("SelectedUserSID") as string == sid.Value)
        {
            logonUI.DeleteValue("SelectedUserSID");
        }
    }

    private SavedSetting Saved(string key, string name)
    {
        using RegistryKey? opened = machine.OpenSubKey(key);

        return opened?.GetValue(name) switch
        {
            string text => new SavedSetting(key, name, text, null),
            int number => new SavedSetting(key, name, null, number),
            _ => new SavedSetting(key, name, null, null),
        };
    }

    private static async Task SaveAsync(string windowsRoot, DeploySessionFile file, CancellationToken cancellationToken)
    {
        string path = FilePathIn(windowsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(file, DeploySessionFileJsonContext.Default.DeploySessionFile), cancellationToken)
            .ConfigureAwait(false);
    }

    private static void Save(string windowsRoot, DeploySessionFile file) =>
        File.WriteAllBytes(FilePathIn(windowsRoot), JsonSerializer.SerializeToUtf8Bytes(file, DeploySessionFileJsonContext.Default.DeploySessionFile));

    private static async Task<DeploySessionFile?> LoadAsync(string windowsRoot, AgentLog log, CancellationToken cancellationToken)
    {
        string path = FilePathIn(windowsRoot);

        return File.Exists(path) ? Parse(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), path, log) : null;
    }

    private static DeploySessionFile? Load(string windowsRoot, AgentLog log)
    {
        string path = FilePathIn(windowsRoot);

        return File.Exists(path) ? Parse(File.ReadAllBytes(path), path, log) : null;
    }

    private static DeploySessionFile? Parse(byte[] json, string path, AgentLog log)
    {
        try
        {
            return JsonSerializer.Deserialize(json, DeploySessionFileJsonContext.Default.DeploySessionFile);
        }
        catch (JsonException exception)
        {
            log.Warning($"{path} cannot be read ({exception.Message}), so the run goes on without DDT's session.");

            return null;
        }
    }

    private void Attempt(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            log.Warning($"Could not {what} ({LogText.OneLine(exception)}).");
        }
    }

    // 40 characters, at least one of each kind, as a local password policy may ask for.
    private static string NewPassword()
    {
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string symbols = "!#%+-.=?@_";

        char[] password = [
            .. RandomNumberGenerator.GetItems<char>(lower + upper + digits + symbols, 36),
            RandomNumberGenerator.GetItems<char>(lower, 1)[0],
            RandomNumberGenerator.GetItems<char>(upper, 1)[0],
            RandomNumberGenerator.GetItems<char>(digits, 1)[0],
            RandomNumberGenerator.GetItems<char>(symbols, 1)[0],
        ];
        RandomNumberGenerator.Shuffle<char>(password);

        return new string(password);
    }
}
