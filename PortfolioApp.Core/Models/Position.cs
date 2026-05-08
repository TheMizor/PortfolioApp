using System;
using System.Collections.Generic;
using System.Text;
using PortfolioApp.Core.Enums;

namespace PortfolioApp.Core.Models;

/// <summary>
/// Représente une position détenue à un instant T, calculée depuis l'historique des transactions.
/// Ce n'est PAS une entité persistée : c'est une projection d'événements.
/// </summary>
public class Position
{
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public AccountType AccountType { get; set; }

    public Guid AssetId { get; set; }
    public string AssetSymbol { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public AssetType AssetType { get; set; }

    public decimal Quantity { get; set; }
    public decimal AveragePrice { get; set; }       // PRU en CMP (Coût Moyen Pondéré)
    public decimal TotalCost { get; set; }          // PRU × Quantité, frais inclus
    public decimal TotalFees { get; set; }          // Cumul des frais payés (info)
    public int TransactionCount { get; set; }

    // Propriétés formatées pour l'affichage UI
    public string QuantityFormatted => AssetType == AssetType.Crypto
        ? Quantity.ToString("N8")
        : Quantity.ToString("N4");

    public string AveragePriceFormatted => AveragePrice.ToString("N4") + " EUR";
    public string TotalCostFormatted => TotalCost.ToString("N2") + " EUR";
    public string TotalFeesFormatted => TotalFees.ToString("N2") + " EUR";
}