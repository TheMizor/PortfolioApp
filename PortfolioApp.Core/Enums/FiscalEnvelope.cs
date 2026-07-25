namespace PortfolioApp.Core.Enums;

/// <summary>
/// Nature fiscale d'un compte. Détermine QUAND un mouvement devient
/// un fait générateur d'imposition.
/// </summary>
public enum FiscalEnvelope
{
    None,     // compte de paiement / trésorerie — hors champ
    PEA,      // fait générateur = sortie de l'enveloppe (retrait d'espèces)
    CTO,      // fait générateur = chaque cession de titre
    Crypto    // fait générateur = cession contre monnaie légale, bien ou service
}

public static class AccountTypeExtensions
{
    public static FiscalEnvelope ToEnvelope(this AccountType type) => type switch
    {
        AccountType.BoursobankPEA => FiscalEnvelope.PEA,
        AccountType.BoursobankCTO => FiscalEnvelope.CTO,
        AccountType.Bitstack      => FiscalEnvelope.Crypto,
        AccountType.Bybit         => FiscalEnvelope.Crypto,
        AccountType.Ledger        => FiscalEnvelope.Crypto,
        _ => FiscalEnvelope.None
    };
}
