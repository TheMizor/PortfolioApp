using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PortfolioApp.Core.Enums;
using PortfolioApp.Core.Interfaces;
using UglyToad.PdfPig;

namespace PortfolioApp.Infrastructure.Providers.Boursobank;

public class BoursobankPdfProvider : IPositionProvider
{
    public AccountType AccountType => AccountType.BoursobankPEA;  // défaut, peut être surchargé par le PDF

    public Task<ImportResult> ImportAsync(string filePath, CancellationToken ct = default)
    {
        var result = new ImportResult { SourceFile = Path.GetFileName(filePath) };

        if (!File.Exists(filePath))
        {
            result.Errors.Add($"Fichier introuvable : {filePath}");
            return Task.FromResult(result);
        }

        try
        {
            string text = ExtractText(filePath);

            // DEBUG : décommenter pour dumper le texte extrait dans un fichier
            // File.WriteAllText(filePath + ".debug.txt", text);

            var parsed = ParseAvisOpere(text, result);
            if (parsed != null)
                result.Transactions.Add(parsed);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Erreur d'analyse PDF : {ex.Message}");
        }

        return Task.FromResult(result);
    }

    private static string ExtractText(string filePath)
    {
        using var pdf = PdfDocument.Open(filePath);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    private static ParsedTransaction? ParseAvisOpere(string text, ImportResult result)
    {
        // 1. Type d'opération
        TransactionType txType;
        if (text.Contains("ACHAT COMPTANT", StringComparison.OrdinalIgnoreCase))
            txType = TransactionType.Buy;
        else if (text.Contains("VENTE COMPTANT", StringComparison.OrdinalIgnoreCase))
            txType = TransactionType.Sell;
        else
        {
            result.Errors.Add("Type d'opération non détecté (ni ACHAT ni VENTE)");
            return null;
        }

        // 2. Type de compte
        AccountType accountType;
        if (Regex.IsMatch(text, @"Compte\s+PEA", RegexOptions.IgnoreCase))
            accountType = AccountType.BoursobankPEA;
        else if (Regex.IsMatch(text, @"Compte\s+(CTO|titres?\s+ordinaire)", RegexOptions.IgnoreCase))
            accountType = AccountType.BoursobankCTO;
        else
        {
            result.Warnings.Add("Type de compte non détecté, défaut PEA");
            accountType = AccountType.BoursobankPEA;
        }

        // 3. Référence -> ExternalId
        var refMatch = Regex.Match(text, @"Référence\s*:\s*(\d{8,})");
        if (!refMatch.Success)
        {
            result.Errors.Add("Référence introuvable");
            return null;
        }
        var externalId = $"BOURSOBANK-{refMatch.Groups[1].Value}";

        // 4. Date d'exécution
        var dateMatch = Regex.Match(text, @"(\d{2}/\d{2}/\d{4})\s+\d{2}:\d{2}:\d{2}");
        if (!dateMatch.Success)
            dateMatch = Regex.Match(text, @"le\s+(\d{2}/\d{2}/\d{4})");

        if (!dateMatch.Success)
        {
            result.Errors.Add("Date introuvable");
            return null;
        }
        var date = DateTime.SpecifyKind(
            DateTime.ParseExact(dateMatch.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture),
            DateTimeKind.Utc);

        // 5. ISIN — recherche contextuelle après le label "Code ISIN"
        var isinMatch = Regex.Match(text, @"Code\s*ISIN\s*[:\s]+([A-Z]{2}[A-Z0-9]{9}\d)", RegexOptions.IgnoreCase);
        if (!isinMatch.Success)
        {
            // Fallback : recherche d'un ISIN n'importe où (sans word boundary)
            isinMatch = Regex.Match(text, @"([A-Z]{2}[A-Z0-9]{9}\d)");
        }
        if (!isinMatch.Success)
        {
            result.Errors.Add("ISIN introuvable");
            result.Warnings.Add($"Texte extrait (300 premiers chars) : {text.Substring(0, Math.Min(300, text.Length))}");
            return null;
        }
        var isin = isinMatch.Groups[1].Value;

        // 6. Cours exécuté
        var coursMatch = Regex.Match(text, @"Cours\s+exécuté\s*:\s*([\d\s,\.]+)\s*EUR", RegexOptions.IgnoreCase);
        if (!coursMatch.Success)
        {
            result.Errors.Add("Cours exécuté introuvable");
            return null;
        }
        var unitPrice = ParseFrenchDecimal(coursMatch.Groups[1].Value);

        // 7. Les montants du tableau (brut, commission, [frais TTF], net)
        // Le label final varie : "au débit" pour un achat, "au crédit" pour une vente.
        var headerMatch = Regex.Match(text,
            @"Montant\s+net\s+au\s+(débit|crédit)\s+de\s+votre\s+compte",
            RegexOptions.IgnoreCase);

        if (!headerMatch.Success)
        {
            result.Errors.Add("Header du tableau de montants introuvable");
            return null;
        }

        var tableContent = text.Substring(headerMatch.Index + headerMatch.Length);
        var amounts = Regex.Matches(tableContent, @"([\d\s\u00A0]+(?:[,\.]\d+)?)\s*EUR")
            .Select(m => ParseFrenchDecimal(m.Groups[1].Value))
            .Take(4)
            .ToList();

        decimal brut, commission, fraisTtf, net;

        if (amounts.Count == 4)
        {
            brut = amounts[0];
            commission = amounts[1];
            fraisTtf = amounts[2];
            net = amounts[3];
        }
        else if (amounts.Count == 3)
        {
            brut = amounts[0];
            commission = amounts[1];
            fraisTtf = 0m;
            net = amounts[2];
        }
        else
        {
            result.Errors.Add($"Nombre de montants inattendu : {amounts.Count} (3 ou 4 attendus)");
            return null;
        }

        // 8. Calcul de la quantité
        var quantity = Math.Round(brut / unitPrice, 8);

        // 9. Vérification de cohérence (différente selon achat ou vente)
        var totalFees = commission + fraisTtf;
        var expected = txType == TransactionType.Buy
            ? brut + totalFees    // Achat : net = brut + frais (tu payes plus)
            : brut - totalFees;   // Vente : net = brut - frais (tu reçois moins)

        if (Math.Abs(expected - net) > 0.05m)
        {
            result.Warnings.Add($"Incohérence montants : attendu {expected:F2}, net {net:F2}");
        }

        // 10. Nom du titre (best-effort, pour RawData uniquement)
        var nameMatch = Regex.Match(text, @"\d{2}:\d{2}:\d{2}\s*\d+\s*([A-Z][A-Z0-9\s\-&\.]+?)\s*Référence", RegexOptions.Singleline);
        var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : isin;

        return new ParsedTransaction
        {
            Date = date,
            Type = txType,
            AccountType = accountType,
            AssetSymbol = isin,                        // ISIN comme symbole pour l'instant
            AssetType = AssetType.Stock,               // simplification : tout en Stock pour le PEA
            Quantity = quantity,
            UnitPrice = unitPrice,
            Fees = commission + fraisTtf,
            ExternalId = externalId,
            RawData = $"{name} | ISIN: {isin}"
        };
    }

    private static decimal ParseFrenchDecimal(string s)
    {
        s = s.Replace(" ", "").Replace("\u00A0", "").Replace(",", ".");
        return decimal.Parse(s, CultureInfo.InvariantCulture);
    }
}
