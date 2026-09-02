namespace HBA.Controls;

/// <summary>
/// Le peu de C# que les contrôles doivent savoir lire.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE TYPE EXISTE PARCE QUE DEUX CONTRÔLES AVAIENT BESOIN DU MÊME DÉCOMPTE, ET
/// QUE DEUX COPIES AURAIENT DIVERGÉ.
///
/// Le contrôle des permissions retire les commentaires pour ne pas prendre un
/// code de permission cité dans un encadré pour une garde réelle. Celui des
/// implémentations les retire pour ne pas rater une méthode dont la signature
/// est séparée de son corps par un commentaire — ce qui lui a valu DIX-NEUF faux
/// positifs à sa première exécution, sur du code qui compilait parfaitement.
///
/// Le second défaut est la preuve du premier : la même lecture naïve, faite deux
/// fois, se trompe deux fois. Elle est donc écrite une fois.
///
/// CE N'EST PAS UN ANALYSEUR C#, ET IL NE FAUT PAS LE PRENDRE POUR TEL. Il sait
/// distinguer une chaîne d'un commentaire, et rien de plus. Tout contrôle qui
/// s'appuie dessus doit dire, dans son propre en-tête, ce que sa lecture ne voit
/// pas.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class SourceCsharp
{
    /// <summary>
    /// Retire commentaires de ligne et de bloc, en respectant les chaînes.
    /// </summary>
    /// <remarks>
    /// UN REMPLACEMENT OU UNE EXPRESSION RÉGULIÈRE NE SUFFIT PAS ICI. Une chaîne
    /// peut contenir <c>//</c> (une URL, un chemin) et un commentaire peut
    /// contenir un guillemet. Il faut donc suivre l'état du lecteur caractère
    /// par caractère.
    ///
    /// LES CHAÎNES SONT CONSERVÉES : ce sont elles que l'on cherche.
    ///
    /// CE QUI N'EST PAS COUVERT : les chaînes interpolées <c>$"…{expr}…"</c> sont
    /// traitées comme des chaînes ordinaires, donc un <c>//</c> à l'intérieur
    /// d'une interpolation serait pris pour du texte. Aucun code de permission
    /// ne s'écrit de cette façon, et le faire serait déjà l'anomalie que ces
    /// contrôles refusent.
    /// </remarks>
    public static string SansCommentaires(string source)
    {
        var sortie = new System.Text.StringBuilder(source.Length);
        var i = 0;
        var n = source.Length;

        while (i < n)
        {
            var c = source[i];

            if (c == '"')
            {
                // Chaîne textuelle @"…" : le seul échappement est "".
                var verbatim = i > 0 && source[i - 1] == '@';
                sortie.Append(c);
                i++;
                while (i < n)
                {
                    if (verbatim)
                    {
                        if (source[i] == '"')
                        {
                            if (i + 1 < n && source[i + 1] == '"')
                            {
                                sortie.Append("\"\"");
                                i += 2;
                                continue;
                            }

                            break;
                        }
                    }
                    else
                    {
                        if (source[i] == '\\' && i + 1 < n)
                        {
                            sortie.Append(source[i]).Append(source[i + 1]);
                            i += 2;
                            continue;
                        }

                        if (source[i] == '"')
                        {
                            break;
                        }

                        // Chaîne non terminée : on abandonne l'état plutôt que
                        // d'avaler le reste du fichier.
                        if (source[i] == '\n')
                        {
                            break;
                        }
                    }

                    sortie.Append(source[i]);
                    i++;
                }

                if (i < n)
                {
                    sortie.Append(source[i]);
                    i++;
                }

                continue;
            }

            if (c == '\'')
            {
                sortie.Append(c);
                i++;
                while (i < n && source[i] != '\'')
                {
                    if (source[i] == '\\')
                    {
                        sortie.Append(source[i]);
                        i++;
                    }

                    if (i < n)
                    {
                        sortie.Append(source[i]);
                        i++;
                    }
                }

                if (i < n)
                {
                    sortie.Append(source[i]);
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                while (i < n && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < n && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    i++;
                }

                i += 2;
                continue;
            }

            sortie.Append(c);
            i++;
        }

        return sortie.ToString();
    }

    /// <summary>
    /// Tous les fichiers `.cs` du code source du dépôt.
    /// </summary>
    /// <remarks>
    /// LE CODE VIT SOUS `services/`, `shared/` ET `apps/` — jamais sous `src/`.
    /// Quatre contrôles Python balayaient `&lt;dépôt&gt;/src`, qui n'a jamais
    /// existé ici, et rendaient « 0 anomalie » sans avoir rien regardé.
    /// <see cref="Depot.Dossier"/> lève si une racine manque.
    /// </remarks>
    public static IEnumerable<string> Fichiers()
    {
        foreach (var racine in new[] { "services", "shared", "apps" })
        {
            foreach (var fichier in Depot.Fichiers(Depot.Dossier(racine), ".cs"))
            {
                yield return fichier;
            }
        }
    }
}
