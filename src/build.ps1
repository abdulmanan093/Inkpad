param([switch]$Test, [switch]$Store, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$referencePath = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$references = 'mscorlib', 'System', 'System.Core', 'System.Xaml', 'System.Xml', 'WindowsBase', 'PresentationCore', 'PresentationFramework', 'System.Printing', 'ReachFramework', 'System.IO.Compression', 'System.Drawing', 'System.Windows.Forms' |
    ForEach-Object { '/r:' + (Join-Path $referencePath ($_ + '.dll')) }
$sources = 'Inkpad.cs', 'Inkpad.Files.cs', 'Inkpad.Shapes.cs', 'Inkpad.Windows.cs', 'Inkpad.Tables.cs', 'Inkpad.Style.cs', 'Inkpad.Formatting.cs', 'Inkpad.Presentation.cs', 'Inkpad.Checkboxes.cs', 'Inkpad.Preferences.cs', 'Inkpad.Commands.cs', 'Inkpad.Writing.cs', 'Inkpad.Media.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ }
$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/optimize+', '/warn:4', '/codepage:65001') + $references
if ($Test) {
    if (!$OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'InkpadTests.exe' }
    $arguments += '/target:exe', '/main:InkpadTests'
    $sources += Join-Path $PSScriptRoot 'Inkpad.Tests.cs'
} else {
    if (!$OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'Inkpad.exe' }
    $arguments += '/target:winexe', ('/win32icon:' + (Join-Path $PSScriptRoot 'Inkpad.ico'))
}
if ($Store) { $arguments += '/define:STORE', '/platform:x64' }
$arguments += '/out:' + $OutputPath
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'Inkpad.ico') + ',Inkpad.Icon'
$arguments += $sources
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Inkpad compilation failed.' }
