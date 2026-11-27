# SaveSpace

SaveSpace is een Windows-app om notities, bestanden en projecten op één plek te bewaren. Je kunt notities schrijven en automatisch laten opslaan, bestanden eraan toevoegen en ze samen als `.noteagent`-pakket bewaren. Bijlagen kunnen onder meer afbeeldingen, video's, muziek en documenten zijn.

## Downloaden en starten op Windows

Download de nieuwste `SaveSpace-windows-x64.zip` op de [Releases-pagina](https://github.com/RedEagleFire/SaveSpace-v1/releases) en pak het ZIP-bestand uit. Start daarna `SaveSpace.exe`. De app bevat .NET 10, dus de .NET SDK of een aparte .NET-installatie is niet nodig.

SaveSpace werkt op Windows 10/11 voor 64-bit pc's. De ingebouwde browser en documentvoorbeelden vereisen de **Microsoft Edge WebView2 Runtime**. Die staat al op de meeste recente Windows-installaties. Als SaveSpace meldt dat WebView2 ontbreekt, start dan `WebView2RuntimeInstaller.exe` uit het uitgepakte ZIP-bestand en probeer SaveSpace opnieuw. De installer haalt de runtime op van Microsoft; internettoegang is nodig. Zie ook de [WebView2 Runtime-documentatie](https://developer.microsoft.com/microsoft-edge/webview2/).

## Zelf bouwen

Voor ontwikkelen of lokaal starten is de .NET 10 SDK nodig. Voer vanuit deze map uit:

```powershell
dotnet run --project SaveSpace.csproj
```
