using PortfolioApp.Core.Entities;
using PortfolioApp.Core.Enums;

namespace PortfolioApp.Core.Fiscal;

/// <summary>
/// Détermine si une transaction constitue un fait générateur d'imposition.
/// Fonction pure : aucune dépendance, aucun accès base, testable isolément.
/// Ne calcule PAS le montant imposable, seulement le déclenchement.
/// </summary>
public static class TaxRules
{
    private static readonly HashSet<string> LegalTender =
        new(StringComparer.OrdinalIgnoreCase) { "EUR", "USD", "GBP", "CHF" };

    /// <summary>
    /// La transaction doit avoir ses navigations Account et Asset chargées.
    /// </summary>
    public static bool IsTaxableEvent(Transaction tx) => tx.Account.Type.ToEnvelope() switch
    {
        // PEA : rien n'est imposable tant que les liquidités restent dans l'enveloppe.
        // Le fait générateur est le retrait d'espèces, pas la vente du titre.
        FiscalEnvelope.PEA => tx.Type == TransactionType.Withdrawal
                              && tx.Asset.Type == AssetType.Cash,

        // CTO : chaque cession de titre est imposable immédiatement.
        FiscalEnvelope.CTO => tx.Type == TransactionType.Sell,

        // Crypto : seule une cession contre monnaie légale (ou bien / service).
        // Un échange contre un stablecoin n'est pas un fait générateur.
        FiscalEnvelope.Crypto => tx.Type == TransactionType.Sell
                                 && tx.CounterAssetSymbol is not null
                                 && LegalTender.Contains(tx.CounterAssetSymbol),

        _ => false
    };

    /// <summary>
    /// Date à partir de laquelle un retrait PEA échappe à l'impôt sur le revenu
    /// (les prélèvements sociaux restent dus). Null si le compte n'est pas un PEA
    /// ou si la date d'ouverture n'est pas renseignée.
    /// </summary>
    public static DateTime? PeaMaturityDate(Account account) =>
        account.Type.ToEnvelope() == FiscalEnvelope.PEA && account.OpeningDate.HasValue
            ? account.OpeningDate.Value.AddYears(5)
            : null;
}
