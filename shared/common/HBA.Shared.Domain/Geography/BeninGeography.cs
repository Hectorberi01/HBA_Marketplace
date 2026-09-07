using System.Text;

namespace HBA.Shared.Domain.Geography;

/// <summary>Un des douze départements du Bénin.</summary>
public sealed record BeninDepartment(string Code, string Name);

/// <summary>Une des 77 communes du Bénin, rattachée à son département.</summary>
/// <param name="Code">
/// Identifiant STABLE, en minuscules sans accent (« abomey-calavi », « seme-podji
/// »).
/// </param>
/// <param name="Name">Libellé d'affichage, accentué (« Abomey-Calavi », « Sèmè-Podji »).</param>
/// <param name="DepartmentCode">Code du département de rattachement.</param>
public sealed record BeninCommune(string Code, string Name, string DepartmentCode)
{
    /// <summary>Département de rattachement, résolu depuis <see cref="DepartmentCode"/>.</summary>
    public BeninDepartment Department => BeninGeography.Department(DepartmentCode)!;
}


/// <summary>Une commune telle qu'elle est servie aux applications.</summary>
public sealed record BeninCommuneView(string Code, string Name, string DepartmentCode, string DepartmentName);

/// <summary>Un département tel qu'il est servi aux applications.</summary>
public sealed record BeninDepartmentView(string Code, string Name);

/// <summary>Charge utile complète du référentiel, prête à sérialiser.</summary>
public sealed record BeninGeographyReference(
    string CountryCode,
    string DialingCode,
    int PhoneLength,
    IReadOnlyList<BeninDepartmentView> Departments,
    IReadOnlyList<BeninCommuneView> Communes);

/// <summary>DÉCOUPAGE ADMINISTRATIF DU BÉNIN — 12 DÉPARTEMENTS, 77 COMMUNES.</summary>
public static class BeninGeography
{
    /// <summary>Indicatif téléphonique du Bénin.</summary>
    public const string DialingCode = "+229";

    /// <summary>Longueur du numéro national, hors indicatif, depuis la migration de 2024.</summary>
    public const int LocalPhoneLength = 10;

    /// <summary>Code pays ISO 3166-1 alpha-2.</summary>
    public const string CountryCode = "BJ";

    public static IReadOnlyList<BeninDepartment> Departments { get; } =
    [
        new("alibori", "Alibori"),
        new("atacora", "Atacora"),
        new("atlantique", "Atlantique"),
        new("borgou", "Borgou"),
        new("collines", "Collines"),
        new("couffo", "Couffo"),
        new("donga", "Donga"),
        new("littoral", "Littoral"),
        new("mono", "Mono"),
        new("oueme", "Ouémé"),
        new("plateau", "Plateau"),
        new("zou", "Zou"),
    ];

    public static IReadOnlyList<BeninCommune> Communes { get; } =
    [
        // ── Alibori (6) ──────────────────────────────────────────────────────────
        new("banikoara", "Banikoara", "alibori"),
        new("gogounou", "Gogounou", "alibori"),
        new("kandi", "Kandi", "alibori"),
        new("karimama", "Karimama", "alibori"),
        new("malanville", "Malanville", "alibori"),
        new("segbana", "Ségbana", "alibori"),

        // ── Atacora (9) ──────────────────────────────────────────────────────────
        new("boukoumbe", "Boukoumbé", "atacora"),
        new("cobly", "Cobly", "atacora"),
        new("kerou", "Kérou", "atacora"),
        new("kouande", "Kouandé", "atacora"),
        new("materi", "Matéri", "atacora"),
        new("natitingou", "Natitingou", "atacora"),
        new("ouassa-pehunco", "Ouassa-Péhunco", "atacora"),
        new("tanguieta", "Tanguiéta", "atacora"),
        new("toucountouna", "Toucountouna", "atacora"),

        // ── Atlantique (8) ───────────────────────────────────────────────────────
        new("abomey-calavi", "Abomey-Calavi", "atlantique"),
        new("allada", "Allada", "atlantique"),
        new("kpomasse", "Kpomassè", "atlantique"),
        new("ouidah", "Ouidah", "atlantique"),
        new("so-ava", "Sô-Ava", "atlantique"),
        new("toffo", "Toffo", "atlantique"),
        new("tori-bossito", "Tori-Bossito", "atlantique"),
        new("ze", "Zè", "atlantique"),

        // ── Borgou (8) ───────────────────────────────────────────────────────────
        new("bembereke", "Bembéréké", "borgou"),
        new("kalale", "Kalalé", "borgou"),
        new("n-dali", "N'Dali", "borgou"),
        new("nikki", "Nikki", "borgou"),
        new("parakou", "Parakou", "borgou"),
        new("perere", "Pèrèrè", "borgou"),
        new("sinende", "Sinendé", "borgou"),
        new("tchaourou", "Tchaourou", "borgou"),

        // ── Collines (6) ─────────────────────────────────────────────────────────
        new("bante", "Bantè", "collines"),
        new("dassa-zoume", "Dassa-Zoumè", "collines"),
        new("glazoue", "Glazoué", "collines"),
        new("ouesse", "Ouèssè", "collines"),
        new("savalou", "Savalou", "collines"),
        new("save", "Savè", "collines"),

        // ── Couffo (6) ───────────────────────────────────────────────────────────
        new("aplahoue", "Aplahoué", "couffo"),
        new("djakotomey", "Djakotomey", "couffo"),
        new("dogbo", "Dogbo", "couffo"),
        new("klouekanme", "Klouékanmè", "couffo"),
        new("lalo", "Lalo", "couffo"),
        new("toviklin", "Toviklin", "couffo"),

        // ── Donga (4) ────────────────────────────────────────────────────────────
        new("bassila", "Bassila", "donga"),
        new("copargo", "Copargo", "donga"),
        new("djougou", "Djougou", "donga"),
        new("ouake", "Ouaké", "donga"),

        // ── Littoral (1) ─────────────────────────────────────────────────────────
        new("cotonou", "Cotonou", "littoral"),

        // ── Mono (6) ─────────────────────────────────────────────────────────────
        new("athieme", "Athiémé", "mono"),
        new("bopa", "Bopa", "mono"),
        new("come", "Comè", "mono"),
        new("grand-popo", "Grand-Popo", "mono"),
        new("houeyogbe", "Houéyogbé", "mono"),
        new("lokossa", "Lokossa", "mono"),

        // ── Ouémé (9) ────────────────────────────────────────────────────────────
        new("adjarra", "Adjarra", "oueme"),
        new("adjohoun", "Adjohoun", "oueme"),
        new("aguegues", "Aguégués", "oueme"),
        new("akpro-misserete", "Akpro-Missérété", "oueme"),
        new("avrankou", "Avrankou", "oueme"),
        new("bonou", "Bonou", "oueme"),
        new("dangbo", "Dangbo", "oueme"),
        new("porto-novo", "Porto-Novo", "oueme"),
        new("seme-podji", "Sèmè-Podji", "oueme"),

        // ── Plateau (5) ──────────────────────────────────────────────────────────
        new("adja-ouere", "Adja-Ouèrè", "plateau"),
        new("ifangni", "Ifangni", "plateau"),
        new("ketou", "Kétou", "plateau"),
        new("pobe", "Pobè", "plateau"),
        new("sakete", "Sakété", "plateau"),

        // ── Zou (9) ──────────────────────────────────────────────────────────────
        new("abomey", "Abomey", "zou"),
        new("agbangnizoun", "Agbangnizoun", "zou"),
        new("bohicon", "Bohicon", "zou"),
        new("cove", "Covè", "zou"),
        new("djidja", "Djidja", "zou"),
        new("ouinhi", "Ouinhi", "zou"),
        new("zagnanado", "Zagnanado", "zou"),
        new("za-kpota", "Za-Kpota", "zou"),
        new("zogbodomey", "Zogbodomey", "zou"),
    ];

    /// <summary>
    /// LE RÉFÉRENTIEL PRÊT À SERVIR — CONSTRUIT PARESSEUSEMENT, ET C'EST
    /// OBLIGATOIRE.
    /// </summary>
    public static BeninGeographyReference Reference => LazyReference.Value;

    private static readonly Lazy<BeninGeographyReference> LazyReference = new(() => new(
        CountryCode,
        DialingCode,
        LocalPhoneLength,
        [.. Departments.Select(d => new BeninDepartmentView(d.Code, d.Name))],
        [.. Communes
            .OrderBy(c => c.Code, StringComparer.Ordinal)
            .Select(c => new BeninCommuneView(c.Code, c.Name, c.DepartmentCode, c.Department.Name))]));

    private static readonly Dictionary<string, BeninCommune> ByCode =
        Communes.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, BeninDepartment> DepartmentsByCode =
        Departments.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>Index de RATTRAPAGE : libellé normalisé → commune.</summary>
    private static readonly Dictionary<string, BeninCommune> ByNormalizedName = BuildNameIndex();

    public static BeninDepartment? Department(string? code)
        => string.IsNullOrWhiteSpace(code) ? null
            : DepartmentsByCode.TryGetValue(code.Trim(), out var d) ? d : null;

    /// <summary>Commune correspondant à un code stocké.</summary>
    public static BeninCommune? Commune(string? code)
        => string.IsNullOrWhiteSpace(code) ? null
            : ByCode.TryGetValue(code.Trim(), out var c) ? c : null;

    /// <summary>Le code est-il celui d'une commune connue ?</summary>
    public static bool IsKnownCommune(string? code) => Commune(code) is not null;

    /// <summary>Libellé d'affichage d'un code stocké.</summary>
    public static string CommuneName(string? code) => Commune(code)?.Name ?? (code ?? string.Empty).Trim();

    /// <summary>
    /// Retrouve une commune à partir d'un LIBELLÉ saisi librement (reprise de
    /// données, import CSV).
    /// </summary>
    public static BeninCommune? MatchByName(string? name)
    {
        var key = Normalize(name);
        return key.Length == 0 ? null : ByNormalizedName.GetValueOrDefault(key);
    }

    /// <summary>
    /// Résout une valeur d'entrée en CODE de commune, qu'elle soit déjà un code ou
    /// un libellé.
    /// </summary>
    public static string? ResolveCommuneCode(string? codeOrName)
        => Commune(codeOrName)?.Code ?? MatchByName(codeOrName)?.Code;

    // ── Téléphone ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalise un numéro béninois en <c> +229XXXXXXXXXX</c>, ou <c> null</c> s'il
    /// ne peut pas l'être.
    /// </summary>
    public static string? NormalizePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());

        // « 00229… » (préfixe international à l'ancienne) puis « 229… ».
        if (digits.StartsWith("00229", StringComparison.Ordinal))
        {
            digits = digits[5..];
        }
        else if (digits.StartsWith("229", StringComparison.Ordinal) && digits.Length > LocalPhoneLength)
        {
            digits = digits[3..];
        }

        return digits.Length == LocalPhoneLength ? DialingCode + digits : null;
    }

    /// <summary>Le numéro est-il un numéro béninois valide ?</summary>
    public static bool IsValidPhone(string? input) => NormalizePhone(input) is not null;

    // ── Interne ─────────────────────────────────────────────────────────────────

    private static Dictionary<string, BeninCommune> BuildNameIndex()
    {
        var index = new Dictionary<string, BeninCommune>(StringComparer.Ordinal);

        foreach (var commune in Communes)
        {
            // Le libellé accentué ET le code : « Sèmè-Podji » comme « seme-podji »
            // se normalisent vers la même clé, mais on ajoute les deux par prudence
            // (une orthographe future pourrait diverger du code).
            index.TryAdd(Normalize(commune.Name), commune);
            index.TryAdd(Normalize(commune.Code), commune);
        }

        // Variantes réellement rencontrées, qu'aucune normalisation mécanique ne
        // rattrape.
        AddAlias(index, "seme kpodji", "seme-podji");
        AddAlias(index, "semekpodji", "seme-podji");
        AddAlias(index, "pehunco", "ouassa-pehunco");
        AddAlias(index, "dogbo tota", "dogbo");
        AddAlias(index, "calavi", "abomey-calavi");
        AddAlias(index, "porto novo", "porto-novo");

        return index;
    }

    private static void AddAlias(Dictionary<string, BeninCommune> index, string alias, string communeCode)
    {
        if (ByCode.TryGetValue(communeCode, out var commune))
        {
            index.TryAdd(Normalize(alias), commune);
        }
    }

    /// <summary>Minuscules, sans accent, sans ponctuation, espaces repliés.</summary>
    public static string FoldForKey(string? value) => Normalize(value);

    /// <summary>REPLI D'ACCENTS PAR TABLE EXPLICITE — PAS PAR `string.Normalize(FormD)`.</summary>
    private const string AccentedChars = "àáâãäåçèéêëìíîïñòóôõöùúûüýÿÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝ";

    private const string FoldedChars = "aaaaaaceeeeiiiinooooouuuuyyaaaaaaceeeeiiiinooooouuuuy";

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = false;

        foreach (var raw in value.Trim())
        {
            // Repli d'accent d'abord, minuscule ASCII ensuite : on ne dépend ni de
            // l'ICU, ni de la culture courante, ni du mode de globalisation.
            var index = AccentedChars.IndexOf(raw);
            var ch = index >= 0 ? FoldedChars[index] : char.ToLowerInvariant(raw);

            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSeparator = false;
                continue;
            }

            // Tirets, apostrophes, espaces, soulignés : un seul séparateur logique.
            if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append(' ');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().TrimEnd();
    }
}
