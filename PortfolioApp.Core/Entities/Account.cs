using PortfolioApp.Core.Enums;
using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel.DataAnnotations.Schema;

namespace PortfolioApp.Core.Entities;

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public string Currency { get; set; } = "EUR";

    // Enveloppe fiscale, dérivée du type de compte : rien à saisir, rien à persister
    [NotMapped]
    public FiscalEnvelope Envelope => Type.ToEnvelope();

    // Date d'ouverture du compte (seuil des 5 ans du PEA)
    public DateTime? OpeningDate { get; set; }

    // Navigation property : toutes les transactions liées à ce compte
    public List<Transaction> Transactions { get; set; } = new();
}