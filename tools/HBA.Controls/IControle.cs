namespace HBA.Controls;

/// <summary>Le résultat d'un contrôle : des constats, et de quoi les situer.</summary>
/// <param name="Fautes">
/// Ce qui doit faire échouer la barrière. Vide = le contrôle est passé.
/// </param>
/// <param name="Constats">
/// Ce qui mérite d'être lu sans faire échouer — un compte, un périmètre.
/// </param>
/// <param name="NonCouvert">
/// CE QUE LE CONTRÔLE N'A PAS REGARDÉ, et qui doit être dit à voix haute.
/// Un outil absent, une partie sautée : sans cette liste, un contrôle qui
/// s'ignore rend le même vert qu'un contrôle qui a tout vu.
/// </param>
public sealed record Verdict(
    IReadOnlyList<string> Fautes,
    IReadOnlyList<string> Constats,
    IReadOnlyList<string> NonCouvert)
{
    /// <summary>Un verdict sans faute.</summary>
    public static Verdict Sain(params string[] constats)
        => new([], constats, []);
}

/// <summary>
/// Un contrôle statique du dépôt.
/// </summary>
/// <remarks>
/// UN CONTRÔLE N'IMPRIME RIEN. Il rend un <see cref="Verdict"/>, et c'est le
/// lanceur qui met en forme. C'est ce qui permet de l'appeler depuis un test :
/// un contrôle qui écrit sur la sortie standard ne se vérifie qu'à l'œil, et
/// c'est ainsi qu'on obtient des contrôles qui ne contrôlent rien.
/// </remarks>
public interface IControle
{
    /// <summary>Nom court, celui qu'on tape en ligne de commande.</summary>
    string Nom { get; }

    /// <summary>Une ligne : ce que ce contrôle empêche.</summary>
    string Resume { get; }

    /// <summary>Exécute le contrôle. Ne doit rien imprimer.</summary>
    Verdict Executer();
}
