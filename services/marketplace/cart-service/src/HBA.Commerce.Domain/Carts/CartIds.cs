namespace HBA.Commerce.Domain.Carts;

/// <summary>Identité forte d'un panier.</summary>
public readonly record struct CartId(Guid Value)
{
    public static CartId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Statut du panier.</summary>
public enum CartStatus
{
    Active = 0,
    CheckedOut = 1,

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ÉTAT INATTEIGNABLE : AUCUN CHEMIN DE CODE NE POSE `Abandoned` (lot 9.2).
    ///
    /// Aucun balayeur n'existe. Un panier reste `Active` indéfiniment — et depuis
    /// que `ux_carts_active_buyer` (lot 8.2) impose UN SEUL panier actif par
    /// acheteur, ce panier-là est le sien POUR TOUJOURS : il ne s'abandonne pas,
    /// il se vide ou il se solde.
    ///
    /// LA VALEUR EST CONSERVÉE, ET C'EST DÉLIBÉRÉ. La retirer effacerait le
    /// seul vocabulaire dont disposera le balayeur le jour où il sera écrit, et
    /// ferait croire au lecteur suivant que l'abandon n'a jamais été prévu. Ce
    /// n'est pas du bruit : c'est une fonction déclarée et non construite.
    ///
    /// Le statut est stocké en TEXTE : conserver la valeur ne coûte rien en base.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    Abandoned = 2
}
