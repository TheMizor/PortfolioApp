using System;
using System.Collections.Generic;
using System.Text;
using PortfolioApp.Core.Enums;

namespace PortfolioApp.Core.Entities;

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Fees { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string? RawData { get; set; }
    public TransactionConfidence Confidence { get; set; } = TransactionConfidence.Documented;

    // Foreign keys
    public Guid AccountId { get; set; }
    public Guid AssetId { get; set; }

    // Navigation properties
    public Account Account { get; set; } = null!;
    public Asset Asset { get; set; } = null!;

    // Calculé, pas stocké : montant total de l'opération en devise du compte
    public decimal TotalAmount => Quantity * UnitPrice + Fees;
}