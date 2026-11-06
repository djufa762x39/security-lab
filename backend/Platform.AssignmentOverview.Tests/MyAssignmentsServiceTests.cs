using Platform.AssignmentOverview.Application;
using Platform.AssignmentOverview.Tests.Support;
using Platform.Assignments.Domain.Models;

namespace Platform.AssignmentOverview.Tests;

// ASN-02 TIM 16
// Kriterijum: "Student vidi samo aktivne dodele svojih kurseva."
[TestFixture]
public sealed class MyAssignmentsServiceTests
{
    private FakeStudentCourseReader _courses = null!;
    private FakeAssignmentRepository _assignments = null!;
    private MyAssignmentsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _courses = new FakeStudentCourseReader();
        _assignments = new FakeAssignmentRepository();
        _service = new MyAssignmentsService(_courses, _assignments);
    }

    // Pozitivan: student vidi dodele sa svojih kurseva.
    [Test]
    public async Task Student_SeesAssignmentsFromOwnCourses()
    {
        var oib = NewAssignment("oib");
        var mreze = NewAssignment("mreze");
        _courses.Enroll("ana", "oib", "mreze");
        _assignments.Add(oib, mreze);

        var result = await _service.GetForStudentAsync("ana");

        Assert.That(
            result.Select(v => v.AssignmentId),
            Is.EquivalentTo(new[] { oib.Id, mreze.Id }));
    }

    // Negativan: dodela sa kursa na koji student nije upisan se ne vidi.
    [Test]
    public async Task Student_DoesNotSeeAssignmentFromOtherCourse()
    {
        _courses.Enroll("ana", "oib");
        _assignments.Add(NewAssignment("baze"));

        var result = await _service.GetForStudentAsync("ana");

        Assert.That(result, Is.Empty);
    }

    // Negativan: cak i ako tudji modul greskom vrati tudju dodelu, servis je izbacuje.
    [Test]
    public async Task ForeignAssignment_IsFilteredEvenIfRepositoryReturnsIt()
    {
        var own = NewAssignment("oib");
        _courses.Enroll("ana", "oib");
        _assignments.Add(own, NewAssignment("baze"));
        _assignments.ReturnAllCourses = true;

        var result = await _service.GetForStudentAsync("ana");

        Assert.That(
            result.Select(v => v.AssignmentId),
            Is.EqualTo(new[] { own.Id }));
    }

    // Negativan: prazan ID studenta se odbija.
    [Test]
    public void EmptyStudentId_IsRejected()
    {
        Assert.That(
            async () => await _service.GetForStudentAsync(""),
            Throws.TypeOf<ArgumentException>());
    }

    private static Assignment NewAssignment(string courseId)
        => new(
            Guid.NewGuid(),
            courseId,
            "rev-1",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(7),
            3);
}