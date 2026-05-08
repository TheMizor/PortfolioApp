using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PortfolioApp.Core.Entities;
using PortfolioApp.Core.Enums;
using PortfolioApp.Infrastructure.Data;

namespace PortfolioApp.Desktop;

public partial class ManualTransactionWindow : Window
{
    public bool TransactionSaved { get; private set; }

    private Guid? _editingTransactionId;

    /// <summary>Constructeur pour créer une nouvelle transaction.</summary>
    public ManualTransactionWindow()
    {
        InitializeComponent();
        InitializeFields();
        SetupPreviewListeners();
    }

    /// <summary>Constructeur pour éditer une transaction existante.</summary>
    public ManualTransactionWindow(Transaction existing) : this()
    {
        _editingTransactionId = existing.Id;
        Title = "Modifier une transaction";
        BtnSave.Content = "Mettre à jour";

        // Pré-remplir les champs
        DpDate.SelectedDate = existing.Date;
        CbAccount.SelectedItem = existing.Account.Type;
        CbTransactionType.SelectedItem = existing.Type;
        TxtSymbol.Text = existing.Asset.Symbol;
        CbAssetType.SelectedItem = existing.Asset.Type;
        TxtQuantity.Text = existing.Quantity.ToString(CultureInfo.InvariantCulture);
        TxtUnitPrice.Text = existing.UnitPrice.ToString(CultureInfo.InvariantCulture);
        TxtFees.Text = existing.Fees.ToString(CultureInfo.InvariantCulture);
        TxtNote.Text = existing.RawData ?? string.Empty;
    }

    private void SetupPreviewListeners()
    {
        TxtQuantity.TextChanged += (_, _) => UpdatePreview();
        TxtUnitPrice.TextChanged += (_, _) => UpdatePreview();
        TxtFees.TextChanged += (_, _) => UpdatePreview();
        CbTransactionType.SelectionChanged += (_, _) => UpdatePreview();
    }

    private void InitializeFields()
    {
        DpDate.SelectedDate = DateTime.Today;

        // Comptes : tous les AccountType de l'enum
        CbAccount.ItemsSource = Enum.GetValues<AccountType>();
        CbAccount.SelectedIndex = 0;

        // Types de transaction
        CbTransactionType.ItemsSource = Enum.GetValues<TransactionType>();
        CbTransactionType.SelectedItem = TransactionType.Buy;

        // Types d'actif
        CbAssetType.ItemsSource = Enum.GetValues<AssetType>();
        CbAssetType.SelectedItem = AssetType.Crypto;

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (!TryParseInputs(out var quantity, out var unitPrice, out var fees))
        {
            TxtPreview.Text = "Aperçu : remplir les champs numériques pour voir le calcul...";
            return;
        }

        var grossAmount = quantity * unitPrice;
        var totalCost = grossAmount + fees;

        var typeDescription = (CbTransactionType.SelectedItem as TransactionType?) switch
        {
            TransactionType.Buy => "ACHAT — Coût total (frais inclus)",
            TransactionType.Sell => "VENTE — Net reçu (frais déduits)",
            TransactionType.Deposit => "DÉPÔT — Montant entrant",
            TransactionType.Withdrawal => "RETRAIT — Montant sortant",
            _ => "Total"
        };

        var netAmount = (CbTransactionType.SelectedItem as TransactionType?) == TransactionType.Sell
            ? grossAmount - fees
            : grossAmount + fees;

        TxtPreview.Text = $"""
            Quantité × Prix unitaire = Montant brut :
              {quantity:N8} × {unitPrice:N4} = {grossAmount:N2} EUR
            
            Frais : {fees:N2} EUR
            
            {typeDescription} : {netAmount:N2} EUR
            """;
    }

    private bool TryParseInputs(out decimal quantity, out decimal unitPrice, out decimal fees)
    {
        quantity = 0;
        unitPrice = 0;
        fees = 0;

        return TryParseDecimal(TxtQuantity.Text, out quantity)
            && TryParseDecimal(TxtUnitPrice.Text, out unitPrice)
            && TryParseDecimal(TxtFees.Text, out fees);
    }

    private static bool TryParseDecimal(string input, out decimal result)
    {
        // Accepte point ou virgule comme séparateur décimal
        var normalized = input?.Replace(',', '.').Trim() ?? string.Empty;
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // Validation
        if (DpDate.SelectedDate == null)
        {
            MessageBox.Show("La date est obligatoire.", "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtSymbol.Text))
        {
            MessageBox.Show("Le symbole de l'actif est obligatoire.", "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseInputs(out var quantity, out var unitPrice, out var fees))
        {
            MessageBox.Show("Les champs Quantité, Prix unitaire et Frais doivent être des nombres valides.",
                "Saisie invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (quantity <= 0)
        {
            MessageBox.Show("La quantité doit être strictement positive.", "Saisie invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            var accountType = (AccountType)CbAccount.SelectedItem;
            var assetType = (AssetType)CbAssetType.SelectedItem;
            var txType = (TransactionType)CbTransactionType.SelectedItem;
            var symbol = TxtSymbol.Text.Trim().ToUpper();

            // Récupère ou crée le compte
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == accountType.ToString());
            if (account == null)
            {
                account = new Account { Name = accountType.ToString(), Type = accountType, Currency = "EUR" };
                db.Accounts.Add(account);
                await db.SaveChangesAsync();
            }

            // Récupère ou crée l'actif
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Symbol == symbol);
            if (asset == null)
            {
                asset = new Asset
                {
                    Symbol = symbol,
                    Name = symbol,
                    Type = assetType,
                    QuoteCurrency = "EUR"
                };
                db.Assets.Add(asset);
                await db.SaveChangesAsync();
            }

            var date = DateTime.SpecifyKind(DpDate.SelectedDate!.Value, DateTimeKind.Utc);
            var note = string.IsNullOrWhiteSpace(TxtNote.Text) ? null : TxtNote.Text.Trim();

            if (_editingTransactionId.HasValue)
            {
                // Mode édition : on met à jour la transaction existante
                var existing = await db.Transactions.FirstOrDefaultAsync(t => t.Id == _editingTransactionId.Value);
                if (existing == null)
                {
                    MessageBox.Show("Transaction introuvable. Elle a peut-être été supprimée.", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                existing.Date = date;
                existing.Type = txType;
                existing.Quantity = quantity;
                existing.UnitPrice = unitPrice;
                existing.Fees = fees;
                existing.RawData = note;
                existing.AccountId = account.Id;
                existing.AssetId = asset.Id;
                // On ne change PAS l'ExternalId ni le SourceFile : ils gardent l'origine
            }
            else
            {
                // Mode création
                var externalId = $"MANUAL-{Guid.NewGuid():N}";
                var transaction = new Transaction
                {
                    Date = date,
                    Type = txType,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    Fees = fees,
                    ExternalId = externalId,
                    SourceFile = "manual entry",
                    RawData = note,
                    AccountId = account.Id,
                    AssetId = asset.Id
                };
                db.Transactions.Add(transaction);
            }

            await db.SaveChangesAsync();

            TransactionSaved = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'enregistrement : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            BtnSave.IsEnabled = true;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
