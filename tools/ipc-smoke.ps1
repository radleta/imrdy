# tools/ipc-smoke.ps1
# Manual smoke test for the ImrdyInspect named-pipe IPC server.
# Usage: pwsh tools/ipc-smoke.ps1 -Verb ping
# Usage: Measure-Command { pwsh tools/ipc-smoke.ps1 -Verb ping }
param([string]$Verb = "ping", [string]$SessionId = "")
$req = @{ verb = $Verb; sessionId = $SessionId; outputPath = $null } | ConvertTo-Json -Compress
$body = [System.Text.Encoding]::UTF8.GetBytes($req)
$len  = [BitConverter]::GetBytes([int32]$body.Length)
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "ImrdyInspect", "InOut")
$pipe.Connect(2000)
$pipe.Write($len, 0, 4); $pipe.Write($body, 0, $body.Length); $pipe.Flush()
$rlBuf = New-Object byte[] 4
$pipe.Read($rlBuf, 0, 4) | Out-Null
$rlen = [BitConverter]::ToInt32($rlBuf, 0)
$rBuf = New-Object byte[] $rlen
$pipe.Read($rBuf, 0, $rlen) | Out-Null
[System.Text.Encoding]::UTF8.GetString($rBuf)
$pipe.Dispose()
