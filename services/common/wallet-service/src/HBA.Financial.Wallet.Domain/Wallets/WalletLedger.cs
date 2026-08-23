using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>
/// Invariant comptable du §10.13 : dans une opération, la somme des débits égale
/// la somme des crédits.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CET INVARIANT MÉRITE D'ÊTRE ÉCRIT, ET PAS SEULEMENT ESPÉRÉ.
///
/// Une comptabilité déséquilibrée ne se signale pas. Elle produit des soldes qui
/// « ont l'air » corrects pendant des mois, jusqu'au jour où quelqu'un additionne
/// les mouvements et trouve autre chose que le solde stocké. À ce moment-là, il
/// est trop tard pour savoir QUELLE écriture manquait : il faut rejouer tout
/// l'historique, en devinant.
///
/// Vérifier à l'écriture transforme une dérive silencieuse et cumulative en un
/// échec immédiat, sur l'opération fautive, avec son identifiant.
///
/// PAR DEVISE, ET C'EST ESSENTIEL.
///
/// Additionner 5 000 XOF et 8 EUR donnerait 5 008 d'une unité qui n'existe pas.
/// Un déséquilibre réel pourrait alors se compenser entre deux devises et passer
/// inaperçu — exactement le cas que cet invariant existe pour attraper.
///
/// IL A ÉTÉ ÉCRIT, TESTÉ, ET N'A JAMAIS ÉTÉ APPELÉ (ISSUE-051).
///
/// Et ce n'était pas un oubli : aucun site NE POUVAIT l'appeler. La partie double
/// suppose que chaque mouvement ait sa contrepartie, et la contrepartie n'était
/// modélisée nulle part. Une confirmation de commande n'écrivait que des CRÉDITS
/// — net vendeur, commission, frais provider, frais de port — parce que l'argent
/// venait de la carte de l'acheteur, qui n'était pas un compte. Un remboursement
/// n'écrivait que des DÉBITS. Appliquer l'invariant à ces opérations aurait
/// échoué, à juste titre.
///
/// `WalletOwnerType.External` est cette contrepartie — lire son encadré. Depuis
/// qu'elle existe, l'invariant est appelé pour de bon, là où il MORD réellement.
///
/// « OÙ IL MORD » N'EST PAS UNE FORMULE : C'EST LA SEULE CHOSE QUI COMPTE ICI.
///
/// Un invariant dont les deux membres sortent de la même variable ne peut pas
/// échouer. Écrire « je crédite 1 000 au vendeur, donc je débite 1 000 à
/// l'extérieur » ne vérifie rien du tout — cela fabrique un garde-fou qui passe
/// toujours, c'est-à-dire le pire des deux mondes : le coût d'un contrôle, sans le
/// contrôle.
///
/// L'opération convertie est LA CONFIRMATION DE COMMANDE, marchandise et
/// restauration : le brut encaissé d'un côté, sa répartition en net vendeur +
/// commission + frais provider de l'autre, plus les frais de port des deux côtés.
/// Les deux membres ne partagent aucune variable. L'invariant vérifie donc que la
/// répartition d'une vente est EXHAUSTIVE, et attrape une composante oubliée ou un
/// arrondi qui dérive — au moment exact où l'écriture se fait, sur la commande
/// fautive.
///
/// Les autres mouvements — un reversement, une retenue de virement, un crédit de
/// remboursement — sont tautologiques par construction : un seul montant, deux
/// écritures. Les grouper ferait passer l'invariant à tous les coups. Ce serait le
/// pire des deux mondes : le coût d'un contrôle, sans le contrôle.
///
/// LA CONTRE-PASSATION D'UN RETOUR N'EST PAS SOUS L'INVARIANT, ET IL FAUT DIRE
/// POURQUOI.
///
/// Elle en aurait la forme — brut rendu d'un côté, reprise du net + restitution de
/// commission et de frais de l'autre — et elle échouerait pourtant sur des cas
/// LÉGITIMES. Deux raisons, toutes deux réelles :
///
///   • `SellerEarning.Reverse` borne les quatre montants SÉPARÉMENT, chacun à son
///     propre reliquat. Sur une seconde reprise partielle, le brut inscrit cesse
///     d'être égal à net + commission + frais. C'est un choix défendable du
///     domaine, pas un défaut — mais il rend l'égalité fausse ;
///   • `RefundAmount` inclut parfois les frais de livraison, qui ne sont pas du
///     revenu vendeur. Le handler plafonne son prorata à 1 pour cette raison, et
///     la part « port » du remboursement n'a aujourd'hui aucune écriture en
///     regard.
///
/// Poser l'invariant là-dessus ferait échouer un retour parfaitement normal et le
/// mettrait en lettre morte. Ce serait remplacer une dérive silencieuse par une
/// panne — un plus mauvais échange. Ce qu'il faudrait d'abord : que la reprise soit
/// bornée sur le BRUT et les trois autres déduits, et que la part « port » d'un
/// remboursement sorte du compte « livraison ». Les deux sont du modèle de domaine
/// et ne s'improvisent pas ici.
///
/// CE QUE L'INVARIANT NE DIT PAS.
///
/// Il garantit la cohérence INTERNE d'une opération. Il ne dit pas que le montant
/// est le bon, ni qu'il correspond à ce que le prestataire a réellement encaissé :
/// aucun rapprochement bancaire n'existe. Il transforme une dérive silencieuse en
/// un échec immédiat sur l'opération fautive — ce qui manquait — et rien de plus.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class WalletLedger
{
    /// <summary>
    /// Vérifie qu'un ensemble d'écritures formant UNE opération s'équilibre.
    /// Un ensemble vide est valide : une opération sans mouvement n'est pas une faute.
    /// </summary>
    public static Result EnsureBalanced(IReadOnlyCollection<WalletTransaction> entries)
    {
        if (entries.Count == 0)
        {
            return Result.Success();
        }

        var transactions = entries.Select(e => e.TransactionId).Distinct().ToList();

        if (transactions.Count > 1)
        {
            return Result.Failure(Error.Validation(
                "wallet.ledger.mixed_transactions",
                "Ces écritures n'appartiennent pas à la même opération : "
                + $"{transactions.Count} identifiants distincts."));
        }

        foreach (var parDevise in entries.GroupBy(e => e.Currency, StringComparer.OrdinalIgnoreCase))
        {
            var credits = parDevise.Where(e => e.Direction == WalletDirection.Credit).Sum(e => e.Amount);
            var debits = parDevise.Where(e => e.Direction == WalletDirection.Debit).Sum(e => e.Amount);

            if (credits != debits)
            {
                return Result.Failure(Error.Validation(
                    "wallet.ledger.unbalanced",
                    $"Opération {transactions[0]} déséquilibrée en {parDevise.Key} : "
                    + $"{credits} au crédit contre {debits} au débit."));
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Identifiant d'une nouvelle opération. Exposé pour que le site appelant le
    /// tire UNE fois et le passe à chaque écriture — c'est ce partage qui fait
    /// l'opération.
    /// </summary>
    public static Guid NewTransactionId() => Guid.NewGuid();
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// TRADUIT UNE CLÉ D'IDEMPOTENCE TEXTUELLE EN LA RÉFÉRENCE `Guid` QUE LE GRAND
/// LIVRE SAIT INDEXER.
///
/// POURQUOI CETTE TRADUCTION EXISTE, ET CE QU'ELLE N'EST PAS.
///
/// `WalletTransaction.ReferenceId` est un `Guid` : c'est ce que le registre
/// d'idempotence (`ExistsForReferenceAsync`, et les index uniques partiels de
/// `wallet_transactions`) sait comparer. Or la clé qui protège un crédit de
/// remboursement client vient de l'appelant sous forme de TEXTE — l'en-tête
/// `Idempotency-Key` du §5, un jeton opaque.
///
/// Il n'y avait que trois issues : ajouter une colonne texte au grand livre et
/// dupliquer le mécanisme d'unicité qui existe déjà ; INVENTER une référence
/// (l'identifiant du remboursement, la commande…), ce que tout ce module refuse
/// explicitement ailleurs ; ou projeter la clé de l'appelant dans l'espace des
/// `Guid`, sans rien inventer. C'est la troisième.
///
/// CE N'EST PAS UNE CLÉ FABRIQUÉE : la clé reste celle de l'appelant. On n'en
/// dérive PAS une à partir du montant ou de la commande — ce repli, refusé dans
/// `CustomerRefund.IdempotencyKey`, interdirait un second remboursement partiel
/// légitime tout en laissant passer un rejeu dès qu'un franc change.
///
/// LA PORTÉE INCLUT LE PROPRIÉTAIRE, ET C'EST INDISPENSABLE.
///
/// Le jeton est choisi par l'appelant et n'embarque personne. Deux clients dont
/// les jetons se recoupent — un générateur mal amorcé, un client qui recycle —
/// verraient le second crédit pris pour un rejeu du premier : le second client ne
/// serait JAMAIS remboursé, en silence, et le grand livre montrerait une seule
/// écriture parfaitement cohérente. Le propriétaire entre donc dans le condensat.
/// C'est le même raisonnement que `(OrderId, IdempotencyKey)` sur
/// `customer_refunds`.
///
/// CE QUE ÇA NE COUVRE PAS : deux remboursements DIFFÉRENTS d'un même client
/// envoyés avec la MÊME clé sont, par définition d'une clé d'idempotence, traités
/// comme un seul. C'est le contrat du §5 et la responsabilité de l'appelant ; il
/// n'existe aucun moyen de distinguer ce cas d'un vrai rejeu.
///
/// SHA-256 tronqué à 128 bits : la collision entre deux couples distincts est
/// hors d'atteinte pratique, et l'échec éventuel serait un crédit refusé — pas un
/// crédit doublé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class WalletReference
{
    /// <summary>
    /// Référence stable pour le couple (propriétaire, clé d'idempotence). Le même
    /// couple rend TOUJOURS le même `Guid` — c'est ce qui fait qu'un rejeu retrouve
    /// l'écriture déjà passée au lieu d'en créer une seconde.
    /// </summary>
    public static Guid FromIdempotencyKey(Guid ownerId, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException(
                "Une référence d'idempotence ne se dérive pas d'une clé vide : l'appelant doit refuser avant.",
                nameof(idempotencyKey));
        }

        var graine = $"{ownerId:D}:{idempotencyKey.Trim()}";
        var condensat = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(graine));

        return new Guid(condensat.AsSpan(0, 16));
    }
}
