using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PortfolioApp.Core.Entities;

namespace PortfolioApp.Infrastructure.Data;

public class PortfolioDbContext : DbContext
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<PriceQuote> PriceQuotes => Set<PriceQuote>();

    public PortfolioDbContext(DbContextOptions<PortfolioDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Précision décimale pour les montants : 28 chiffres total, 8 après la virgule
        // (assez pour BTC qui a 8 décimales, et tout le reste)
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.Property(t => t.Quantity).HasPrecision(28, 8);
            entity.Property(t => t.UnitPrice).HasPrecision(28, 8);
            entity.Property(t => t.Fees).HasPrecision(28, 8);

            // Index sur ExternalId pour la déduplication rapide
            entity.HasIndex(t => t.ExternalId).IsUnique();

            // Index sur Date pour les requêtes par période
            entity.HasIndex(t => t.Date);
        });

        modelBuilder.Entity<PriceQuote>(entity =>
        {
            entity.Property(p => p.Price).HasPrecision(28, 8);
            entity.HasIndex(p => new { p.AssetId, p.Date });
        });

        modelBuilder.Entity<Asset>(entity =>
        {
            entity.HasIndex(a => a.Symbol).IsUnique();
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasIndex(a => a.Name).IsUnique();
        });
    }
}