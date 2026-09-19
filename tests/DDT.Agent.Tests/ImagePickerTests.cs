using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ImagePickerTests
{
    private static readonly AgentImageChoice s_pro = new(Guid.Parse("0193a4b2-0000-7000-8000-00000000a001"), "Windows 11 Pro", "Professional", "en-US", 5_000_000_000, 15_000_000_000);
    private static readonly AgentImageChoice s_education = new(Guid.Parse("0193a4b2-0000-7000-8000-00000000a002"), "Windows 11 Education", "Education", null, 5_000_000_000, 15_000_000_000);

    [Fact]
    public async Task PicksAnImageForTheOnlyDiskAfterErase()
    {
        (ImagePicker picker, ScriptedSignInPrompt prompt, _) = Create([FakeDeploymentTools.Disk(0)], nameRequired: false, "2", "ERASE");

        AgentPickRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(new AgentPickRequest(s_education.Id, 0, null), request);
        Assert.Equal(["Image number", "Type ERASE to continue"], prompt.Labels);
    }

    [Fact]
    public async Task AsksAgainForANumberThatIsNotOnTheList()
    {
        (ImagePicker picker, ScriptedSignInPrompt prompt, StringWriter console) = Create([FakeDeploymentTools.Disk(0)], nameRequired: false, "3", "one", " 1 ", "ERASE");

        AgentPickRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(s_pro.Id, request?.ImageId);
        Assert.Equal(["Image number", "Image number", "Image number", "Type ERASE to continue"], prompt.Labels);
        Assert.Equal(2, Lines(console).Count(line => line.EndsWith("Type a number from 1 to 2.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AsksForTheDiskWhenThereAreSeveral()
    {
        (ImagePicker picker, ScriptedSignInPrompt prompt, StringWriter console) = Create(
            [FakeDeploymentTools.Disk(0), FakeDeploymentTools.Disk(2, partitions: 3)],
            nameRequired: false,
            "1",
            "1",
            "2",
            "ERASE");

        AgentPickRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(new AgentPickRequest(s_pro.Id, 2, null), request);
        Assert.Equal(FakeDeploymentTools.Disk(2, partitions: 3), picker.ChosenDisk);
        Assert.Equal(["Image number", "Disk number", "Disk number", "Type ERASE to continue"], prompt.Labels);
        Assert.Contains(Lines(console), line => line.EndsWith("Type one of the disk numbers shown: 0, 2.", StringComparison.Ordinal));
        Assert.Contains(
            Lines(console),
            line => line.EndsWith("All data on disk 2 (Test disk 2, 256 GB, 3 partitions) will be erased and Windows 11 Pro installed.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AsksForAValidComputerNameWhenTheDomainNeedsOne()
    {
        (ImagePicker picker, ScriptedSignInPrompt prompt, StringWriter console) = Create([FakeDeploymentTools.Disk(0)], nameRequired: true, "1", "PC_01", "PC-01", "ERASE");

        AgentPickRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(new AgentPickRequest(s_pro.Id, 0, "PC-01"), request);
        Assert.Equal(["Image number", "Computer name", "Computer name", "Type ERASE to continue"], prompt.Labels);
        Assert.Contains(Lines(console), line => line.EndsWith("A computer name can hold only the letters A to Z, digits and hyphens.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("erase")]
    [InlineData("yes")]
    public async Task AnythingButEraseGoesBackToTheImageList(string typed)
    {
        (ImagePicker picker, ScriptedSignInPrompt prompt, StringWriter console) = Create([FakeDeploymentTools.Disk(0)], nameRequired: false, "1", typed, "2", "ERASE");

        AgentPickRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(s_education.Id, request?.ImageId);
        Assert.Equal(["Image number", "Type ERASE to continue", "Image number", "Type ERASE to continue"], prompt.Labels);
        Assert.Contains(Lines(console), line => line.EndsWith("Nothing was erased.", StringComparison.Ordinal));
        Assert.Equal(2, Lines(console).Count(line => line.EndsWith("Images this machine can install:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ARefusedChoiceStartsOverWithAFreshList()
    {
        (ImagePicker picker, _, _) = Create([FakeDeploymentTools.Disk(0)], nameRequired: false, "1", "ERASE");

        Assert.NotNull(await AnswerAllAsync(picker));

        picker.Refused("This machine cannot pick an image now.");

        Assert.False(picker.IsOffered);
    }

    [Fact]
    public void OffersNothingWithoutImagesOrDisks()
    {
        (ImagePicker picker, _, _) = Create([], nameRequired: false);

        Assert.False(picker.IsOffered);

        picker.Offer([], [FakeDeploymentTools.Disk(0)], nameRequired: false);

        Assert.False(picker.IsOffered);
    }

    private static (ImagePicker Picker, ScriptedSignInPrompt Prompt, StringWriter Console) Create(
        LocalDisk[] disks,
        bool nameRequired,
        params string[] typed)
    {
        StringWriter console = new();
        ScriptedSignInPrompt prompt = new(typed);
        ImagePicker picker = new(prompt, new AgentLog(new ImmediateTimeProvider(), console));
        picker.Offer([s_pro, s_education], disks, nameRequired);

        return (picker, prompt, console);
    }

    private static string[] Lines(StringWriter console) => console.ToString().Split(Environment.NewLine);

    // Reads and accepts typed lines until the picker has a request.
    private static async Task<AgentPickRequest?> AnswerAllAsync(ImagePicker picker)
    {
        while (await picker.ReadAsync(TestContext.Current.CancellationToken) is { } typed)
        {
            if (picker.Accept(typed) is { } request)
            {
                return request;
            }
        }

        return null;
    }
}
