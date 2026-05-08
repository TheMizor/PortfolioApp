using System;
using System.Collections.Generic;
using System.Text;

namespace PortfolioApp.Infrastructure.Data;

public static class DatabaseConfig
{
    public static string DatabasePath
    {
        get
        {
            var fiscaliteRoot = @"C:\Users\simon\Documents\fiscalité";
            var appFolder = Path.Combine(fiscaliteRoot, "_portfolio_app");
            Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, "portfolio.db");
        }
    }

    public static string ConnectionString => $"Data Source={DatabasePath}";
}