using System.Text;
using System.Xml;
using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack.Ksa;

// Generates the KSA Mudad WPS XML file.
// Format: Mudad XML v1 — simplified structural representation.
// VERIFY: Obtain the current Mudad WPS integration specification from
// the Ministry of Human Resources before connecting to the live portal.

public sealed class KsaWageProtectionExporter : IWageProtectionExporter
{
    public Task<WageProtectionExportResult> ExportAsync(
        WageProtectionExportInput input, CancellationToken ct = default)
    {
        var xml = BuildMudadXml(input);
        var bytes = Encoding.UTF8.GetBytes(xml);
        var fileName = $"mudad_wps_{input.EstablishmentId}_{input.PeriodYear}{input.PeriodMonth:D2}.xml";
        return Task.FromResult(new WageProtectionExportResult(bytes, fileName, "mudad-xml", input.Employees.Count));
    }

    private static string BuildMudadXml(WageProtectionExportInput input)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8, OmitXmlDeclaration = false };

        using var writer = XmlWriter.Create(sb, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("MudadWPS");
        writer.WriteAttributeString("Version", "1.0");

        writer.WriteStartElement("Header");
        writer.WriteElementString("EmployerID",     input.EstablishmentId);
        writer.WriteElementString("EmployerName",   input.CompanyNameEn);
        writer.WriteElementString("EmployerIBAN",   input.EmployerIban);
        writer.WriteElementString("Period",         $"{input.PeriodYear}-{input.PeriodMonth:D2}");
        writer.WriteElementString("RecordCount",    input.Employees.Count.ToString());
        writer.WriteElementString("TotalNetPay",    input.Employees.Sum(e => e.NetPay).ToString("F2"));
        writer.WriteEndElement(); // Header

        writer.WriteStartElement("Employees");
        foreach (var emp in input.Employees)
        {
            writer.WriteStartElement("Employee");
            writer.WriteElementString("EmpCode",      emp.EmployeeCode);
            writer.WriteElementString("FullNameEn",   emp.FullNameEn);
            writer.WriteElementString("NationalID",   emp.NationalId);
            writer.WriteElementString("Nationality",  emp.Nationality);
            writer.WriteElementString("IBAN",         emp.IbanOrAccount);
            writer.WriteElementString("BankCode",     emp.BankCode);
            writer.WriteElementString("BasicSalary",  emp.Salary.Basic.ToString("F2"));
            writer.WriteElementString("Housing",      emp.Salary.HousingAllowance.ToString("F2"));
            writer.WriteElementString("Transport",    emp.Salary.TransportAllowance.ToString("F2"));
            writer.WriteElementString("OtherAllow",   emp.Salary.OtherAllowances.ToString("F2"));
            writer.WriteElementString("GrossSalary",  emp.Salary.Gross.ToString("F2"));
            writer.WriteElementString("NetPay",       emp.NetPay.ToString("F2"));
            writer.WriteEndElement(); // Employee
        }
        writer.WriteEndElement(); // Employees

        writer.WriteEndElement(); // MudadWPS
        writer.WriteEndDocument();
        writer.Flush();
        return sb.ToString();
    }
}
