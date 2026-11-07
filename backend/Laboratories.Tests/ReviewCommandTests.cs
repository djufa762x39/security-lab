using Platform.Api.Modules.RevisionReviews;

namespace Laboratories.Tests;

public sealed class ReviewCommandTests
{
    private const string Hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [TestCase("return", " ")]
    [TestCase("approve", "unexpected")]
    [TestCase("archive", null)]
    public void Parse_RejectsInvalidActionOrComment(string action, string? comment) =>
        Assert.That(ReviewCommand.Parse(action, new ReviewBody("2", Hash, comment), "request-1"), Is.Null);

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("9223372036854775808")]
    public void Parse_RejectsInvalidVersion(string version) =>
        Assert.That(ReviewCommand.Parse("submit", new ReviewBody(version, Hash, null), "request-1"), Is.Null);

    [Test]
    public void Parse_NormalizesReturnCommentWithoutLosingVersionPrecision()
    {
        var command = ReviewCommand.Parse("return", new ReviewBody("9007199254740993", Hash, "  Popravi šemu  "), "request-1");

        Assert.That(command, Is.EqualTo(new ReviewCommand("return", 9007199254740993L, Hash, "request-1", "Popravi šemu")));
    }
}
