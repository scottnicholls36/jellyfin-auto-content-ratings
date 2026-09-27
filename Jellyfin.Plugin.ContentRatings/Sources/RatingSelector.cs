using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.ContentRatings.Sources;

/// <summary>
/// Picks the right certification out of a source's response. Kept free of I/O so it can be unit tested.
/// </summary>
internal static class RatingSelector
{
    // Theatrical first, as that is the certification people recognise; TV and premiere releases last.
    private static readonly int[] _tmdbReleaseTypePreference = [3, 2, 4, 5, 6, 1];

    // A fixed table rather than RegionInfo, which is unreliable when the server runs in invariant globalisation mode
    // (as some container images do). Covers every country Jellyfin ships a rating system for, plus a few more.
    private static readonly Dictionary<string, string> _alpha2ToAlpha3 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AR"] = "arg", ["AT"] = "aut", ["AU"] = "aus", ["BE"] = "bel", ["BG"] = "bgr", ["BR"] = "bra",
        ["CA"] = "can", ["CH"] = "che", ["CL"] = "chl", ["CN"] = "chn", ["CO"] = "col", ["CZ"] = "cze",
        ["DE"] = "deu", ["DK"] = "dnk", ["EE"] = "est", ["ES"] = "esp", ["FI"] = "fin", ["FR"] = "fra",
        ["GB"] = "gbr", ["GR"] = "grc", ["HK"] = "hkg", ["HU"] = "hun", ["ID"] = "idn", ["IE"] = "irl",
        ["IL"] = "isr", ["IN"] = "ind", ["IS"] = "isl", ["IT"] = "ita", ["JP"] = "jpn", ["KR"] = "kor",
        ["KZ"] = "kaz", ["LT"] = "ltu", ["LV"] = "lva", ["MX"] = "mex", ["MY"] = "mys", ["NL"] = "nld",
        ["NO"] = "nor", ["NZ"] = "nzl", ["PH"] = "phl", ["PL"] = "pol", ["PT"] = "prt", ["RO"] = "rou",
        ["RU"] = "rus", ["SE"] = "swe", ["SG"] = "sgp", ["SK"] = "svk", ["TH"] = "tha", ["TR"] = "tur",
        ["TW"] = "twn", ["UA"] = "ukr", ["US"] = "usa", ["UY"] = "ury", ["VN"] = "vnm", ["ZA"] = "zaf",
    };

    public static SourceRating? SelectTmdbMovie(TmdbReleaseDatesResponse? response, IReadOnlyList<string> countryCodes)
    {
        if (response is null)
        {
            return null;
        }

        foreach (var country in countryCodes)
        {
            var releases = response.Results
                .Where(r => string.Equals(r.CountryCode, country, StringComparison.OrdinalIgnoreCase))
                .SelectMany(r => r.ReleaseDates)
                .Where(d => !string.IsNullOrWhiteSpace(d.Certification))
                .ToList();

            if (releases.Count == 0)
            {
                continue;
            }

            var best = releases
                .OrderBy(d => ReleaseTypeRank(d.Type))
                .First();

            return new SourceRating(best.Certification!.Trim(), country);
        }

        return null;
    }

    public static SourceRating? SelectTmdbTv(TmdbContentRatingsResponse? response, IReadOnlyList<string> countryCodes)
    {
        if (response is null)
        {
            return null;
        }

        foreach (var country in countryCodes)
        {
            var match = response.Results.FirstOrDefault(r =>
                string.Equals(r.CountryCode, country, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(r.Rating));

            if (match is not null)
            {
                return new SourceRating(match.Rating!.Trim(), country);
            }
        }

        return null;
    }

    public static SourceRating? SelectTvdb(TvdbExtendedRecord? record, IReadOnlyList<string> countryCodes)
    {
        if (record?.ContentRatings is null)
        {
            return null;
        }

        foreach (var country in countryCodes)
        {
            var alpha3 = ToAlpha3(country);
            if (alpha3 is null)
            {
                continue;
            }

            var match = record.ContentRatings.FirstOrDefault(r =>
                string.Equals(r.Country, alpha3, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(r.Name));

            if (match is not null)
            {
                return new SourceRating(match.Name!.Trim(), country);
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the value stored on the item. With a prefix this matches Jellyfin's own TMDB provider
    /// (<c>GB-15</c>, <c>FSK-16</c>, and US ratings left bare).
    /// </summary>
    public static string Format(SourceRating rating, bool includeCountryPrefix)
    {
        if (!includeCountryPrefix || string.Equals(rating.CountryCode, "US", StringComparison.OrdinalIgnoreCase))
        {
            return rating.Rating;
        }

        var value = rating.CountryCode.ToUpperInvariant() + "-" + rating.Rating;
        return value.StartsWith("DE-", StringComparison.OrdinalIgnoreCase) ? "FSK-" + value[3..] : value;
    }

    /// <summary>
    /// Converts ISO 3166-1 alpha-2 to the lower-case alpha-3 codes TVDB uses.
    /// </summary>
    public static string? ToAlpha3(string alpha2)
        => _alpha2ToAlpha3.TryGetValue(alpha2.Trim(), out var alpha3) ? alpha3 : null;

    private static int ReleaseTypeRank(int type)
    {
        var index = Array.IndexOf(_tmdbReleaseTypePreference, type);
        return index < 0 ? int.MaxValue : index;
    }
}
