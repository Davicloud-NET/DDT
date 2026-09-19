using System.Globalization;
using DDT.Contracts.Agents;
using DDT.Core.Unattend;

namespace DDT.Agent.Deployment;

// What the technician signed in at the machine has chosen so far: an image, a disk when there are several, a
// computer name when a domain needs one, and finally ERASE. Anything else at the last question goes back to
// the image list, so nothing is erased by a stray key.
public sealed class ImagePicker(ISignInPrompt prompt, AgentLog log)
{
    public const string ConfirmationWord = "ERASE";

    private IReadOnlyList<AgentImageChoice> _images = [];
    private IReadOnlyList<LocalDisk> _disks = [];
    private bool _nameRequired;
    private PickerQuestion _question = PickerQuestion.None;
    private AgentImageChoice? _image;
    private LocalDisk? _disk;
    private string? _computerName;

    public bool IsAvailable => prompt.IsAvailable;

    // True once there is something to choose from.
    public bool IsOffered => _question != PickerQuestion.None;

    // The disk chosen so far, which is the confirmed one once Accept returns a request.
    public LocalDisk? ChosenDisk => _disk;

    public void Offer(IReadOnlyList<AgentImageChoice> images, IReadOnlyList<LocalDisk> disks, bool nameRequired)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(disks);

        if (images.Count == 0 || disks.Count == 0)
        {
            Reset();

            return;
        }

        _images = images;
        _disks = disks;
        _nameRequired = nameRequired;
        StartOver();
    }

    public void Reset()
    {
        _images = [];
        _disks = [];
        _question = PickerQuestion.None;
        _image = null;
        _disk = null;
        _computerName = null;
    }

    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        switch (_question)
        {
            case PickerQuestion.Image:
                log.Information("Images this machine can install:");

                for (int number = 1; number <= _images.Count; number++)
                {
                    log.Information($"  {number}. {Describe(_images[number - 1])}");
                }

                return prompt.ReadLineAsync("Image number", secret: false, cancellationToken);
            case PickerQuestion.Disk:
                log.Information("Disks DDT can install on:");

                foreach (LocalDisk disk in _disks)
                {
                    log.Information($"  {disk.Describe()}");
                }

                return prompt.ReadLineAsync("Disk number", secret: false, cancellationToken);
            case PickerQuestion.ComputerName:
                log.Information("This machine joins the domain and needs a computer name.");

                return prompt.ReadLineAsync("Computer name", secret: false, cancellationToken);
            case PickerQuestion.Confirmation:
                log.Warning(
                    $"All data on disk {_disk!.Number} ({_disk.DisplayModel}, {ByteSize.Format(_disk.SizeBytes)}, " +
                    $"{LocalDisk.Partitions(_disk.PartitionCount)}) will be erased and {_image!.Name} installed.");

                return prompt.ReadLineAsync($"Type {ConfirmationWord} to continue", secret: false, cancellationToken);
            default:
                throw new InvalidOperationException("There is nothing to pick yet.");
        }
    }

    // The request to send once the technician has confirmed, otherwise null.
    public AgentPickRequest? Accept(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);

        string answer = typed.Trim();

        switch (_question)
        {
            case PickerQuestion.Image:
                if (!int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1 || number > _images.Count)
                {
                    log.Warning($"Type a number from 1 to {_images.Count}.");

                    return null;
                }

                _image = _images[number - 1];
                _disk = _disks.Count == 1 ? _disks[0] : null;
                _question = _disk is null ? PickerQuestion.Disk : NameOrConfirmation();

                return null;
            case PickerQuestion.Disk:
                LocalDisk? disk = int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out int diskNumber)
                    ? _disks.FirstOrDefault(candidate => candidate.Number == diskNumber)
                    : null;

                if (disk is null)
                {
                    log.Warning($"Type one of the disk numbers shown: {string.Join(", ", _disks.Select(candidate => candidate.Number))}.");

                    return null;
                }

                _disk = disk;
                _question = NameOrConfirmation();

                return null;
            case PickerQuestion.ComputerName:
                if (!ComputerNames.IsValid(answer, out string error))
                {
                    log.Warning(error);

                    return null;
                }

                _computerName = answer;
                _question = PickerQuestion.Confirmation;

                return null;
            case PickerQuestion.Confirmation:
                if (!string.Equals(answer, ConfirmationWord, StringComparison.Ordinal))
                {
                    log.Information("Nothing was erased.");
                    StartOver();

                    return null;
                }

                return new AgentPickRequest(_image!.Id, _disk!.Number, _computerName);
            default:
                return null;
        }
    }

    public void Picked(AgentDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        log.Information($"{deployment.ImageName} is going to be installed on disk {_disk?.Number}.");
        Reset();
    }

    // The server would not take the choice; what it says decides whether offering again makes sense, so the picker
    // starts from a fresh image list.
    public void Refused(string reason)
    {
        log.Warning($"The server did not accept the choice: {reason}");
        Reset();
    }

    // Nothing reached the server, or nothing came back: the confirmation is asked for again.
    public void NotSent(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        log.Warning($"Cannot send the choice to the server ({exception.Message}). Try again.");
    }

    private void StartOver()
    {
        _question = PickerQuestion.Image;
        _image = null;
        _disk = null;
        _computerName = null;
    }

    private PickerQuestion NameOrConfirmation() => _nameRequired ? PickerQuestion.ComputerName : PickerQuestion.Confirmation;

    private static string Describe(AgentImageChoice image)
    {
        IEnumerable<string> details = new[] { image.Edition, image.Language }.OfType<string>()
            .Append($"{ByteSize.Format(image.InstalledBytes)} installed");

        return $"{image.Name} ({string.Join(", ", details)})";
    }
}
