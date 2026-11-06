using Platform.AssignmentOverview.Domain;

namespace Platform.AssignmentOverview.Contracts;

// ASN-02 TIM 16
// Ugovor koji koristi API endpoint GET /api/me/assignments (Lazar).
// studentId uvek dolazi iz sesije na serveru, nikad iz zahteva klijenta.
public interface IMyAssignmentsService
{
    ValueTask<IReadOnlyList<StudentAssignmentView>> GetForStudentAsync(
        string studentId,
        CancellationToken cancellationToken = default);
}