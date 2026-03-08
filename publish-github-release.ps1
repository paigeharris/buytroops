param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [string]$Title,
    [string]$Notes = "",
    [switch]$Draft,
    [switch]$Prerelease,
    [switch]$SkipBuild,
    [switch]$IncludeLetMeFight,
    [string]$LetMeFightRepoPath,
    [string]$GitHubToken,
    [string]$EnvFilePath
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        return $PSScriptRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
        return (Split-Path -Parent $PSCommandPath)
    }

    return (Get-Location).Path
}

function Import-DotEnv {
    param([string]$Path)

    $result = @{}
    if (-not (Test-Path $Path)) {
        return $result
    }

    $rawLines = @(Get-Content -Path $Path)
    foreach ($line in $rawLines) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
        if ($trimmed.StartsWith("#")) { continue }
        if ($trimmed.IndexOf("=") -lt 1) { continue }

        $parts = $trimmed.Split("=", 2)
        $key = $parts[0].Trim()
        $value = $parts[1].Trim()

        if ([string]::IsNullOrWhiteSpace($key)) { continue }

        if (
            ($value.StartsWith("'") -and $value.EndsWith("'")) -or
            ($value.StartsWith('"') -and $value.EndsWith('"'))
        ) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        $result[$key] = $value
    }

    # Convenience fallback: allow .env to contain only the token value.
    if ($result.Count -eq 0 -and $rawLines.Count -eq 1) {
        $single = $rawLines[0].Trim()
        if (-not [string]::IsNullOrWhiteSpace($single) -and -not $single.StartsWith("#")) {
            $result["GITHUB_TOKEN"] = $single
        }
    }

    return $result
}

function Get-GitHubRepoFromOrigin {
    param([string]$RepoRoot)

    $origin = (git -C $RepoRoot remote get-url origin).Trim()
    if ([string]::IsNullOrWhiteSpace($origin)) {
        throw "Could not resolve git origin URL."
    }

    $regex = "github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+?)(?:\.git)?$"
    $match = [regex]::Match($origin, $regex, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) {
        throw "Origin is not a GitHub URL: $origin"
    }

    return @{
        Owner = $match.Groups["owner"].Value
        Repo = $match.Groups["repo"].Value
    }
}

function Get-ModuleVersionFromSubModule {
    param([string]$SubModulePath)
    [xml]$xml = Get-Content -Path $SubModulePath
    return $xml.Module.Version.value
}

function New-ModuleZipFromBuild {
    param(
        [string]$RepoRoot,
        [string]$Tag,
        [string]$ModuleName,
        [string]$SubModulePath,
        [string]$DllPath,
        [string]$PdbPath,
        [string]$OutputRoot
    )

    if (-not (Test-Path $SubModulePath)) {
        throw "Missing SubModule.xml for ${ModuleName}: $SubModulePath"
    }
    if (-not (Test-Path $DllPath)) {
        throw "Missing DLL for ${ModuleName}: $DllPath"
    }

    $stagingRoot = Join-Path $OutputRoot $ModuleName
    $stagingModuleRoot = Join-Path $stagingRoot $ModuleName
    $stagingBinDir = Join-Path $stagingModuleRoot "bin\Win64_Shipping_Client"

    if (Test-Path $stagingRoot) {
        Remove-Item $stagingRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $stagingBinDir -Force | Out-Null
    Copy-Item $SubModulePath (Join-Path $stagingModuleRoot "SubModule.xml") -Force
    Copy-Item $DllPath (Join-Path $stagingBinDir ([System.IO.Path]::GetFileName($DllPath))) -Force

    if (-not [string]::IsNullOrWhiteSpace($PdbPath) -and (Test-Path $PdbPath)) {
        Copy-Item $PdbPath (Join-Path $stagingBinDir ([System.IO.Path]::GetFileName($PdbPath))) -Force
    }

    $zipName = "$ModuleName.$Tag.zip"
    $zipPath = Join-Path $OutputRoot $zipName
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Compress-Archive -Path $stagingModuleRoot -DestinationPath $zipPath -Force
    return $zipPath
}

function Get-ComparableAssetName {
    param([string]$Name)
    if ($null -eq $Name) { return "" }
    return ([regex]::Replace($Name.ToLowerInvariant(), "[^a-z0-9]", ""))
}

function Get-GitHubHeaders {
    param([string]$Token)
    return @{
        "Accept" = "application/vnd.github+json"
        "Authorization" = "Bearer $Token"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "buytroops-release-script"
    }
}

function Get-ReleaseByTag {
    param(
        [string]$Owner,
        [string]$Repo,
        [string]$Tag,
        [hashtable]$Headers
    )

    $uri = "https://api.github.com/repos/$Owner/$Repo/releases/tags/$Tag"
    try {
        return Invoke-RestMethod -Method Get -Uri $uri -Headers $Headers
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        if ($statusCode -eq 404) {
            return $null
        }
        throw
    }
}

function New-Release {
    param(
        [string]$Owner,
        [string]$Repo,
        [string]$Tag,
        [string]$Title,
        [string]$Notes,
        [bool]$Draft,
        [bool]$Prerelease,
        [hashtable]$Headers
    )

    $uri = "https://api.github.com/repos/$Owner/$Repo/releases"
    $payload = @{
        tag_name = $Tag
        name = $Title
        body = $Notes
        draft = $Draft
        prerelease = $Prerelease
    } | ConvertTo-Json

    return Invoke-RestMethod -Method Post -Uri $uri -Headers $Headers -Body $payload
}

function Remove-ReleaseAssetIfExists {
    param(
        [object]$Release,
        [string]$AssetName,
        [hashtable]$Headers
    )

    if ($null -eq $Release -or $null -eq $Release.assets) {
        return
    }

    $targetComparable = Get-ComparableAssetName -Name $AssetName
    $existing = $Release.assets |
        Where-Object { (Get-ComparableAssetName -Name $_.name) -eq $targetComparable } |
        Select-Object -First 1
    if ($null -eq $existing) {
        return
    }

    if (-not ($Release.url -match "repos/(?<owner>[^/]+)/(?<repo>[^/]+)/releases/")) {
        throw "Could not parse owner/repo from release URL: $($Release.url)"
    }

    $owner = $matches["owner"]
    $repo = $matches["repo"]
    $deleteUri = "https://api.github.com/repos/$owner/$repo/releases/assets/$($existing.id)"

    Invoke-RestMethod -Method Delete -Uri $deleteUri -Headers $Headers | Out-Null
}

function Upload-ReleaseAsset {
    param(
        [object]$Release,
        [string]$AssetPath,
        [hashtable]$Headers
    )

    $assetName = [System.IO.Path]::GetFileName($AssetPath)
    Remove-ReleaseAssetIfExists -Release $Release -AssetName $assetName -Headers $Headers

    $uploadUrl = $Release.upload_url -replace "\{\?name,label\}", ""
    $uri = $uploadUrl + "?name=" + [System.Uri]::EscapeDataString($assetName)

    Invoke-RestMethod `
        -Method Post `
        -Uri $uri `
        -Headers $Headers `
        -ContentType "application/zip" `
        -InFile $AssetPath | Out-Null
}

function Get-VersionInfoFromTag {
    param([string]$Tag)

    $match = [regex]::Match($Tag, '^v?(?<bannerlord>\d+\.\d+\.\d+)\.(?<revision>\d+)$')
    if (-not $match.Success) {
        throw "Tag must look like v1.3.15.7 so build-and-deploy.ps1 can use the same version."
    }

    return @{
        BannerlordVersion = $match.Groups["bannerlord"].Value
        VersionRevision = [int]$match.Groups["revision"].Value
    }
}

$repoRoot = Get-RepoRoot
$repoInfo = Get-GitHubRepoFromOrigin -RepoRoot $repoRoot

if ([string]::IsNullOrWhiteSpace($EnvFilePath)) {
    $EnvFilePath = Join-Path $repoRoot ".env"
}

$envValues = Import-DotEnv -Path $EnvFilePath

if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        $GitHubToken = $env:GITHUB_TOKEN
    }
    elseif ($envValues.ContainsKey("GITHUB_TOKEN")) {
        $GitHubToken = $envValues["GITHUB_TOKEN"]
    }
    elseif ($envValues.ContainsKey("GH_TOKEN")) {
        $GitHubToken = $envValues["GH_TOKEN"]
    }
}

if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
    throw "Missing GitHub token. Set GITHUB_TOKEN environment variable or pass -GitHubToken."
}

if ([string]::IsNullOrWhiteSpace($Title)) {
    $Title = $Tag
}

if ($IncludeLetMeFight -and [string]::IsNullOrWhiteSpace($LetMeFightRepoPath)) {
    $LetMeFightRepoPath = Join-Path (Split-Path -Parent $repoRoot) "letmefight"
}

if (-not $SkipBuild) {
    $versionInfo = Get-VersionInfoFromTag -Tag $Tag
    $buildArgs = @(
        "-Configuration", "Release",
        "-BannerlordVersion", $versionInfo.BannerlordVersion,
        "-VersionRevision", $versionInfo.VersionRevision
    )

    if ($IncludeLetMeFight) {
        $buildArgs += "-IncludeLetMeFight"
        if (-not [string]::IsNullOrWhiteSpace($LetMeFightRepoPath)) {
            $buildArgs += @("-LetMeFightRepoPath", $LetMeFightRepoPath)
        }
    }

    Write-Host "[build] Running build-and-deploy.ps1 for Release..."
    & (Join-Path $repoRoot "build-and-deploy.ps1") @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed."
    }
}

$outputRoot = Join-Path $env:TEMP ("buytroops-github-release-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

try {
    $assets = @()

    $buyTroopsZip = New-ModuleZipFromBuild `
        -RepoRoot $repoRoot `
        -Tag $Tag `
        -ModuleName "BuyTroops" `
        -SubModulePath (Join-Path $repoRoot "SubModule.xml") `
        -DllPath (Join-Path $repoRoot "BuyTroops\bin\x64\Release\BuyTroops.dll") `
        -PdbPath (Join-Path $repoRoot "BuyTroops\bin\x64\Release\BuyTroops.pdb") `
        -OutputRoot $outputRoot
    $assets += $buyTroopsZip

    $paigeFixesZip = New-ModuleZipFromBuild `
        -RepoRoot $repoRoot `
        -Tag $Tag `
        -ModuleName "PaigeBannerlordWarsailsFixes" `
        -SubModulePath (Join-Path $repoRoot "PaigeBannerlordWarsailsFixes\SubModule.xml") `
        -DllPath (Join-Path $repoRoot "PaigeBannerlordWarsailsFixes\bin\Win64_Shipping_Client\PaigeFixes.dll") `
        -PdbPath (Join-Path $repoRoot "PaigeBannerlordWarsailsFixes\bin\Win64_Shipping_Client\PaigeFixes.pdb") `
        -OutputRoot $outputRoot
    $assets += $paigeFixesZip

    if ($IncludeLetMeFight -and (Test-Path $LetMeFightRepoPath)) {
        $letMeFightZip = New-ModuleZipFromBuild `
            -RepoRoot $repoRoot `
            -Tag $Tag `
            -ModuleName "LetMeFight" `
            -SubModulePath (Join-Path $LetMeFightRepoPath "SubModule.xml") `
            -DllPath (Join-Path $LetMeFightRepoPath "BuyTroops\bin\x64\Release\LetMeFight.dll") `
            -PdbPath (Join-Path $LetMeFightRepoPath "BuyTroops\bin\x64\Release\LetMeFight.pdb") `
            -OutputRoot $outputRoot
        $assets += $letMeFightZip
    }

    $headers = Get-GitHubHeaders -Token $GitHubToken
    $release = Get-ReleaseByTag -Owner $repoInfo.Owner -Repo $repoInfo.Repo -Tag $Tag -Headers $headers
    if ($null -eq $release) {
        Write-Host "[github] Creating release $Tag on $($repoInfo.Owner)/$($repoInfo.Repo)..."
        $release = New-Release `
            -Owner $repoInfo.Owner `
            -Repo $repoInfo.Repo `
            -Tag $Tag `
            -Title $Title `
            -Notes $Notes `
            -Draft ([bool]$Draft) `
            -Prerelease ([bool]$Prerelease) `
            -Headers $headers
    }
    else {
        Write-Host "[github] Release $Tag already exists. Uploading/replacing assets..."
    }

    foreach ($asset in $assets) {
        Write-Host "[github] Uploading asset: $asset"
        Upload-ReleaseAsset -Release $release -AssetPath $asset -Headers $headers
    }

    Write-Host "[done] GitHub release updated: $($release.html_url)"
}
finally {
    if (Test-Path $outputRoot) {
        Remove-Item $outputRoot -Recurse -Force
    }
}
