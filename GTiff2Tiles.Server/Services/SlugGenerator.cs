using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GTiff2Tiles.Server.Services;

public sealed partial class SlugGenerator
{
    private static readonly Regex InvalidCharsRegex = InvalidChars();
    private static readonly Regex MultiDashRegex = MultiDash();

    public string GenerateSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "catalog";

        string normalized = value.Trim().Normalize(NormalizationForm.FormD);
        StringBuilder builder = new();

        foreach (char character in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            char lower = char.ToLowerInvariant(character);
            builder.Append(char.IsLetterOrDigit(lower) ? lower : '-');
        }

        string slug = InvalidCharsRegex.Replace(builder.ToString(), "-");
        slug = MultiDashRegex.Replace(slug, "-").Trim('-');

        return string.IsNullOrWhiteSpace(slug) ? "catalog" : slug;
    }

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex InvalidChars();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();
}
