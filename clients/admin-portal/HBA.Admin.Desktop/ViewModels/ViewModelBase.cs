using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Notification de changement, écrite à la main.</summary>
/// <remarks>
/// PAS DE `CommunityToolkit.Mvvm`, ET C'EST UN CHOIX POUR CE LOT.
///
/// Ses `[ObservableProperty]` sont engendrés par un générateur de source : le
/// code que l'on lit n'est pas celui qui compile, et une erreur s'y présente
/// sous la forme d'un symbole introuvable dans un fichier qui n'existe pas sur
/// le disque. Pour un socle de quatre vues-modèles, la quinzaine de lignes
/// ci-dessous coûte moins qu'un paquet de plus dans la gestion centralisée.
///
/// Le jour où les écrans se compteront par dizaines, le générateur redeviendra
/// le bon outil.
/// </remarks>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Notifier([CallerMemberName] string? propriete = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriete));

    /// <summary>Affecte et notifie si la valeur a changé.</summary>
    protected bool Definir<T>(ref T champ, T valeur, [CallerMemberName] string? propriete = null)
    {
        if (EqualityComparer<T>.Default.Equals(champ, valeur))
        {
            return false;
        }

        champ = valeur;
        Notifier(propriete);
        return true;
    }
}

/// <summary>Commande asynchrone qui ne peut pas se déclencher deux fois.</summary>
/// <remarks>
/// LA RÉENTRANCE EST BLOQUÉE ICI, PAS DANS CHAQUE BOUTON.
///
/// Sur un poste d'administration, un double clic sur « approuver » part en deux
/// requêtes. Certaines routes sont idempotentes, d'autres non — et savoir
/// lesquelles ne doit pas être la responsabilité d'une vue. `_enCours` ferme la
/// porte pour toutes.
/// </remarks>
public sealed class CommandeAsync : ICommand
{
    private readonly Func<Task> _action;
    private readonly Func<bool>? _peutSExecuter;
    private bool _enCours;

    public CommandeAsync(Func<Task> action, Func<bool>? peutSExecuter = null)
    {
        _action = action;
        _peutSExecuter = peutSExecuter;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parametre)
        => !_enCours && (_peutSExecuter?.Invoke() ?? true);

    public async void Execute(object? parametre)
    {
        if (!CanExecute(parametre))
        {
            return;
        }

        _enCours = true;
        Reevaluer();

        try
        {
            await _action();
        }
        finally
        {
            // `finally` OBLIGATOIRE : sans lui, une exception laisserait la
            // commande définitivement désactivée. Le bouton resterait gris, et
            // seule une relance de l'application le débloquerait.
            _enCours = false;
            Reevaluer();
        }
    }

    /// <summary>À appeler quand une condition de `peutSExecuter` a changé.</summary>
    public void Reevaluer() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Commande asynchrone qui reçoit un paramètre de liaison.</summary>
/// <remarks>
/// LE PARAMÈTRE EST TYPÉ, ET UN PARAMÈTRE D'UN AUTRE TYPE NE FAIT RIEN.
///
/// `CommandParameter` traverse le moteur de liaison en `object?`. Le convertir de
/// force ferait tomber l'application sur une faute de gabarit XAML — c'est-à-dire
/// sur une erreur d'écran, à l'exécution, chez l'utilisateur. Un paramètre
/// inattendu est donc ignoré ; le bouton reste inerte, ce qui se voit et ne
/// casse rien.
/// </remarks>
public sealed class CommandeAsync<T> : ICommand
    where T : class
{
    private readonly Func<T, Task> _action;
    private readonly Func<T, bool>? _peutSExecuter;
    private bool _enCours;

    public CommandeAsync(Func<T, Task> action, Func<T, bool>? peutSExecuter = null)
    {
        _action = action;
        _peutSExecuter = peutSExecuter;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parametre)
        => !_enCours && parametre is T valeur && (_peutSExecuter?.Invoke(valeur) ?? true);

    public async void Execute(object? parametre)
    {
        if (parametre is not T valeur || !CanExecute(parametre))
        {
            return;
        }

        _enCours = true;
        Reevaluer();

        try
        {
            await _action(valeur);
        }
        finally
        {
            _enCours = false;
            Reevaluer();
        }
    }

    public void Reevaluer() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Réclame une saisie à l'administrateur avant d'agir.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// UNE INTERFACE, PARCE QU'UNE VUE-MODÈLE N'OUVRE PAS DE FENÊTRE.
///
/// Le geste naturel serait d'appeler directement une `Window` depuis la
/// vue-modèle. Cela la rendrait intestable et, surtout, la lierait au fil
/// d'interface : le jour où une élévation serait demandée depuis un travail de
/// fond, l'application se figerait sans message.
///
/// DEUX MÉTHODES, ET NON UNE SEULE AVEC UN DRAPEAU.
///
/// La première version n'en avait qu'une, réutilisée pour les deux usages. Un
/// motif de rejet se serait alors affiché EN POINTS, comme un mot de passe :
/// l'administrateur ne se relit pas, et ce texte-là part au vendeur. Deux
/// méthodes rendent le masquage impossible à confondre.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IDemandeurDeSaisie
{
    /// <summary>
    /// Mot de passe de l'administrateur, saisie MASQUÉE. Rend <c>null</c> s'il renonce.
    /// </summary>
    /// <param name="geste">Ce qu'il s'apprête à faire, écrit pour lui.</param>
    Task<string?> MotDePasseAsync(string geste);

    /// <summary>
    /// Motif d'une décision, saisie EN CLAIR — il est destiné au vendeur.
    /// </summary>
    Task<string?> MotifAsync(string geste);

    /// <summary>
    /// Référence d'un virement exécuté à la main, saisie EN CLAIR.
    /// </summary>
    /// <remarks>
    /// UNE TROISIÈME MÉTHODE, PARCE QUE CE N'EST NI UN MOTIF NI UN SECRET.
    ///
    /// Pour un virement client, aucun webhook ne confirmera que l'argent est
    /// parti : cette référence est la SEULE preuve, et c'est ce que l'invite doit
    /// dire. La confondre avec un motif afficherait « ce texte sera transmis au
    /// demandeur » sur un champ qui sert de pièce comptable.
    /// </remarks>
    Task<string?> ReferenceAsync(string geste);
}
