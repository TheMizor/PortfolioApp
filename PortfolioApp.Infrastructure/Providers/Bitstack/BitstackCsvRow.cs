using System;
using System.Collections.Generic;
using System.Text;
using CsvHelper.Configuration.Attributes;

namespace PortfolioApp.Infrastructure.Providers.Bitstack;

/// <summary>
/// Représente une ligne brute du CSV Bitstack, mapping direct des colonnes.
/// Ne pas utiliser hors du provider, c'est un DTO de parsing uniquement.
/// </summary>
public class BitstackCsvRow
{
    [Name("Type")]
    public string Type { get; set; } = string.Empty;

    [Name("Date")]
    public DateTime Date { get; set; }

    [Name("Fuseau horaire")]
    public string TimeZone { get; set; } = string.Empty;

    [Name("Montant reçu")]
    public decimal? AmountReceived { get; set; }

    [Name("Monnaie ou jeton reçu")]
    public string CurrencyReceived { get; set; } = string.Empty;

    [Name("Montant envoyé")]
    public decimal? AmountSent { get; set; }

    [Name("Monnaie ou jeton envoyé")]
    public string CurrencySent { get; set; } = string.Empty;

    [Name("Frais")]
    public decimal? Fees { get; set; }

    [Name("Monnaie ou jeton des frais")]
    public string FeesCurrency { get; set; } = string.Empty;

    [Name("Description")]
    public string Description { get; set; } = string.Empty;

    [Name("Prix du jeton du montant envoyé")]
    public decimal? PriceSent { get; set; }

    [Name("Prix du jeton du montant recu")]
    public decimal? PriceReceived { get; set; }

    [Name("Prix du jeton des frais")]
    public decimal? PriceFees { get; set; }

    [Name("Adresse")]
    public string Address { get; set; } = string.Empty;

    [Name("Transaction hash")]
    public string TransactionHash { get; set; } = string.Empty;

    [Name("ID Externe")]
    public string ExternalId { get; set; } = string.Empty;
}