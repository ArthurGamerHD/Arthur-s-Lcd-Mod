# Downloads the Acrux UI SDK from the GitHub releases, checks its signature against the Acrux release key below, and
# only then replaces packages\Acrux.Sdk.*.nupkg and the Acrux.Sdk version in this folder's .csproj files.
#
# usage: update-acrux.bat            latest release
#        update-acrux.bat 0.0.2      a specific version
# Works with Windows PowerShell 5.1 and PowerShell 7.
param([string]$Version = "")
$ErrorActionPreference = "Stop"

$Repo = "ArthurGamerHD/AcruxUI"
# Acrux release key (RSA-3072, the same key as update-acrux.sh). Packages signed with any other key are rejected.
$PublicKey = "<RSAKeyValue><Modulus>+ML+JAGxqLMkP0z50vyy+pYqHm+zerK4+FcuRdcZxgIyxWTOo9pIrevoBpxeYzkd++bzY4K4pyqj142Liz3zGYn/CJqr4nyTguBjpLETbTAQdkYCTERyjhRboXHKpANwx3R31hl19LxwMJ8IK1Ikr7bUE8iGMuWJkcevJWhkd2XC1aB27dCuiTgP7uDlniw3pKpilhQMbhplf60swt/w5jyHJra7DRBlK0yg97619HteYu3CxWE/6X1xk16i9moD4DU4wW1VNrdlvKqExLEhpH18W7cJLdPeFnlgN635j2mMfjX/vH6MDkoDqBW9X+758yh67hHmTXsSvcTRB2SElmL3AUIUcPXyNt082egyKIPUuGjiLwNv9ZzK8MrQ4N2Usr3CLgG3cfiNNykoP2Dx656UIXIjpdbi6SZgfi4gs+WttA93bB/CcGu7u+rof5ATqYM1cy3/nD6eLbJv7YxHADcA8stQ/0bsiz9MFtQJEd8ED11+7WMGKa7ZNM3Nxksv</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>"

# Windows PowerShell 5.1 defaults to TLS 1.0, which GitHub refuses.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$Packages = Join-Path $Here "packages"

if ($Version -eq "") {
    try { $release = Invoke-RestMethod -UseBasicParsing "https://api.github.com/repos/$Repo/releases/latest" }
    catch { throw "update-acrux: no published release found for $Repo (offline, or the repository is not public). Pass a version to try one directly." }
    $Version = $release.tag_name -replace '^v', ''
}
if ($Version -notmatch '^[0-9A-Za-z.-]+$') { throw "update-acrux: bad version '$Version'" }

$Name = "Acrux.Sdk.$Version.nupkg"
if (Test-Path (Join-Path $Packages $Name)) {
    Write-Host "Acrux SDK $Version is already installed."
    exit 0
}

$Tmp = Join-Path ([IO.Path]::GetTempPath()) ("acrux-sdk-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $Tmp | Out-Null
try {
    # ACRUX_SDK_URL points at a mirror; the signature is checked either way.
    $Url = if ($env:ACRUX_SDK_URL) { $env:ACRUX_SDK_URL } else { "https://github.com/$Repo/releases/download/v$Version" }
    $Package = Join-Path $Tmp $Name
    Write-Host "Downloading Acrux SDK $Version..."
    try {
        Invoke-WebRequest -UseBasicParsing -Uri "$Url/$Name" -OutFile $Package
        Invoke-WebRequest -UseBasicParsing -Uri "$Url/$Name.sig" -OutFile "$Package.sig"
    } catch {
        throw "update-acrux: could not download $Name and its signature from $Url (no such release, offline, or not public); nothing was changed."
    }

    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    $rsa.FromXmlString($PublicKey)
    $ok = $rsa.VerifyData([IO.File]::ReadAllBytes($Package), [Security.Cryptography.CryptoConfig]::MapNameToOID("SHA256"),
                          [IO.File]::ReadAllBytes("$Package.sig"))
    if (-not $ok) { throw "update-acrux: SIGNATURE CHECK FAILED for $Name; nothing was changed." }

    # The signature covers the bytes, not the file name: make sure a signed older package is not served as a newer one.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $entry = $zip.GetEntry("Acrux.Sdk.nuspec")
        if ($entry -eq $null) { throw "update-acrux: $Name has no Acrux.Sdk.nuspec; nothing was changed." }
        $reader = New-Object IO.StreamReader($entry.Open())
        $nuspec = $reader.ReadToEnd()
        $reader.Dispose()
    } finally { $zip.Dispose() }
    if ($nuspec -notmatch "<id>Acrux\.Sdk</id>" -or $nuspec -notmatch ("<version>" + [regex]::Escape($Version) + "</version>")) {
        throw "update-acrux: $Name does not contain Acrux.Sdk $Version; nothing was changed."
    }

    New-Item -ItemType Directory -Force -Path $Packages | Out-Null
    Get-ChildItem $Packages -Filter "Acrux.Sdk.*.nupkg*" | Remove-Item
    Move-Item $Package, "$Package.sig" $Packages
} finally {
    Remove-Item -Recurse -Force $Tmp -ErrorAction SilentlyContinue
}

Get-ChildItem $Here -Recurse -Depth 2 -Filter *.csproj | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName)
    if ($text -match 'Include="Acrux\.Sdk"') {
        $text = [regex]::Replace($text, '(<PackageReference Include="Acrux\.Sdk" Version=")[^"]*(")', ('${1}' + $Version + '${2}'))
        [IO.File]::WriteAllText($_.FullName, $text)
        Write-Host "  $($_.Name) -> Acrux.Sdk $Version"
    }
}

Write-Host "Acrux SDK $Version installed (signature verified). Rebuild the mod."
