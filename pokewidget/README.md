# PokeWidget (C# / WPF)

Portage du userscript : logique du jeu (`PokeWidget.Core`, testée) et application WPF (`PokeWidget.App`).

## Mise en route
```
dotnet new sln -n PokeWidget
dotnet sln add PokeWidget.Core PokeWidget.Core.Tests PokeWidget.App
dotnet test
dotnet run --project PokeWidget.App
```
La version de .NET se change dans `Directory.Build.props` (`DotNetVersion`).

## Tests de non-régression de la seed
`PokeWidget.Core.Tests/Vectors.g.cs` est généré par `node tools/gen-vectors.js` à partir des fonctions JS d'origine.
Si les tests `Hash_and_rng_match_the_userscript` / `Spawn_matches_the_userscript` passent, une même seed donne
les mêmes Pokémon que le userscript.

## Contenu
- `SeededRng`, `SpawnService` : RNG et apparitions (port exact du JS)
- `Balls` (BallType, CatchMath) : probabilités et types de balls
- `GameState` : balls, rattrapage horaire, lancer, Pokédex
- `ProgressFile` : export/import compatible avec l'ancien .json
- `Storage` : chemins, JSON atomique, `GameRepository`

## Test manuel de PokeWidget.App
1. Le widget apparaît en bas à droite, sans icône dans la barre des tâches ni dans Alt+Tab.
2. Clic droit : réduire/agrandir, mode (bureau / au-dessus), position, quitter.
3. Mode **bureau** : ouvre une fenêtre par-dessus (le widget passe dessous), puis fais Win+D. Note ce qui se passe.
4. Icône réduite : elle se balance quand un nouveau Pokémon est apparu (tant qu'il n'a pas été vu en grand), et brille si chromatique.
5. Lance des balls : la ball est consommée tout de suite, le résultat s'affiche après la secousse.
6. Relance l'application : balls et Pokédex sont conservés (`%AppData%\PokeWidget\state.json`).

## Étape suivante testée à la main : Pokédex, aide, réglages, zone de notification
- Boutons ?, 📖, ⚙️ dans le widget (et dans le menu clic droit / l'icône de la zone de notification).
- Pokédex : recherche (`pika`, `25`, `#25`), chargement des sprites à la demande.
- Réglages : seed, position, mode, lancement avec Windows, export/import avec les boîtes de dialogue Windows.
- Icône de la zone de notification : clic gauche = afficher le widget, clic droit = menu.

## Mises à jour (Releases GitHub)
- Au plus une vérification automatique par jour (nouvel essai dans 1 h en cas d'échec), ~4 s après le lancement puis en continu.
- Une notification (une seule par version) + un lien dans le widget + une entrée dans le menu de la zone de notification.
- Le lien ouvre la page de la release sur github.com (jamais une URL arbitraire).
- Version installée = `<Version>` de `PokeWidget.App.csproj`. Pour publier : incrémenter cette version, créer une release GitHub dont le tag est `vX.Y.Z`.
- Pour tester : mettre `<Version>0.9.0</Version>` en local, créer une release `v1.0.0`, puis « Vérifier maintenant » dans les réglages.

## Installation et mises à jour automatiques (Velopack)
- L'application s'installe avec `PokeWidget-win-Setup.exe` (dans `%LocalAppData%\PokeWidget`, sans droits administrateur).
- La progression reste dans `%AppData%\PokeWidget` : elle survit aux mises à jour **et** à la désinstallation.
- Détection : `UpdateService` (API GitHub). Installation : Velopack (`Installer la version X et redémarrer`).
  Hors installation (dotnet run, dossier copié), le lien ouvre la page de la release.

### Publier une version
```
./build/release.ps1 -Version 1.1.0              # build + paquet en local (dossier Releases/)
./build/release.ps1 -Version 1.1.0 -Upload      # + release GitHub v1.1.0 (GITHUB_TOKEN requis)
```
Ou, automatiquement : `git tag v1.1.0 && git push origin v1.1.0` (workflow `.github/workflows/release.yml`).
Le tag GitHub doit toujours être `vX.Y.Z` avec le même X.Y.Z que la version empaquetée.

## Sauvegarde obfusquée
- La progression est dans `%AppData%\PokeWidget\state.dat` (JSON → GZip → AES-GCM). Toute modification à la main est détectée : le fichier est alors mis de côté (`state.dat.corrupt-...`) et une partie vierge démarre.
- L'ancien `state.json` en clair est migré automatiquement au premier lancement, puis supprimé.
- Export/import : fichiers `.pokesave` (chiffrés). Les anciens exports `.json` restent importables ; mettre `ProgressFile.AllowLegacyJsonImport` à `false` pour fermer cette voie.
- Ce n'est de l'obfuscation, pas de la sécurité : la clé est dans l'exécutable.
