using HBA.Controls;
using HBA.Controls.Controles;

// ═══════════════════════════════════════════════════════════════════════════════
// LE LANCEUR DES CONTRÔLES STATIQUES DU DÉPÔT.
//
//     dotnet run --project tools/HBA.Controls              tous les contrôles
//     dotnet run --project tools/HBA.Controls -- solution  un seul
//     dotnet run --project tools/HBA.Controls -- --liste   ce qui existe
//
//     dotnet run --project tools/HBA.Controls -- images-affectees <base>
//     dotnet run --project tools/HBA.Controls -- images-affectees --tous
//         la matrice de construction de la CI, en JSON sur la sortie standard.
//
//     dotnet run --project tools/HBA.Controls -- compose-prod
//         engendre docker-compose.prod.yml depuis docker-compose.dev.yml.
//
//     dotnet run --project tools/HBA.Controls -- resume-tests [dossier]
//         relit les rapports .trx et nomme les cas de test en échec.
//
// IL REMPLACE PROGRESSIVEMENT `scripts/check-*.py`. Tant que le portage n'est
// pas fini, les deux coexistent et `scripts/check-all.sh` lance les deux : un
// contrôle porté est RETIRÉ du côté Python dans le même commit que son arrivée
// ici. Deux exemplaires du même contrôle divergeraient, et c'est celui qui se
// tait qu'on croirait.
//
// LE CODE DE SORTIE EST 1 DÈS QU'UNE FAUTE EXISTE. Un lanceur qui rend 0 « pour
// ne pas bloquer » transforme la barrière en décoration.
//
// CE QUE CE LANCEUR NE FAIT PAS : il ne compile rien, ne joint aucun cluster,
// n'appelle aucun service. Tous les contrôles ici lisent des fichiers du dépôt.
// ═══════════════════════════════════════════════════════════════════════════════

IControle[] controles =
[
    new AccoladesControle(),
    new AdressesServiceControle(),
    new AuditTrailControle(),
    new AutorisationsGrpcControle(),
    new ChainesConnexionControle(),
    new ConfigEtGardesControle(),
    new DockerfilesControle(),
    new EventConsumersControle(),
    new EventContractsControle(),
    new GatewayControle(),
    new GrpcRpcControle(),
    new GrpcStubsControle(),
    new ImplementationsControle(),
    new KafkaTopicsControle(),
    new MigrationsControle(),
    new PermissionsControle(),
    new ReferencesControle(),
    new SolutionControle(),
    new UsingsControle(),
    new WorkflowsControle(),
];

// ═══════════════════════════════════════════════════════════════════════════════
// LES VERBES QUI NE SONT PAS DES CONTRÔLES.
//
// `images-affectees` ne rend pas un verdict : il rend la matrice de construction
// de la CI, sur la sortie standard, en JSON. Le mélanger aux contrôles ferait
// écrire ce JSON au milieu d'un rapport de barrière.
if (args.Length > 0 && args[0] == ImagesAffectees.Verbe)
{
    return ImagesAffectees.Executer(args);
}

if (args.Length > 0 && args[0] == ComposeProd.Verbe)
{
    return ComposeProd.Executer();
}

// `resume-tests` ne rend pas non plus de verdict : il RELIT les rapports `.trx`
// d'une exécution de tests et nomme les cas tombés. Il rend toujours 0 — le
// rouge appartient à `dotnet test`, qui l'a déjà posé.
if (args.Length > 0 && args[0] == ResumeTests.Verbe)
{
    return ResumeTests.Executer(args);
}

var demandes = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

// Un contrôle qui porterait le nom d'un verbe deviendrait inatteignable, en
// silence. On refuse la collision plutôt que de la découvrir en production.
string[] verbes = [ImagesAffectees.Verbe, ComposeProd.Verbe, ResumeTests.Verbe];
var collision = controles.FirstOrDefault(c => verbes.Contains(c.Nom));
if (collision is not null)
{
    Console.Error.WriteLine($"un contrôle porte le nom du verbe « {collision.Nom} » : "
                            + "il ne pourrait jamais être lancé");
    return 2;
}

if (args.Contains("--liste"))
{
    Console.WriteLine($"{controles.Length} contrôle(s) :");
    foreach (var c in controles)
    {
        Console.WriteLine($"  {c.Nom,-22} {c.Resume}");
    }

    return 0;
}

if (demandes.Length > 0)
{
    var inconnus = demandes.Where(d => !controles.Any(c => c.Nom == d)).ToArray();
    if (inconnus.Length > 0)
    {
        // UN NOM INCONNU EST UNE ERREUR, PAS UN NON-ÉVÉNEMENT. Filtrer en
        // silence sur un nom mal tapé ferait passer « aucun contrôle exécuté »
        // pour « aucune faute ».
        Console.Error.WriteLine("contrôle(s) inconnu(s) : " + string.Join(", ", inconnus));
        Console.Error.WriteLine("connus : " + string.Join(", ", controles.Select(c => c.Nom)));
        return 2;
    }

    controles = [.. controles.Where(c => demandes.Contains(c.Nom))];
}

var total = 0;
var nonCouvert = new List<string>();

foreach (var controle in controles)
{
    Verdict verdict;
    try
    {
        verdict = controle.Executer();
    }
    catch (Exception erreur)
    {
        // UN CONTRÔLE QUI LÈVE EST UN CONTRÔLE QUI ÉCHOUE. Rattraper pour
        // continuer est juste ; rattraper pour rendre 0 ne l'est pas.
        Console.WriteLine($"❌ {controle.Nom} — le contrôle s'est interrompu");
        Console.WriteLine($"     {erreur.GetType().Name} : {erreur.Message}");
        total++;
        continue;
    }

    var marque = verdict.Fautes.Count == 0 ? "✔" : "❌";
    Console.WriteLine($"{marque} {controle.Nom} — {controle.Resume}");

    foreach (var constat in verdict.Constats)
    {
        Console.WriteLine($"     {constat}");
    }

    foreach (var faute in verdict.Fautes)
    {
        Console.WriteLine($"     {faute}");
    }

    nonCouvert.AddRange(verdict.NonCouvert.Select(x => $"{controle.Nom} : {x}"));
    total += verdict.Fautes.Count;
}

// CE QUI N'A PAS ÉTÉ REGARDÉ SE DIT À LA FIN, PAS SEULEMENT DANS LES
// COMMENTAIRES. Une barrière verte qui a sauté la moitié de son travail est le
// défaut qu'on a corrigé quatre fois dans ce dépôt.
if (nonCouvert.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Ce qui n'est PAS couvert :");
    foreach (var ligne in nonCouvert)
    {
        Console.WriteLine($"  · {ligne}");
    }
}

Console.WriteLine();
Console.WriteLine($"{controles.Length} contrôle(s), {total} faute(s).");
return total == 0 ? 0 : 1;
