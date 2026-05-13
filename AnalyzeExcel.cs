using ClosedXML.Excel;
using System;
using System.Linq;

namespace ReconcileDocs.Tools
{
    internal static class AnalyzeExcelProgram
    {
        internal static void Main()
        {
        var filePath = @"C:\Users\user\Downloads\List of Accelist's Infrastrucutre Billing Updated.xlsx";
        
        try
        {
            using (var workbook = new XLWorkbook(filePath))
            {
                Console.WriteLine("=== SHEET NAMES ===");
                foreach (var ws in workbook.Worksheets)
                {
                    Console.WriteLine($"Sheet: {ws.Name}");
                }
                
                var listSheet = workbook.Worksheets.FirstOrDefault(ws => 
                    string.Equals(ws.Name?.Trim(), "List active biling", StringComparison.OrdinalIgnoreCase));
                
                if (listSheet != null)
                {
                    Console.WriteLine($"\n=== LIST ACTIVE BILING SHEET ===");
                    var usedRows = listSheet.RowsUsed().ToList();
                    Console.WriteLine($"Total rows: {usedRows.Count}");
                    
                    // Print first 5 rows
                    for (int i = 0; i < Math.Min(5, usedRows.Count); i++)
                    {
                        var row = usedRows[i];
                        var cells = row.CellsUsed().Select(c => c.GetString()).ToList();
                        Console.WriteLine($"\nRow {row.RowNumber()}: {string.Join(" | ", cells)}");
                    }
                }
                else
                {
                    Console.WriteLine("\nSheet 'List active biling' NOT FOUND");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}\n{ex.StackTrace}");
        }
        }
    }
}
