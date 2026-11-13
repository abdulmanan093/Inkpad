param([switch]$Test)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$referencePath = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$references = 'mscorlib', 'System', 'System.Core', 'System.Xaml', 'WindowsBase', 'PresentationCore', 'PresentationFramework', 'System.IO.Compression', 'System.Drawing', 'System.Windows.Forms' |
    ForEach-Object { '/r:' + (Join-Path $referencePath ($_ + '.dll')) }
$sources = 'Inkpad.cs', 'Inkpad.Files.cs', 'Inkpad.Shapes.cs', 'Inkpad.Windows.cs', 'Inkpad.Tables.cs', 'Inkpad.Style.cs', 'Inkpad.Formatting.cs', 'Inkpad.Presentation.cs', 'Inkpad.Checkboxes.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ }
$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/optimize+', '/warn:4', '/codepage:65001') + $references
if ($Test) {
    $arguments += '/target:exe', '/main:InkpadTests', ('/out:' + (Join-Path $PSScriptRoot 'InkpadTests.exe'))
    $sources += Join-Path $PSScriptRoot 'Inkpad.Tests.cs'
} else {
    $arguments += '/target:winexe', ('/win32icon:' + (Join-Path $PSScriptRoot 'Inkpad.ico')), ('/out:' + (Join-Path $PSScriptRoot 'Inkpad.exe'))
}
$arguments += $sources
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Inkpad compilation failed.' }
