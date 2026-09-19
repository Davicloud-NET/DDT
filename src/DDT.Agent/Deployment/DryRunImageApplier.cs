using DDT.Core.Wim;

namespace DDT.Agent.Deployment;

// Checks that the downloaded file is a WIM holding the image, without wimlib: a normal Windows session lacks the
// privileges wimlib's strict mode needs.
public sealed class DryRunImageApplier(AgentLog log) : IImageApplier
{
    public void Prepare()
    {
    }

    public async Task ApplyAsync(string wimPath, int index, string targetRoot, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(percent);

        percent.Report(0);

        IReadOnlyList<WimImageInfo> images;
        FileStream wim = new(wimPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        await using (wim.ConfigureAwait(false))
        {
            try
            {
                images = await WimMetadata.ReadAsync(wim, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidWimException exception)
            {
                throw new DeploymentStepException(exception.Message, exception);
            }
        }

        WimImageInfo image = images.FirstOrDefault(candidate => candidate.Index == index)
            ?? throw new DeploymentStepException($"The downloaded file holds no image {index}. Upload the image again.");

        log.Information($"Dry run: wimlib is not run. It would apply image {index} ({image.Name}, {image.EditionId}, {image.Architecture}) to {targetRoot}.");

        await File.WriteAllTextAsync(
            Path.Combine(targetRoot, "ddt-dry-run-applied.txt"),
            $"Image {index} of {wimPath}: {image.Name}",
            cancellationToken).ConfigureAwait(false);

        percent.Report(100);
    }
}
