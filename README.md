# PortfolioApp

Agrégateur de positions patrimoniales personnel. Réunit crypto, actions et ETF
dans une base unique, à partir des exports bruts des plateformes, avec pour
objectif la *fiscalité-readiness* : être capable, le jour où un événement
imposable survient, de produire les bons chiffres sans rien reconstituer.

Application de bureau Windows, mono-utilisateur, données strictement locales.

---

## Architecture

Trois projets, dépendances unidirectionnelles :

```
PortfolioApp.Desktop  (net10.0-windows, WPF)
        │
        ▼
PortfolioApp.Infrastructure  (net10.0)
        │
        ▼
PortfolioApp.Core  (net10.0)
```

| Projet | Rôle | Ne connaît pas |
|---|---|---|
| **Core** | Entités, enums, modèles de projection, interfaces, règles fiscales pures | EF Core, WPF, le système de fichiers |
| **Infrastructure** | `DbContext`, migrations, parsers, services d'import et de calcul | WPF |
| **Desktop** | Fenêtres WPF, code-behind | — |

`Core` ne référence aucun paquet externe : c'est du C# nu. `TaxRules` y vit
justement parce que c'est une fonction pure, testable sans base ni UI.

### Stack

| Composant | Version |
|---|---|
| .NET | 10 |
| Entity Framework Core + SQLite | 10.0.10 |
| CsvHelper | 33.1.0 |
| PdfPig | 0.1.15 |

---

## Modèle de données

### `Account`

| Champ | Rôle |
|---|---|
| `Name`, `Type` | Identité du compte (`Bitstack`, `BoursobankPEA`, `BoursobankCTO`, `Bybit`, `Ledger`) |
| `Currency` | Devise de tenue, EUR partout |
| `Envelope` | **Dérivé, non persisté.** Nature fiscale : `PEA`, `CTO`, `Crypto`, `None` |
| `OpeningDate` | Date d'ouverture — seuil des 5 ans du PEA |

`Name` porte un index unique : un seul compte par type.

### `Asset`

`Symbol` (index unique), `Name`, `Type` (`Crypto`, `Stock`, `ETF`, `Cash`),
`Isin`, `QuoteCurrency`.

Pour les titres Boursobank, l'ISIN sert de symbole.

### `Transaction`

Le cœur. Une ligne = un mouvement élémentaire.

| Champ | Rôle |
|---|---|
| `Date`, `Type`, `Quantity`, `UnitPrice`, `Fees` | L'opération |
| `ExternalId` | **Index unique** — clé de déduplication |
| `SourceFile` | Fichier d'origine, ou `manual entry` / `transfer` |
| `RawData` | Note libre / trace de l'origine |
| `Confidence` | `Verified` (import auto), `Documented` (saisie justifiée), `Estimated` (PRU estimé) |
| `CounterAssetSymbol` | Contrepartie : `EUR`, `USDT`… `null` pour un dépôt ou un transfert |

Précision décimale `(28, 8)` sur les montants — suffisant pour les 8 décimales
du BTC.

`TransactionType` : `Buy`, `Sell`, `Fee`, `Deposit`, `Withdrawal`, `Transfer`.

### `PriceQuote`

Entité et table présentes, **jamais alimentées ni lues**. Prévue pour
l'historisation des cours, qui n'existe pas encore.

### Emplacement de la base

```
C:\Users\simon\Documents\fiscalité\_portfolio_app\portfolio.db
```

Chemin codé en dur dans `DatabaseConfig`. Le dossier est créé au besoin.
Les préférences (dernier dossier scanné) vivent ailleurs, dans
`%LOCALAPPDATA%\PortfolioApp\settings.json`.

---

## Comment ça marche

### Import

Chaque source implémente `IPositionProvider` : elle lit un fichier et renvoie
une liste de `ParsedTransaction` plus des avertissements non bloquants.

**`BitstackCsvProvider`** — CSV mensuel Bitstack, via CsvHelper.

| Ligne du CSV | Traitement |
|---|---|
| `Échange` (DCA, achat par carte) | `Buy` BTC, contrepartie `EUR` |
| `Dépôt` crypto (cadeau, cashback) | `Deposit` au prix marché du jour |
| `Dépôt` EUR (recharge du compte) | **ignoré** |
| `Retrait` crypto | `Withdrawal`, frais convertis BTC → EUR |
| `Retrait` EUR (paiement carte) | **ignoré** |

Les mouvements EUR sont volontairement écartés : la carte Bitstack sert de moyen
de paiement, son solde n'a aucune portée patrimoniale ni fiscale.

**`BoursobankPdfProvider`** — avis d'opéré PDF, via PdfPig. Détecte achat ou
vente, l'enveloppe (PEA ou CTO), l'ISIN par recherche contextuelle, et s'adapte
à la présence ou non de TTF selon le titre. Les relevés de compte, d'espèces et
de titres ne sont pas reconnus : ils restent dans le dossier comme pièces
justificatives.

**`ImportService`** orchestre : déduplication par `ExternalId`, création à la
volée des comptes et actifs manquants, persistance. Tout ce qui passe par là est
marqué `Verified`.

Il génère aussi les **miroirs Ledger** : tout retrait crypto produit
automatiquement un `Deposit` équivalent sur le compte `Ledger`, au même prix
unitaire — hypothèse du passage en cold storage, éditable si la destination
était autre.

**`FolderScanService`** parcourt un dossier racine, ne descend que dans les
sous-dossiers connus (`BITSTACK`, `BOURSOBANK`), y cherche récursivement les
fichiers du bon type, et rend compte fichier par fichier : nouvelles lignes,
doublons, format non reconnu, dossiers ignorés.

L'approche est une **liste blanche** : tout ce qui n'est pas explicitement
reconnu est laissé tranquille. Le dossier peut donc contenir n'importe quoi
sans risque.

### Calcul des positions

`PositionService` ne lit aucune position stockée : il **rejoue l'historique**.
Les transactions sont groupées par couple (compte, actif) puis parcourues dans
l'ordre chronologique.

Convention **CMP** (coût moyen pondéré) :

- `Buy` et `Deposit` augmentent la quantité et recalculent le prix moyen, frais inclus
- `Sell` et `Withdrawal` consomment la quantité au prix moyen courant, sans le modifier
- Sous le seuil de 10⁻⁶, la position est remise à zéro

Conséquence pratique : corriger une transaction ancienne recalcule tout
correctement, sans migration ni recalcul à déclencher.

### Fiscalité

`TaxRules.IsTaxableEvent` répond à une seule question — *ce mouvement
déclenche-t-il l'impôt ?* — selon l'enveloppe du compte :

| Enveloppe | Fait générateur |
|---|---|
| PEA | Retrait d'espèces hors de l'enveloppe (pas la vente du titre) |
| CTO | Chaque cession de titre |
| Crypto | Cession contre monnaie légale (`EUR`, `USD`, `GBP`, `CHF`) |

`PeaMaturityDate` renvoie la date d'ouverture + 5 ans.

Fonction pure, aucune dépendance. **Écrite mais appelée nulle part.**

---

## Interface

Trois onglets.

**Imports** — un bouton principal « Tout importer » qui scanne le dossier
mémorisé, un bouton pour changer ce dossier, les imports fichier par fichier,
la saisie manuelle, le transfert entre comptes, et un journal détaillé.

**Positions** — grille compte / symbole / type / quantité / PRU / coût total /
frais cumulés / nombre de transactions. Case à cocher pour inclure les positions
soldées.

**Transactions** — grille complète, triable, avec édition et suppression.

Un overlay modal à barre indéterminée couvre chaque opération longue, toujours
retiré dans un `finally`.

### Saisie manuelle

Pour tout ce qu'aucun parser ne couvre — Bybit essentiellement. Date, compte,
type, symbole, quantité, prix unitaire, frais, note, fiabilité, contrepartie.
Aperçu du calcul en temps réel, séparateur décimal tolérant (point ou virgule).

La fiabilité ne propose que `Documented` et `Estimated` : `Verified` est réservé
aux imports. À l'édition d'une transaction importée, la liste s'élargit pour
préserver sa valeur.

### Transfert entre comptes

Génère deux transactions liées — un `Withdrawal` sur la source, un `Deposit` sur
la destination — au même PRU. Opération fiscalement neutre.

**À n'utiliser que pour ce qu'aucun fichier ne capture** : Bybit vers Ledger,
Ledger vers Bybit. Pour Bitstack vers Ledger, le CSV et le miroir automatique
s'en chargent déjà ; cliquer ici créerait un doublon.

---

## Organisation du dossier de données

```
fiscalité/
├── BITSTACK/            → scanné, CSV
│   ├── 2025/
│   └── 2026/
├── BOURSOBANK/          → scanné, PDF
│   └── 2026/
│       ├── ACHATS/
│       └── VENTES/
├── BYBIT/               → ignoré, conservé comme justificatifs
├── declaration .../     → ignoré
└── _portfolio_app/      → la base SQLite
```

Le dossier joue deux rôles : **source** pour l'application, **archive** pour
l'administration. Les deux ne se gênent pas.

## Usage courant

1. Déposer les nouveaux CSV Bitstack et PDF Boursobank dans les bons sous-dossiers
2. Ouvrir l'app, cliquer sur « Tout importer »
3. Saisir manuellement l'activité Bybit s'il y en a eu

Environ trente secondes par mois.

---

## État actuel

### Acquis

- Import automatique Bitstack : DCA, cadeaux, cashbacks, retraits, frais convertis
- Import automatique Boursobank : achats et ventes, PEA et CTO, plusieurs gabarits de PDF
- Miroirs Ledger générés automatiquement
- Déduplication fiable par identifiant externe : réimporter est sans risque
- Saisie manuelle, édition, suppression de n'importe quelle transaction
- Transfert inter-comptes à PRU conservé
- Positions recalculées à la volée, PRU au CMP
- Traçabilité par niveau de fiabilité
- Enveloppes fiscales et contrepartie des opérations
- Scan d'un dossier complet en un clic, dossier mémorisé

### Manques

| Manque | Conséquence | Prérequis |
|---|---|---|
| Aucune valorisation courante | On voit le PRU, jamais ce que ça vaut aujourd'hui | Source de prix |
| Pas d'historique de prix | Le calcul de plus-value crypto est impossible | Alimenter `PriceQuote` |
| Pas de cash intra-PEA | La règle PEA de `TaxRules` reste sans données | Générer les mouvements d'espèces à l'import |
| `TaxRules` non branchée | Aucun effet visible | Une colonne dans la vue Transactions |
| Stablecoins non suivis | Une vente BTC → USDT casse la chaîne de suivi | Traiter l'USDT comme un actif |
| Pas de calcul de plus-value | Rien à reporter dans une déclaration | Les deux premiers points |
| Bybit entièrement manuel | Saisie à la main | Un parser dédié, jugé non rentable |
| Relevés Boursobank non lus | Versements et dividendes invisibles | Un parser dédié |

### Dettes techniques

- **Projet orphelin** : `PortfolioApp/PortfolioApp.csproj` cible .NET Framework 4.7.2,
  vestige de l'assistant Visual Studio. Hors solution, inutilisé, à supprimer.
- **Chemin de base en dur** dans `DatabaseConfig` — bloque tout usage sur une autre machine.
- **`PriceQuote` mort** : table créée, jamais utilisée.
- **Tout en code-behind**, pas de MVVM ni d'injection de dépendances. Le `DbContext`
  est instancié à la main dans chaque handler. Acceptable à cette taille, pénible au-delà.
- **Aucun test automatisé.** `TaxRules` et `PositionService` sont pourtant des
  fonctions pures, faciles à couvrir.
- **`AccountType` figé** : ajouter une enveloppe impose une recompilation.
- La vue Transactions ne se rafraîchit pas seule après une saisie manuelle.

---

## Suite envisagée

Dans l'ordre, chaque étape débloquant la suivante :

1. **Cash intra-PEA** — générer les mouvements d'espèces à l'import des avis d'opéré.
   Réveille la règle PEA et rend visible où vont les euros après une vente.
2. **Historisation des prix** — alimenter `PriceQuote` par date. Débloque à la fois
   la valorisation courante et le calcul de plus-value.
3. **Calcul 150 VH bis** — plus-value crypto. Exige un prix d'acquisition **global**
   à tous actifs numériques confondus, la valorisation totale à la date de chaque
   cession, et le report des fractions déjà déduites. Ce n'est pas le PRU par
   position que calcule l'app aujourd'hui.
4. **Récapitulatif PDF** — format libre, pas un formulaire officiel. Juste les
   chiffres à recopier, archivés une fois l'an.

Aucun déclencheur à ce jour : aucune cession crypto, PEA ouvert début 2026 donc
loin du seuil des 5 ans.

---

## Commandes utiles

```powershell
# Migration
dotnet ef migrations add <Nom> --project PortfolioApp.Infrastructure --startup-project PortfolioApp.Desktop
dotnet ef database update --project PortfolioApp.Infrastructure --startup-project PortfolioApp.Desktop

# Binaire autonome
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o <dossier>
```

## Avertissement

Outil personnel. Les règles fiscales qu'il encode reflètent une compréhension du
droit français à un instant donné et changent à chaque loi de finances — le taux
du prélèvement forfaitaire unique est passé de 30 % à 31,4 % au 1ᵉʳ janvier 2026.
Aucun chiffre produit ici ne remplace une vérification sur impots.gouv.fr ou
l'avis d'un professionnel.
