namespace Platform.AssignmentOverview.Contracts;

// ASN-02 TIM 16
// Vraca kurseve na kojima je student AKTIVNO upisan kao student.
// Ispisan ili suspendovan student se ne racuna.
// Modul ne cita tudju bazu clanstava (ACC-17); implementaciju daje
// modul za clanstva (tim 3) preko adaptera.
public interface IStudentCourseReader
{
    ValueTask<IReadOnlyCollection<string>> GetActiveCourseIdsAsync(
        string studentId,
        CancellationToken cancellationToken = default);
}