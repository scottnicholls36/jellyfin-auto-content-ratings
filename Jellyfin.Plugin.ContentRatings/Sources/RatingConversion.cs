using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ContentRatings.Sources;

/// <summary>
/// Turns US ratings into another country's using a user-editable table. The results are
/// equivalents, not official classifications.
/// </summary>
public static class RatingConversion
{
    /// <summary>
    /// The country whose ratings are converted.
    /// </summary>
    public const string FromCountry = "US";

    /// <summary>
    /// The default table, US to UK (BBFC). Where there is no clean match it leans cautious.
    /// </summary>
    public const string DefaultUsToGb =
        "G = U\n" +
        "TV-Y = U\n" +
        "TV-G = U\n" +
        "PG = PG\n" +
        "TV-Y7 = PG\n" +
        "TV-Y7-FV = PG\n" +
        "TV-PG = PG\n" +
        "PG-13 = 12A\n" +
        "TV-14 = 15\n" +
        "R = 15\n" +
        "TV-MA = 18\n" +
        "NC-17 = 18";

    /// <summary>
    /// Parses a table of <c>US = converted</c> lines. Blank lines, lines starting with <c>#</c>
    /// and lines without an <c>=</c> are ignored.
    /// </summary>
    /// <param name="table">The table text.</param>
    /// <returns>US rating to converted rating, case-insensitive.</returns>
    public static IReadOnlyDictionary<string, string> Parse(string? table)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in (table ?? string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();
            var split = line.IndexOf('=', StringComparison.Ordinal);
            if (line.Length == 0 || line.StartsWith('#') || split <= 0)
            {
                continue;
            }

            var from = line[..split].Trim();
            var to = line[(split + 1)..].Trim();
            if (from.Length > 0 && to.Length > 0)
            {
                map[from] = to;
            }
        }

        return map;
    }
}
