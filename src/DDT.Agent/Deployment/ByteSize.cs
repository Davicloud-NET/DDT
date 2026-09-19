using System.Globalization;

namespace DDT.Agent.Deployment;

// Sizes as diskpart and Explorer show them: binary units with the usual names, so a technician can compare.
public static class ByteSize
{
    private const double Kilobyte = 1024;
    private const double Megabyte = Kilobyte * 1024;
    private const double Gigabyte = Megabyte * 1024;
    private const double Terabyte = Gigabyte * 1024;

    public static string Format(long bytes) => bytes switch
    {
        >= (long)Terabyte => Scaled(bytes / Terabyte, "TB"),
        >= (long)Gigabyte => Scaled(bytes / Gigabyte, "GB"),
        >= (long)Megabyte => Scaled(bytes / Megabyte, "MB"),
        >= (long)Kilobyte => Scaled(bytes / Kilobyte, "KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };

    // One decimal below ten, none above, as in 4.2 GB and 128 GB.
    private static string Scaled(double value, string unit) =>
        value < 10
            ? string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {unit}")
            : string.Create(CultureInfo.InvariantCulture, $"{value:0} {unit}");
}
