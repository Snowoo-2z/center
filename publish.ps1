#requires -Version 5.1
# Encoding: this file is plain ASCII on purpose. Windows PowerShell 5.1 reads
# BOM-less .ps1 files as ANSI/cp1252; em-dashes, check marks and other non-ASCII
# punctuation then decode to smart quote bytes ("), which PowerShell treats as
# string delimiters -> parser error: the string terminator " is missing.
# Keep this file ASCII (or at least UTF-8 with BOM) when editing it.
<#
.SYNOPSIS
    Builds Center.

.DESCRIPTION
    Default  : Native AOT publish (fast startup, ~4 files in the output folder).
    -SingleFile : self-contained single-file publish (literally ONE .exe, but JIT).

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Runtime win-arm64
    .\publish.ps1 -SingleFile -Open
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [switch]$SingleFile,

    [switch]$Open
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\Center\Center.csproj'
$mode = if ($SingleFile) { 'singlefile' } else { 'aot' }
$outDir = Join-Path $root "artifacts\$Runtime-$mode"

Write-Host ""
Write-Host "  Center - publication $Runtime ($mode)" -ForegroundColor White
Write-Host "  Sortie : $outDir" -ForegroundColor DarkGray
Write-Host ""

if ($SingleFile) {
    dotnet publish $project -c Release -r $Runtime -o $outDir `
        -p:PublishAot=false `
        -p:SelfContained=true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false
}
else {
    dotnet publish $project -c Release -r $Runtime -o $outDir
}

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish a echoue (code $LASTEXITCODE)."
}

Write-Host ""
Get-ChildItem $outDir -File |
    Sort-Object Length -Descending |
    Select-Object Name, @{ Name = 'Taille (MB)'; Expression = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize

$exe = Join-Path $outDir 'Center.exe'
Write-Host "  Executable : $exe" -ForegroundColor Green
Write-Host ""

if ($Open) {
    Start-Process $exe
}
