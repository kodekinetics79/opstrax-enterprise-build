using System.Text;
using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack.Qatar;

// Qatar WPS — Qatar Central Bank (QCB) Standard Input File (SIF) format.
// Format: pipe-delimited; structure mirrors the QCB WPS SIF specification.
// VERIFY: Obtain the current QCB WPS SIF specification from the
// Qatar Central Bank before connecting to the live portal.

public sealed class QatarWageProtectionExporter : IWageProtectionExporter
{
    public Task<WageProtectionExportResult> ExportAsync(
        WageProtectionExportInput input, CancellationToken ct = default)
    {
        var lines = new List<string>();

        // Header: H|format|version|employer_cr|year|month|currency|count
        lines.Add($"H|QCBSIF|1.0|{input.EstablishmentId}|{input.PeriodYear}|{input.PeriodMonth:D2}|QAR|{input.Employees.Count}");

        // Employer line
        lines.Add($"E|{input.CompanyNameEn}|{input.CompanyNameAr}|{input.EstablishmentId}|{input.EmployerIban}");

        // Data rows: D|emp_code|national_id|nationality|iban|bank_code|basic|gross|net_pay
        foreach (var emp in input.Employees)
        {
            lines.Add(
                $"D|{emp.EmployeeCode}|{emp.NationalId}|{emp.Nationality}|" +
                $"{emp.IbanOrAccount}|{emp.BankCode}|" +
                $"{emp.Salary.Basic:F2}|{emp.Salary.Gross:F2}|{emp.NetPay:F2}");
        }

        // Trailer: T|count|total_net
        lines.Add($"T|{input.Employees.Count}|{input.Employees.Sum(e => e.NetPay):F2}");

        var content = string.Join("\n", lines);
        var bytes = Encoding.UTF8.GetBytes(content);
        var fileName = $"qcb_wps_{input.EstablishmentId}_{input.PeriodYear}{input.PeriodMonth:D2}.sif";
        return Task.FromResult(new WageProtectionExportResult(bytes, fileName, "qcb-sif", input.Employees.Count));
    }
}
