using System;

namespace Jellyfin.Plugin.ContentRatings.Sources;

/// <summary>
/// Thrown when a source rejects the configured credentials. Aborts the run, as every
/// later request would fail the same way.
/// </summary>
public class RatingSourceAuthException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RatingSourceAuthException"/> class.
    /// </summary>
    public RatingSourceAuthException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RatingSourceAuthException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    public RatingSourceAuthException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RatingSourceAuthException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public RatingSourceAuthException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
