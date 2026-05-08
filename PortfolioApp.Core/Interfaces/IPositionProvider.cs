using System;
using System.Collections.Generic;
using System.Text;
using PortfolioApp.Core.Enums;

namespace PortfolioApp.Core.Interfaces;

public interface IPositionProvider
{
    AccountType AccountType { get; }
    Task<ImportResult> ImportAsync(string filePath, CancellationToken ct = default);
}

public class ImportResult
{
    public List<ParsedTransaction> Transactions { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public string SourceFile { get; set; } = string.Empty;
}

public class ParsedTransaction
{
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public string AssetSymbol { get; set; } = string.Empty;   // "BTC", "EUR"
    public AssetType AssetType { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Fees { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string? RawData { get; set; }
}