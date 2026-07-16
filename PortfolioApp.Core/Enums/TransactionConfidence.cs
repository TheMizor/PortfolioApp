namespace PortfolioApp.Core.Enums;

public enum TransactionConfidence
{
    Verified,      // Import auto (Bitstack CSV, Boursobank PDF)
    Documented,    // Saisie manuelle avec justificatif
    Estimated      // Saisie manuelle sans justificatif solide
}