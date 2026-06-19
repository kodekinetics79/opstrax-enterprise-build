using Zayra.Api.Models;

namespace Zayra.Api.Application.Employees;

/// <summary>
/// Single source of truth for employee sensitive-field masking.
/// Any role that does not satisfy CanViewSensitive() must have these fields cleared
/// before the employee object leaves the application boundary.
/// </summary>
public static class EmployeeSensitiveMask
{
    public static void Apply(Employee employee)
    {
        employee.Salary = null;
        employee.BankName = string.Empty;
        employee.BankIban = string.Empty;
        employee.WpsBankDetails = string.Empty;
        employee.PassportNumber = string.Empty;
        employee.IqamaNumber = string.Empty;
        employee.MedicalInformation = string.Empty;
        employee.DisciplinaryRecords = string.Empty;
        employee.TerminationReason = string.Empty;
    }
}
