using Platform.Assignments.Domain.Enums;
using Platform.Assignments.Domain.Models;

namespace Platform.Tests.Assignments;

// KT1 TIM 15
[TestFixture]
public sealed class AssignmentTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new(2026, 11, 9, 23, 59, 59, TimeSpan.Zero);

    [Test]
    public void Create_WithValidValues_CreatesActiveAssignment()
    {
        var assignment = CreateAssignment();

        Assert.Multiple(() =>
        {
            Assert.That(assignment.Status, Is.EqualTo(AssignmentStatus.Active));
            Assert.That(assignment.MaxAttempts, Is.EqualTo(2));
            Assert.That(assignment.GetAvailability(Start), Is.EqualTo(AssignmentAvailability.Available));
        });
    }

    [Test]
    public void Create_WithZeroMaxAttempts_Throws()
    {
        Assert.That(
            () => new Assignment(
                Guid.NewGuid(),
                "course-oib",
                "revision-1",
                Start,
                Due,
                0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Create_WhenAvailabilityBeginsAfterDueDate_Throws()
    {
        Assert.That(
            () => new Assignment(
                Guid.NewGuid(),
                "course-oib",
                "revision-1",
                Due.AddMinutes(1),
                Due,
                2),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void GetAvailability_AfterDueDate_ReturnsExpired()
    {
        var assignment = CreateAssignment();

        var availability = assignment.GetAvailability(Due.AddSeconds(1));

        Assert.That(availability, Is.EqualTo(AssignmentAvailability.Expired));
    }

    [Test]
    public void GetAvailability_AfterDueDate_WhenLateStartIsAllowed_ReturnsAvailable()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            Start,
            Due,
            2,
            allowStartAfterDeadline: true);

        var availability = assignment.GetAvailability(Due.AddSeconds(1));

        Assert.That(availability, Is.EqualTo(AssignmentAvailability.Available));
    }

    private static Assignment CreateAssignment() => new(
        Guid.NewGuid(),
        "course-oib",
        "revision-1",
        Start,
        Due,
        maxAttempts: 2);
}
