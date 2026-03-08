param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("x64")]
    [string]$Platform = "x64",

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$BannerlordVersion = "1.3.15",

    [ValidateRange(0, 99)]
    [int]$VersionRevision = 1,

    [switch]$IncludeLetMeFight,
    [string]$LetMeFightRepoPath,

    [switch]$SkipBuild,
    [switch]$DocumentsOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $repoRoot) {
    throw "Could not determine repository root from script location."
}

$repoParent = Split-Path -Parent $repoRoot

$moduleVersion = "v$BannerlordVersion.$VersionRevision"
Write-Host "[version] Using module version: $moduleVersion"

$moduleTargets = @()
$docsModules = Join-Path $env:USERPROFILE "Documents\Mount and Blade II Bannerlord\Modules"
$moduleTargets += $docsModules
$steamModules = $null

if (-not $DocumentsOnly) {
    $programFilesX86 = ${env:ProgramFiles(x86)}
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $steamModules = Join-Path $programFilesX86 "Steam\steamapps\common\Mount & Blade II Bannerlord\Modules"
        if (Test-Path $steamModules) {
            $moduleTargets += $steamModules
        }
        else {
            Write-Host "[deploy] Steam modules path not found, skipping: $steamModules"
        }
    }
}

function Invoke-ProjectBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    Write-Host "[build] $ProjectPath ($Configuration|$Platform)"
    & dotnet build $ProjectPath -c $Configuration -p:Platform=$Platform
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed: $ProjectPath"
    }
}

function Set-ModuleVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SubModulePath,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    if (-not (Test-Path $SubModulePath)) {
        throw "SubModule.xml not found: $SubModulePath"
    }

    [xml]$xml = Get-Content -Path $SubModulePath
    if ($null -eq $xml.Module -or $null -eq $xml.Module.Version) {
        throw "Could not find <Version> node in $SubModulePath"
    }

    $oldValue = $xml.Module.Version.value
    if ($oldValue -ne $Version) {
        $xml.Module.Version.value = $Version
        $xml.Save($SubModulePath)
        Write-Host "[version] $SubModulePath : $oldValue -> $Version"
    }
    else {
        Write-Host "[version] $SubModulePath already $Version"
    }
}

function Resolve-BuyTroopsOutputDir {
    $dir = Join-Path $repoRoot ("BuyTroops\bin\" + $Platform + "\" + $Configuration)
    if (Test-Path $dir) {
        return $dir
    }

    throw "BuyTroops output directory not found: $dir"
}

function Resolve-PaigeFixesOutputDir {
    $dir = Join-Path $repoRoot "PaigeBannerlordWarsailsFixes\bin\Win64_Shipping_Client"
    if (Test-Path $dir) {
        return $dir
    }

    throw "PaigeBannerlordWarsailsFixes output directory not found: $dir"
}

function Resolve-LetMeFightOutputDir {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LetMeFightRoot
    )

    $candidates = @(
        (Join-Path $LetMeFightRoot ("BuyTroops\bin\" + $Platform + "\" + $Configuration)),
        (Join-Path $LetMeFightRoot "BuyTroops\bin\Win64_Shipping_Client"),
        (Join-Path $docsModules "LetMeFight\bin\Win64_Shipping_Client")
    )

    if (-not [string]::IsNullOrWhiteSpace($steamModules)) {
        $candidates += (Join-Path $steamModules "LetMeFight\bin\Win64_Shipping_Client")
    }

    foreach ($candidate in $candidates) {
        $dll = Join-Path $candidate "LetMeFight.dll"
        if (Test-Path $dll) {
            return $candidate
        }
    }

    throw "LetMeFight output directory not found. Checked: $($candidates -join '; ')"
}

function Deploy-ModuleBinary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ModuleName,

        [Parameter(Mandatory = $true)]
        [string]$SubModulePath,

        [Parameter(Mandatory = $true)]
        [string]$BinaryOutputDir,

        [Parameter(Mandatory = $true)]
        [string]$AssemblyName
    )

    if (-not (Test-Path $subModulePath)) {
        throw "Missing SubModule.xml for $ModuleName at $subModulePath"
    }

    $dllPath = Join-Path $BinaryOutputDir ($AssemblyName + ".dll")
    if (-not (Test-Path $dllPath)) {
        throw "Missing DLL for $ModuleName at $dllPath"
    }

    $pdbPath = Join-Path $BinaryOutputDir ($AssemblyName + ".pdb")

    foreach ($targetRoot in $moduleTargets) {
        $targetModuleRoot = Join-Path $targetRoot $ModuleName
        $targetBinDir = Join-Path $targetModuleRoot "bin\Win64_Shipping_Client"

        New-Item -ItemType Directory -Path $targetBinDir -Force | Out-Null

        Copy-Item $subModulePath (Join-Path $targetModuleRoot "SubModule.xml") -Force
        Copy-Item $dllPath (Join-Path $targetBinDir ($AssemblyName + ".dll")) -Force

        if (Test-Path $pdbPath) {
            Copy-Item $pdbPath (Join-Path $targetBinDir ($AssemblyName + ".pdb")) -Force
        }

        Write-Host "[deploy] $ModuleName -> $targetModuleRoot"
    }
}

function Remove-StaleModuleBinary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ModuleName,

        [Parameter(Mandatory = $true)]
        [string]$AssemblyName
    )

    foreach ($targetRoot in $moduleTargets) {
        $targetBinDir = Join-Path (Join-Path $targetRoot $ModuleName) "bin\Win64_Shipping_Client"
        foreach ($ext in @("dll", "pdb")) {
            $filePath = Join-Path $targetBinDir ($AssemblyName + "." + $ext)
            if (Test-Path $filePath) {
                Remove-Item $filePath -Force
                Write-Host "[cleanup] Removed stale file: $filePath"
            }
        }
    }
}

function New-ReleaseZip {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ModuleName,

        [Parameter(Mandatory = $true)]
        [string]$SubModulePath,

        [Parameter(Mandatory = $true)]
        [string]$BinaryOutputDir,

        [Parameter(Mandatory = $true)]
        [string]$AssemblyName,

        [Parameter(Mandatory = $true)]
        [string]$PackageName,

        [Parameter(Mandatory = $true)]
        [string]$ModuleVersion
    )

    $dllPath = Join-Path $BinaryOutputDir ($AssemblyName + ".dll")
    if (-not (Test-Path $dllPath)) {
        throw "Missing DLL for $ModuleName at $dllPath"
    }

    $releaseDir = Join-Path (Join-Path $repoRoot "releases") $ModuleVersion
    $stagingRoot = Join-Path $env:TEMP ("buytroops-release-" + $PackageName + "-" + [Guid]::NewGuid().ToString("N"))
    $stagingModuleRoot = Join-Path $stagingRoot $ModuleName
    $stagingBinDir = Join-Path $stagingModuleRoot "bin\Win64_Shipping_Client"
    $zipPath = Join-Path $releaseDir ($PackageName + " " + $ModuleVersion + ".zip")
    $pdbPath = Join-Path $BinaryOutputDir ($AssemblyName + ".pdb")

    try {
        New-Item -ItemType Directory -Path $stagingBinDir -Force | Out-Null
        New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

        Copy-Item $SubModulePath (Join-Path $stagingModuleRoot "SubModule.xml") -Force
        Copy-Item $dllPath (Join-Path $stagingBinDir ($AssemblyName + ".dll")) -Force

        if (Test-Path $pdbPath) {
            Copy-Item $pdbPath (Join-Path $stagingBinDir ($AssemblyName + ".pdb")) -Force
        }

        if (Test-Path $zipPath) {
            Remove-Item $zipPath -Force
        }

        Compress-Archive -Path $stagingModuleRoot -DestinationPath $zipPath -Force
        Write-Host "[package] $PackageName -> $zipPath"
    }
    finally {
        if (Test-Path $stagingRoot) {
            Remove-Item $stagingRoot -Recurse -Force
        }
    }
}

$buyTroopsSubModulePath = Join-Path $repoRoot "SubModule.xml"
$paigeFixesSubModulePath = Join-Path $repoRoot "PaigeBannerlordWarsailsFixes\SubModule.xml"
$letMeFightProjectPath = $null
$letMeFightSubModulePath = $null
$hasLetMeFight = $false

if ($IncludeLetMeFight) {
    if ([string]::IsNullOrWhiteSpace($LetMeFightRepoPath)) {
        $LetMeFightRepoPath = Join-Path $repoParent "letmefight"
    }

    $letMeFightProjectPath = Join-Path $LetMeFightRepoPath "BuyTroops\LetMeFight.csproj"
    $letMeFightSubModulePath = Join-Path $LetMeFightRepoPath "SubModule.xml"
    $hasLetMeFight = (Test-Path $letMeFightProjectPath) -and (Test-Path $letMeFightSubModulePath)
}

Set-ModuleVersion -SubModulePath $buyTroopsSubModulePath -Version $moduleVersion
Set-ModuleVersion -SubModulePath $paigeFixesSubModulePath -Version $moduleVersion
if ($IncludeLetMeFight -and $hasLetMeFight) {
    Set-ModuleVersion -SubModulePath $letMeFightSubModulePath -Version $moduleVersion
}
elseif ($IncludeLetMeFight) {
    Write-Host "[build] LetMeFight repo not found at $LetMeFightRepoPath, skipping build/deploy."
}

if (-not $SkipBuild) {
    Invoke-ProjectBuild (Join-Path $repoRoot "BuyTroops\BuyTroops.csproj")
    Invoke-ProjectBuild (Join-Path $repoRoot "PaigeBannerlordWarsailsFixes\PaigeBannerlordWarsailsFixes.csproj")
    if ($IncludeLetMeFight -and $hasLetMeFight) {
        Invoke-ProjectBuild $letMeFightProjectPath
    }
}

$buyTroopsOut = Resolve-BuyTroopsOutputDir
$paigeFixesOut = Resolve-PaigeFixesOutputDir

Deploy-ModuleBinary `
    -ModuleName "BuyTroops" `
    -SubModulePath (Join-Path $repoRoot "SubModule.xml") `
    -BinaryOutputDir $buyTroopsOut `
    -AssemblyName "BuyTroops"

Deploy-ModuleBinary `
    -ModuleName "PaigeBannerlordWarsailsFixes" `
    -SubModulePath $paigeFixesSubModulePath `
    -BinaryOutputDir $paigeFixesOut `
    -AssemblyName "PaigeFixes"
Remove-StaleModuleBinary `
    -ModuleName "PaigeBannerlordWarsailsFixes" `
    -AssemblyName "PaigeBannerlordWarsailsFixes"
New-ReleaseZip `
    -ModuleName "PaigeBannerlordWarsailsFixes" `
    -SubModulePath $paigeFixesSubModulePath `
    -BinaryOutputDir $paigeFixesOut `
    -AssemblyName "PaigeFixes" `
    -PackageName "PaigeFixes" `
    -ModuleVersion $moduleVersion

if ($IncludeLetMeFight -and $hasLetMeFight) {
    $letMeFightOut = Resolve-LetMeFightOutputDir -LetMeFightRoot $LetMeFightRepoPath
    Deploy-ModuleBinary `
        -ModuleName "LetMeFight" `
        -SubModulePath $letMeFightSubModulePath `
        -BinaryOutputDir $letMeFightOut `
        -AssemblyName "LetMeFight"
}

Write-Host "[done] Build and deploy complete for version $moduleVersion."
