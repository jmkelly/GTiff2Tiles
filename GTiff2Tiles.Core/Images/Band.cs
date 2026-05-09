using NetVips;

namespace GTiff2Tiles.Core.Images;

/// <summary>
/// Represents image band metadata and helpers.
/// </summary>
public class Band
{
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
        {
            image = image.Bandjoin(band.Value);
        }
    }

    /// <summary>
    /// Appends default bands until the target count is reached.
    /// </summary>
    public static void AddDefaultBands(ref Image image, int bandsCount)
    {
        ArgumentNullException.ThrowIfNull(image);

        while (image.Bands < bandsCount)
        {
            image = image.Bandjoin(DefaultValue);
        }
    }
}
