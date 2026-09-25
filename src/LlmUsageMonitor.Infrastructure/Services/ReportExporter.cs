using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;

namespace LlmUsageMonitor.Infrastructure.Services;

public sealed class ReportExporter : IReportExporter
{
    private readonly IUsageStore _usage;

    public ReportExporter(IUsageStore usage) => _usage = usage;

    public async Task ExportAsync(ReportFormat format, string path, UsageQuery query, CancellationToken ct = default)
    {
        var records = await _usage.GetRecentAsync(1_000_000, query, ct);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        switch (format)
        {
            case ReportFormat.Csv:
                await ExportCsvAsync(path, records);
                break;
            case ReportFormat.Excel:
                ExportExcel(path, records);
                break;
            case ReportFormat.Json:
                await ExportJsonAsync(path, records);
                break;
        }
    }

    private static async Task ExportCsvAsync(string path, IReadOnlyList<UsageRecord> records)
    {
        await using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
        await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteField("Timestamp");
        csv.WriteField("Provider");
        csv.WriteField("Model");
        csv.WriteField("CredentialId");
        csv.WriteField("GroupId");
        csv.WriteField("InputTokens");
        csv.WriteField("OutputTokens");
        csv.WriteField("CachedTokens");
        csv.WriteField("Requests");
        csv.WriteField("CostUsd");
        csv.WriteField("Source");
        await csv.NextRecordAsync();

        foreach (var r in records)
        {
            csv.WriteField(r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            csv.WriteField(ProviderCatalog.Get(r.Provider).DisplayName);
            csv.WriteField(r.Model);
            csv.WriteField(r.CredentialId?.ToString() ?? string.Empty);
            csv.WriteField(r.GroupId?.ToString() ?? string.Empty);
            csv.WriteField(r.InputTokens);
            csv.WriteField(r.OutputTokens);
            csv.WriteField(r.CachedTokens);
            csv.WriteField(r.Requests);
            csv.WriteField(r.CostUsd.ToString(CultureInfo.InvariantCulture));
            csv.WriteField(r.Source.ToString());
            await csv.NextRecordAsync();
        }
    }

    private static void ExportExcel(string path, IReadOnlyList<UsageRecord> records)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Usage");
        var headers = new[]
        {
            "Timestamp", "Provider", "Model", "CredentialId", "GroupId",
            "InputTokens", "OutputTokens", "CachedTokens", "Requests", "CostUsd", "Source"
        };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(1, i + 1).Style.Font.Bold = true;
        }

        var row = 2;
        foreach (var r in records)
        {
            sheet.Cell(row, 1).Value = r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            sheet.Cell(row, 2).Value = ProviderCatalog.Get(r.Provider).DisplayName;
            sheet.Cell(row, 3).Value = r.Model;
            sheet.Cell(row, 4).Value = r.CredentialId?.ToString() ?? string.Empty;
            sheet.Cell(row, 5).Value = r.GroupId?.ToString() ?? string.Empty;
            sheet.Cell(row, 6).Value = r.InputTokens;
            sheet.Cell(row, 7).Value = r.OutputTokens;
            sheet.Cell(row, 8).Value = r.CachedTokens;
            sheet.Cell(row, 9).Value = r.Requests;
            sheet.Cell(row, 10).Value = r.CostUsd;
            sheet.Cell(row, 11).Value = r.Source.ToString();
            row++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    private static async Task ExportJsonAsync(string path, IReadOnlyList<UsageRecord> records)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, records, options);
    }
}
