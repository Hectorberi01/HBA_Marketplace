using System.Diagnostics;

namespace HBA.Catalog.Infrastructure.Observability.Tracing;

/// <summary>
/// La source d'activite de ce service.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// STATIQUE, ET C'EST LA REGLE D'OpenTelemetry, PAS UN RACCOURCI.
///
/// Une `ActivitySource` est faite pour vivre aussi longtemps que le processus :
/// en creer une par requete produirait des sources orphelines que le collecteur
/// ne rattache a rien.
///
/// LE NOM EST CELUI DU MODULE, PAS DU SERVICE HTTP. Trois hotes montent plusieurs
/// modules dans un processus ; un nom par hote melangerait leurs traces sans
/// qu'on puisse les separer apres coup.
///
/// CE QUE ÇA NE FAIT PAS : declarer la source ne l'exporte pas. Le pipeline
/// OpenTelemetry — exportateur, protocole, ressource — reste pose par
/// `AddHbaTelemetry`, dans le socle : deux configurations d'exportateur seraient
/// deux formats de trace pour un meme collecteur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class SourcesDActivite
{
    public const string Nom = "HBA.Catalog.Infrastructure";

    public static readonly ActivitySource Source = new(Nom);
}
