using System.Globalization;
using Grpc.Core;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>
/// L'argent qui traverse gRPC en TEXTE : écriture et lecture, au même endroit.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE DÉPÔT A TROIS REPRÉSENTATIONS DE L'ARGENT, PAS DEUX (D39).
///
/// L'audit en comptait deux — `numeric(18,2)` d'un côté, `bigint` de l'autre. Il
/// en manquait une, et c'est celle qui a des angles vifs : une CINQUANTAINE de
/// champs monétaires des contrats gRPC sont des `string`.
///
///   1. `decimal` / `numeric(18,2)` — le cœur marchandise, repas et financier ;
///   2. `long` / `bigint` — promotions et delivery_pricing, en francs entiers,
///      choix argumenté (le franc CFA n'a pas de sous-unité) ;
///   3. `string` — sur le fil gRPC, un décimal formaté en texte.
///
/// La troisième n'est pas un défaut en soi : protobuf n'a pas de type décimal, et
/// `double` serait pire — un binaire flottant ne représente pas exactement 0,1.
/// Le texte en `InvariantCulture` est le choix correct. Ce qui l'était moins,
/// c'est ce que faisait la LECTURE.
///
/// HUIT LECTEURS, SEPT QUI RENDAIENT ZÉRO SANS RIEN DIRE.
///
/// Sept des huit fonctions de lecture du dépôt s'écrivaient
/// `TryParse(…) ? valeur : 0m`. Seule celle de `FinancialGrpcService` refusait.
///
/// ET « ZÉRO » N'EST PAS UNE VALEUR NEUTRE POUR DE L'ARGENT.
///
/// Un champ `string` de protobuf 3 vaut la chaîne VIDE quand l'émetteur ne le
/// pose pas — il n'y a pas de « non renseigné ». Un chemin de code qui oublie une
/// affectation, un producteur plus ancien qui ne connaît pas un champ ajouté
/// (règle additive D32) : dans les deux cas le lecteur recevait `""` et lisait
/// « zéro franc ».
///
/// Le cas le plus cher est démontrable. `ReturnLifecycleCommands` calcule
/// `plafondCommande = CapturedAmount − AlreadyRefundedAmount`. Un zéro silencieux
/// sur `AlreadyRefundedAmount` REMONTE le plafond de remboursement — c'est
/// exactement le défaut qu'ISSUE-014 a fermé, et le texte de ce fichier-là le dit
/// encore : « `AlreadyRefundedAmount: 0m` EN DUR : le plafond ignorait purement et
/// simplement ce qui avait déjà été rendu ». La représentation en texte pouvait
/// le rouvrir, sans qu'une ligne de code fautive n'apparaisse nulle part.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class MontantSurLeFil
{
    /// <summary>
    /// Un montant vers le fil.
    /// </summary>
    /// <remarks>
    /// `InvariantCulture` OBLIGATOIRE, ET CE N'EST PAS DE LA PRUDENCE
    /// D'ÉCOLE. Un conteneur dont la locale est française écrit « 1234,50 » avec
    /// `ToString()` nu. Le lecteur, lui, analyse en invariant : il y verrait
    /// 123 450, ou rien. Les deux services compilent, aucun test unitaire ne
    /// change, et l'écart n'apparaît qu'en production, sur une image dont
    /// quelqu'un a réglé la locale.
    /// </remarks>
    public static string Ecrire(decimal montant)
        => montant.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Un montant venu du fil, qui DOIT être présent.
    /// </summary>
    /// <remarks>
    /// REFUSE PLUTÔT QUE DE RENDRE ZÉRO. `InvalidArgument` est le statut juste :
    /// l'émetteur a envoyé quelque chose d'inexploitable, ce n'est pas une panne
    /// et le disjoncteur ne doit pas le compter — voir
    /// <see cref="DisjoncteurClientInterceptor"/>.
    ///
    /// ET IL FAUT LE NOM DU CHAMP. « montant invalide » sur un message qui en
    /// porte douze n'apprend rien à qui lit le journal à deux heures du matin.
    /// </remarks>
    public static decimal Lire(string? valeur, string champ)
        => Analyser(valeur) ?? throw new RpcException(new Status(
            StatusCode.InvalidArgument,
            $"« {champ} » n'est pas un montant exploitable."));

    /// <summary>
    /// Un montant venu du fil qui peut légitimement être ABSENT.
    /// </summary>
    /// <remarks>
    /// ABSENT ET INVALIDE NE SONT PAS LA MÊME CHOSE, ET C'EST TOUT L'INTÉRÊT.
    ///
    /// Vide rend <c>null</c> — le prix promotionnel d'un produit qui n'en a pas.
    /// Une chaîne PRÉSENTE mais illisible est refusée : « douze mille » n'est pas
    /// un prix absent, c'est un prix cassé, et le confondre avec l'absence est
    /// précisément ce qui rendait zéro.
    /// </remarks>
    public static decimal? LireOuAbsent(string? valeur, string champ)
        => string.IsNullOrWhiteSpace(valeur)
            ? null
            : Analyser(valeur) ?? throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"« {champ} » n'est pas un montant exploitable."));

    /// <remarks>
    /// `NumberStyles.Number` ET NON `Any`. `Any` accepte les symboles
    /// monétaires et la notation exponentielle : « 1E3 » deviendrait mille francs,
    /// et « $12 » douze. Deux des huit lecteurs du dépôt utilisaient `Any` — sans
    /// raison, et sans que personne n'ait choisi ces tolérances.
    /// </remarks>
    private static decimal? Analyser(string? valeur)
        => decimal.TryParse(valeur, NumberStyles.Number, CultureInfo.InvariantCulture, out var montant)
            ? montant
            : null;
}
