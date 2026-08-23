using HBA.ProofOfDelivery.Application;
using HBA.Shared.IntegrationEvents;

namespace HBA.Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-056 — l'OTP constant « 123456 » et la preuve rejouable.
///
/// `VerifyOtp` calculait une empreinte SHA-256, en vérifiait la longueur — 64,
/// toujours vraie — puis comparait la chaîne À UN LITTÉRAL. Le hachage donnait à
/// la fonction l'apparence d'un mécanisme de sécurité, et c'est ce qui l'a fait
/// passer en relecture. Et `submit` n'avait AUCUNE garde d'état : une preuve
/// déjà vérifiée se resoumettait à l'infini, republiant à chaque fois la clôture
/// de la course.
///
/// CES TESTS PORTENT SUR UNE MAQUETTE EN MÉMOIRE. `ProofStore` n'a ni base ni
/// migration : ce qui est éprouvé ici est vrai DANS un processus et ne survit ni
/// au redémarrage, ni à un second réplica. C'est dit dans l'encadré de la classe,
/// et ce n'est pas ces tests qui le corrigeront.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PreuveDeRemiseTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    private static CreateProofRequest UneRemise() =>
        new(Guid.NewGuid(), null, "DROPOFF", "Awa Sossou", Guid.NewGuid());

    /// <summary>
    /// LE TEST QUI FERME LA FAILLE D'ORIGINE : « 123456 » ne vaut plus rien.
    ///
    /// Il est théoriquement possible que le code tiré au hasard SOIT « 123456 » —
    /// une chance sur un million. On tire donc plusieurs preuves : la probabilité
    /// qu'un échec soit dû au hasard devient négligeable, et surtout, avant la
    /// correction, ce test échouait à TOUS les tirages.
    /// </summary>
    [Fact]
    public async Task Le_code_constant_123456_ne_verifie_plus_rien()
    {
        var store = new ProofStore();
        var publieur = new PublieurMuet();

        for (var i = 0; i < 25; i++)
        {
            var emission = store.Create(UneRemise(), Maintenant);

            var resultat = await store.SubmitAsync(
                emission.Proof.Id, new SubmitProofRequest(null, "123456"), publieur, default, Maintenant);

            resultat.Status.Should().NotBe(SubmitStatus.Verified);
        }
    }

    [Fact]
    public void Le_code_emis_est_aleatoire_et_propre_a_chaque_course()
    {
        var store = new ProofStore();

        var codes = Enumerable.Range(0, 50)
            .Select(_ => store.Create(UneRemise(), Maintenant).Otp)
            .ToArray();

        codes.Should().OnlyContain(c => c.Length == ProofStore.OtpDigits && c.All(char.IsDigit));
        codes.Distinct().Count().Should().BeGreaterThan(40, "un code par course, tiré au hasard");
    }

    [Fact]
    public async Task Le_bon_code_verifie_la_preuve()
    {
        var store = new ProofStore();
        var emission = store.Create(UneRemise(), Maintenant);

        var resultat = await store.SubmitAsync(
            emission.Proof.Id, new SubmitProofRequest(null, emission.Otp), new PublieurMuet(), default, Maintenant);

        resultat.Status.Should().Be(SubmitStatus.Verified);
        resultat.Proof!.OtpVerified.Should().BeTrue();
        resultat.Proof.Status.Should().Be("VERIFIED");
    }

    /// <summary>
    /// Le test que l'audit exige : une preuve DÉJÀ VÉRIFIÉE n'est pas rejouable.
    ///
    /// Le rejeu ne se contentait pas de réussir : il republiait `ProofVerified`
    /// et `DeliveryProofCompleted`, donc autant de clôtures de course et de
    /// déclenchements de paiement du livreur.
    /// </summary>
    [Fact]
    public async Task Une_preuve_deja_verifiee_n_est_pas_rejouable()
    {
        var store = new ProofStore();
        var publieur = new PublieurMuet();
        var emission = store.Create(UneRemise(), Maintenant);

        (await store.SubmitAsync(
            emission.Proof.Id, new SubmitProofRequest(null, emission.Otp), publieur, default, Maintenant))
            .Status.Should().Be(SubmitStatus.Verified);

        var publicationsApresPremiere = publieur.Publies;

        var rejeu = await store.SubmitAsync(
            emission.Proof.Id, new SubmitProofRequest("Quelqu'un d'autre", emission.Otp), publieur, default, Maintenant);

        rejeu.Status.Should().Be(SubmitStatus.AlreadySubmitted);
        publieur.Publies.Should().Be(publicationsApresPremiere, "un rejeu ne republie RIEN");

        // ET LE NOM DU DESTINATAIRE N'A PAS BOUGÉ. Le rejeu permettait au
        // livreur de réécrire, après coup et sans trace, le nom de la personne
        // qui avait reçu le colis.
        rejeu.Proof!.RecipientName.Should().Be("Awa Sossou");
    }

    [Fact]
    public async Task Un_code_expire_est_refuse()
    {
        var store = new ProofStore();
        var emission = store.Create(UneRemise(), Maintenant);

        var apresExpiration = Maintenant.Add(ProofStore.OtpLifetime).AddSeconds(1);

        var resultat = await store.SubmitAsync(
            emission.Proof.Id, new SubmitProofRequest(null, emission.Otp), new PublieurMuet(), default, apresExpiration);

        resultat.Status.Should().Be(SubmitStatus.OtpExpired);
    }

    /// <summary>
    /// L'expiration est franche : le code vaut jusqu'à sa date, pas au-delà.
    /// </summary>
    [Fact]
    public async Task Un_code_est_encore_valable_juste_avant_son_echeance()
    {
        var store = new ProofStore();
        var emission = store.Create(UneRemise(), Maintenant);

        var justeAvant = Maintenant.Add(ProofStore.OtpLifetime).AddSeconds(-1);

        var resultat = await store.SubmitAsync(
            emission.Proof.Id, new SubmitProofRequest(null, emission.Otp), new PublieurMuet(), default, justeAvant);

        resultat.Status.Should().Be(SubmitStatus.Verified);
    }

    /// <summary>
    /// Le test que l'audit exige : un code faux ÉPUISE les tentatives.
    ///
    /// Sans plafond, six chiffres ne sont qu'un million d'essais — et « submit »
    /// s'appelle en boucle. C'est le compteur, pas l'aléa, qui rend le code sûr.
    /// </summary>
    [Fact]
    public async Task Un_code_faux_epuise_les_tentatives_puis_bloque()
    {
        var store = new ProofStore();
        var publieur = new PublieurMuet();
        var emission = store.Create(UneRemise(), Maintenant);

        var faux = Faux(emission.Otp);

        for (var essai = 0; essai < ProofStore.MaxOtpAttempts; essai++)
        {
            var refus = await store.SubmitAsync(
                emission.Proof.Id, new SubmitProofRequest(null, faux), publieur, default, Maintenant);

            refus.Status.Should().Be(SubmitStatus.OtpInvalid);
        }

        var bloque = await store.SubmitAsync(
            emission.Proof.Id, new SubmitProofRequest(null, faux), publieur, default, Maintenant);

        bloque.Status.Should().Be(SubmitStatus.OtpLocked);

        // ET LE BLOCAGE TIENT MÊME CONTRE LE BON CODE. Sinon le plafond ne
        // serait qu'un ralentisseur : on épuise, puis on continue.
        var avecLeBon = await store.SubmitAsync(
            emission.Proof.Id, new SubmitProofRequest(null, emission.Otp), publieur, default, Maintenant);

        avecLeBon.Status.Should().Be(SubmitStatus.OtpLocked);
    }

    /// <summary>
    /// Un code PÉRIMÉ ne consomme pas de tentative : ce n'est pas une erreur du
    /// livreur, et le lui compter épuiserait ses essais sans qu'il puisse rien y
    /// faire.
    /// </summary>
    [Fact]
    public async Task Un_code_expire_ne_consomme_pas_de_tentative()
    {
        var store = new ProofStore();
        var publieur = new PublieurMuet();
        var emission = store.Create(UneRemise(), Maintenant);
        var apresExpiration = Maintenant.Add(ProofStore.OtpLifetime).AddSeconds(1);

        for (var i = 0; i < ProofStore.MaxOtpAttempts * 2; i++)
        {
            (await store.SubmitAsync(
                emission.Proof.Id, new SubmitProofRequest(null, emission.Otp), publieur, default, apresExpiration))
                .Status.Should().Be(SubmitStatus.OtpExpired, "et jamais « bloqué »");
        }
    }

    /// <summary>
    /// USAGE UNIQUE, ÉPROUVÉ EN CONCURRENCE RÉELLE.
    ///
    /// `ProofStore` est en mémoire : deux soumissions peuvent vraiment entrer en
    /// même temps. Une seule doit vérifier — sinon la course est clôturée deux
    /// fois et le livreur payé deux fois.
    /// </summary>
    [Fact]
    public async Task Deux_soumissions_simultanees_du_bon_code_une_seule_verifie()
    {
        var store = new ProofStore();
        var publieur = new PublieurMuet();
        var emission = store.Create(UneRemise(), Maintenant);

        var barriere = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<SubmitProofOutcome> Soumettre()
        {
            await barriere.Task;
            return await store.SubmitAsync(
                emission.Proof.Id, new SubmitProofRequest(null, emission.Otp), publieur, default, Maintenant);
        }

        var a = Soumettre();
        var b = Soumettre();
        barriere.SetResult();

        var resultats = await Task.WhenAll(a, b);

        resultats.Count(r => r.Status == SubmitStatus.Verified).Should().Be(1);
        resultats.Count(r => r.Status == SubmitStatus.AlreadySubmitted).Should().Be(1);
    }

    /// <summary>
    /// Sans code du tout, la preuve par MÉDIA reste recevable : c'est ce qui
    /// empêche cette correction de bloquer les remises tant qu'aucun canal ne
    /// porte le code au destinataire.
    /// </summary>
    [Fact]
    public async Task Sans_code_une_photo_suffit_encore()
    {
        var store = new ProofStore();
        var emission = store.Create(UneRemise(), Maintenant);

        store.Presign(emission.Proof.Id, new PresignProofMediaRequest(
            "PHOTO", "remise.jpg", "image/jpeg", new string('a', 64), Maintenant, 120_000));

        var resultat = await store.SubmitAsync(
            emission.Proof.Id, new SubmitProofRequest(null, null), new PublieurMuet(), default, Maintenant);

        resultat.Status.Should().Be(SubmitStatus.Verified);
        resultat.Proof!.OtpVerified.Should().BeFalse("aucun code n'a été présenté");
    }

    /// <summary>
    /// UN CODE FOURNI ET FAUX FAIT ÉCHOUER LA SOUMISSION, MÊME AVEC UNE PHOTO.
    ///
    /// L'ancienne règle — `otpVerified || médias` — rendait le code facultatif :
    /// on envoyait n'importe quoi et la photo passait. Un code FAUX n'est pas un
    /// code ABSENT : c'est le signe qu'on est au mauvais endroit, ou devant la
    /// mauvaise personne.
    /// </summary>
    [Fact]
    public async Task Un_code_faux_ne_se_rattrape_pas_avec_une_photo()
    {
        var store = new ProofStore();
        var emission = store.Create(UneRemise(), Maintenant);

        store.Presign(emission.Proof.Id, new PresignProofMediaRequest(
            "PHOTO", "remise.jpg", "image/jpeg", new string('a', 64), Maintenant, 120_000));

        var resultat = await store.SubmitAsync(
            emission.Proof.Id,
            new SubmitProofRequest(null, Faux(emission.Otp)),
            new PublieurMuet(), default, Maintenant);

        resultat.Status.Should().Be(SubmitStatus.OtpInvalid);
    }

    [Fact]
    public async Task Une_preuve_inconnue_rend_introuvable()
    {
        var store = new ProofStore();

        (await store.SubmitAsync(
            Guid.NewGuid(), new SubmitProofRequest(null, "000000"), new PublieurMuet(), default, Maintenant))
            .Status.Should().Be(SubmitStatus.NotFound);
    }

    /// <summary>Un code de même longueur, garanti différent du bon.</summary>
    private static string Faux(string bon)
    {
        var chiffres = bon.ToCharArray();
        chiffres[0] = chiffres[0] == '0' ? '1' : '0';
        return new string(chiffres);
    }

    private sealed class PublieurMuet : IIntegrationEventPublisher
    {
        private int _publies;

        public int Publies => _publies;

        public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _publies);
            return Task.CompletedTask;
        }
    }
}
