using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Toute route qui reçoit un fichier décide-t-elle de son type par les OCTETS ?
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// LE `Content-Type` D'UN TÉLÉVERSEMENT EST UNE DÉCLARATION DU CLIENT.
///
/// `IFormFile.ContentType` vient de l'en-tête multipart, écrit par l'appelant.
/// `curl --form 'f=@charge.bin;type=image/png'` suffit à le forger. Le passer
/// plus loin — au stockage, à un processeur d'image, à un décodeur — laisse le
/// client choisir comment ses propres octets seront interprétés par nos
/// serveurs et par les navigateurs de nos utilisateurs.
///
/// `FileSignature` et `UploadValidation` ont été écrits pour fermer exactement
/// ça. Le problème n'est pas qu'ils manquent : c'est qu'une route peut ne pas
/// les appeler, et que RIEN ne le signale.
///
/// C'EST ARRIVÉ, ET LA MANIÈRE DONT ÇA A ÉCHAPPÉ EST LE VRAI SUJET.
///
/// `CatalogEndpoints.ProcessProductImageAsync` recevait un `IFormFile` et
/// passait `file.ContentType` à `RemoveBackgroundWhiteAsync`, qui le repose en
/// en-tête vers rembg. Media-service, lui, appelait `UploadValidation`.
///
/// Une recherche des APPELANTS de `UploadValidation` ne pouvait pas trouver
/// catalog : son ABSENCE d'appel était le défaut. Chercher le nom d'un type ne
/// trouve jamais l'endroit qui aurait dû s'en servir. Ce contrôle part donc de
/// l'autre bout — qui reçoit un fichier — parce que c'est le seul bout qui
/// énumère les cas à couvrir.
///
/// CE QU'IL VÉRIFIE
///   Toute méthode dont la signature porte un paramètre `IFormFile` doit, dans
///   son corps, appeler `FileSignature.Detect` ou `UploadValidation.Check*`.
///
/// CE QU'IL NE VÉRIFIE PAS
///   • que le type détecté est ensuite RÉELLEMENT celui transmis en aval. Une
///     route peut appeler `FileSignature.Detect`, ignorer le résultat et passer
///     `file.ContentType` quand même : ce contrôle la déclarerait saine.
///   • les téléversements qui n'empruntent pas `IFormFile` — flux bruts,
///     `Request.Body`, multipart lu à la main.
///   • que la signature détectée corresponde au contenu entier. `FileSignature`
///     lit un en-tête ; un polyglotte reste un polyglotte. Ce n'est pas un
///     antivirus, et le contrôle n'en fait pas un.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class TeleversementsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "televersements";

    /// <inheritdoc/>
    public string Resume => "toute route qui reçoit un IFormFile déduit le type des octets, pas de l'en-tête";

    /// <summary>
    /// Une méthode dont un paramètre est un `IFormFile`. Le `?` est optionnel :
    /// une route qui accepte le fichier absent en reçoit un quand même.
    /// </summary>
    private static readonly Regex Methode = new(
        @"(?<nom>\w+)\s*\(\s*(?<params>[^)]*\bIFormFile\??\s+\w+[^)]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex Controle = new(
        @"FileSignature\.Detect|UploadValidation\.Check\w*",
        RegexOptions.Compiled);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        // L'absence d'un dossier LÈVE : un contrôle qui ne peut rien regarder ne
        // doit pas rendre « 0 anomalie ». Voir l'encadré de `Depot`.
        var racines = new[] { Depot.Dossier("services"), Depot.Dossier("bff") };

        var fautes = new List<string>();
        var constats = new List<string>();
        var routes = 0;

        foreach (var racine in racines)
        {
            foreach (var chemin in Depot.Fichiers(racine, ".cs"))
            {
                var brut = File.ReadAllText(chemin);
                if (!brut.Contains("IFormFile", StringComparison.Ordinal))
                {
                    continue;
                }

                // SANS LES COMMENTAIRES, ET C'EST LA CONDITION POUR QUE LE COMPTE
                // SOIT VRAI. Ce dépôt commente abondamment `IFormFile` et
                // `UploadValidation` : les garder ferait passer pour une route un
                // encadré qui en parle, et pour un contrôle une phrase qui le
                // mentionne. Le contrôle rendrait alors vert par bavardage.
                var texte = SourceCsharp.SansCommentaires(brut);

                foreach (Match methode in Methode.Matches(texte))
                {
                    routes++;
                    var nom = methode.Groups["nom"].Value;
                    var relatif = Depot.Relatif(chemin);

                    // LE CORPS, PAS LE FICHIER. Un fichier d'endpoints porte
                    // souvent plusieurs routes ; chercher le contrôle n'importe où
                    // dans le fichier laisserait une route non gardée passer pour
                    // gardée grâce à sa voisine — exactement le genre de vert que
                    // ce contrôle existe pour refuser.
                    var corps = CorpsApres(texte, methode.Index + methode.Length);
                    if (corps is null)
                    {
                        fautes.Add(
                            $"{relatif} : `{nom}` prend un `IFormFile` mais son corps "
                            + "n'a pas pu être délimité — le contrôle n'a RIEN pu "
                            + "vérifier sur cette route.");
                        continue;
                    }

                    if (!Controle.IsMatch(corps))
                    {
                        fautes.Add(
                            $"{relatif} : `{nom}` reçoit un `IFormFile` sans appeler "
                            + "`FileSignature.Detect` ni `UploadValidation.Check*`. "
                            + "Le type transmis en aval est alors celui que le client "
                            + "a déclaré.");
                    }
                    else
                    {
                        constats.Add($"gardée : {nom,-28} {relatif}");
                    }
                }
            }
        }

        constats.Insert(0, $"{routes} méthode(s) reçoivent un `IFormFile`.");

        return new Verdict(
            fautes,
            constats,
            [
                "que le type détecté soit RÉELLEMENT celui transmis en aval : une route "
                + "qui appelle `FileSignature.Detect` puis passe `file.ContentType` "
                + "quand même est comptée comme gardée",
                "les téléversements hors `IFormFile` — `Request.Body`, flux bruts, "
                + "multipart lu à la main",
                "le contenu au-delà de l'en-tête : `FileSignature` n'est pas un "
                + "antivirus, et un polyglotte reste un polyglotte",
            ]);
    }

    /// <summary>
    /// Le corps de la méthode dont la signature se termine à
    /// <paramref name="depuis"/>, ou <c>null</c> si les accolades ne se ferment
    /// pas — un corps mal délimité doit se dire, pas se deviner.
    /// </summary>
    private static string? CorpsApres(string texte, int depuis)
    {
        var ouverture = texte.IndexOf('{', depuis);
        if (ouverture < 0)
        {
            return null;
        }

        // Une signature suivie de `=>` plutôt que de `{` : le corps est
        // l'expression jusqu'au `;`. On le rend tel quel — il peut parfaitement
        // porter l'appel de contrôle.
        var flechette = texte.IndexOf("=>", depuis, StringComparison.Ordinal);
        if (flechette >= 0 && flechette < ouverture)
        {
            var fin = texte.IndexOf(';', flechette);
            return fin < 0 ? null : texte[flechette..fin];
        }

        var profondeur = 0;
        for (var i = ouverture; i < texte.Length; i++)
        {
            if (texte[i] == '{')
            {
                profondeur++;
            }
            else if (texte[i] == '}')
            {
                profondeur--;
                if (profondeur == 0)
                {
                    return texte[ouverture..i];
                }
            }
        }

        return null;
    }
}
