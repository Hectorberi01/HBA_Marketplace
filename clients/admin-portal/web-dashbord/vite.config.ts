import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

/**
 * MANDATAIRE DE DÉVELOPPEMENT.
 *
 * POURQUOI IL EXISTE.
 *
 * Le navigateur refuse `http://localhost:5173` -> `https://api.hba-express.com` :
 *
 *   Response to preflight request doesn't pass access control check:
 *   No 'Access-Control-Allow-Origin' header is present
 *
 * Ce n'est pas une panne du portail ni de l'API. C'est la politique d'origine
 * du navigateur, et elle a raison : la passerelle n'expose AUCUNE configuration
 * CORS — ni `AddCors` ni `UseCors` dans apps/api-gateway. Aucune origine web
 * n'est donc autorisée, ce qui n'avait jamais gêné personne tant que les seuls
 * clients étaient l'application mobile et les tests d'intégration, qui ne sont
 * pas des navigateurs.
 *
 * Le serveur de développement relaie donc les appels : le navigateur ne parle
 * qu'à `localhost:5173`, une seule origine, et la question du CORS ne se pose
 * plus. C'est un contournement DE DÉVELOPPEMENT.
 *
 * CE QUE CELA NE RÉSOUT PAS : le portail une fois DÉPLOYÉ. Servi depuis son
 * propre nom d'hôte, il retombera exactement sur cette erreur, et là seul le
 * serveur peut la lever — en déclarant les origines autorisées dans la
 * passerelle. Ce mandataire ne fait que déplacer l'échéance.
 *
 * `changeOrigin: true` N'EST PAS UN DÉTAIL. Sans lui, l'en-tête `Host` reste
 * `localhost:5173`, la règle Traefik `Host(\`api.hba-express.com\`)` ne
 * correspond plus, et la réponse est un 404 qui ne parle ni de mandataire ni
 * d'en-tête.
 */
export default defineConfig(({ mode }) => {
    const env = loadEnv(mode, process.cwd(), 'VITE_')
    const cible = env.VITE_API_PROXY_TARGET ?? 'https://api.hba-express.com'

    return {
        plugins: [react()],
        server: {
            proxy: {
                '/api': {
                    target: cible,
                    changeOrigin: true,
                    secure: true,
                },
            },
        },
    }
})
