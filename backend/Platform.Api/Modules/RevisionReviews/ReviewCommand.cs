using System.Globalization;
using Platform.Laboratories.Domain.Rules;

namespace Platform.Api.Modules.RevisionReviews;

public sealed record ReviewBody(string? RowVersion, string? ContentHash, string? Comment);

public sealed record ReviewCommand(string Action, long Version, string Hash, string Key, string? Comment)
{
    public static ReviewCommand? Parse(string action, ReviewBody? body, string key)
    {
        if (action is not ("submit" or "approve" or "return") || body is null
            || !long.TryParse(body.RowVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || version < 1 || !RevisionContentHash.IsValid(body.ContentHash!)
            || key.Length is < 1 or > 128 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            return null;

        var comment = body.Comment?.Trim();
        if (action == "return" ? string.IsNullOrEmpty(comment) || comment.Length > 1000 : body.Comment is not null)
            return null;

        return new ReviewCommand(action, version, body.ContentHash!, key, comment);
    }
}
