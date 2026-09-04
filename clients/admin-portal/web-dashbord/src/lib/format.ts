/**
 * FORMATAGE.
 *
 * LA DEVISE VIENT DE LA DONNÉE, JAMAIS D'UNE CONSTANTE.
 *
 * La plateforme vise l'UEMOA, donc le franc CFA, mais la commande porte son
 * propre champ `currency` — et une commande en euro affichée « 12 000 FCFA »
 * serait fausse d'un facteur six cent cinquante, sans que rien ne le signale.
 *
 * `fr-FR` explicite plutôt que la locale du navigateur : la console est en
 * français, et laisser le navigateur décider donnerait des séparateurs de
 * milliers différents d'un poste à l'autre pour les mêmes chiffres.
 */

export function formaterMontant(montant: number, devise: string): string {
    try {
        return new Intl.NumberFormat('fr-FR', {
            style: 'currency',
            currency: devise,
            // Le franc CFA n'a pas de sous-unité : deux décimales y sont
            // toujours « ,00 » et n'ajoutent que du bruit.
            maximumFractionDigits: devise === 'XOF' ? 0 : 2,
        }).format(montant)
    } catch {
        // Un code devise absent du registre ISO fait lever `Intl`. On rend
        // quand même le chiffre : perdre le symbole vaut mieux que perdre la
        // ligne entière derrière un écran d'erreur.
        return `${montant} ${devise}`
    }
}

/** Date courte et heure : sur une console d'exploitation, l'heure compte. */
export function formaterDate(iso: string): string {
    const d = new Date(iso)
    if (Number.isNaN(d.getTime())) return iso
    return new Intl.DateTimeFormat('fr-FR', {
        dateStyle: 'short',
        timeStyle: 'short',
    }).format(d)
}

/**
 * Identifiant abrégé pour l'affichage.
 *
 * Un GUID complet occupe une colonne entière et ne se lit pas. Les huit
 * premiers caractères suffisent à distinguer deux lignes à l'œil ; l'entier
 * reste dans l'attribut `title` et se copie d'un clic droit.
 */
export function abreger(id: string): string {
    return id.length > 8 ? id.slice(0, 8) : id
}
