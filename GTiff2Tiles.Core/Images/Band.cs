using NetVips;

namespace GTiff2Tiles.Core.Images;

/// <summary>
/// Represents image band metadata and helpers.
/// </summary>
public sealed class Band
{
    private static readonly double[] DefaultBand1 = [DefaultValue];
    private static readonly double[] DefaultBand2 = [DefaultValue, DefaultValue];
    private static readonly double[] DefaultBand3 = [DefaultValue, DefaultValue, DefaultValue];

    /// <summary>
    /// Default band value.
    /// </summary>
    public const int DefaultValue = 255;

    /// <summary>
    /// Current band value.
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// Creates a new <see cref="Band"/>.
    /// </summary>
    public Band(int value = DefaultValue)
    {
        if (value < 0 || value > 255)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    /// <summary>
    /// Appends explicit bands to an image.
    /// </summary>
    public static void AddBands(ref Image image, IEnumerable<Band> bands)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(bands);

        foreach (Band band in bands)
            image = image.Bandjoin(band.Value);
    }

    /// <summary>
    /// Appends default bands until the target count is reached.
    /// </summary>
    public static void AddDefaultBands(ref Image image, int bandsCount)
    {
        ArgumentNullException.ThrowIfNull(image);

        int missingBandsCount = bandsCount - image.Bands;

        if (missingBandsCount <= 0)
            return;

        if (missingBandsCount == 1 && (image.Bands == 1 || image.Bands == 3))
        {
            image = image.AddAlpha();
            return;
        }

        image = image.BandjoinConst(GetDefaultBandValues(missingBandsCount));
    }

    private static double[] GetDefaultBandValues(int missingBandsCount) => missingBandsCount switch
    {
        1 => DefaultBand1,
        2 => DefaultBand2,
        3 => DefaultBand3,
        _ => CreateDefaultBandValues(missingBandsCount)
    };

    private static double[] CreateDefaultBandValues(int missingBandsCount)
    {
        double[] values = new double[missingBandsCount];
        Array.Fill(values, DefaultValue);
        return values;
    }
}
