namespace DDT.Agent.Deployment;

// Turns a byte count into whole percents of total, passed on only when the number changes.
internal sealed class ByteProgress(IProgress<int> percent, long total)
{
    private int _last = -1;

    public void Report(long completed)
    {
        int current = total <= 0 ? 100 : (int)Math.Clamp(completed * 100 / total, 0, 100);

        if (current != _last)
        {
            _last = current;
            percent.Report(current);
        }
    }
}
