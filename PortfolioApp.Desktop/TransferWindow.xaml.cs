using System.Globalization;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using PortfolioApp.Core.Entities;
using PortfolioApp.Core.Enums;
using PortfolioApp.Core.Models;
using PortfolioApp.Infrastructure.Data;
using PortfolioApp.Infrastructure.Services;

namespace PortfolioApp.Desktop;

public class AssetPositionItem
{
    public Position Position { get; set; } = null!;
    public string Display => $"{Position.AssetSymbol} ({Position.QuantityFormatted})";
}

public partial class TransferWindow : Window
{
    public bool TransferSaved { get; private set; }

    private List<Position> _allPositions = new();

    public TransferWindow()
    {
        InitializeComponent();
        DpDate.SelectedDate = DateTime.Today;

        TxtQuantity.TextChanged += (_, _) => UpdatePreview();
        CbAccountDest.SelectionChanged += (_, _) => UpdatePreview();

        Loaded += async (_, _) => await LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        try
        {
            var options = new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlite(DatabaseConfig.ConnectionString)
                .Options;

            using var db = new PortfolioDbContext(options);
            await db.Database.MigrateAsync();

            // Charger toutes les positions actuelles (non vides)
            var service = new PositionService(db);
            _allPositions = await service.GetCurrentPositionsAsync(includeEmpty: false);

            // Comptes sources : uniquement ceux qui ont des positions non vides
            var sourceAccounts = _allPositions
                .Select(p => p.AccountType)
                .Distinct()
                .OrderBy(a => a.ToString())
                .ToList();

            CbAccountSource.ItemsSource = sourceAccounts;

            // Comptes destination : tous les AccountType de l'enum
            CbAccountDest.ItemsSource = Enum.GetValues<AccountType>();

            if (sourceAccounts.Any())
                CbAccountSource.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur de chargement : {ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CbAccountSource_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CbAccountSource.SelectedItem is not AccountType sourceType)
        {
            CbAsset.ItemsSource = null;
            return;
        }

        // Filtrer les positions du compte source
        var assets = _allPositions
            .Where(p => p.AccountType == sourceType)
            .Select(p => new AssetPositionItem { Position = p })
            .ToList();

        CbAsset.ItemsSource = assets;

        if (assets.Any())
            CbAsset.SelectedIndex = 0;

        // Filtrer le compte destination pour exclure le source
        var allDest = Enum.GetValues<AccountType>().Where(a => a != sourceType).ToList();
        CbAccountDest.ItemsSource = allDest;
        if (allDest.Any())
            CbAccountDest.SelectedIndex = 0;

        UpdatePreview();
    }

    private void CbAsset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CbAsset.SelectedItem is AssetPositionItem item)
        {
            TxtAvailable.Text = $"{item.Position.QuantityFormatted} {item.Position.AssetSymbol} (PRU {item.Position.AveragePriceFormatted})";
        }
        else
        {
            TxtAvailable.Text = "—";
        }
        UpdatePreview();
    }

    private void BtnTransferAll_Click(object sender, RoutedEventArgs e)
    {
        if (CbAsset.SelectedItem is AssetPositionItem item)
        {
            TxtQuantity.Text = item.Position.Quantity.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void UpdatePreview()
    {
        if (CbAsset.SelectedItem is not AssetPositionItem item ||
            CbAccountDest.SelectedItem is not AccountType destType ||
            !TryParseDecimal(TxtQuantity.Text, out var quantity))
        {
            TxtPreview.Text = "Aperçu : sélectionner un actif, une destination et une quantité valide.";
            return;
        }

        var sourceType = (AccountType)CbAccountSource.SelectedItem;

        if (quantity <= 0)
        {
            TxtPreview.Text = "Aperçu : la quantité doit être positive.";
            return;
        }

        if (quantity > item.Position.Quantity)
        {
            TxtPreview.Text = $"⚠️  Quantité ({quantity:N8}) supérieure à la position disponible ({item.Position.QuantityFormatted}).";
            return;
        }

        TxtPreview.Text =
            $"Transfert : {quantity:N8} {item.Position.AssetSymbol}\n" +
            $"  De  : {sourceType}\n" +
            $"  Vers : {destType}\n" +
            $"  PRU conservé : {item.Position.AveragePriceFormatted}\n" +
            $"  (opération fiscalement neutre)";
    }

    private static bool TryParseDecimal(string input, out decimal result)
    {
        var normalized = input?.Replace(',', '.').Trim() ?? string.Empty;
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (DpDate.SelectedDate == null)
        {
            MessageBox.Show("La date est obligatoire.", "Champ manquant",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CbAccountSource.SelectedItem is not AccountType sourceType ||
            CbAccountDest.SelectedItem is not AccountType destType ||
            CbAsset.SelectedItem is not AssetPositionItem item)
        {
            MessageBox.Show("Sélectionnez source, actif et destination.", "Champ manquant",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sourceType == destType)
        {
            MessageBox.Show("Le compte source et destination doivent être différents.", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseDecimal(TxtQuantity.Text, out var quantity) || quantity <= 0)
        {
            MessageBox.Show("La quantité doit être un nombre positif.", "Saisie invalide",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (quantity > item.Position.Quantity + 0.000001m)
        {
            MessageBox.Show($"La quantité demandée ({quantity}) dépasse la position disponible ({item.Position.Quantity}).",
                "Quantité insuffisante", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnSave.IsEnabled = false;

        try
        {
            var options = new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlite(DatabaseConfig.ConnectionString)
                .Options;

            using var db = new PortfolioDbContext(options);
            await db.Database.MigrateAsync();

            // Récupérer les comptes (créer destination si nécessaire)
            var sourceAccount = await db.Accounts.FirstAsync(a => a.Type == sourceType);
            var destAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Type == destType);
            if (destAccount == null)
            {
                destAccount = new Account { Name = destType.ToString(), Type = destType, Currency = "EUR" };
                db.Accounts.Add(destAccount);
                await db.SaveChangesAsync();
            }

            var asset = await db.Assets.FirstAsync(a => a.Id == item.Position.AssetId);
            var date = DateTime.SpecifyKind(DpDate.SelectedDate!.Value, DateTimeKind.Utc);
            var note = string.IsNullOrWhiteSpace(TxtNote.Text) ? null : TxtNote.Text.Trim();

            // Génère un ID de groupe unique pour lier les 2 transactions
            var transferId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var rawData = $"Transfert {sourceType} → {destType} (id {transferId}){(note != null ? " | " + note : "")}";

            // 1. Withdrawal du compte source
            var withdrawal = new Transaction
            {
                Date = date,
                Type = TransactionType.Withdrawal,
                Quantity = quantity,
                UnitPrice = item.Position.AveragePrice,   // PRU conservé
                Fees = 0m,
                ExternalId = $"TRANSFER-OUT-{transferId}",
                SourceFile = "transfer",
                RawData = rawData,
                AccountId = sourceAccount.Id,
                AssetId = asset.Id
            };

            // 2. Deposit sur le compte destination (au même PRU)
            var deposit = new Transaction
            {
                Date = date,
                Type = TransactionType.Deposit,
                Quantity = quantity,
                UnitPrice = item.Position.AveragePrice,   // PRU conservé
                Fees = 0m,
                ExternalId = $"TRANSFER-IN-{transferId}",
                SourceFile = "transfer",
                RawData = rawData,
                AccountId = destAccount.Id,
                AssetId = asset.Id
            };

            db.Transactions.Add(withdrawal);
            db.Transactions.Add(deposit);
            await db.SaveChangesAsync();

            TransferSaved = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors du transfert : {ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
            BtnSave.IsEnabled = true;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}