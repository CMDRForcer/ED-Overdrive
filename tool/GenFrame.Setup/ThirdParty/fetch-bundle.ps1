# Fetches the parts ED-Overdrive ships inside its exe (embedded at build time, see GenFrame.Setup.csproj),
# pinned by SHA-256. Runs before every build; files that are already there with the right hash are kept.
# Keep in step with InstallService.BundledEdaa / BundledReShade.
param([string]$Out = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$items = @(
    @{ Name = 'EDAA.zip'
       Url  = 'https://github.com/CMDRForcer/EDAA/releases/download/v0.7.0-cmdr1/EDAA-0.7.0-cmdr1.zip'
       Sha  = '71de5071ea2eb94cf89fcde5bda4b9c00c91defcff66d89022f5829fb229c763' },
    @{ Name = 'ReShade_Setup_Addon.exe'
       Url  = 'https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe'
       Sha  = 'afe4c8f13048306307983b8b3d41d5bf00a86820440b0e57dea10950e1176445' }
)

foreach ($item in $items) {
    $file = Join-Path $Out $item.Name
    if ((Test-Path $file) -and (Get-FileHash $file -Algorithm SHA256).Hash.ToLower() -eq $item.Sha) {
        continue
    }
    Write-Host "ED-Overdrive bundle: downloading $($item.Url)"
    Invoke-WebRequest -Uri $item.Url -OutFile "$file.part" -UseBasicParsing -UserAgent 'ED-Overdrive-build'
    $hash = (Get-FileHash "$file.part" -Algorithm SHA256).Hash.ToLower()
    if ($hash -ne $item.Sha) {
        Remove-Item "$file.part"
        throw "Unexpected SHA-256 for $($item.Name): $hash"
    }
    Move-Item "$file.part" $file -Force
}
