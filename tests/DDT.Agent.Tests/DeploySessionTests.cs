// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using Microsoft.Win32;
using Xunit;

namespace DDT.Agent.Tests;

// DDT's session in the installed Windows, from the hand-over's plan to its end, with its accounts in memory and
// HKEY_LOCAL_MACHINE and HKEY_USERS as keys of the test's own in the current user's hive, which it deletes.
public sealed class DeploySessionTests : IDisposable
{
    private static readonly XNamespace s_unattend = "urn:schemas-microsoft-com:unattend";

    private readonly string _root = Directory.CreateTempSubdirectory("ddt-session-").FullName;
    private readonly string _keys = $@"Software\DDT-test-{Guid.NewGuid():N}";
    private readonly RegistryKey _machine;
    private readonly RegistryKey _users;
    private readonly FakeSessionAccounts _accounts = new();
    private readonly RecordingToolRunner _tools = new();
    private readonly StringWriter _console = new();
    private readonly List<(SecurityIdentifier Sid, string Pipe)> _started = [];

    public DeploySessionTests()
    {
        _machine = Registry.CurrentUser.CreateSubKey($@"{_keys}\HKLM");
        _users = Registry.CurrentUser.CreateSubKey($@"{_keys}\HKU");
    }

    public void Dispose()
    {
        _machine.Dispose();
        _users.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keys, throwOnMissingSubKey: false);
        _console.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string ConsolePath => DeploySession.ConsolePathIn(_root);

    private DeploySessionFile File => JsonSerializer.Deserialize(
        System.IO.File.ReadAllBytes(DeploySession.FilePathIn(_root)),
        DeploySessionFileJsonContext.Default.DeploySessionFile)!;

    [Fact]
    public async Task ThePlanPutsASignInForTheSessionIntoTheAnswerFile()
    {
        StageConsole();
        WriteAnswerFile();

        Assert.True(await DeploySession.PlanAsync(_root, Log(), Cancellation));

        DeploySessionFile file = File;
        Assert.StartsWith("ddt-console-", file.PipeName, StringComparison.Ordinal);
        Assert.Equal(40, file.Password!.Length);
        Assert.Null(file.Saved);

        XElement autoLogon = XDocument.Load(UnattendFile.PathIn(_root)).Descendants(s_unattend + "AutoLogon").Single();
        Assert.Equal("Microsoft-Windows-Shell-Setup", autoLogon.Parent!.Attribute("name")!.Value);
        Assert.Equal("oobeSystem", autoLogon.Parent!.Parent!.Attribute("pass")!.Value);
        Assert.Same(autoLogon, autoLogon.Parent.Elements().First());
        Assert.Equal(DeploySession.AccountName, autoLogon.Element(s_unattend + "Username")!.Value);
        Assert.Equal("1", autoLogon.Element(s_unattend + "LogonCount")!.Value);
        Assert.Equal("true", autoLogon.Element(s_unattend + "Enabled")!.Value);
        Assert.Equal(
            file.Password + "Password",
            Encoding.Unicode.GetString(Convert.FromBase64String(autoLogon.Element(s_unattend + "Password")!.Element(s_unattend + "Value")!.Value)));
    }

    [Fact]
    public async Task WithoutAnAnswerFileThereIsNoSession()
    {
        StageConsole();

        Assert.False(await DeploySession.PlanAsync(_root, Log(), Cancellation));
        await Session().PrepareAsync(Cancellation);
        Session().SetupFinished();

        Assert.Empty(_accounts.Calls);
        Assert.Empty(_started);
        Assert.Null(Machine(DeploySession.WinlogonPath, "AutoAdminLogon"));
        Assert.Contains("the machine shows Windows' own screens", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuringSetupTheAccountAndItsShellAreReadyButSetupKeepsTheSignIn()
    {
        await PlanAsync();

        await Session().PrepareAsync(Cancellation);

        Assert.Equal(["create DDTDeploy", "create the profile of DDTDeploy"], _accounts.Calls);
        Assert.Equal(File.Password, _accounts.PasswordOf(DeploySession.AccountName));
        Assert.Null(_accounts.StoredAutoLogonPassword);

        // The account's registry, loaded from its profile for the moment, names the console and takes away what
        // Ctrl+Alt+Del offers.
        string hive = Path.Combine(_accounts.ProfilePath, "NTUSER.DAT");
        Assert.Equal(
            [
                RecordingToolRunner.CommandLine(OfflineServiceRegistration.RegPath, "load", $@"HKU\{DeploySession.HiveName}", hive),
                RecordingToolRunner.CommandLine(OfflineServiceRegistration.RegPath, "unload", $@"HKU\{DeploySession.HiveName}"),
            ],
            _tools.Calls);

        (SecurityIdentifier sid, string pipe) = Assert.Single(_started);
        Assert.Equal(FakeSessionAccounts.AccountSid, sid);
        Assert.Equal(File.PipeName, pipe);
        Assert.Equal($"\"{ConsolePath}\" --pipe {pipe} --session", User(DeploySession.HiveName, DeploySession.UserWinlogonPath, "Shell"));
        Assert.Equal(1, User(DeploySession.HiveName, DeploySession.UserSystemPoliciesPath, "DisableTaskMgr"));
        Assert.Equal(1, User(DeploySession.HiveName, DeploySession.UserSystemPoliciesPath, "DisableLockWorkstation"));
        Assert.Equal(1, User(DeploySession.HiveName, DeploySession.UserSystemPoliciesPath, "DisableChangePassword"));
        Assert.Equal(1, User(DeploySession.HiveName, DeploySession.UserExplorerPoliciesPath, "NoLogoff"));
        Assert.Equal(1, Machine(DeploySession.SystemPoliciesPath, "HideFastUserSwitching"));
        Assert.Equal(0, Machine(DeploySession.SystemPoliciesPath, "EnableFirstLogonAnimation"));

        // Setup signs in its own first user between its restarts, and the answer file signs in as DDTDeploy after them.
        Assert.Null(Machine(DeploySession.WinlogonPath, "AutoAdminLogon"));
        Assert.Null(Machine(DeploySession.WinlogonPath, "DefaultUserName"));
    }

    [Fact]
    public async Task OnceSetupHasFinishedWindowsSignsInAsTheAccountAfterEveryRestart()
    {
        await PlanAsync();
        await Session().PrepareAsync(Cancellation);
        SetMachine(DeploySession.WinlogonPath, "DefaultPassword", "left by setup");
        SetMachine(DeploySession.WinlogonPath, "AutoLogonCount", 0);

        Session().SetupFinished();

        Assert.Equal(File.Password, _accounts.StoredAutoLogonPassword);
        Assert.Equal("1", Machine(DeploySession.WinlogonPath, "AutoAdminLogon"));
        Assert.Equal("1", Machine(DeploySession.WinlogonPath, "ForceAutoLogon"));
        Assert.Equal(DeploySession.AccountName, Machine(DeploySession.WinlogonPath, "DefaultUserName"));
        Assert.Equal(Environment.MachineName, Machine(DeploySession.WinlogonPath, "DefaultDomainName"));

        // Not in the key, which everyone can read.
        Assert.Null(Machine(DeploySession.WinlogonPath, "DefaultPassword"));
        Assert.Null(Machine(DeploySession.WinlogonPath, "AutoLogonCount"));
    }

    [Fact]
    public async Task AnotherStartOfTheServiceKeepsThePasswordAndThePipe()
    {
        await PlanAsync();
        await Session().PrepareAsync(Cancellation);
        string password = _accounts.PasswordOf(DeploySession.AccountName)!;

        await Session().PrepareAsync(Cancellation);

        Assert.Equal(password, _accounts.PasswordOf(DeploySession.AccountName));
        Assert.Equal(2, _started.Count);
        Assert.Equal(_started[0].Pipe, _started[1].Pipe);
        Assert.Single(_accounts.Calls, call => call == "create DDTDeploy");
    }

    [Fact]
    public async Task WritesToTheRegistryOfTheSignedInAccountWithoutLoadingIt()
    {
        await PlanAsync();
        await Session().PrepareAsync(Cancellation);
        _users.CreateSubKey(FakeSessionAccounts.AccountSid.Value).Dispose();

        await Session().PrepareAsync(Cancellation);

        Assert.Equal(2, _tools.Calls.Count);
        Assert.StartsWith("\"", (string)User(FakeSessionAccounts.AccountSid.Value, DeploySession.UserWinlogonPath, "Shell")!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePasswordChangesOnceTheSessionIsUpAndTheFileForgetsIt()
    {
        await PlanAsync();
        DeploySession session = Session();
        await session.PrepareAsync(Cancellation);
        session.SetupFinished();
        string planned = _accounts.StoredAutoLogonPassword!;

        session.SessionIsUp();
        string renewed = _accounts.StoredAutoLogonPassword!;
        session.SessionIsUp();

        Assert.NotEqual(planned, renewed);
        Assert.Equal(renewed, _accounts.StoredAutoLogonPassword);
        Assert.Equal(renewed, _accounts.PasswordOf(DeploySession.AccountName));
        Assert.Null(File.Password);
        Assert.Single(_accounts.Calls, call => call == "set the password of DDTDeploy");

        // A later end of setup, as after a restart, keeps it.
        Session().SetupFinished();
        Assert.Equal(renewed, _accounts.StoredAutoLogonPassword);
    }

    [Fact]
    public async Task EndingPutsTheMachineBackAndDeletesTheAccountAndItsProfile()
    {
        SetMachine(DeploySession.SystemPoliciesPath, "EnableFirstLogonAnimation", 1);
        await PlanAsync();
        await Session().PrepareAsync(Cancellation);
        Session().SetupFinished();
        _accounts.SignIn(1);
        SetMachine(DeploySession.LogonUIPath, "LastLoggedOnUserSID", FakeSessionAccounts.AccountSid.Value);
        SetMachine(DeploySession.LogonUIPath, "LastLoggedOnUser", @".\DDTDeploy");
        int before = _accounts.Calls.Count;

        Assert.True(await Session().EndAsync(signOut: true, Cancellation));

        Assert.Equal(
            ["delete the auto-logon password", "disable DDTDeploy", "sign out session 1", "delete the profile", "delete DDTDeploy"],
            _accounts.Calls.Skip(before));
        Assert.Equal("0", Machine(DeploySession.WinlogonPath, "AutoAdminLogon"));
        Assert.Null(Machine(DeploySession.WinlogonPath, "ForceAutoLogon"));
        Assert.Null(Machine(DeploySession.WinlogonPath, "DefaultUserName"));
        Assert.Null(Machine(DeploySession.SystemPoliciesPath, "HideFastUserSwitching"));
        Assert.Equal(1, Machine(DeploySession.SystemPoliciesPath, "EnableFirstLogonAnimation"));
        Assert.Null(Machine(DeploySession.LogonUIPath, "LastLoggedOnUserSID"));
        Assert.Null(Machine(DeploySession.LogonUIPath, "LastLoggedOnUser"));
        Assert.False(System.IO.File.Exists(DeploySession.FilePathIn(_root)));
    }

    // Signing in as DDTDeploy keeps setup from deleting its first user.
    [Fact]
    public async Task EndingDeletesTheAccountSetupLeftBehind()
    {
        await PlanAsync();
        await Session().PrepareAsync(Cancellation);
        _accounts.Create(RegistrySetupProbe.SetupUser, "set up by Windows");

        Assert.True(await Session().EndAsync(signOut: true, Cancellation));

        Assert.False(_accounts.Exists(RegistrySetupProbe.SetupUser));
        Assert.Equal(["delete DDTDeploy", "delete the profile", "delete defaultuser0"], _accounts.Calls.TakeLast(3));
        Assert.Contains("Deleted defaultuser0, the temporary account Windows setup left behind.", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterAFailureTheSessionStaysUntilSomeoneSignsOut()
    {
        await PlanAsync();
        await Session().PrepareAsync(Cancellation);
        Session().SetupFinished();
        _accounts.SignIn(1);
        ManualTimeProvider time = new();

        Task<bool> ending = Session(time).EndAsync(signOut: false, Cancellation);
        await WaitForAsync(() => time.PendingTimers > 0);

        // Nobody can sign in with it any more, but it stays signed in and shows the failure.
        Assert.True(_accounts.IsDisabled(DeploySession.AccountName));
        Assert.Equal("0", Machine(DeploySession.WinlogonPath, "AutoAdminLogon"));
        Assert.DoesNotContain("sign out session 1", _accounts.Calls);
        Assert.False(ending.IsCompleted);

        _accounts.SignedOutByHand();
        time.Advance(DeploySession.SignOutPollInterval);

        Assert.True(await ending);
        Assert.False(_accounts.Exists(DeploySession.AccountName));
    }

    [Fact]
    public async Task AStopWhileWaitingForTheSignOutLeavesTheAccountToTheNextStart()
    {
        await PlanAsync();
        await Session().PrepareAsync(Cancellation);
        _accounts.SignIn(1);
        using CancellationTokenSource stop = new();
        ManualTimeProvider time = new();

        Task<bool> ending = Session(time).EndAsync(signOut: false, stop.Token);
        await WaitForAsync(() => time.PendingTimers > 0);
        await stop.CancelAsync();

        Assert.False(await ending);
        Assert.True(_accounts.Exists(DeploySession.AccountName));
        Assert.True(System.IO.File.Exists(DeploySession.FilePathIn(_root)));
    }

    [Fact]
    public async Task AProfileStillInUseIsTriedAgain()
    {
        await PlanAsync();
        await Session().PrepareAsync(Cancellation);
        _accounts.ProfileInUse = 2;

        Assert.True(await Session().EndAsync(signOut: true, Cancellation));

        Assert.False(_accounts.HasProfile);
        Assert.False(_accounts.Exists(DeploySession.AccountName));
    }

    [Fact]
    public async Task EndingWithoutASessionChangesNothing()
    {
        Assert.True(await Session().EndAsync(signOut: true, Cancellation));

        Assert.Empty(_accounts.Calls);
        Assert.Null(Machine(DeploySession.WinlogonPath, "AutoAdminLogon"));
    }

    [Fact]
    public async Task TheFirstStartKeepsThePoliciesAsTheyWere()
    {
        SetMachine(DeploySession.SystemPoliciesPath, "HideFastUserSwitching", 1);
        await PlanAsync();

        await Session().PrepareAsync(Cancellation);

        Assert.Equal(
            [
                new SavedSetting(DeploySession.SystemPoliciesPath, "HideFastUserSwitching", null, 1),
                new SavedSetting(DeploySession.SystemPoliciesPath, "EnableFirstLogonAnimation", null, null),
            ],
            File.Saved);
    }

    private async Task PlanAsync()
    {
        StageConsole();
        WriteAnswerFile();
        Assert.True(await DeploySession.PlanAsync(_root, Log(), Cancellation));
    }

    private DeploySession Session(TimeProvider? time = null) => new(
        _root,
        _accounts,
        new SessionRegistry(_machine, _users, _tools),
        Log(),
        time ?? new ImmediateTimeProvider(),
        (sid, pipe) => _started.Add((sid, pipe)));

    private AgentLog Log() => new(TimeProvider.System, _console);

    private void StageConsole()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConsolePath)!);
        System.IO.File.WriteAllText(ConsolePath, "MZ");
    }

    // The passes as the server's answer file has them.
    private void WriteAnswerFile()
    {
        XElement Component(string name, params object[] content) => new(
            s_unattend + "component",
            new XAttribute("name", name),
            new XAttribute("processorArchitecture", "amd64"),
            content);

        XDocument document = new(new XElement(
            s_unattend + "unattend",
            new XElement(s_unattend + "settings", new XAttribute("pass", "specialize"), Component("Microsoft-Windows-Shell-Setup", new XElement(s_unattend + "ComputerName", "PC-1"))),
            new XElement(
                s_unattend + "settings",
                new XAttribute("pass", "oobeSystem"),
                Component("Microsoft-Windows-Shell-Setup", new XElement(s_unattend + "OOBE", new XElement(s_unattend + "HideEULAPage", "true"))))));

        string path = UnattendFile.PathIn(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        document.Save(path);
    }

    private object? Machine(string key, string name)
    {
        using RegistryKey? opened = _machine.OpenSubKey(key);

        return opened?.GetValue(name);
    }

    private void SetMachine(string key, string name, object value)
    {
        using RegistryKey created = _machine.CreateSubKey(key);
        created.SetValue(name, value);
    }

    private object? User(string root, string key, string name)
    {
        using RegistryKey? opened = _users.OpenSubKey($@"{root}\{key}");

        return opened?.GetValue(name);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10, Cancellation);
        }

        Assert.True(condition());
    }
}
