using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using HBA.Delivery.Proof.Domain.Entities;
using HBA.ProofOfDelivery.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.ProofOfDelivery.Application;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PREUVE DE REMISE — MAQUETTE EN MÉMOIRE, RÈGLES RÉELLES.
///
/// CE SERVICE N'A NI BASE, NI MIGRATION, NI OUTBOX DRAINÉE (ISSUE-007).
///
/// Tout ce que cette classe protège vit dans des `ConcurrentDictionary` de
/// processus, DISPARAÎT AU REDÉMARRAGE, et n'est pas partagé entre deux
/// réplicas. Les corrections d'ISSUE-056 sont donc RÉELLES dans un processus et
/// nulles entre deux. Elles sont écrites ici pour que le défaut ne SURVIVE PAS à
/// l'implémentation de ce service : le jour où il aura un `DbContext`, la règle
/// sera déjà là et il n'y aura qu'à la persister.
///
/// ET IL EXISTE DÉJÀ UN AUTRE MÉCANISME, LUI PERSISTÉ. C'est
/// `Delivery.IssuedPin` et `Delivery.FailedProofAttempts`, dans delivery-service,
/// qui a une base et des migrations. Ce service en est un DOUBLON en mémoire.
/// La question à trancher n'est pas « comment le persister » mais « lequel des
/// deux garde-t-on » — voir le rapport du lot 5.1/5.3.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ProofStore
{
    /// <summary>
    /// Longueur du code. Six chiffres se dictent au téléphone sans se tromper ;
    /// quatre ne laissent que dix mille possibilités, et huit ne se retiennent
    /// pas le temps de les répéter.
    /// </summary>
    public const int OtpDigits = 6;

    /// <summary>
    /// Au-delà, le code est refusé et il faut en réémettre un.
    ///
    /// Quinze minutes couvrent l'arrivée du livreur et la recherche du téléphone
    /// par le destinataire. Plus long, et un code intercepté reste utilisable
    /// une demi-journée ; plus court, et on refuse des remises légitimes pendant
    /// que le client cherche son SMS.
    /// </summary>
    public static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Tentatives infructueuses avant blocage.
    ///
    /// LE COMPTEUR EST CE QUI DONNE UN SENS À L'ALÉA. Six chiffres, c'est un
    /// million de possibilités — et « submit » s'appelle en boucle. Sans plafond,
    /// un code aléatoire n'est qu'un code constant qu'il faut chercher un peu
    /// plus longtemps. Cinq essais ramènent la probabilité à 0,0005 %.
    ///
    /// Même valeur que `Delivery.MaxFailedProofAttempts`, délibérément : deux
    /// plafonds différents pour le même geste finiraient par diverger, et le
    /// livreur verrait sa preuve bloquée à un endroit et pas à l'autre.
    /// </summary>
    public const int MaxOtpAttempts = 5;

    private readonly ConcurrentDictionary<Guid, DeliveryProof> _proofs = new();
    private readonly ConcurrentDictionary<Guid, List<ProofMedia>> _media = new();
    private readonly ConcurrentDictionary<Guid, OtpChallenge> _challenges = new();

    /// <summary>
    /// UN VERROU UNIQUE POUR TOUT LE CHEMIN DE SOUMISSION, ET C'EST VOULU.
    ///
    /// L'usage unique et le comptage des tentatives sont des règles LIRE-PUIS-
    /// ÉCRIRE : `ConcurrentDictionary` garantit l'atomicité de chaque opération,
    /// pas celle d'une séquence. Deux soumissions simultanées liraient toutes
    /// deux « DRAFT, 4 tentatives » et passeraient toutes deux.
    ///
    /// Un verrou par preuve serait plus fin ; à l'échelle d'une maquette dont
    /// l'état tient en mémoire, il n'achèterait rien et se relit moins bien. Le
    /// jour où ce service aura une base, ce verrou n'aura de toute façon plus
    /// aucune valeur — il ne couvre qu'un processus — et devra être remplacé par
    /// un `UPDATE … WHERE Status = 'DRAFT'` conditionnel.
    /// </summary>
    private readonly object _verrouSoumission = new();

    /// <summary>
    /// Crée la preuve et ÉMET SON CODE.
    /// </summary>
    /// <remarks>
    /// LE CODE EN CLAIR N'EST RENDU QU'ICI, ET UNE SEULE FOIS.
    ///
    /// Il n'est pas conservé : seul son empreinte SHA-256 reste en mémoire.
    /// L'appelant est responsable de le faire parvenir AU DESTINATAIRE — jamais
    /// au livreur, qui doit se le faire dicter. C'est exactement la discipline de
    /// `Delivery.IssuedPin`, et c'est ce qui fait qu'une preuve prouve quelque
    /// chose : un code que le livreur peut lire ne prouve que sa présence devant
    /// son propre téléphone.
    ///
    /// HACHÉ ET NON CHIFFRÉ — LE CHOIX EST DÉLIBÉRÉ.
    ///
    /// `ISecretProtector` (AES-GCM) existe pour les secrets qu'il faut RELIRE :
    /// un code de réinitialisation traverse l'outbox et doit ressortir en clair
    /// à l'autre bout. Ici, personne n'a jamais besoin de relire le code — on ne
    /// fait que le COMPARER. Le hachage est donc strictement plus sûr, à
    /// fonctionnalité égale : même une lecture complète de la mémoire du
    /// processus ne rend aucun code utilisable.
    ///
    /// (C'est le raisonnement de `Sha256TokenGenerator` dans identity-service, et
    /// ce service ne référence de toute façon pas `HBA.Shared.Application` : y
    /// injecter `ISecretProtector` aurait aussi demandé une dépendance que son
    /// hôte n'enregistre pas — il n'appelle pas `AddBuildingBlocksInfrastructure`.)
    ///
    /// CE QUI RESTE OUVERT : AUCUN CANAL NE PORTE ENCORE CE CODE AU CLIENT.
    /// La chaîne de notification n'est pas branchée sur ce service, et sa file
    /// d'événements n'est jamais drainée (ISSUE-007). Le code est donc correct,
    /// et personne ne le reçoit. La voie de preuve par MÉDIA, elle, reste
    /// utilisable — c'est ce qui empêche cette correction de bloquer les remises.
    /// </remarks>
    public ProofIssued Create(CreateProofRequest request, DateTimeOffset? nowUtc = null)
    {
        var maintenant = nowUtc ?? DateTimeOffset.UtcNow;

        var proof = new DeliveryProof(
            Guid.NewGuid(),
            request.DeliveryId,
            request.StopId,
            request.Type,
            "DRAFT",
            request.RecipientName,
            false,
            maintenant,
            request.DriverId);

        var code = GenerateOtp();

        _proofs[proof.Id] = proof;
        // `OtpChallenge` VIENT DU DOMAINE (`HBA.Delivery.Proof.Domain.Entities`).
        // Il y dormait depuis toujours, avec exactement les bons champs, pendant
        // que cette couche comparait à « 123456 ». Voir le commentaire du csproj.
        // Seule l'EMPREINTE est conservée : le code en clair sort de cette
        // méthode et n'y revient jamais.
        _challenges[proof.Id] = new OtpChallenge(
            Guid.NewGuid(), proof.Id, Hash(code), maintenant.Add(OtpLifetime), 0, false);

        return new ProofIssued(proof, code);
    }

    public PresignedProofMedia? Presign(Guid proofId, PresignProofMediaRequest request)
    {
        if (!_proofs.ContainsKey(proofId))
        {
            return null;
        }

        var objectKey = $"proofs/{proofId:N}/{Guid.NewGuid():N}-{request.FileName}";
        var media = new ProofMedia(Guid.NewGuid(), proofId, request.MediaType, objectKey, request.MimeType, request.Sha256, request.CapturedAt, request.SizeBytes);
        _media.AddOrUpdate(proofId, _ => [media], (_, existing) =>
        {
            existing.Add(media);
            return existing;
        });

        return new PresignedProofMedia(media.Id, objectKey, $"https://storage.local/{objectKey}?signature=dev", DateTimeOffset.UtcNow.AddMinutes(10));
    }

    /// <summary>
    /// Soumet la preuve.
    /// </summary>
    /// <remarks>
    /// CE QUI ÉTAIT CASSÉ — ISSUE-056, ET IL Y AVAIT DEUX DÉFAUTS.
    ///
    ///   1. L'OTP ÉTAIT LA CONSTANTE « 123456 ». `VerifyOtp` calculait bien une
    ///      empreinte SHA-256, en vérifiait la longueur — 64, toujours vraie — et
    ///      comparait ensuite la chaîne À UN LITTÉRAL. Le hachage ne servait à
    ///      RIEN : il donnait au code l'apparence d'un mécanisme de sécurité, et
    ///      c'est ce qui l'a fait passer en relecture. N'importe qui, connaissant
    ///      un identifiant de preuve, clôturait n'importe quelle livraison.
    ///
    ///   2. AUCUNE GARDE D'ÉTAT. Une preuve déjà VÉRIFIÉE se resoumettait
    ///      indéfiniment, et chaque rejeu republiait `ProofVerified` ET
    ///      `DeliveryProofCompleted` — donc, en bout de chaîne, autant de
    ///      clôtures de course et de déclenchements de paiement du livreur.
    ///      Le rejeu pouvait aussi RÉÉCRIRE `RecipientName` : le nom de la
    ///      personne ayant reçu le colis était modifiable après coup, sans
    ///      trace, par celui-là même qui l'avait livré.
    ///
    /// UN CODE FOURNI ET FAUX FAIT ÉCHOUER LA SOUMISSION, MÊME S'IL Y A DES
    /// MÉDIAS. L'ancienne règle — `otpVerified || médias` — rendait le code
    /// facultatif : le livreur envoyait n'importe quoi et une photo suffisait.
    /// Un code FAUX n'est pas un code ABSENT : c'est le signe qu'on est au
    /// mauvais endroit, ou devant la mauvaise personne. Sans code du tout, la
    /// preuve par média reste recevable, comme avant.
    /// </remarks>
    public async Task<SubmitProofOutcome> SubmitAsync(
        Guid proofId,
        SubmitProofRequest request,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default,
        DateTimeOffset? nowUtc = null)
    {
        var maintenant = nowUtc ?? DateTimeOffset.UtcNow;

        DeliveryProof? soumise;
        SubmitStatus statut;

        lock (_verrouSoumission)
        {
            if (!_proofs.TryGetValue(proofId, out var proof))
            {
                return new SubmitProofOutcome(SubmitStatus.NotFound, null);
            }

            // LA GARDE D'ÉTAT. Une preuve n'est soumise qu'UNE fois : elle
            // quitte « DRAFT » pour ne plus y revenir.
            if (proof.Status != "DRAFT")
            {
                return new SubmitProofOutcome(SubmitStatus.AlreadySubmitted, proof);
            }

            var challenge = _challenges.GetValueOrDefault(proofId);
            var codeFourni = !string.IsNullOrWhiteSpace(request.Otp);
            var otpVerifie = false;

            if (codeFourni)
            {
                if (challenge is null)
                {
                    // Aucun code n'a été émis pour cette preuve : en présenter un
                    // ne peut pas réussir. On ne dit pas « faux », on dit
                    // « expiré » — les deux se traitent pareil côté livreur, et
                    // distinguer les deux renseignerait un attaquant.
                    return new SubmitProofOutcome(SubmitStatus.OtpExpired, proof);
                }

                if (challenge.Verified)
                {
                    return new SubmitProofOutcome(SubmitStatus.OtpAlreadyUsed, proof);
                }

                if (challenge.Attempts >= MaxOtpAttempts)
                {
                    return new SubmitProofOutcome(SubmitStatus.OtpLocked, proof);
                }

                // L'EXPIRATION EST TESTÉE AVANT LA COMPARAISON, et elle ne
                // consomme pas de tentative : un code périmé n'est pas une
                // erreur du livreur, et le lui compter épuiserait ses essais
                // sans qu'il puisse rien y faire.
                if (maintenant >= challenge.ExpiresAt)
                {
                    return new SubmitProofOutcome(SubmitStatus.OtpExpired, proof);
                }

                otpVerifie = FixedTimeEquals(challenge.OtpHash, Hash(request.Otp!));

                if (!otpVerifie)
                {
                    _challenges[proofId] = challenge with { Attempts = challenge.Attempts + 1 };
                    return new SubmitProofOutcome(SubmitStatus.OtpInvalid, proof);
                }

                // USAGE UNIQUE : le code est consommé dès qu'il a réussi, dans
                // le même verrou. Deux soumissions concurrentes portant le bon
                // code ne peuvent pas réussir toutes les deux.
                _challenges[proofId] = challenge with { Verified = true };
            }

            var aDesMedias = _media.TryGetValue(proofId, out var items) && items.Count > 0;
            statut = otpVerifie || aDesMedias ? SubmitStatus.Verified : SubmitStatus.Rejected;

            soumise = proof with
            {
                Status = statut == SubmitStatus.Verified ? "VERIFIED" : "REJECTED",
                RecipientName = string.IsNullOrWhiteSpace(request.RecipientName) ? proof.RecipientName : request.RecipientName,
                OtpVerified = otpVerifie
            };

            _proofs[proofId] = soumise;
        }

        // LES PUBLICATIONS SONT HORS DU VERROU. Un `await` sous `lock` ne
        // compile pas, et c'est tant mieux : tenir un verrou pendant un appel
        // réseau est la meilleure façon de bloquer tout le service. L'état est
        // déjà écrit — un second appelant verra « déjà soumise » quoi qu'il
        // arrive ici.
        await publisher.PublishAsync(new ProofSubmittedIntegrationEvent
        {
            ProofId = soumise.Id,
            DeliveryId = soumise.DeliveryId
        }, cancellationToken);

        if (statut == SubmitStatus.Verified)
        {
            await publisher.PublishAsync(new ProofVerifiedIntegrationEvent
            {
                ProofId = soumise.Id,
                DeliveryId = soumise.DeliveryId
            }, cancellationToken);

            if (soumise.Type == "DROPOFF")
            {
                await publisher.PublishAsync(new DeliveryProofCompletedIntegrationEvent
                {
                    DeliveryId = soumise.DeliveryId,
                    ProofId = soumise.Id
                }, cancellationToken);
            }
        }
        else
        {
            await publisher.PublishAsync(new ProofRejectedIntegrationEvent
            {
                ProofId = soumise.Id,
                DeliveryId = soumise.DeliveryId,
                Reason = "PROOF_NOT_VERIFIABLE"
            }, cancellationToken);
        }

        return new SubmitProofOutcome(statut, soumise);
    }

    public IReadOnlyList<ProofSummary> ListByDelivery(Guid deliveryId) =>
        _proofs.Values
            .Where(proof => proof.DeliveryId == deliveryId)
            .OrderBy(proof => proof.CreatedAt)
            .Select(proof => new ProofSummary(proof, _media.GetValueOrDefault(proof.Id) ?? []))
            .ToArray();

    public bool HasValidDropoffProof(Guid deliveryId) =>
        _proofs.Values.Any(proof => proof.DeliveryId == deliveryId && proof.Type == "DROPOFF" && proof.Status == "VERIFIED");

    /// <summary>Le livreur qui a ouvert cette preuve, ou <c>null</c> si elle n'existe pas.</summary>
    public Guid? OwnerOf(Guid proofId) =>
        _proofs.TryGetValue(proofId, out var proof) ? proof.DriverId : null;

    /// <summary>
    /// Six chiffres cryptographiquement aléatoires, sans biais de modulo —
    /// <c>RandomNumberGenerator.GetInt32</c> est uniforme sur [0, 10).
    ///
    /// PAS DE `Random` : il est prédictible à partir de quelques tirages, et
    /// c'est précisément ce dont on se protège ici.
    /// </summary>
    private static string GenerateOtp()
    {
        var chiffres = new char[OtpDigits];
        for (var i = 0; i < OtpDigits; i++)
        {
            chiffres[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }

        return new string(chiffres);
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Comparaison à temps constant.
    ///
    /// UN `==` SUR DES CHAÎNES S'ARRÊTE AU PREMIER CARACTÈRE QUI DIFFÈRE. La
    /// durée de la réponse trahit alors le nombre de caractères déjà justes, et
    /// un million de possibilités se réduit à quelques dizaines d'essais par
    /// position. Même discipline que `User.FixedTimeEquals` dans
    /// identity-service.
    /// </summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var resultat = 0;
        for (var i = 0; i < a.Length; i++)
        {
            resultat |= a[i] ^ b[i];
        }

        return resultat == 0;
    }
}

/// <summary>Issue d'une soumission de preuve.</summary>
public enum SubmitStatus
{
    /// <summary>Aucune preuve sous cet identifiant.</summary>
    NotFound = 0,

    /// <summary>La preuve a déjà quitté « DRAFT » : elle n'est pas rejouable.</summary>
    AlreadySubmitted = 1,

    /// <summary>Le code présenté est faux. Une tentative a été consommée.</summary>
    OtpInvalid = 2,

    /// <summary>Le code a expiré, ou aucun code n'a été émis. Aucune tentative consommée.</summary>
    OtpExpired = 3,

    /// <summary>Le code a déjà servi.</summary>
    OtpAlreadyUsed = 4,

    /// <summary>Trop de tentatives : un humain doit intervenir.</summary>
    OtpLocked = 5,

    /// <summary>Ni code valide, ni média : rien ne prouve la remise.</summary>
    Rejected = 6,

    /// <summary>La remise est prouvée.</summary>
    Verified = 7
}

public sealed record SubmitProofOutcome(SubmitStatus Status, DeliveryProof? Proof);

/// <summary>
/// Une preuve fraîchement créée, ET son code en clair.
///
/// <c>Otp</c> NE DOIT JAMAIS ÊTRE RENVOYÉ DANS UNE RÉPONSE HTTP LUE PAR LE
/// LIVREUR. Voir l'encadré de <see cref="ProofStore.Create"/> : c'est ce qui
/// distingue une preuve d'une case à cocher.
/// </summary>
public sealed record ProofIssued(DeliveryProof Proof, string Otp);

public sealed record DeliveryProof(Guid Id, Guid DeliveryId, Guid? StopId, string Type, string Status, string? RecipientName, bool OtpVerified, DateTimeOffset CreatedAt, Guid DriverId);
public sealed record ProofMedia(Guid Id, Guid ProofId, string MediaType, string ObjectKey, string MimeType, string Sha256, DateTimeOffset CapturedAt, long SizeBytes);
public sealed record ProofSummary(DeliveryProof Proof, IReadOnlyList<ProofMedia> Media);
public sealed record CreateProofRequest(Guid DeliveryId, Guid? StopId, string Type, string? RecipientName, Guid DriverId);
public sealed record PresignProofMediaRequest(string MediaType, string FileName, string MimeType, string Sha256, DateTimeOffset CapturedAt, long SizeBytes);
public sealed record PresignedProofMedia(Guid MediaId, string ObjectKey, string UploadUrl, DateTimeOffset ExpiresAt);
public sealed record SubmitProofRequest(string? RecipientName, string? Otp);
