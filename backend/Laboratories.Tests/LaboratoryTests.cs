using Platform.Api.Modules.Laboratories.Domain;

namespace Laboratories.Tests;

public class LaboratoryTests
{
    [Test]
    public void Create_GeneratesDistinctNonEmptyIds()
    {
        var first = new Laboratory("SQL injection");
        var second = new Laboratory("SQL injection");

        Assert.That(first.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(second.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WithMissingTitle_Throws(string? title)
    {
        Assert.Throws<ArgumentException>(() => new Laboratory(title!));
    }

    [Test]
    public void Create_WithOverlongTitle_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Laboratory(new string('a', Laboratory.MaxTitleLength + 1)));
    }

    [Test]
    public void Create_WithMaximumLengthTitle_Accepts()
    {
        string title = new string('a', Laboratory.MaxTitleLength);

        var lab = new Laboratory(title);

        Assert.That(lab.Title, Is.EqualTo(title));
    }

    [Test]
    public void Create_WithEmptyExistingId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Laboratory(Guid.Empty, "SQL injection"));
    }

    [Test]
    public void Rename_KeepsSameId()
    {
        Guid id = Guid.NewGuid();
        Laboratory lab = new Laboratory(id, "SQL injection");

        lab.Rename("SQL injection - napredno");

        Assert.That(lab.Id, Is.EqualTo(id));
        Assert.That(lab.Title, Is.EqualTo("SQL injection - napredno"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Rename_WithMissingTitle_ThrowsAndPreservesState(string? title)
    {
        var lab = new Laboratory("Original title");
        Guid originalId = lab.Id;

        Assert.Throws<ArgumentException>(() => lab.Rename(title!));

        Assert.That(lab.Id, Is.EqualTo(originalId));
        Assert.That(lab.Title, Is.EqualTo("Original title"));
    }

    [Test]
    public void Rename_WithOverlongTitle_ThrowsAndPreservesState()
    {
        var lab = new Laboratory("Original title");
        Guid originalId = lab.Id;

        Assert.Throws<ArgumentException>(() =>
            lab.Rename(new string('a', Laboratory.MaxTitleLength + 1)));

        Assert.That(lab.Id, Is.EqualTo(originalId));
        Assert.That(lab.Title, Is.EqualTo("Original title"));
    }

    [Test]
    public void Rename_WithMaximumLengthTitle_AcceptsAndKeepsId()
    {
        var lab = new Laboratory("Original title");
        Guid originalId = lab.Id;
        string title = new string('a', Laboratory.MaxTitleLength);

        lab.Rename(title);

        Assert.That(lab.Id, Is.EqualTo(originalId));
        Assert.That(lab.Title, Is.EqualTo(title));
    }

    [Test]
    public void CreateAndRename_TrimSurroundingWhitespace()
    {
        var lab = new Laboratory("  Original title  ");
        Assert.That(lab.Title, Is.EqualTo("Original title"));

        lab.Rename("  Updated title  ");

        Assert.That(lab.Title, Is.EqualTo("Updated title"));
    }
}
