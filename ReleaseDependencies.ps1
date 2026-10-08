# Shared by Release.ps1; dot-sourcing this file does not build or restore anything.
function Invoke-ReleaseCommand {
    param([string]$File, [string[]]$Arguments)
    & $File @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$File failed (exit $LASTEXITCODE)." }
}

function Get-CfitPackages {
    param([string]$PackageRepo)
    $result = @{}
    foreach ($name in 'AppLogger', 'AppTools', 'SimConnectLib', 'AppFramework', 'Installer') {
        $id = "CFIT.$name"
        $pattern = '^' + [regex]::Escape($id) + '\.(\d+\.\d+\.\d+\.\d+)\.nupkg$'
        $candidates = @(Get-ChildItem -LiteralPath $PackageRepo -File | ForEach-Object {
            if ($_.Name -match $pattern) {
                [pscustomobject]@{ Id = $id; Version = [version]$Matches[1]; Path = $_.FullName }
            }
        } | Sort-Object Version -Descending)
        if ($candidates.Count -eq 0) { throw "Missing local package: $id ($PackageRepo)" }
        $result[$id] = $candidates[0]
    }
    return $result
}

function Set-CfitReferences {
    param([string]$Project, [hashtable]$Packages)
    $content = [IO.File]::ReadAllText($Project)
    $pattern = '<PackageReference\b[^>]*\bInclude="(?<id>CFIT\.[^"]+)"[^>]*\bVersion="(?<version>[^"]+)"'
    $updated = [regex]::Replace($content, $pattern, {
        param($match)
        $id = $match.Groups['id'].Value
        if (-not $Packages.ContainsKey($id)) { throw "Missing package version for $id in $Project" }
        $value = $match.Groups['version']
        $offset = $value.Index - $match.Index
        return $match.Value.Substring(0, $offset) + $Packages[$id].Version.ToString() + $match.Value.Substring($offset + $value.Length)
    })
    # Refuse unsupported CFIT reference formats rather than silently leaving an old version.
    $xml = [xml]$updated
    foreach ($ref in $xml.SelectNodes('//*[local-name()="PackageReference"]')) {
        if ($ref.Include -like 'CFIT.*' -and $ref.Version -ne $Packages[$ref.Include].Version.ToString()) {
            throw "Unsupported CFIT reference in $Project : $($ref.Include)"
        }
    }
    if ($updated -ne $content) { [IO.File]::WriteAllText($Project, $updated, [Text.UTF8Encoding]::new($false)) }
}

function Set-InstallerCfitReferences {
    param([string]$RepoRoot, [hashtable]$Packages)
    $configPath = Join-Path $RepoRoot 'Installer/packages.config'
    $projectPath = Join-Path $RepoRoot 'Installer/Installer.csproj'
    $config = [IO.File]::ReadAllText($configPath)
    $project = [IO.File]::ReadAllText($projectPath)
    $xml = [xml]$config
    foreach ($package in $xml.packages.package) {
        if ($package.id -notlike 'CFIT.*') { continue }
        $id = $package.id
        $old = $package.version
        $new = $Packages[$id].Version.ToString()
        $pattern = '(<package\b[^>]*\bid="' + [regex]::Escape($id) + '"[^>]*\bversion=")[^"]+(")'
        $config = [regex]::Replace($config, $pattern, { param($m) $m.Groups[1].Value + $new + $m.Groups[2].Value })
        $project = $project.Replace("$id.$old\", "$id.$new\").Replace("$id, Version=$old,", "$id, Version=$new,")
    }
    [void][xml]$config
    $projectXml = [xml]$project
    foreach ($reference in $projectXml.SelectNodes('//*[local-name()="Reference"]')) {
        $id = ($reference.Include -split ',')[0]
        if ($id -notlike 'CFIT.*') { continue }
        $version = $Packages[$id].Version.ToString()
        if ($reference.Include -notlike "$id, Version=$version,*" -or
            $reference.HintPath -notlike "*\$id.$version\*") {
            throw "Installer reference/HintPath was not updated: $id"
        }
    }
    [IO.File]::WriteAllText($configPath, $config, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))
}

function Assert-CfitPackageDependencies {
    param([hashtable]$Packages)
    foreach ($package in $Packages.Values) {
        $zip = [IO.Compression.ZipFile]::OpenRead($package.Path)
        try {
            $entry = @($zip.Entries | Where-Object FullName -like '*.nuspec')[0]
            $reader = [IO.StreamReader]::new($entry.Open())
            try { $nuspec = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
            foreach ($dependency in $nuspec.SelectNodes('//*[local-name()="dependency"]')) {
                if ($dependency.id -notlike 'CFIT.*') { continue }
                $expected = $Packages[$dependency.id].Version.ToString()
                if ($dependency.version -ne $expected -and $dependency.version -ne "[$expected, )") {
                    throw "$($package.Id) still declares $($dependency.id)/$($dependency.version), expected $expected"
                }
            }
        } finally { $zip.Dispose() }
    }
}

function Get-CfitPackageBytes {
    param([string]$Package, [string]$Name, [string]$Framework = 'net10')
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $entries = @($zip.Entries | Where-Object {
            $_.FullName -like "lib/$Framework*/$Name" -or $_.FullName -eq "runtimes/win-x64/native/$Name"
        })
        if ($entries.Count -ne 1) { throw "Expected one $Name ($Framework) in $Package" }
        $stream = $entries[0].Open()
        $buffer = [IO.MemoryStream]::new()
        try { $stream.CopyTo($buffer); return ,$buffer.ToArray() }
        finally { $stream.Dispose(); $buffer.Dispose() }
    } finally { $zip.Dispose() }
}

function Assert-CfitRelease {
    param([string]$Output, [hashtable]$Packages, [string]$Payload)
    $zip = if ($Payload) { [IO.Compression.ZipFile]::OpenRead($Payload) } else { $null }
    try {
        foreach ($id in $Packages.Keys) {
            $name = "$id.dll"
            $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData((Get-CfitPackageBytes $Packages[$id].Path $name)))
            $actual = (Get-FileHash -LiteralPath (Join-Path $Output $name) -Algorithm SHA256).Hash
            if ($actual -ne $expected) { throw "Published $name does not match the selected local CFIT package." }
            if ($zip) {
                $entry = $zip.GetEntry($name)
                if (-not $entry) { throw "Missing $name in installer payload." }
                $stream = $entry.Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
                finally { $stream.Dispose() }
                if ($hash -ne $expected) { throw "Installer payload contains a different $name." }
            }
        }
        foreach ($app in 'PilotsDock', 'ProfileManager') {
            $deps = Get-Content -LiteralPath (Join-Path $Output "$app.deps.json") -Raw | ConvertFrom-Json
            foreach ($property in $deps.libraries.PSObject.Properties) {
                if ($property.Name -notlike 'CFIT.*') { continue }
                $id, $version = $property.Name.Split('/')
                if ($version -ne $Packages[$id].Version.ToString()) { throw "$app dependency manifest still references $id/$version." }
            }
        }
    } finally { if ($zip) { $zip.Dispose() } }
}
