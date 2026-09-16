namespace DDT.Agent;

public interface ISignInPrompt
{
    // False when nobody can type at this machine, for example when input is redirected.
    bool IsAvailable { get; }

    // Completes with the typed line, or with null once cancelled.
    Task<string?> ReadLineAsync(string label, bool secret, CancellationToken cancellationToken);
}
