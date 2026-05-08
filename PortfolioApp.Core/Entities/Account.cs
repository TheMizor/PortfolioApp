using PortfolioApp.Core.Enums;
using System;
using System.Collections.Generic;
using System.Text;
using System.Transactions;

namespace PortfolioApp.Core.Entities;

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public string Currency { get; set; } = "EUR";

    // Navigation property : toutes les transactions liées à ce compte
    public List<Transaction> Transactions { get; set; } = new();
}