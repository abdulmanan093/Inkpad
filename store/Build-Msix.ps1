[CmdletBinding(DefaultParameterSetName = 'Submission')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Submission')][string]$IdentityName,
    [Parameter(Mandatory, ParameterSetName = 'Submission')][string]$Publisher,
    [Parameter(Mandatory, ParameterSetName = 'Submission')][string]$PublisherDisplayName,
    [Parameter(Mandatory, ParameterSetName = 'Validation')][switch]$ValidationOnly,
    [string]$AppName = 'Inkpad',
    [string]$MakeAppx,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$src = Join-Path (Split-Path -Parent $PSScriptRoot) 'src'
if (!$MakeAppx) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $MakeAppx = Get-ChildItem -LiteralPath $kits -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\MakeAppx.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (!$MakeAppx) { throw 'Install the Windows SDK or provide the path to MakeAppx.exe.' }
}
$MakeAppx = (Resolve-Path -LiteralPath $MakeAppx).Path

if ($ValidationOnly) {
    $IdentityName = 'Inkpad.PackagingTest'
    $Publisher = 'CN=Inkpad Packaging Test'
    $PublisherDisplayName = 'Inkpad Packaging Test'
}
if (!$OutputPath) {
    $filename = if ($ValidationOnly) { 'Inkpad.validation.msix' } else { 'Inkpad.msix' }
    $OutputPath = Join-Path $PSScriptRoot $filename
}
$OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$work = (New-Item -Path (Join-Path $PSScriptRoot 'obj') -ItemType Directory -Force).FullName
$layout = (New-Item -Path (Join-Path $work ([Guid]::NewGuid().ToString('N'))) -ItemType Directory).FullName
$assets = (New-Item -Path (Join-Path $layout 'Assets') -ItemType Directory).FullName
$app = Join-Path $layout 'Inkpad.exe'
& (Join-Path $src 'build.ps1') -Store -OutputPath $app

Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::new((Join-Path $src 'Inkpad.ico'), 256, 256)
$original = $icon.ToBitmap()
try {
    $sizes = @{ 'StoreLogo.png' = 50; 'Square44x44Logo.png' = 44; 'Square150x150Logo.png' = 150 }
    foreach ($entry in $sizes.GetEnumerator()) {
        $size = $entry.Value
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($original, 0, 0, $size, $size)
            } finally { $graphics.Dispose() }
            $bitmap.Save((Join-Path $assets $entry.Key), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    }
} finally {
    $original.Dispose()
    $icon.Dispose()
}

[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
$manifest.Package.Identity.SetAttribute('Name', $IdentityName)
$manifest.Package.Identity.SetAttribute('Publisher', $Publisher)
$manifest.Package.Identity.SetAttribute('Version', (Get-Item -LiteralPath $app).VersionInfo.FileVersion)
$manifest.Package.Properties.DisplayName = $AppName
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$manifest.Package.Applications.Application.VisualElements.SetAttribute('DisplayName', $AppName)
$manifest.Save((Join-Path $layout 'AppxManifest.xml'))

& $MakeAppx pack /d $layout /p $OutputPath /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE." }
if ($ValidationOnly) { Write-Host 'Validation package uses a test identity and cannot be submitted to the Store.' }
