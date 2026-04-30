using System.Text.RegularExpressions;

namespace ReconcileDocs.Application.Services;

public sealed class StatementPeriodExtractor
{
    /// <summary>
    /// Tries to extract a statement period (month/year) from file content.
    /// Returns the first day of the extracted month, or null if unable to detect.
    /// </summary>
    public static DateTime? ExtractPeriod(byte[] fileContent, string contentType)
    {
        // For PDFs, try to extract text and look for date patterns
        if (contentType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Try to find patterns like "Jan 2024", "January 2024", "2024-01", etc.
            var patterns = new[]
            {
                @"(?i)(Jan|February|March|April|May|June|July|August|Sept|October|Nov|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+(\d{4})",
                @"(\d{4})-(\d{2})",
                @"(\d{1,2})/(\d{4})",
                @"Statement\s+for\s+(?i)(Jan|February|March|April|May|June|July|August|Sept|October|Nov|December)\s+(\d{4})"
            };

            try
            {
                var text = System.Text.Encoding.UTF8.GetString(fileContent);
                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(text, pattern);
                    if (match.Success)
                    {
                        return TryParseDateFromMatch(match);
                    }
                }
            }
            catch { }
        }

        return null;
    }

    private static DateTime? TryParseDateFromMatch(Match match)
    {
        try
        {
            // Try MM-YYYY pattern
            if (match.Groups.Count >= 3 && int.TryParse(match.Groups[2].Value, out var year))
            {
                if (match.Groups[1].Value.Length <= 2 && int.TryParse(match.Groups[1].Value, out var month))
                {
                    if (month >= 1 && month <= 12)
                        return new DateTime(year, month, 1);
                }
            }

            // Try month name pattern
            var monthName = match.Groups[1].Value;
            if (int.TryParse(match.Groups[2].Value, out var yearFromName))
            {
                var month = MonthNameToNumber(monthName);
                if (month > 0)
                    return new DateTime(yearFromName, month, 1);
            }
        }
        catch { }

        return null;
    }

    private static int MonthNameToNumber(string monthName)
    {
        return monthName.ToLower() switch
        {
            "january" or "jan" => 1,
            "february" or "feb" => 2,
            "march" or "mar" => 3,
            "april" or "apr" => 4,
            "may" => 5,
            "june" or "jun" => 6,
            "july" or "jul" => 7,
            "august" or "aug" => 8,
            "september" or "sept" or "sep" => 9,
            "october" or "oct" => 10,
            "november" or "nov" => 11,
            "december" or "dec" => 12,
            _ => 0
        };
    }
}
