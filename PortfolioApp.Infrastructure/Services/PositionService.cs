using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PortfolioApp.Core.Entities;
using PortfolioApp.Core.Enums;
using PortfolioApp.Core.Models;
using PortfolioApp.Infrastructure.Data;

namespace PortfolioApp.Infrastructure.Services;

public class PositionService
{
    private readonly PortfolioDbContext _db;

    public PositionService(PortfolioDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Calcule les positions actuelles depuis l'historique complet des transactions.
    /// </summary>
    /// <param name="accountId">Filtre optionnel sur un compte.</param>
    /// <param name="includeEmpty">Si true, inclut aussi les positions soldées (qty = 0).</param>
    public async Task<List<Position>> GetCurrentPositionsAsync(
        Guid? accountId = null,
        bool includeEmpty = false,
        CancellationToken ct = default)
    {
        var query = _db.Transactions
            .Include(t => t.Asset)
            .Include(t => t.Account)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(t => t.AccountId == accountId.Value);

        var transactions = await query
            .OrderBy(t => t.Date)
            .ToListAsync(ct);

        var positions = transactions
            .GroupBy(t => new { t.AccountId, t.AssetId })
            .Select(g => CalculatePosition(g.ToList()))
            .ToList();

        if (!includeEmpty)
            positions = positions.Where(p => p.Quantity > 0.000001m).ToList();

        // Tri : par compte, puis par valeur totale décroissante
        return positions
            .OrderBy(p => p.AccountName)
            .ThenByDescending(p => p.TotalCost)
            .ToList();
    }

    /// <summary>
    /// Calcule une position depuis un groupe de transactions (même actif, même compte).
    /// Convention CMP (Coût Moyen Pondéré) : à chaque achat, on recalcule le prix moyen.
    /// Une vente ne modifie pas le PRU, elle consomme la quantité au PRU actuel.
    /// </summary>
    private static Position CalculatePosition(List<Transaction> txs)
    {
        var first = txs.First();
        decimal quantity = 0m;
        decimal averagePrice = 0m;
        decimal totalCost = 0m;
        decimal totalFees = 0m;

        foreach (var tx in txs)
        {
            totalFees += tx.Fees;

            switch (tx.Type)
            {
                case TransactionType.Buy:
                case TransactionType.Deposit:
                    // CMP : nouveau PRU = (ancien coût total + nouveau coût) / quantité totale
                    var costAdded = tx.Quantity * tx.UnitPrice + tx.Fees;
                    var newQuantity = quantity + tx.Quantity;

                    if (newQuantity > 0)
                    {
                        totalCost += costAdded;
                        averagePrice = totalCost / newQuantity;
                    }
                    quantity = newQuantity;
                    break;

                case TransactionType.Sell:
                case TransactionType.Withdrawal:
                    // La vente consomme la quantité au PRU actuel, le PRU ne change pas.
                    var costRemoved = tx.Quantity * averagePrice;
                    quantity -= tx.Quantity;
                    totalCost -= costRemoved;

                    if (quantity <= 0.000001m)
                    {
                        quantity = 0;
                        averagePrice = 0;
                        totalCost = 0;
                    }
                    break;
            }
        }

        return new Position
        {
            AccountId = first.AccountId,
            AccountName = first.Account.Name,
            AccountType = first.Account.Type,
            AssetId = first.AssetId,
            AssetSymbol = first.Asset.Symbol,
            AssetName = first.Asset.Name,
            AssetType = first.Asset.Type,
            Quantity = Math.Round(quantity, 8),
            AveragePrice = Math.Round(averagePrice, 8),
            TotalCost = Math.Round(totalCost, 2),
            TotalFees = Math.Round(totalFees, 2),
            TransactionCount = txs.Count
        };
    }
}