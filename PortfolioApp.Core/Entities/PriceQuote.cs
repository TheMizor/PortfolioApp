using System;
using System.Collections.Generic;
using System.Text;

namespace PortfolioApp.Core.Entities;

public class PriceQuote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Source { get; set; } = string.Empty;

    public Guid AssetId { get; set; }
    public Asset Asset { get; set; } = null!;
}