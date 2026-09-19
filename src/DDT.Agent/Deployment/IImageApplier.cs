namespace DDT.Agent.Deployment;

public interface IImageApplier
{
    // Makes sure an apply can run, before anything is erased. Throws with a sentence for the operator.
    void Prepare();

    Task ApplyAsync(string wimPath, int index, string targetRoot, IProgress<int> percent, CancellationToken cancellationToken);
}
