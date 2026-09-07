using HBA.Controls;
using HBA.Controls.Controles;

// LE LANCEUR DES CONTRÔLES STATIQUES DU DÉPÔT.

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
    new TeleversementsControle(),
    new UsingsControle(),
    new WorkflowsControle(),
];

// LES VERBES QUI NE SONT PAS DES CONTRÔLES.
if (args.Length > 0 && args[0] == ImagesAffectees.Verbe)
{
    return ImagesAffectees.Executer(args);
}

if (args.Length > 0 && args[0] == ComposeProd.Verbe)
{
    return ComposeProd.Executer();
}

// `resume-tests` ne rend pas non plus de verdict : il RELIT les rapports `.trx`
// d'une exécution de tests et nomme les cas tombés.
if (args.Length > 0 && args[0] == ResumeTests.Verbe)
{
    return ResumeTests.Executer(args);
}

var demandes = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

// Un contrôle qui porterait le nom d'un verbe deviendrait inatteignable, en
// silence.
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
        // UN NOM INCONNU EST UNE ERREUR, PAS UN NON-ÉVÉNEMENT. Filtrer en silence
        // sur un nom mal tapé ferait passer « aucun contrôle exécuté » pour «
        // aucune faute ».
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
        // UN CONTRÔLE QUI LÈVE EST UN CONTRÔLE QUI ÉCHOUE. Rattraper pour continuer
        // est juste ; rattraper pour rendre 0 ne l'est pas.
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

// CE QUI N'A PAS ÉTÉ REGARDÉ SE DIT À LA FIN, PAS SEULEMENT DANS LES COMMENTAIRES.
// Une barrière verte qui a sauté la moitié de son travail est le défaut qu'on a
// corrigé quatre fois dans ce dépôt.
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
