using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Connexion, second facteur compris.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE MOT DE PASSE N'EST PAS CONSERVÉ ENTRE LES DEUX ÉTAPES.
///
/// Le geste naturel serait de le garder pour le renvoyer avec le code : deux
/// appels, un seul formulaire. Cela laisserait un mot de passe d'administrateur
/// en mémoire d'objet pendant tout le temps que dure la saisie du code — et,
/// surtout, dans une propriété liée que le moteur de liaison conserve.
///
/// Il est donc renvoyé depuis le champ, qui reste rempli et masqué le temps de la
/// seconde étape, et effacé dès la session ouverte.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ConnexionViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly Action _ouvrir;

    private string _courriel = string.Empty;
    private string _motDePasse = string.Empty;
    private string _code = string.Empty;
    private string? _erreur;
    private bool _codeExige;
    private bool _enCours;

    public ConnexionViewModel(ClientApiAdmin api, Action ouvrir)
    {
        _api = api;
        _ouvrir = ouvrir;
        Connecter = new CommandeAsync(ConnecterAsync, () => !EnCours && Complet);
    }

    public string Courriel
    {
        get => _courriel;
        set { if (Definir(ref _courriel, value)) Connecter.Reevaluer(); }
    }

    public string MotDePasse
    {
        get => _motDePasse;
        set { if (Definir(ref _motDePasse, value)) Connecter.Reevaluer(); }
    }

    public string Code
    {
        get => _code;
        set { if (Definir(ref _code, value)) Connecter.Reevaluer(); }
    }

    /// <summary>Message d'échec, ou <c>null</c>.</summary>
    public string? Erreur
    {
        get => _erreur;
        private set { if (Definir(ref _erreur, value)) Notifier(nameof(EnErreur)); }
    }

    public bool EnErreur => !string.IsNullOrEmpty(_erreur);

    /// <summary>Le serveur a-t-il réclamé le second facteur ?</summary>
    public bool CodeExige
    {
        get => _codeExige;
        private set => Definir(ref _codeExige, value);
    }

    public bool EnCours
    {
        get => _enCours;
        private set { if (Definir(ref _enCours, value)) Connecter.Reevaluer(); }
    }

    public CommandeAsync Connecter { get; }

    private bool Complet
        => !string.IsNullOrWhiteSpace(_courriel)
           && !string.IsNullOrWhiteSpace(_motDePasse)
           && (!_codeExige || !string.IsNullOrWhiteSpace(_code));

    private async Task ConnecterAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var resultat = await _api.ConnecterAsync(
                _courriel.Trim(), _motDePasse, _codeExige ? _code.Trim() : null);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            switch (resultat.Valeur)
            {
                case IssueConnexion.Ouverte:
                    // Les deux champs sensibles partent AVANT la navigation :
                    // après, cette vue-modèle peut survivre le temps que le
                    // ramasse-miettes passe.
                    MotDePasse = string.Empty;
                    Code = string.Empty;
                    _ouvrir();
                    break;

                case IssueConnexion.CodeExige:
                    CodeExige = true;

                    // CE N'EST PAS UNE ERREUR, ET LE TEXTE NE DOIT PAS EN AVOIR
                    //    L'AIR. Les identifiants ont été acceptés ; il manque une
                    //    étape. Afficher « échec » ici ferait recommencer la
                    //    saisie du mot de passe, qui est correcte.
                    Erreur = "Saisissez le code de votre application d'authentification.";
                    break;

                default:
                    Erreur = "Identifiants refusés.";
                    break;
            }
        }
        finally
        {
            EnCours = false;
        }
    }
}
