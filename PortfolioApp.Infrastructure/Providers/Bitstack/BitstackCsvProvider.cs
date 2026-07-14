using CsvHelper;
using CsvHelper.Configuration;
using PortfolioApp.Core.Enums;
using PortfolioApp.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Globalization;
using System.Text;

namespace PortfolioApp.Infrastructure.Providers.Bitstack;

public class BitstackCsvProvider : IPositionProvider
{
    public AccountType AccountType => AccountType.Bitstack;

    public async Task<ImportResult> ImportAsync(string filePath, CancellationToken ct = default)
    {
        var result = new ImportResult { SourceFile = Path.GetFileName(filePath) };

        if (!File.Exists(filePath))
        {
            result.Errors.Add($"Fichier introuvable : {filePath}");
            return result;
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,    // ignore les colonnes manquantes
            BadDataFound = null,          // tolère les données mal formatées
            TrimOptions = TrimOptions.Trim
        };

        try
        {
            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, config);

            await foreach (var row in csv.GetRecordsAsync<BitstackCsvRow>(ct))
            {
                var parsed = MapRow(row, result);
                if (parsed != null)
                    result.Transactions.Add(parsed);
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Erreur de parsing : {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Convertit une ligne CSV en ParsedTransaction selon le type d'opération Bitstack.
    /// Retourne null si la ligne n'est pas exploitable (avec un warning loggé).
    /// </summary>
    private ParsedTransaction? MapRow(BitstackCsvRow row, ImportResult result)
    {
        if (string.IsNullOrWhiteSpace(row.ExternalId))
        {
            result.Warnings.Add($"Ligne sans ID externe ignorée (date={row.Date:yyyy-MM-dd})");
            return null;
        }

        return row.Type switch
        {
            "Échange" => MapExchange(row, result),
            "Dépôt" => MapDeposit(row, result),
            "Retrait" => MapWithdrawal(row, result),
            _ => LogUnknownType(row, result)
        };
    }

    private ParsedTransaction? MapExchange(BitstackCsvRow row, ImportResult result)
    {
        // Échange = achat BTC : on reçoit du BTC contre de l'EUR
        if (row.CurrencyReceived != "BTC" || row.CurrencySent != "EUR")
        {
            result.Warnings.Add(
                $"Échange non-BTC/EUR ignoré (reçu={row.CurrencyReceived}, envoyé={row.CurrencySent}, id={row.ExternalId})");
            return null;
        }

        if (!row.AmountReceived.HasValue || !row.PriceReceived.HasValue)
        {
            result.Warnings.Add($"Échange sans montant ou prix (id={row.ExternalId})");
            return null;
        }

        return new ParsedTransaction
        {
            Date = DateTime.SpecifyKind(row.Date, DateTimeKind.Utc),
            Type = TransactionType.Buy,
            AccountType = AccountType.Bitstack,
            AssetSymbol = "BTC",
            AssetType = AssetType.Crypto,
            Quantity = row.AmountReceived.Value,
            UnitPrice = row.PriceReceived.Value,
            Fees = row.Fees ?? 0m,
            ExternalId = row.ExternalId,
            RawData = row.Description
        };
    }

    private ParsedTransaction? MapDeposit(BitstackCsvRow row, ImportResult result)
    {
        if (string.IsNullOrEmpty(row.CurrencyReceived) || !row.AmountReceived.HasValue)
        {
            result.Warnings.Add($"Dépôt sans monnaie ou sans montant (id={row.ExternalId})");
            return null;
        }

        // Cas 1 : Dépôt en EUR (versement classique)
        if (row.CurrencyReceived == "EUR")
        {
            return null;
        }

        // Cas 2 : Dépôt en crypto (cadeaux, récompenses, transferts entrants)
        // Le prix d'acquisition est le prix marché à la date du dépôt
        if (!row.PriceReceived.HasValue)
        {
            result.Warnings.Add($"Dépôt crypto sans prix de référence (id={row.ExternalId})");
            return null;
        }

        return new ParsedTransaction
        {
            Date = DateTime.SpecifyKind(row.Date, DateTimeKind.Utc),
            Type = TransactionType.Deposit,
            AccountType = AccountType.Bitstack,
            AssetSymbol = row.CurrencyReceived,
            AssetType = AssetType.Crypto,
            Quantity = row.AmountReceived.Value,
            UnitPrice = row.PriceReceived.Value,    // prix marché à la date
            Fees = row.Fees ?? 0m,
            ExternalId = row.ExternalId,
            RawData = $"{row.Description} (dépôt crypto)"
        };
    }

    private ParsedTransaction? MapWithdrawal(BitstackCsvRow row, ImportResult result)
    {
        var symbol = row.CurrencySent;
        var amount = row.AmountSent;

        if (string.IsNullOrEmpty(symbol) || !amount.HasValue)
        {
            result.Warnings.Add($"Retrait sans montant ou monnaie (id={row.ExternalId})");
            return null;
        }

        if (symbol == "EUR")
        {
            return null;
        }

        // Conversion des frais en EUR si nécessaire
        decimal feesInEur = 0m;
        if (row.Fees.HasValue && row.Fees.Value > 0)
        {
            if (row.FeesCurrency == "EUR")
            {
                feesInEur = row.Fees.Value;
            }
            else if (row.FeesCurrency == symbol && row.PriceSent.HasValue)
            {
                // Frais en crypto : on les convertit avec le prix de référence du retrait
                feesInEur = Math.Round(row.Fees.Value * row.PriceSent.Value, 2);
            }
            else
            {
                result.Warnings.Add($"Devise des frais non gérée pour retrait (id={row.ExternalId})");
            }
        }

        // Prix unitaire : pour un retrait crypto, on utilise le prix de référence Bitstack
        var unitPrice = symbol == "EUR" ? 1m : (row.PriceSent ?? 0m);

        return new ParsedTransaction
        {
            Date = DateTime.SpecifyKind(row.Date, DateTimeKind.Utc),
            Type = TransactionType.Withdrawal,
            AccountType = AccountType.Bitstack,
            AssetSymbol = symbol,
            AssetType = symbol == "EUR" ? AssetType.Cash : AssetType.Crypto,
            Quantity = amount.Value,
            UnitPrice = unitPrice,
            Fees = feesInEur,
            ExternalId = row.ExternalId,
            RawData = row.Description
        };
    }

    private ParsedTransaction? LogUnknownType(BitstackCsvRow row, ImportResult result)
    {
        result.Warnings.Add($"Type d'opération inconnu '{row.Type}' (id={row.ExternalId})");
        return null;
    }
}