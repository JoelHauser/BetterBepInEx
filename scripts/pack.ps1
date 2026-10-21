<#
.SYNOPSIS
    Builds BetterBepInEx, runs the tests, checks the output's references, and zips it.

.DESCRIPTION
    Run this through PowerShell, not Bash; Bash mangles 'H:\SPT4.1.X' into 'H:SPT4.1.X'.
    The zip holds only the plugin DLL, in the layout of an SPT install. Never a README.

.PARAMETER SPTPath
    The SPT install root, e.g. H:\SPT4.1.X.

.PARAMETER Install
    Also copy the built plugin into the install's BepInEx\plugins.

.EXAMPLE
    scripts\pack.ps1 -SPTPath H:\SPT4.1.X
    scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install
#>
param(
    [Parameter(Mandatory = $true)][string]$SPTPath,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$plugin = Join-Path $root 'src\BetterBepInEx\BetterBepInEx.csproj'
$tests = Join-Path $root 'tests\BetterBepInEx.Tests\BetterBepInEx.Tests.csproj'

if (-not (Test-Path (Join-Path $SPTPath 'BepInEx\core\BepInEx.dll'))) {
    throw "SPTPath '$SPTPath' is not an SPT install root: no BepInEx\core\BepInEx.dll."
}

# ---------------------------------------------------------------- version agreement
$csprojVersion = ([xml](Get-Content $plugin)).Project.PropertyGroup.Version | Where-Object { $_ }
$sourceVersion = (Select-String -Path (Join-Path $root 'src\BetterBepInEx\BetterBepInExPlugin.cs') `
        -Pattern 'PluginVersion\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value

if ($csprojVersion -ne $sourceVersion) {
    throw "Version mismatch: csproj says '$csprojVersion', BetterBepInExPlugin.PluginVersion says '$sourceVersion'."
}
Write-Host "Version $csprojVersion" -ForegroundColor Cyan

# ---------------------------------------------------------------- build and test
Write-Host 'Building...' -ForegroundColor Cyan
dotnet build $plugin -c Release "-p:SPTPath=$SPTPath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Write-Host 'Testing...' -ForegroundColor Cyan
dotnet test $tests --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

# ---------------------------------------------------------------- reference check
# Assembly-CSharp in Managed is not what the game runs (the Launcher's delta renames types), so
# the plugin must not reference it; the one game member it uses is found by name at runtime.
$built = Join-Path $root 'src\BetterBepInEx\bin\Release\BetterBepInEx.dll'
$cecil = Join-Path $SPTPath 'SPT_Runtime\Mono.Cecil.dll'

if (Test-Path $cecil) {
    Add-Type -Path $cecil
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule($built)
    $refs = $module.AssemblyReferences | ForEach-Object { $_.Name }
    $module.Dispose()

    $forbidden = $refs | Where-Object { $_ -like 'Assembly-CSharp*' -or $_ -like 'spt-*' }
    if ($forbidden) {
        throw "The built plugin references the game assembly: $($forbidden -join ', ')"
    }
    Write-Host "References clean: $($refs -join ', ')" -ForegroundColor Green
}
else {
    Write-Warning "No Mono.Cecil at '$cecil'; skipping the reference check."
}

# ---------------------------------------------------------------- stage and zip
$staging = Join-Path $root 'releases\staging'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
$pluginDir = Join-Path $staging 'BepInEx\plugins'
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item $built $pluginDir

# Unpacked over the SPT root, so the layout inside has to be exactly the install's layout. Only
# what the mod needs at runtime goes in; no README or other docs.
$expected = @('BepInEx\plugins\BetterBepInEx.dll')
$actual = Get-ChildItem $staging -Recurse -File |
    ForEach-Object { $_.FullName.Substring($staging.Length + 1) } | Sort-Object
$diff = Compare-Object ($expected | Sort-Object) $actual
if ($diff) {
    throw "Staged layout is wrong:`n$($diff | Format-Table -AutoSize | Out-String)"
}

$zip = Join-Path $root "releases\BetterBepInEx_V$csprojVersion.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item $staging -Recurse -Force
Write-Host "Packed $zip" -ForegroundColor Green

# ---------------------------------------------------------------- install
if ($Install) {
    $dest = Join-Path $SPTPath 'BepInEx\plugins'
    Copy-Item $built $dest -Force
    Write-Host "Installed to $dest" -ForegroundColor Green
}
