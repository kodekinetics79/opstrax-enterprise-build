using System.Text;
using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack.Uae;

// UAE Wages Protection System — MOHRE Standard Input File (SIF) format.
// Format: pipe-delimited, one header row + one data row per employee.
// VERIFY: Obtain current MOHRE WPS SIF specification and field lengths
// from the Central Bank of UAE / MOHRE before connecting to the live portal.

public sealed class UaeWageProtectionExporter : IWageProtectionExporter
{
    public Task<WageProtectionExportResult> ExportAsync(
        WageProtectionExportInput input, CancellationToken ct = default)
    {
        var lines = new List<string>();

        // Header: H|format|version|establishment|year|month|currency|count
        lines.Add($"H|MOHRESIF|1.0|{input.EstablishmentId}|{input.PeriodYear}|{input.PeriodMonth:D2}|AED|{input.Employees.Count}");

        // Employer line
        lines.Add($"E|{input.CompanyNameEn}|{input.EstablishmentId}|{input.EmployerIban}");

        // Data rows: D|emp_code|national_id|nationality|iban|basic|gross|net_pay
        foreach (var emp in input.Employees)
        {
            lines.Add(
                $"D|{emp.EmployeeCode}|{emp.NationalId}|{emp.Nationality}|" +
                $"{emp.IbanOrAccount}|{emp.Salary.Basic:F2}|{emp.Salary.Gross:F2}|{emp.NetPay:F2}");
        }

        // Trailer: T|record_count|total_net_pay
        lines.Add($"T|{input.Employees.Count}|{input.Employees.Sum(e => e.NetPay):F2}");

        var content = string.Join("\n", lines);
        var bytes = Encoding.UTF8.GetBytes(content);
        var fileName = $"mohre_wps_{input.EstablishmentId}_{input.PeriodYear}{input.PeriodMonth:D2}.sif";
        return Task.FromResult(new WageProtectionExportResult(bytes, fileName, "mohre-sif", input.Employees.Count));
    }
}
