<#
.SYNOPSIS
  Construit, empaquete (Velopack) et publie une version de Poke-widget.
.EXAMPLE
  ./build/release.ps1 -Version 1.0.0            # build local : rien n'est envoye
.EXAMPLE
  ./build/release.ps1 -Version 1.1.0 -Upload    # + release GitHub (GITHUB_TOKEN ou -Token requis)
.EXAMPLE
  ./build/release.ps1 -Version 1.1.0 -FrameworkDependent   # paquet leger : le setup installe le runtime .NET si besoin
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$Upload,
    [switch]$FrameworkDependent,
    [string]$Token = $env:GITHUB_TOKEN
)

$ErrorActionPreference = 'Stop'

# --- A adapter si besoin ---
$RepoUrl = 'https://github.com/ulgrude/widget-pokemon'
$PackId  = 'PokeWidget'
$Title   = 'Poke-widget'   # ASCII : PowerShell 5.1 transmet mal les accents aux outils externes
$Authors = 'Ulgrude'
# ---------------------------

$Root     = Split-Path $PSScriptRoot -Parent
$Proj     = Join-Path $Root 'PokeWidget.App/PokeWidget.App.csproj'
$Publish  = Join-Path $Root 'publish'
$Releases = Join-Path $Root 'Releases'

function Check([string]$what) {
    if ($LASTEXITCODE -ne 0) { throw "$what a echoue (code $LASTEXITCODE)" }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version invalide '$Version' : attendu X.Y.Z (ex. 1.2.0)" }
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"

Write-Host "==> Tests" -ForegroundColor Cyan
dotnet test (Join-Path $Root 'PokeWidget.Core.Tests') -c Release
Check 'dotnet test'

Write-Host "==> Restauration" -ForegroundColor Cyan
dotnet restore $Proj
Check 'dotnet restore'

# vpk doit avoir la MEME version que le package Velopack reference par l'application
$assets = Get-Content (Join-Path $Root 'PokeWidget.App/obj/project.assets.json') -Raw | ConvertFrom-Json
$lib = $assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'Velopack/*' } | Select-Object -First 1
if (-not $lib) { throw "Package Velopack introuvable dans project.assets.json" }
$VpkVersion = $lib -replace '^Velopack/', ''
Write-Host "==> vpk $VpkVersion" -ForegroundColor Cyan
dotnet tool update -g vpk --version $VpkVersion
Check 'installation de vpk'

Write-Host "==> Publication $Version" -ForegroundColor Cyan
if (Test-Path $Publish) { Remove-Item $Publish -Recurse -Force }
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
dotnet publish $Proj -c Release -r win-x64 --self-contained $selfContained "-p:Version=$Version" -o $Publish
Check 'dotnet publish'

if ($Upload) {
    # Recupere les versions precedentes pour generer des mises a jour "delta" (petites)
    Write-Host "==> Versions precedentes (pour les deltas)" -ForegroundColor Cyan
    $dl = @('download', 'github', '--repoUrl', $RepoUrl, '--outputDir', $Releases)
    if ($Token) { $dl += @('--token', $Token) }
    vpk @dl
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'Aucune version precedente recuperee (normal pour la premiere release) : pas de delta.'
        $global:LASTEXITCODE = 0
    }
}

Write-Host "==> Paquet Velopack" -ForegroundColor Cyan
$pack = @('pack', '--packId', $PackId, '--packVersion', $Version, '--packDir', $Publish,
          '--mainExe', 'PokeWidget.exe', '--packTitle', $Title, '--packAuthors', $Authors, '--outputDir', $Releases,
          '--icon', (Join-Path $Root 'PokeWidget.App/Assets/app.ico'))
if ($FrameworkDependent) {
    $tfm = ([xml](Get-Content (Join-Path $Root 'Directory.Build.props'))).Project.PropertyGroup.DotNetVersion | Where-Object { $_ } | Select-Object -First 1
    $pack += @('--framework', "$tfm-x64-desktop")
}
vpk @pack
Check 'vpk pack'

if ($Upload) {
    if (-not $Token) { throw "-Upload demande un jeton GitHub (variable GITHUB_TOKEN ou parametre -Token)" }
    Write-Host "==> Envoi sur GitHub (v$Version)" -ForegroundColor Cyan
    vpk upload github --repoUrl $RepoUrl --token $Token --publish --releaseName "$Title $Version" --tag "v$Version" --outputDir $Releases
    Check 'vpk upload'
}

Write-Host ""
Write-Host "Termine. Installateur : $(Join-Path $Releases "$PackId-win-Setup.exe")" -ForegroundColor Green
