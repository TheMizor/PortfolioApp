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
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BtnRefreshTransactions_Click(object sender, RoutedEventArgs e)
    {
        BtnRefreshTransactions.IsEnabled = false;

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
        BtnRefreshPositions.IsEnabled = false;

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
            BtnRefreshPositions.IsEnabled = true;
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
        }
    }

    private void Log(string message)
    {
        TxtLog.AppendText(message + Environment.NewLine);
        TxtLog.ScrollToEnd();
    }
}