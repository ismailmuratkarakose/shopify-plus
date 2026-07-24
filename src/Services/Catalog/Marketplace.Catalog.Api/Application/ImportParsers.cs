using System.Globalization;
using ClosedXML.Excel;

namespace Marketplace.Catalog.Api.Application;

/// <summary>İçeri aktarım dosyasından okunan ham satır (1-bazlı satır numarasıyla).</summary>
public record ImportRow(int RowNumber, Dictionary<string, string> Fields);

/// <summary>
/// CSV / XLSX satır okuyucu. Başlık satırı zorunludur; kolon adları büyük/küçük harf duyarsız.
/// CSV'de ayraç başlıktan koklanır (Türkçe Excel noktalı virgülle dışa aktarır) ve tırnaklı
/// alanlar desteklenir.
/// </summary>
public static class ImportParsers
{
    public static readonly string[] KnownColumns =
        ["barcode", "title", "price", "description", "brand", "category", "imageurl", "sku",
         "compareatprice", "stockquantity"];

    public static List<ImportRow> Parse(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".csv" or ".txt" => ParseCsv(stream),
            ".xlsx" => ParseXlsx(stream),
            _ => throw new InvalidDataException($"Desteklenmeyen dosya tipi: {ext}. CSV veya XLSX yükleyin.")
        };
    }

    // --- CSV ---

    private static List<ImportRow> ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var headerLine = reader.ReadLine()
            ?? throw new InvalidDataException("Dosya boş: başlık satırı bulunamadı.");

        // Ayraç koklama: Türkçe Excel ';' kullanır, standart CSV ','.
        var delimiter = headerLine.Count(c => c == ';') > headerLine.Count(c => c == ',') ? ';' : ',';
        var headers = SplitCsvLine(headerLine, delimiter).Select(NormalizeHeader).ToList();

        var rows = new List<ImportRow>();
        var rowNo = 1; // başlık = satır 1
        while (reader.ReadLine() is { } line)
        {
            rowNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = SplitCsvLine(line, delimiter);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count && i < values.Count; i++)
                fields[headers[i]] = values[i].Trim();
            rows.Add(new ImportRow(rowNo, fields));
        }
        return rows;
    }

    private static List<string> SplitCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == delimiter) { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }

    // --- XLSX ---

    private static List<ImportRow> ParseXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("Çalışma sayfası bulunamadı.");

        var headerRow = sheet.Row(1);
        var headers = new Dictionary<int, string>();
        foreach (var cell in headerRow.CellsUsed())
            headers[cell.Address.ColumnNumber] = NormalizeHeader(cell.GetString());
        if (headers.Count == 0)
            throw new InvalidDataException("Başlık satırı (1. satır) boş.");

        var rows = new List<ImportRow>();
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (col, name) in headers)
            {
                var cell = row.Cell(col);
                fields[name] = cell.IsEmpty() ? "" : cell.GetString().Trim();
            }
            if (fields.Values.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(new ImportRow(row.RowNumber(), fields));
        }
        return rows;
    }

    private static string NormalizeHeader(string h) =>
        h.Trim().ToLowerInvariant()
            .Replace(" ", "").Replace("_", "").Replace("-", "")
            // Türkçe başlıklara tolerans
            .Replace("barkod", "barcode").Replace("ürünadı", "title").Replace("urunadi", "title")
            .Replace("fiyat", "price").Replace("açıklama", "description").Replace("aciklama", "description")
            .Replace("marka", "brand").Replace("kategori", "category").Replace("görsel", "imageurl")
            .Replace("gorsel", "imageurl").Replace("stok", "stockquantity");

    /// <summary>Ondalık ayracı hem nokta hem virgül kabul eder (1.349,90 / 1349.90 / 1349,90).</summary>
    public static bool TryParseDecimal(string? value, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var s = value.Trim().Replace(" ", "");
        // "1.349,90" (TR binlik+ondalık) → "1349.90"
        if (s.Contains(',') && s.Contains('.'))
            s = s.Replace(".", "").Replace(',', '.');
        else if (s.Contains(','))
            s = s.Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }
}
