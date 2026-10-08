# slim-server.ps1 - a stand-in for GitHub's release API and download host, on this machine only,
# for test-slim-update.ps1. Serves:
#   .../releases/latest              -> the JSON file given in -Json
#   .../releases/download/<tag>/<f>  -> <f> from -Root
# Every request is appended to -Log ("GET <path>"). -KBps throttles file downloads (0 = full speed),
# so the test can press "cancel" in the middle. Stops when -Stop appears or after -Minutes.
# ASCII only on purpose.
param([int]$Port, [string]$Root, [string]$Json, [string]$Log, [int]$KBps = 0, [string]$Stop, [int]$Minutes = 20)
$l = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), $Port
$l.Start()
$deadline = [DateTime]::UtcNow.AddMinutes($Minutes)
try {
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path $Stop)) {
        if (-not $l.Pending()) { Start-Sleep -Milliseconds 40; continue }
        $c = $l.AcceptTcpClient()
        try {
            $ns = $c.GetStream(); $ns.ReadTimeout = 15000
            $ms = New-Object IO.MemoryStream; $buf = New-Object byte[] 8192
            while ($true) {
                $n = $ns.Read($buf, 0, $buf.Length); if ($n -le 0) { break }
                $ms.Write($buf, 0, $n)
                if ([Text.Encoding]::ASCII.GetString($ms.ToArray()).Contains("`r`n`r`n")) { break }
            }
            $req = [Text.Encoding]::ASCII.GetString($ms.ToArray())
            $path = ($req -split ' ')[1]
            Add-Content -Path $Log -Value ("GET " + $path) -Encoding ASCII
            $body = $null; $type = 'application/octet-stream'
            if ($path -like '*/releases/latest') { $body = [IO.File]::ReadAllBytes($Json); $type = 'application/json' }
            elseif ($path -like '*/releases/download/*') {
                $f = Join-Path $Root ($path.Split('/')[-1])
                if (Test-Path $f) { $body = [IO.File]::ReadAllBytes($f) }
            }
            if ($body -eq $null) {
                $hb = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 404 Not Found`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
                $ns.Write($hb, 0, $hb.Length)
            } else {
                $hb = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: $type`r`nContent-Length: " + $body.Length + "`r`nConnection: close`r`n`r`n")
                $ns.Write($hb, 0, $hb.Length)
                if ($KBps -gt 0 -and $type -ne 'application/json') {
                    for ($o = 0; $o -lt $body.Length; $o += 1024) {
                        $ns.Write($body, $o, [Math]::Min(1024, $body.Length - $o))
                        Start-Sleep -Milliseconds ([int](1000 / $KBps))
                    }
                } else { $ns.Write($body, 0, $body.Length) }
                $ns.Flush()
            }
        } catch { Add-Content -Path $Log -Value ("ERR " + $_.Exception.Message) -Encoding ASCII }
        finally { $c.Close() }
    }
} finally { $l.Stop() }
