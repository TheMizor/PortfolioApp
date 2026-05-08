using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using PortfolioApp.Infrastructure.Data;
using PortfolioApp.Infrastructure.Providers.Bitstack;
using PortfolioApp.Infrastructure.Providers.Boursobank;
using PortfolioApp.Infrastructure.Services;
using System.IO;
using System.Windows;

namespace PortfolioApp.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BtnImportBitstack_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Fichiers CSV (*.csv)|*.csv",
            Title = "Sélectionner un CSV Bitstack"
        };

        if (dialog.ShowDialog() != true)
            return;

        BtnImportBitstack.IsEnabled = false;
        Log($"=== Import démarré : {dialog.FileName} ===");

        try
        {
            // Configuration du DbContext
            var options = new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlite(DatabaseConfig.ConnectionString)
                .Options;

            using var db = new PortfolioDbContext(options);
            await db.Database.MigrateAsync();   // applique les migrations si pas déjà fait

            // Service d'import
            var provider = new BitstackCsvProvider();
            var importService = new ImportService(db);

            var summary = await importService.ImportAsync(provider, dialog.FileName);

            // Affichage du résultat
            Log($"Fichier : {summary.SourceFile}");
            Log($"Importées : {summary.Imported}");
            Log($"Doublons ignorés : {summary.Duplicates}");

            if (summary.Warnings.Any())
            {
                Log($"\nAvertissements ({summary.Warnings.Count}) :");
                foreach (var w in summary.Warnings)
                    Log($"  - {w}");
            }

            if (summary.Errors.Any())
            {
                Log($"\nErreurs ({summary.Errors.Count}) :");
                foreach (var err in summary.Errors)
                    Log($"  - {err}");
            }

            // Petit récap des données en base
            var totalTx = await db.Transactions.CountAsync();
            var totalAccounts = await db.Accounts.CountAsync();
            var totalAssets = await db.Assets.CountAsync();
            Log($"\n--- État de la base ---");
            Log($"Comptes : {totalAccounts} | Actifs : {totalAssets} | Transactions : {totalTx}");
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION : {ex.Message}");
            Log(ex.StackTrace ?? "");
        }
        finally
        {
            BtnImportBitstack.IsEnabled = true;
        }
    }

    private async void BtnImportBoursobank_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Fichiers PDF (*.pdf)|*.pdf",
            Title = "Sélectionner un avis d'opéré Boursobank"
        };

        if (dialog.ShowDialog() != true)
            return;

        BtnImportBoursobank.IsEnabled = false;
        Log($"=== Import démarré : {dialog.FileName} ===");

        try
        {
            var options = new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlite(DatabaseConfig.ConnectionString)
                .Options;

            using var db = new PortfolioDbContext(options);
            await db.Database.MigrateAsync();

            var provider = new BoursobankPdfProvider();
            var importService = new ImportService(db);

            var summary = await importService.ImportAsync(provider, dialog.FileName);

            Log($"Fichier : {summary.SourceFile}");
            Log($"Importées : {summary.Imported}");
            Log($"Doublons ignorés : {summary.Duplicates}");

            if (summary.Warnings.Any())
            {
                Log($"\nAvertissements ({summary.Warnings.Count}) :");
                foreach (var w in summary.Warnings) Log($"  - {w}");
            }

            if (summary.Errors.Any())
            {
                Log($"\nErreurs ({summary.Errors.Count}) :");
                foreach (var err in summary.Errors) Log($"  - {err}");
            }

            var totalTx = await db.Transactions.CountAsync();
            var totalAccounts = await db.Accounts.CountAsync();
            var totalAssets = await db.Assets.CountAsync();
            Log($"\n--- État de la base ---");
            Log($"Comptes : {totalAccounts} | Actifs : {totalAssets} | Transactions : {totalTx}");
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION : {ex.Message}");
            Log(ex.StackTrace ?? "");
        }
        finally
        {
            BtnImportBoursobank.IsEnabled = true;
        }
    }

    private void Log(string message)
    {
        TxtLog.AppendText(message + Environment.NewLine);
        TxtLog.ScrollToEnd();
    }
}