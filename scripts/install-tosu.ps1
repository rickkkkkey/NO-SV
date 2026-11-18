$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$dest = Join-Path $PSScriptRoot "..\tosu"
New-Item -ItemType Directory -Force -Path $dest | Out-Null

Write-Host "Looking up the latest tosu release..."
$release = Invoke-RestMethod "https://api.github.com/repos/tosuapp/tosu/releases/latest" `
    -Headers @{ "User-Agent" = "NoSvMania" }

$asset = $release.assets |
    Where-Object { $_.name -match "windows" -and $_.name -match "\.zip$" } |
    Select-Object -First 1

if (-not $asset) {
    throw "No Windows zip found in the latest tosu release. Download it manually from https://github.com/tosuapp/tosu/releases and extract it into the 'tosu' folder."
}

$zip = Join-Path $env:TEMP $asset.name
Write-Host "Downloading $($asset.name)..."
Invoke-WebRequest $asset.browser_download_url -OutFile $zip
Expand-Archive $zip -DestinationPath $dest -Force
Remove-Item $zip

# Some releases extract into a subfolder; flatten so tosu\tosu.exe exists.
if (-not (Test-Path (Join-Path $dest "tosu.exe"))) {
    $exe = Get-ChildItem $dest -Recurse -Filter "tosu.exe" | Select-Object -First 1
    if ($exe) { Copy-Item (Join-Path $exe.DirectoryName "*") $dest -Recurse -Force }
}

if (Test-Path (Join-Path $dest "tosu.exe")) {
    Write-Host "Installed tosu $($release.tag_name) to $dest"
} else {
    throw "Downloaded tosu but could not find tosu.exe. Check the 'tosu' folder."
}
