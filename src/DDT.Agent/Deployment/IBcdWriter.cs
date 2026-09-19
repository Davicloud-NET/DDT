namespace DDT.Agent.Deployment;

public interface IBcdWriter
{
    // Makes the applied Windows bootable and registers its recovery environment.
    Task WriteAsync(TargetVolumes volumes, CancellationToken cancellationToken);

    // Makes the firmware start Windows Boot Manager on the new system partition first. The last change to the
    // machine, after the answer file: until then a restart still starts it from the network. A failure is only a
    // warning, because Windows is installed either way.
    Task PutWindowsFirstAsync(TargetVolumes volumes, CancellationToken cancellationToken);

    // Puts back the firmware boot entries and order that PutWindowsFirstAsync changed in this run, for a run that
    // ends without finishing. Never throws: a failure is only a warning.
    Task RestoreBootOrderAsync(CancellationToken cancellationToken);
}
