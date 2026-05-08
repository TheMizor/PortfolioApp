using PortfolioApp.Core.Enums;
using System;
using System.Collections.Generic;
using System.Text;
using System.Transactions;

namespace PortfolioApp.Core.Entities;

public class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AssetType Type { get; set; }
    public string? Isin { get; set; }
    public string QuoteCurrency { get; set; } = "EUR";

    public List<Transaction> Transactions { get; set; } = new();
    public List<PriceQuote> PriceQuotes { get; set; } = new();
}