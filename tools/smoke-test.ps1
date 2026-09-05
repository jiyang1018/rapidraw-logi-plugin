<#
.SYNOPSIS
    Exercises RapidRAW's external control API over its socket, with no console hardware.

.DESCRIPTION
    Start RapidRAW (the external-control build) and open an image in the editor
    first, then run this. It connects to 127.0.0.1:47820, prints the greeting,
    pulls the parameter table, nudges Exposure up and back down, rates the image
    3 stars and clears it again, and prints the final state. Every message in
    both directions is echoed, so it doubles as a protocol reference.

    Nothing is left changed on the image: every edit is reverted. The edit does
    hit RapidRAW's undo history and auto-save, so use a test image.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\smoke-test.ps1
#>

[CmdletBinding()]
param(
    [string] $ComputerName = '127.0.0.1',
    [int]    $Port = 47820,
    [string] $Param = 'exposure',
    [double] $Delta = 0.5
)

$ErrorActionPreference = 'Stop'

function Send-Line {
    param([Parameter(Mandatory)] [System.IO.StreamWriter] $Writer, [Parameter(Mandatory)] [string] $Json)
    Write-Host "-> $Json" -ForegroundColor DarkGray
    $Writer.Write($Json + "`n")
    $Writer.Flush()
}

function Read-Replies {
    param(
        [Parameter(Mandatory)] [System.IO.StreamReader] $Reader,
        [Parameter(Mandatory)] [System.Net.Sockets.NetworkStream] $Stream,
        [int] $QuietMs = 400,
        [switch] $Full
    )
    # Read until the line has been quiet for $QuietMs. State snapshots are large;
    # show only the interesting fields unless -Full.
    $Stream.ReadTimeout = $QuietMs
    while ($true) {
        try { $line = $Reader.ReadLine() } catch { break }
        if ($null -eq $line) { Write-Host '<- (connection closed)' -ForegroundColor Red; break }
        $colour = if ($line -match '"type":"error"') { 'Red' } else { 'Green' }
        if (-not $Full -and $line.Length -gt 200 -and $line -match '"type":"state"') {
            try {
                $o = $line | ConvertFrom-Json
                $img = if ($o.image) { "$($o.image.name) rating=$($o.image.rating)" } else { 'none' }
                $line = "{type:state view=$($o.view) image=$img canUndo=$($o.canUndo) $Param=$($o.params.$Param) ... $($o.params.PSObject.Properties.Count) params}"
            } catch { }
        }
        elseif (-not $Full -and $line -match '"type":"params"') {
            try { $o = $line | ConvertFrom-Json; $line = "{type:params protocol=$($o.protocol) count=$($o.params.Count)}" } catch { }
        }
        Write-Host "<- $line" -ForegroundColor $colour
    }
}

Write-Host "Connecting to ${ComputerName}:${Port} ..." -ForegroundColor Cyan
try {
    $client = [System.Net.Sockets.TcpClient]::new($ComputerName, $Port)
}
catch {
    Write-Host @"
Could not connect.

  * Is RapidRAW running, and is it the build with external control?
  * Check its log (Settings > Data > View Application Logs) for
    "External control: listening on 127.0.0.1:$Port".
  * If the port was changed in settings.json (externalControlPort), pass -Port.
"@ -ForegroundColor Red
    exit 1
}

$stream = $client.GetStream()
$reader = [System.IO.StreamReader]::new($stream, [System.Text.UTF8Encoding]::new($false))
$writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($false))

Write-Host "`n[1] Greeting and last known state" -ForegroundColor Cyan
Read-Replies -Reader $reader -Stream $stream -QuietMs 800

Write-Host "`n[2] Parameter table" -ForegroundColor Cyan
Send-Line $writer '{"type":"get_params","ref":1}'
Read-Replies -Reader $reader -Stream $stream

Write-Host "`n[3] Nudge $Param by +$Delta, then back" -ForegroundColor Cyan
Send-Line $writer ('{"type":"adjust","param":"' + $Param + '","delta":' + $Delta + '}')
Read-Replies -Reader $reader -Stream $stream
Write-Host '    (watch the preview: it should have changed; the slider in the Basic panel too)'
Start-Sleep -Milliseconds 600
Send-Line $writer ('{"type":"adjust","param":"' + $Param + '","delta":' + (-$Delta) + '}')
Read-Replies -Reader $reader -Stream $stream

Write-Host "`n[4] Rate 3 stars, then clear (rate_3 twice toggles)" -ForegroundColor Cyan
Send-Line $writer '{"type":"action","id":"rate_3"}'
Read-Replies -Reader $reader -Stream $stream
Start-Sleep -Milliseconds 400
Send-Line $writer '{"type":"action","id":"rate_3"}'
Read-Replies -Reader $reader -Stream $stream

Write-Host "`n[5] Final state" -ForegroundColor Cyan
Send-Line $writer '{"type":"get_state","ref":2}'
Read-Replies -Reader $reader -Stream $stream

$client.Close()
Write-Host "`nDone. If [3] moved the preview and [4] toggled the stars, the API is working; the plugin will too." -ForegroundColor Green
