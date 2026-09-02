namespace HBA.Controls.Controles;

/// <summary>
/// Un seul nom de chaîne de connexion : « Default ».
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE QUI ÉTAIT CASSÉ.
///
/// Le compose ne renseigne que `CONNECTIONSTRINGS__DEFAULT`. Un module hérité du
/// monolithe demandait encore « Marketplace » — un nom qui avait survécu au
/// déménagement. Le service compilait, démarrait, puis levait sur une clé que
/// personne n'avait eu l'intention de fournir. C'était le dernier des dix-huit.
///
/// CE CONTRÔLE VIVAIT DANS `check-all.sh`, en trois lignes de `grep`, « tenu ici
/// parce qu'il tient en trois lignes ». C'était la dernière logique de contrôle
/// restée dans le script : la déplacer ici permet à `check-all.sh` de n'être
/// plus qu'un appel, et à ce contrôle d'être appelable seul comme les autres.
///
/// CE QU'IL NE COUVRE PAS.
///
///   • les chaînes obtenues autrement que par `GetConnectionString("…")` —
///     `Configuration["ConnectionStrings:X"]` passe au travers ;
///   • le nom construit dynamiquement : seul le littéral est lu ;
///   • `shared/` et `apps/` : le contrôle d'origine ne regardait que
///     `services/`, et ce port garde ce périmètre plutôt que de l'élargir en
///     silence — un élargissement change ce que « 0 faute » veut dire.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ChainesConnexionControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "chaines-connexion";

    /// <inheritdoc/>
    public string Resume => "tous les installeurs lisent la chaîne « Default »";

    private const string Attendu = "GetConnectionString(\"Default\")";

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fautes = new List<string>();
        var lus = 0;

        foreach (var fichier in Depot.Fichiers(Depot.Dossier("services"), ".cs"))
        {
            lus++;
            var lignes = File.ReadAllLines(fichier);
            for (var i = 0; i < lignes.Length; i++)
            {
                if (!lignes[i].Contains("GetConnectionString(\"", StringComparison.Ordinal)
                    || lignes[i].Contains(Attendu, StringComparison.Ordinal))
                {
                    continue;
                }

                fautes.Add($"{Depot.Relatif(fichier)}:{i + 1} — {lignes[i].Trim()}");
            }
        }

        return new Verdict(
            fautes,
            fautes.Count == 0
                ? [$"{lus} fichier(s) .cs lus dans services/ — tous les installeurs lisent « Default »."]
                : [$"{lus} fichier(s) .cs lus dans services/."],
            [
                "les chaînes obtenues autrement que par `GetConnectionString(\"…\")`, "
                + "et les noms construits dynamiquement",
                "`shared/` et `apps/` : le périmètre est celui du contrôle d'origine, "
                + "l'élargir changerait ce que « 0 faute » veut dire",
            ]);
    }
}
