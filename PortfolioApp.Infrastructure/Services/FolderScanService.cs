using PortfolioApp.Core.Enums;
using PortfolioApp.Core.Interfaces;
using PortfolioApp.Infrastructure.Providers.Bitstack;
using PortfolioApp.Infrastructure.Providers.Boursobank;

namespace PortfolioApp.Infrastructure.Services;

public class FolderScanService
{
    private readonly ImportService _importService;

    public FolderScanService(ImportService importService)
    {
        _importService = importService;
    }

    /// <summary>
    /// Scanne un dossier racine et importe tous les fichiers reconnus.
    /// Route automatiquement selon le sous-dossier source (BITSTACK, BOURSOBANK).
    /// </summary>
    public async Task<ScanResult> ScanAndImportAsync(string rootPath, CancellationToken ct = default)
    {
        var result = new ScanResult { RootPath = rootPath };

        if (!Directory.Exists(rootPath))
        {
            result.Errors.Add($"Dossier introuvable : {rootPath}");
            return result;
        }

        // Routing par sous-dossier source
        var routes = new Dictionary<string, IPositionProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["BITSTACK"] = new BitstackCsvProvider(),
            ["BOURSOBANK"] = new BoursobankPdfProvider()
        };

        foreach (var sourceDir in Directory.GetDirectories(rootPath))
        {
            var sourceName = Path.GetFileName(sourceDir);

            if (!routes.TryGetValue(sourceName.ToUpper(), out var provider))
            {
                var hasFiles = Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories).Any();
                if (hasFiles)
                {
                    result.IgnoredFolders.Add(sourceName);
                }
                continue;
            }

            // Récupère récursivement les fichiers attendus selon la source
            var pattern = sourceName.Equals("BITSTACK", StringComparison.OrdinalIgnoreCase)
                ? "*.csv"
                : "*.pdf";
            var files = Directory.GetFiles(sourceDir, pattern, SearchOption.AllDirectories);

            foreach (var file in files.OrderBy(f => f))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var summary = await _importService.ImportAsync(provider, file, ct);
                    // Marquer si aucune transaction (ni nouvelle ni doublon) n'a été extraite
                    var wasParsed = summary.Imported > 0 || summary.Duplicates > 0;
                    result.FileResults.Add(new FileImportResult
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        Source = sourceName,
                        Imported = summary.Imported,
                        Duplicates = summary.Duplicates,
                        WarningCount = summary.Warnings.Count,
                        ErrorCount = summary.Errors.Count,
                        WasParsed = wasParsed
                    });
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Erreur sur {Path.GetFileName(file)} : {ex.Message}");
                }
            }
        }

        return result;
    }
}

public class ScanResult
{
    public string RootPath { get; set; } = string.Empty;
    public List<FileImportResult> FileResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public int TotalImported => FileResults.Sum(f => f.Imported);
    public int TotalDuplicates => FileResults.Sum(f => f.Duplicates);
    public int FilesProcessed => FileResults.Count;
    public int FilesWithNewData => FileResults.Count(f => f.Imported > 0);
    public List<string> IgnoredFolders { get; set; } = new();
}

public class FileImportResult
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public bool WasParsed { get; set; }
}