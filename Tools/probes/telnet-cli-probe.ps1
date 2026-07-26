# telnet-cli-probe.ps1 — minimal RouterOS Telnet (TCP 23) CLI probe.
#
# Účel: rychle ověřit, co RouterOS přes telnet/PTY reálně vrací na konkrétní CLI příkaz —
# nezávisle na tik4net knihovně. Použito při ladění kapitoly C (Telnet/CLI vrstva).
#
# Reprodukuje to, co dělá tik4net.Telnet.TelnetClient:
#   - IAC option negotiation (odmítne DO/WILL),
#   - VT100 cursor-probe negotiation (odpoví na ESC[6n cursor reportem — JINAK RouterOS
#     považuje terminál za 1×1 a NEVYKRESLÍ výstup → uvidíš jen "\r\r\r\r] >"),
#   - login <user>+ct s odmítnutím "new password>" nagu přes Ctrl-C.
# Login ČEKÁ na prompty (Login:/Password:) — neposílá podle fixních pauz, aby se sekvence
# nerozsynchronizovala a router nelogoval falešné login failures. Výstup tiskne raw
# (ESC jako \e, CR/LF jako \r/\n).
#
# Použití — souřadnice routeru vezmi z tik4net.integrationtests/App.config (host/user/pass):
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools/probes/telnet-cli-probe.ps1 `
#     -RouterHost <host> -User <user> `
#     -Command ':put [/interface print detail as-value]', ':put [/system resource print as-value]'
#   POZN.: pro prázdné heslo -Pass VYNECH (default je ''); '-Pass ''' přes -File je nespolehlivé.
#
# Klíčové ověřené poznatky (viz findings-cli.md §10 v poznámkách k vývoji):
#   - bare 'print as-value' v terminálu NIC nevypíše → použij ':put [/path print as-value]'
#   - ':put' vrací JEDEN řádek, záznamy oddělené ';', hranice na '.id='
#   - 'print detail as-value' = plná sada polí; bez detail jen souhrn
#   - 'where' hodnoty se speciálními znaky (/,:) musí být v uvozovkách: where address="192.168.1.1/24"
#   - live countery (firewall bytes/packets) NEjsou v 'print detail as-value' → potřeba 'print stats'

param(
  [Parameter(Mandatory = $true)]
  [string]   $RouterHost,
  [string]   $User       = 'admin',
  [string]   $Pass       = '',
  [string[]] $Command    = @(':put [/interface print detail as-value]')
)

$ErrorActionPreference = 'Stop'
$enc = [System.Text.Encoding]::ASCII
$c = $null; $s = $null   # (re)assigned per login attempt below

# Reads whatever bytes are currently available (non-blocking), appends decoded text to $sb,
# and answers Telnet IAC + VT100 cursor probes. Returns $true if any bytes were read.
function Pump([System.Text.StringBuilder]$sb) {
  if (-not $s.DataAvailable) { return $false }
  $buf = New-Object byte[] 8192
  $n = $s.Read($buf, 0, 8192)
  if ($n -le 0) { return $false }
  [void]$sb.Append($enc.GetString($buf, 0, $n))
  # answer Telnet IAC: IAC DO x -> IAC WONT x ; IAC WILL x -> IAC DONT x
  for ($i = 0; $i -lt $n - 2; $i++) {
    if ($buf[$i] -eq 0xFF) {
      $verb = $buf[$i + 1]; $opt = $buf[$i + 2]
      if ($verb -eq 0xFD) { $rb = [byte[]](0xFF, 0xFC, $opt); $s.Write($rb, 0, 3); $s.Flush() }
      elseif ($verb -eq 0xFB) { $rb = [byte[]](0xFF, 0xFE, $opt); $s.Write($rb, 0, 3); $s.Flush() }
    }
  }
  # answer VT100 cursor-position probe ESC[6n -> ESC[24;80R
  for ($i = 0; $i -lt $n - 3; $i++) {
    if ($buf[$i] -eq 0x1B -and $buf[$i+1] -eq 0x5B -and $buf[$i+2] -eq 0x36 -and $buf[$i+3] -eq 0x6E) {
      $rep = $enc.GetBytes("`e[24;80R"); $s.Write($rep, 0, $rep.Length); $s.Flush()
    }
  }
  return $true
}
# Read for a fixed duration (used for command output where there is no single end-marker).
function ReadFor($ms) {
  $sb = New-Object System.Text.StringBuilder
  $end = (Get-Date).AddMilliseconds($ms)
  while ((Get-Date) -lt $end) { if (-not (Pump $sb)) { Start-Sleep -Milliseconds 20 } }
  return $sb.ToString()
}
# Read until the accumulated text matches $pattern (regex) or $maxMs elapses. Returns the text.
function ReadUntil([string]$pattern, [int]$maxMs) {
  $sb = New-Object System.Text.StringBuilder
  $end = (Get-Date).AddMilliseconds($maxMs)
  while ((Get-Date) -lt $end) {
    if (Pump $sb) { if ($sb.ToString() -match $pattern) { return $sb.ToString() } }
    else { Start-Sleep -Milliseconds 20 }
  }
  return $sb.ToString()
}
# Read/answer until the stream stays silent for $quietMs (lets IAC/VT100 negotiation finish before
# we type — sending mid-negotiation makes RouterOS mis-read the credentials and log a login failure).
function DrainQuiet([int]$quietMs) {
  $last = Get-Date
  $sb = New-Object System.Text.StringBuilder
  while (((Get-Date) - $last).TotalMilliseconds -lt $quietMs) {
    if (Pump $sb) { $last = Get-Date } else { Start-Sleep -Milliseconds 20 }
  }
}
# POZOR: "`e" (ESC) je escape až od PowerShellu 6 — pod Windows PowerShellem 5.1, pro který je tenhle
# skript psaný, se vyhodnotí jako holé písmeno "e". Dřív tu tedy stálo `-replace "e", '\e'`: každé "e"
# ve výstupu routeru se přepsalo na "\e" ("file" → "fil\e") a skutečné ESC bajty naopak zmizely — přesně
# naruby proti tomu, k čemu Vis je. ESC skládej explicitně.
$Esc = [regex]::Escape([string][char]27)
function Vis($t) { return ($t -replace "`r", '\r' -replace "`n", '\n' -replace $Esc, '\e') }
function Send($t) { $b = $enc.GetBytes($t); $s.Write($b, 0, $b.Length); $s.Flush() }

# --- Login (fixed-delay; proven reliable for this crude probe) ---
# Sequence: wait "Login:" -> <user>+ct + ENTER -> wait -> <password> + ENTER -> banner + log lines +
# "Change your password" + "new password>" -> Ctrl-C -> shell prompt "] >".
# '+ct' (dumb fixed-width terminal) is IMPORTANT here: it tells RouterOS to skip the multi-round VT100
# capability probe. A crude client that answers probes with a fixed cursor report (Pump) desyncs that
# negotiation and the credential read, which intermittently produces "incorrect username or password".
# (A real terminal / the tik4net library answers probes correctly and can log in as plain "admin"/"+c".)
# Fixed ReadFor delays are deliberate — they proved more reliable than prompt-driven sends here.
$c = New-Object System.Net.Sockets.TcpClient
$c.Connect($RouterHost, 23)
$s = $c.GetStream()

[void](ReadFor 1200)              # consume IAC negotiation + "Login: "
Send "$User+ct`r`n"
[void](ReadFor 1200)              # consume username echo + "Password: "
Send "$Pass`r`n"                  # password (empty = just ENTER)
$r = ReadFor 1800                 # banner + log lines + "Change your password" + "new password>"
$tries = 0
while (($r -match 'password>') -and ($tries -lt 4)) {   # Ctrl-C to skip the change-password nag
  $cc = [byte[]](0x03); $s.Write($cc, 0, 1); $s.Flush()
  $r = ReadFor 1500; $tries++
}
Write-Output "=== POST-LOGIN ==="; Write-Output (Vis $r); Write-Output ""
if ($r -notmatch '\] >') { Write-Output "!!! LOGIN FAILED (shell prompt '] >' not received). Re-run once; this crude probe's telnet login is timing-sensitive (the tik4net library is the reliable path)."; try { $c.Close() } catch {}; return }

foreach ($cmd in $Command) {
  Send ($cmd + "`r`n")
  # Fixed-duration read: RouterOS redraws the prompt BEFORE the data, so "read until ] >" would stop
  # too early on the redraw. Reading for a fixed window captures echo + data + final prompt.
  $r = ReadFor 2000
  Write-Output "=== CMD: $cmd ==="; Write-Output (Vis $r); Write-Output ""
}
$c.Close()
