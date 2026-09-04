import { useEffect, useState } from 'react'

/**
 * CHAMP DE RECHERCHE, AVEC TEMPORISATION.
 *
 * Sans elle, chaque frappe déclenche une requête : taper « samsung » en envoie
 * sept, dont six dont personne ne lira jamais le résultat. Sur une console
 * d'administration branchée sur la production, ce n'est pas seulement du
 * gaspillage — c'est six requêtes de plus à servir pour chaque recherche de
 * chaque utilisateur.
 *
 * LA VALEUR AFFICHÉE RESTE LOCALE ET IMMÉDIATE. Attendre la temporisation pour
 * afficher les lettres saisies donnerait un champ qui traîne, et c'est le
 * défaut classique de cette mécanique : on temporise la REQUÊTE, jamais le
 * rendu du texte.
 */
export default function BarreRecherche({
    valeur,
    onChange,
    placeholder,
    delai = 350,
}: {
    valeur: string
    onChange: (v: string) => void
    placeholder: string
    delai?: number
}) {
    const [saisie, setSaisie] = useState(valeur)

    /*
     * RESYNCHRONISATION PENDANT LE RENDU, ET NON DANS UN EFFET.
     *
     * La valeur peut changer sans passer par ce champ : bouton « précédent »,
     * lien partagé, remise à zéro d'un filtre. Il faut alors réaligner la
     * saisie affichée.
     *
     * La première version le faisait dans un `useEffect`. Cela marche, mais
     * React rend d'abord l'ancienne saisie, valide, puis rend une seconde fois
     * — l'ancien texte apparaît une image avant d'être remplacé, et oxlint le
     * signale (`react(set-state-in-effect)`).
     *
     * La comparaison pendant le rendu est le motif documenté par React pour
     * « ajuster un état quand une prop change » : la seconde passe a lieu AVANT
     * l'affichage, donc rien de périmé n'atteint l'écran.
     */
    const [valeurVue, setValeurVue] = useState(valeur)
    if (valeur !== valeurVue) {
        setValeurVue(valeur)
        setSaisie(valeur)
    }

    useEffect(() => {
        if (saisie === valeur) return
        const minuterie = setTimeout(() => onChange(saisie), delai)
        return () => clearTimeout(minuterie)
    }, [saisie, valeur, delai, onChange])

    return (
        <div className="barre-recherche">
            <input
                type="search"
                value={saisie}
                placeholder={placeholder}
                aria-label={placeholder}
                onChange={e => setSaisie(e.target.value)}
            />
            {saisie && (
                <button type="button" onClick={() => setSaisie('')} aria-label="Effacer la recherche">
                    ×
                </button>
            )}
        </div>
    )
}
