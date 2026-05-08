using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PortfolioApp.Core.Entities;
using PortfolioApp.Core.Enums;
using PortfolioApp.Core.Interfaces;
using PortfolioApp.Infrastructure.Data;

namespace PortfolioApp.Infrastructure.Services;

public class ImportService
{
    private readonly PortfolioDbContext _db;

    public ImportService(PortfolioDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Importe le résultat d'un provider en base : déduplique, crée les entités
    /// manquantes (Account, Asset) et persiste les transactions.
    /// </summary>
    public async Task<ImportSummary> ImportAsync(
    IPositionProvider provider,
    string filePath,
    CancellationToken ct = default)
    {
        var summary = new ImportSummary { SourceFile = Path.GetFileName(filePath) };

        var parseResult = await provider.ImportAsync(filePath, ct);
        summary.Warnings.AddRange(parseResult.Warnings);
        summary.Errors.AddRange(parseResult.Errors);

        if (parseResult.Errors.Any())
            return summary;

        // Récupérer tous les ExternalId existants pour les comptes concernés
        // (déduplication globale, peu importe le compte exact)
        var incomingIds = parseResult.Transactions.Select(t => t.ExternalId).ToList();
        var existingIds = await _db.Transactions
            .Where(t => incomingIds.Contains(t.ExternalId))
            .Select(t => t.ExternalId)
            .ToHashSetAsync(ct);

        foreach (var parsed in parseResult.Transactions)
        {
            if (existingIds.Contains(parsed.ExternalId))
            {
                summary.Duplicates++;
                continue;
            }

            var account = await GetOrCreateAccountAsync(parsed.AccountType, ct);
            var asset = await GetOrCreateAssetAsync(parsed.AssetSymbol, parsed.AssetType, ct);

            var tx = new Transaction
            {
                Date = parsed.Date,
                Type = parsed.Type,
                Quantity = parsed.Quantity,
                UnitPrice = parsed.UnitPrice,
                Fees = parsed.Fees,
                ExternalId = parsed.ExternalId,
                SourceFile = parseResult.SourceFile,
                RawData = parsed.RawData,
                AccountId = account.Id,
                AssetId = asset.Id
            };

            _db.Transactions.Add(tx);
            summary.Imported++;
        }

        await _db.SaveChangesAsync(ct);
        return summary;
    }

    private async Task<Account> GetOrCreateAccountAsync(AccountType type, CancellationToken ct)
    {
        var name = type.ToString();
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Name == name, ct);

        if (account == null)
        {
            account = new Account { Name = name, Type = type, Currency = "EUR" };
            _db.Accounts.Add(account);
            await _db.SaveChangesAsync(ct);
        }

        return account;
    }

    private async Task<Asset> GetOrCreateAssetAsync(string symbol, AssetType type, CancellationToken ct)
    {
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Symbol == symbol, ct);

        if (asset == null)
        {
            asset = new Asset
            {
                Symbol = symbol,
                Name = symbol,
                Type = type,
                QuoteCurrency = "EUR"
            };
            _db.Assets.Add(asset);
            await _db.SaveChangesAsync(ct);
        }

        return asset;
    }
}

public class ImportSummary
{
    public string SourceFile { get; set; } = string.Empty;
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}