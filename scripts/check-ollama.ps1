#requires -Version 5.1
<#
.SYNOPSIS
    Vérifie qu'Ollama répond, que le modèle est installé et que le streaming
    NDJSON fonctionne — exactement le chemin utilisé par Center.

.EXAMPLE
    .\scripts\check-ollama.ps1
    .\scripts\check-ollama.ps1 -Model "qwen2.5:1.5b" -Prompt "Bonjour"
    .\scripts\check-ollama.ps1 -NoStream
#>
[CmdletBinding()]
param(
    [string]$Model    = 'qwen2.5:1.5b',
    [string]$Endpoint = 'http://localhost:11434',
    [string]$Prompt   = 'Explique le Native AOT en deux phrases.',
    [switch]$NoStream
)

$ErrorActionPreference = 'Stop'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "== $Text" -ForegroundColor White
}

function Fail([string]$Text) {
    Write-Host "   x $Text" -ForegroundColor Red
    exit 1
}

# ----------------------------------------------------------------------
# 1. Daemon
# ----------------------------------------------------------------------
Write-Step "1/3  Ollama — $Endpoint"

try {
    $tags = Invoke-RestMethod -Uri "$Endpoint/api/tags" -TimeoutSec 5
}
catch {
    Fail "Ollama ne répond pas. Lance 'ollama serve' (ou ouvre l'application Ollama)."
}

$names = @($tags.models | ForEach-Object { $_.name })
if ($names.Count -eq 0) {
    Fail "Aucun modèle installé. Lance : ollama pull $Model"
}

Write-Host ("   ok  {0} modèle(s) : {1}" -f $names.Count, ($names -join ', ')) -ForegroundColor Green

# ----------------------------------------------------------------------
# 2. Modèle
# ----------------------------------------------------------------------
Write-Step "2/3  Modèle '$Model'"

if ($names -notcontains $Model) {
    Write-Host "   ! '$Model' n'est pas installé." -ForegroundColor Yellow
    Write-Host "     ollama pull $Model" -ForegroundColor Yellow
    exit 1
}

Write-Host "   ok  présent" -ForegroundColor Green

# ----------------------------------------------------------------------
# 3. Chat
#    JSON construit à la main : PowerShell 5.1 casse les guillemets
#    quand on passe du JSON inline à un exécutable natif (curl.exe).
# ----------------------------------------------------------------------
$escapedPrompt = $Prompt.Replace('\', '\\').Replace('"', '\"')
$streamFlag = if ($NoStream) { 'false' } else { 'true' }
$json = '{"model":"' + $Model + '","stream":' + $streamFlag +
        ',"messages":[{"role":"user","content":"' + $escapedPrompt + '"}]}'

if ($NoStream) {
    Write-Step '3/3  Réponse complète'

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-RestMethod -Uri "$Endpoint/api/chat" -Method Post `
        -Body $json -ContentType 'application/json' -TimeoutSec 300
    $sw.Stop()

    Write-Host ''
    Write-Host $response.message.content
    Write-Host ''
    Write-Host ("   ok  {0:N1} s" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
}
else {
    Write-Step '3/3  Streaming NDJSON (comme Center)'

    Add-Type -AssemblyName System.Net.Http -ErrorAction SilentlyContinue

    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromMinutes(10)

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Post, "$Endpoint/api/chat")
    $request.Content = [System.Net.Http.StringContent]::new(
        $json, [System.Text.Encoding]::UTF8, 'application/json')

    $response = $client.SendAsync(
        $request, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead
    ).GetAwaiter().GetResult()

    if (-not $response.IsSuccessStatusCode) {
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        Fail ("HTTP {0} — {1}" -f ([int]$response.StatusCode), $body)
    }

    $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $reader = [System.IO.StreamReader]::new($stream)
    $sw = [Diagnostics.Stopwatch]::StartNew()

    $firstTokenMs = $null
    $chars = 0
    $chunks = 0

    Write-Host ''
    while ($true) {
        $line = $reader.ReadLine()
        if ($null -eq $line) { break }
        if ($line.Trim().Length -eq 0) { continue }

        try { $chunk = $line | ConvertFrom-Json } catch { continue }

        if ($chunk.message -and $chunk.message.content) {
            if ($null -eq $firstTokenMs) { $firstTokenMs = $sw.Elapsed.TotalMilliseconds }
            $chars += $chunk.message.content.Length
            $chunks++
            Write-Host -NoNewline $chunk.message.content
        }

        if ($chunk.done) { break }
    }

    $sw.Stop()
    $reader.Dispose(); $stream.Dispose(); $response.Dispose(); $request.Dispose(); $client.Dispose()

    $tokens = [math]::Max(1, [math]::Round($chars / 3.8))
    $tps = if ($sw.Elapsed.TotalSeconds -gt 0) { $tokens / $sw.Elapsed.TotalSeconds } else { 0 }

    Write-Host ''
    Write-Host ''
    Write-Host ("   premier token : {0:N0} ms" -f $firstTokenMs) -ForegroundColor DarkGray
    Write-Host ("   total        : {0:N1} s · {1} chunks · ~{2} tokens ({3:N1} tok/s)" -f `
        $sw.Elapsed.TotalSeconds, $chunks, $tokens, $tps) -ForegroundColor DarkGray
}

Write-Host ''
Write-Host "   ✓ Ollama est prêt : tu peux lancer Center." -ForegroundColor Green
Write-Host ''
