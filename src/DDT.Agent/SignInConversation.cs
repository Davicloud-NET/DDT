// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// The sign-in typed at the machine. Like the web sign-in, a wrong password or code only asks for that field again.
// Typed user names never go to the log, because a password typed into the wrong field would be uploaded with them.
public sealed class SignInConversation(IMachineConsole console, AgentLog log)
{
    private string? _userName;
    private string? _password;
    private bool _codeRequired;
    private bool _introduced;
    private string? _error;

    public bool IsAvailable => console.CanAsk;

    public Task<ConsoleAnswer?> ReadAsync(CancellationToken cancellationToken)
    {
        if (_userName is null)
        {
            if (!_introduced)
            {
                _introduced = true;
                log.Information("Sign in with your DDT account at this machine. Enter an empty password to use another account.");
            }

            return console.AskAsync(new SignInQuestion(SignInField.UserName, null, _error), cancellationToken);
        }

        SignInField field = _password is null ? SignInField.Password : SignInField.Code;

        return console.AskAsync(new SignInQuestion(field, _userName, _error), cancellationToken);
    }

    // The request to send once everything the server needs has been typed, otherwise null.
    public AgentSignInRequest? Accept(ConsoleAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        string typed = answer.Back ? string.Empty : answer.Text ?? string.Empty;
        _error = null;

        if (_userName is null)
        {
            _userName = string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();

            return null;
        }

        if (_password is null)
        {
            if (typed.Length == 0)
            {
                Reset();

                return null;
            }

            _password = typed;

            return _codeRequired ? null : new AgentSignInRequest(_userName, _password, null);
        }

        if (string.IsNullOrWhiteSpace(typed))
        {
            _password = null;
            _codeRequired = false;

            return null;
        }

        return new AgentSignInRequest(_userName, _password, typed.Trim());
    }

    public void Handle(AgentSignInStatus status)
    {
        switch (status)
        {
            case AgentSignInStatus.Succeeded:
                log.Information("Signed in.");
                Reset();
                break;
            case AgentSignInStatus.RequiresTwoFactor:
                _codeRequired = true;
                break;
            case AgentSignInStatus.Failed when _codeRequired:
                Warn("That code is not valid. Check the time on the device that shows it.");
                break;
            case AgentSignInStatus.Failed:
                Warn("Wrong user name or password.");
                _password = null;
                break;
            case AgentSignInStatus.LockedOut:
                Warn("This account is locked after too many attempts. Wait a few minutes or use another account.");
                Reset();
                break;
            case AgentSignInStatus.NotPermitted:
                Warn("This account may not authorize machines. Sign in as an operator or administrator.");
                Reset();
                break;
            default:
                Reset();
                break;
        }
    }

    // Nothing reached the server, or nothing came back. The last secret is asked for again rather than resent,
    // because a request that timed out may still have counted toward the lockout.
    public void NotSent(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Warn(exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
            ? "Too many sign in attempts from this address. Wait a few minutes, then try again."
            : $"Cannot sign in ({exception.Message}). Try again.");

        if (!_codeRequired)
        {
            _password = null;
        }
    }

    public void Reset()
    {
        _userName = null;
        _password = null;
        _codeRequired = false;
    }

    private void Warn(string message)
    {
        log.Warning(message);
        _error = message;
    }
}
