using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using PortfolioApp.Infrastructure.Data;
using PortfolioApp.Infrastructure.Providers.Bitstack;
using PortfolioApp.Infrastructure.Providers.Boursobank;
using PortfolioApp.Infrastructure.Services;
using PortfolioApp.Core.Models;
using System.IO;
using System.Windows;
using PortfolioApp.Core.Entities;

namespace PortfolioApp.Desktop;

public partial class MainWindow : Window
{
    private bool _isRefreshing = false;
    private static string DefaultDocumentsFolder =>
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ShowLoading(string message)
    {
        TxtLoadingMessage.Text = message;
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private void HideLoading()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void BtnChangeScanFolder_Click(object sender, RoutedEventArgs e)
    {
        var settings = UserSettings.Load();

        var dialog = new OpenFolderDialog
        {
            Title = "Sélectionner le nouveau dossier à scanner",
            InitialDirectory = settings.LastScanFolder ?? DefaultDocumentsFolder
        };

        if (dialog.ShowDialog() != true)
            return;

        settings.LastScanFolder = dialog.FolderName;
        settings.Save();

        MessageBox.Show($"Dossier mémorisé :\n{dialog.FolderName}",
            "Configuration enregistrée",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void BtnScanFolder_Click(object sender, RoutedEventArgs e)
    {
        // Charger les settings utilisateur
        var settings = UserSettings.Load();
        string rootPath;

        // Si on a déjà un dossier mémorisé ET qu'il existe encore, on l'utilise directement
        if (!string.IsNullOrEmpty(settings.LastScanFolder) && Directory.Exists(settings.LastScanFolder))
        {
            rootPath = settings.LastScanFolder;
        }
        else
        {
            // Sinon, on demande à l'utilisateur de sélectionner un dossier
            var dialog = new OpenFolderDialog
            {
                Title = "Sélectionner le dossier à scanner",
                InitialDirectory = settings.LastScanFolder ?? DefaultDocumentsFolder
            };

            if (dialog.ShowDialog() != true)
                return;

            rootPath = dialog.FolderName;

            // Mémoriser pour la prochaine fois
            settings.LastScanFolder = rootPath;
            settings.Save();
        }

        BtnScanFolder.IsEnabled = false;
        ShowLoading("Scan du dossier en cours...");
        Log($"=== Scan du dossier : {rootPath} ===");

        try
        {
            var options = new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlite(DatabaseConfig.ConnectionString)
                .Options;

            using var db = new PortfolioDbContext(options);
            await db.Database.MigrateAsync();

            var importService = new ImportService(db);
            var scanService = new FolderScanService(importService);

            var result = await scanService.ScanAndImportAsync(rootPath);

            // Détail par fichier
            Log("\n--- Détail par fichier ---");
            foreach (var fr in result.FileResults.OrderBy(f => f.Source).ThenBy(f => f.FileName))
            {
                var status = fr.Imported > 0
                    ? $"✓ {fr.Imported} nouvelle(s)"
                    : fr.WasParsed
                    ? "= rien de nouveau"
                    : "⊘ format non reconnu";

                var details = fr.Duplicates > 0 ? $" (déjà importé: {fr.Duplicates})" : "";
                var warnings = fr.WarningCount > 0 ? $" ⚠ {fr.WarningCount} warning(s)" : "";

                Log($"[{fr.Source}] {fr.FileName}: {status}{details}{warnings}");
            }

            // Récap global
            Log($"\n=== Résumé ===");
            Log($"Fichiers traités : {result.FilesProcessed}");
            Log($"Fichiers avec nouvelles données : {result.FilesWithNewData}");
            Log($"Total nouvelles transactions : {result.TotalImported}");
            Log($"Total doublons ignorés : {result.TotalDuplicates}");

            if (result.Errors.Any())
            {
                Log($"\n⚠ Erreurs ({result.Errors.Count}) :");
                foreach (var err in result.Errors)
                    Log($"  - {err}");
            }

            // État de la base
            var totalTx = await db.Transactions.CountAsync();
            var totalAccounts = await db.Accounts.CountAsync();
            var totalAssets = await db.Assets.CountAsync();
            Log($"\n--- État de la base ---");
            Log($"Comptes : {totalAccounts} | Actifs : {totalAssets} | Transactions : {totalTx}");

            if (result.IgnoredFolders.Any())
            {
                Log($"\n--- Dossiers ignorés (pas de provider) ---");
                foreach (var folder in result.IgnoredFolders)
                    Log($"  - {folder}");
            }

            // Rafraîchir les vues
            BtnRefreshPositions_Click(sender, e);
            if (GridTransactions.Items.Count > 0)
                BtnRefreshTransactions_Click(sender, e);
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION : {ex.Message}");
            Log(ex.StackTrace ?? "");
        }
        finally
        {
            BtnScanFolder.IsEnabled = true;
            HideLoading();
        }
    }

    private void BtnTransfer_Click(object sender, RoutedEventArgs e)
    {
        var window = new TransferWindow { Owner = this };
        var result = window.ShowDialog();

        if (result == true && window.TransferSaved)
        {
            Log($"=== Transfert enregistré ===");
            BtnRefreshPositions_Click(sender, e);
            if (GridTransactions.Items.Count > 0)
                BtnRefreshTransactions_Click(sender, e);
        }
    }

    private async void BtnRefreshTransactions_Click(object sender, RoutedEventArgs e)
    {
        BtnRefreshTransactions.IsEnabled = false;
        ShowLoading("Chargement des transactions...");

        try
        {
            var options = new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlite(DatabaseConfig.ConnectionString)
                .Options;

            using var db = new PortfolioDbContext(options);
            await db.Database.MigrateAsync();

            var transactions = await db.Transactions
                .Include(t => t.Account)
                .Include(t => t.Asset)
                .OrderByDescending(t => t.Date)
                .ToListAsync();

            GridTransactions.ItemsSource = transactions;

            TxtTransactionsSummary.Text = $"{transactions.Count} transaction(s) au total";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnRefreshTransactions.IsEnabled = true;
            HideLoading();
        }
    }

    private void BtnEditTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (GridTransactions.SelectedItem is not Transaction selected)
        {
            MessageBox.Show("Sélectionnez une transaction à modifier.", "Aucune sélection",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new ManualTransactionWindow(selected) { Owner = this };
        var result = window.ShowDialog();

        if (result == true && window.TransactionSaved)
        {
            // Rafraîchit les vues qui peuvent avoir changé
            BtnRefreshTransactions_Click(sender, e);
            BtnRefreshPositions_Click(sender, e);
        }
    }

    private async void BtnDeleteTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (GridTransactions.SelectedItem is not Transaction selected)
        {
            MessageBox.Show("Sélectionnez une transaction à supprimer.", "Aucune sélection",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Supprimer définitivement cette transaction ?\n\n" +
            $"Date : {selected.Date:yyyy-MM-dd}\n" +
            $"Type : {selected.Type}\n" +
            $"Actif : {selected.Asset.Symbol}\n" +
            $"Quantité : {selected.Quantity}",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        ShowLoading("Suppression en cours...");

        try
        {
            var options = new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlite(DatabaseConfig.ConnectionString)
                .Options;

            using var db = new PortfolioDbContext(options);
            await db.Database.MigrateAsync();

            var toDelete = await db.Transactions.FirstOrDefaultAsync(t => t.Id == selected.Id);
            if (toDelete != null)
            {
                db.Transactions.Remove(toDelete);
                await db.SaveChangesAsync();
            }

            BtnRefreshTransactions_Click(sender, e);
            BtnRefreshPositions_Click(sender, e);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            HideLoading();
        }
    }

    private void BtnManualEntry_Click(object sender, RoutedEventArgs e)
    {
        var window = new ManualTransactionWindow
        {
            Owner = this
        };

        var result = window.ShowDialog();

        if (result == true && window.TransactionSaved)
        {
            Log($"=== Saisie manuelle enregistrée ===");
            // Rafraîchir la vue Positions
            BtnRefreshPositions_Click(sender, e);
        }
    }

    private async void BtnRefreshPositions_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        BtnRefreshPositions.IsEnabled = false;
        ShowLoading("Calcul des positions...");

        try
        {
            var options = new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlite(DatabaseConfig.ConnectionString)
                .Options;

            using var db = new PortfolioDbContext(options);
            await db.Database.MigrateAsync();

            var service = new PositionService(db);
            var positions = await service.GetCurrentPositionsAsync(
                includeEmpty: ChkIncludeEmpty.IsChecked == true);

            GridPositions.ItemsSource = positions;

            TxtPositionsSummary.Text = positions.Count == 0
                ? "Aucune position détenue actuellement."
                : $"{positions.Count} position(s) — Coût total cumulé : {positions.Sum(p => p.TotalCost):N2} EUR";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isRefreshing = false;
            BtnRefreshPositions.IsEnabled = true;
            HideLoading();
        }
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
        ShowLoading("Import CSV Bitstack...");
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
            // Rafraîchir l'onglet Positions
            BtnRefreshPositions_Click(sender, e);
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION : {ex.Message}");
            Log(ex.StackTrace ?? "");
        }
        finally
        {
            BtnImportBitstack.IsEnabled = true;
            HideLoading();
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
        ShowLoading("Import PDF Boursobank...");
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
            // Rafraîchir l'onglet Positions
            BtnRefreshPositions_Click(sender, e);
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION : {ex.Message}");
            Log(ex.StackTrace ?? "");
        }
        finally
        {
            BtnImportBoursobank.IsEnabled = true;
            HideLoading();
        }
    }

    private void Log(string message)
    {
        TxtLog.AppendText(message + Environment.NewLine);
        TxtLog.ScrollToEnd();
    }
}