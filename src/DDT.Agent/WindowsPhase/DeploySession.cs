// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Principal;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;

namespace DDT.Agent.WindowsPhase;

// DDT's session in the installed Windows: Windows signs in by itself as DDTDeploy, a standard account only the run uses,
// whose shell is DDT's console, so the machine shows the run. The steps still run in the service as SYSTEM.
public sealed class DeploySession(
    string windowsRoot,
    ISessionAccounts accounts,
    SessionRegistry registry,
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
    private readonly DeploySessionStore _store = new(windowsRoot, log);
    private readonly MachineSignInSettings _signIn = new(registry.Machine);
    private readonly AccountHiveWriter _hive = new(registry.Users, registry.Tools);
    private readonly SessionCleanup _cleanup = new(accounts, log, timeProvider);
    private bool _renewed;

    public static string FilePathIn(string windowsRoot) => Path.Combine(windowsRoot, "DDT", "session.json");

    public static string ConsolePathIn(string windowsRoot) =>
        Path.Combine(windowsRoot, "DDT", WindowsHandOver.ConsoleDirectory, ConsolePipe.FileName);

    // In Windows PE, once the console is staged: setup signs in as DDTDeploy once, at the end of the out-of-box
    // experience, through the answer file's AutoLogon. False without an answer file, as when someone at the machine is to
    // finish setup; then the run shows only on the server.
    public static async Task<bool> PlanAsync(string windowsRoot, AgentLog log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);

        string password = SessionPassword.New();

        if (!await UnattendFile.AddAutoLogonAsync(windowsRoot, AccountName, password, cancellationToken).ConfigureAwait(false))
        {
            log.Information("The run has no answer file to sign in with, so the machine shows Windows' own screens while the run goes on.");

            return false;
        }

        await new DeploySessionStore(windowsRoot, log).SaveAsync(new DeploySessionFile(ConsolePipe.NewName(), password, null), cancellationToken).ConfigureAwait(false);
        log.Information($"Once Windows setup has finished, Windows signs in as {AccountName}, a standard account only this run uses, and shows DDT's console.");

        return true;
    }

    // At every start of the service, from the first one during setup. The profile comes before the first sign-in, so
    // its own settings can name the console as its shell.
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        string console = ConsolePathIn(windowsRoot);

        if (!File.Exists(console) || await _store.LoadAsync(cancellationToken).ConfigureAwait(false) is not { } file)
        {
            return;
        }

        try
        {
            if (file.Saved is null)
            {
                file = file with { Saved = _signIn.Policies() };
                await _store.SaveAsync(file, cancellationToken).ConfigureAwait(false);
            }

            if (!accounts.Exists(AccountName))
            {
                file = await CreateAccountAsync(file, cancellationToken).ConfigureAwait(false);
            }

            SecurityIdentifier sid = accounts.Sid(AccountName);
            string profile = accounts.CreateProfile(sid, AccountName);
            await _hive.WriteAsync(sid, profile, console, file.PipeName, cancellationToken).ConfigureAwait(false);
            _signIn.ChangePolicies();
            startConsole?.Invoke(sid, file.PipeName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.Warning($"DDT's session could not be prepared ({LogText.OneLine(exception)}). The run goes on, but the machine shows Windows' own screens.");
        }
    }

    // Takes over the sign-in settings for the restarts the run still has; until setup has finished they are setup's.
    public void SetupFinished()
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(ConsolePathIn(windowsRoot)) || _store.Load() is not { } file || !accounts.Exists(AccountName))
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

                _signIn.SignInAutomatically();
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
                if (_renewed || _store.Load() is not { } file)
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

    // The machine's settings go back before the account is disabled, its sessions end, and it goes with its profile.
    public async Task<bool> EndAsync(bool signOut, CancellationToken cancellationToken)
    {
        DeploySessionFile? file = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (file is null && !accounts.Exists(AccountName))
        {
            return true;
        }

        _cleanup.Attempt("put back the machine's sign-in settings", () => _signIn.Restore(file));
        _cleanup.Attempt("delete the auto-logon password", () => accounts.SetAutoLogonPassword(null));

        if (accounts.Exists(AccountName))
        {
            SecurityIdentifier sid = accounts.Sid(AccountName);
            _cleanup.Attempt($"disable {AccountName}", () => accounts.Disable(AccountName, SessionPassword.New()));

            if (!await _cleanup.EndSessionsAsync(signOut, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            _cleanup.Attempt("forget the last user of the sign-in screen", () => _signIn.ForgetLastUser(sid));
            await _cleanup.DeleteProfileAsync(sid, AccountName).ConfigureAwait(false);
            _cleanup.Attempt($"delete {AccountName}", () => accounts.Delete(AccountName));
            await _cleanup.DeleteSetupUserAsync().ConfigureAwait(false);
        }

        _store.Delete();
        log.Information($"DDT's session is gone: {AccountName} and its profile are deleted, and Windows' sign-in settings are as they were.");

        return true;
    }

    // With the password the answer file signs in with, or, once that was replaced, with another that SetupFinished
    // makes the auto-logon password.
    private async Task<DeploySessionFile> CreateAccountAsync(DeploySessionFile file, CancellationToken cancellationToken)
    {
        string password = file.Password ?? SessionPassword.New();

        if (file.Password is null)
        {
            file = file with { Password = password };
            await _store.SaveAsync(file, cancellationToken).ConfigureAwait(false);
        }

        accounts.Create(AccountName, password);
        log.Information($"Created {AccountName}, the standard account Windows signs in as to show DDT's console.");

        return file;
    }

    // A new password for the account and the LSA secret, in that order, which the file no longer needs to know.
    private void Renew(DeploySessionFile file)
    {
        string password = SessionPassword.New();
        accounts.SetPassword(AccountName, password);
        accounts.SetAutoLogonPassword(password);

        if (file.Password is not null)
        {
            _store.Save(file with { Password = null });
        }
    }
}
