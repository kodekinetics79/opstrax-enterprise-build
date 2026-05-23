namespace Zayra.Api.Models;
public class AttendanceRecord
{
    public int Id { get; set; }
    public Guid? TenantId { get; set; }
    public int EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public TimeOnly? TimeIn { get; set; }
    public TimeOnly? TimeOut { get; set; }
    public decimal OvertimeHours { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string Status { get; set; } = "Present";
}
