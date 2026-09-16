using System.Net;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// What the technician has typed so far. A wrong password asks again only for the password, and a wrong code
// only for the code, as the web sign in page does. Typed user names never reach the log, because a password
// typed into the wrong field would otherwise be uploaded with it.
public sealed class SignInConversation(ISignInPrompt prompt, AgentLog log)
{
    private string? _userName;
    private string? _password;
    private bool _codeRequired;
    private bool _introduced;

    public bool IsAvailable => prompt.IsAvailable;

    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        if (_userName is null)
        {
            if (!_introduced)
            {
                _introduced = true;
                log.Information("Sign in with your DDT account at this machine. Enter an empty password to use another account.");
            }

            return prompt.ReadLineAsync("User name", secret: false, cancellationToken);
        }

        return _password is null
            ? prompt.ReadLineAsync($"Password for {_userName}", secret: true, cancellationToken)
            : prompt.ReadLineAsync("Authenticator code", secret: false, cancellationToken);
    }

    // The request to send once everything the server needs has been typed, otherwise null.
    public AgentSignInRequest? Accept(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);

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
                log.Warning("That code is not valid. Check the time on the device that shows it.");
                break;
            case AgentSignInStatus.Failed:
                log.Warning("Wrong user name or password.");
                _password = null;
                break;
            case AgentSignInStatus.LockedOut:
                log.Warning("This account is locked after too many attempts. Wait a few minutes or use another account.");
                Reset();
                break;
            case AgentSignInStatus.NotPermitted:
                log.Warning("This account may not authorize machines. Sign in as an operator or administrator.");
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

        log.Warning(exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
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
}
