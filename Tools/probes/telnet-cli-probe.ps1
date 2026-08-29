# telnet-cli-probe.ps1 - minimal RouterOS Telnet (TCP 23) CLI probe.
#
# Purpose: see quickly what RouterOS really returns over telnet/PTY for a given CLI command,
# independently of the tik4net library. Use it whenever a CLI answer has to be ground truth rather
# than something read back through our own stack, which normalises values on the way.
#
# Reproduces what tik4net.Telnet.TelnetClient does:
#   - IAC option negotiation (refuses DO/WILL),
#   - VT100 cursor-probe negotiation (answers ESC[6n with a cursor report - WITHOUT it RouterOS
#     treats the terminal as 1x1 and DRAWS NOTHING, so all you see is "\r\r\r\r] >"),
#   - login as <user>+ct, refusing the "new password>" nag with Ctrl-C.
# Output is printed raw (ESC as \e, CR/LF as \r/\n).
#
# Usage - take the router coordinates from tik4net.integrationtests/App.config (host/user/pass):
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools/probes/telnet-cli-probe.ps1 `
#     -RouterHost <host> -User <user> `
#     -Command ':put [/interface print detail as-value]', ':put [/system resource print as-value]'
#
#   NOTE: for an empty password OMIT -Pass (the default is ''); passing an empty -Pass through -File
#         is unreliable.
#   NOTE: a command containing parentheses, or anything else PowerShell parses as syntax, cannot be
#         passed through -Command at all ("A positional parameter cannot be found"). Put one command
#         per line in a file and use -CommandFile, which bypasses PowerShell's argument parsing.
#
# Verified CLI facts (the reason this probe exists):
#   - a bare 'print as-value' prints NOTHING in a terminal -> use ':put [/path print as-value]'
#   - ':put' returns ONE line, records separated by ';', a record starts at '.id='
#   - 'print detail as-value' = the full field set; without detail only the summary columns
#   - 'where' values containing / or : must be quoted: where address="192.168.1.1/24"
#   - live counters (firewall bytes/packets, interface rx-byte/tx-byte) are in NEITHER form,
#     they need 'print stats'

param(
  [Parameter(Mandatory = $true)]
  [string]   $RouterHost,
  [string]   $User        = 'admin',
  [string]   $Pass        = '',
  [string[]] $Command     = @(':put [/interface print detail as-value]'),
  # One command per line. Blank lines and lines starting with # are skipped. Overrides -Command.
  [string]   $CommandFile = '',
  # Login attempts. Each is a fresh TCP connection - see the login note below.
  [int]      $LoginTries  = 10
)

$ErrorActionPreference = 'Stop'
$enc = [System.Text.Encoding]::ASCII
$c = $null; $s = $null   # (re)assigned per login attempt below
$r = ''

if ($CommandFile) {
  if (-not (Test-Path $CommandFile)) { throw "CommandFile not found: $CommandFile" }
  $Command = @(Get-Content -Path $CommandFile -Encoding UTF8 |
               Where-Object { $_.Trim().Length -gt 0 -and -not $_.TrimStart().StartsWith('#') })
  if ($Command.Count -eq 0) { throw "CommandFile contains no commands: $CommandFile" }
}

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
      $rep = $enc.GetBytes("$([char]27)[24;80R"); $s.Write($rep, 0, $rep.Length); $s.Flush()
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
# Not used by the login below (see the note there) - kept for probes that do have an end marker.
function ReadUntil([string]$pattern, [int]$maxMs) {
  $sb = New-Object System.Text.StringBuilder
  $end = (Get-Date).AddMilliseconds($maxMs)
  while ((Get-Date) -lt $end) {
    if (Pump $sb) { if ($sb.ToString() -match $pattern) { return $sb.ToString() } }
    else { Start-Sleep -Milliseconds 20 }
  }
  return $sb.ToString()
}
# NOTE: "`e" (ESC) is an escape only from PowerShell 6 on - under Windows PowerShell 5.1, which this
# script targets, it evaluates to the bare letter "e". Build ESC explicitly, as below.
$Esc = [regex]::Escape([string][char]27)
function Vis($t) { return ($t -replace "`r", '\r' -replace "`n", '\n' -replace $Esc, '\e') }
function Send($t) { $b = $enc.GetBytes($t); $s.Write($b, 0, $b.Length); $s.Flush() }

# --- Login (fixed delays, retried on a fresh connection) ---
# Sequence: wait "Login:" -> <user>+ct + ENTER -> wait -> <password> + ENTER -> banner + log lines +
# "Change your password" + "new password>" -> Ctrl-C -> shell prompt "] >".
#
# '+ct' (dumb fixed-width terminal) is IMPORTANT: it tells RouterOS to skip the multi-round VT100
# capability probe. A crude client that answers probes with a fixed cursor report (Pump) desyncs that
# negotiation and the credential read, which intermittently produces "incorrect username or password".
# (A real terminal, and the tik4net library, answer probes correctly and log in as plain "admin"/"+c".)
#
# The delays are fixed rather than prompt-driven on purpose: waiting for "Login:" before typing was
# measured WORSE here, because this probe cannot tell a prompt from an echo of one. Fixed delays still
# lose the race sometimes, so the whole login is retried on a NEW TCP connection - reusing the socket
# does not help, the router has already made up its mind about that session.
$loggedIn = $false
for ($attempt = 1; $attempt -le $LoginTries; $attempt++) {
  $c = New-Object System.Net.Sockets.TcpClient
  $c.Connect($RouterHost, 23)
  $s = $c.GetStream()

  [void](ReadFor 2500)              # consume IAC negotiation + "Login: "
  Send "$User+ct`r`n"
  [void](ReadFor 2500)              # consume username echo + "Password: "
  Send "$Pass`r`n"                  # password (empty = just ENTER)
  $r = ReadFor 3000                 # banner + log lines + "Change your password" + "new password>"
  $tries = 0
  while (($r -match 'password>') -and ($tries -lt 4)) {   # Ctrl-C to skip the change-password nag
    $cc = [byte[]](0x03); $s.Write($cc, 0, 1); $s.Flush()
    $r = ReadFor 1500; $tries++
  }
  if ($r -match '\] >') { $loggedIn = $true; break }

  Write-Output "--- login attempt $attempt/$LoginTries did not reach the shell prompt, retrying ---"
  try { $c.Close() } catch {}
  Start-Sleep -Milliseconds 500
}

Write-Output "=== POST-LOGIN ==="; Write-Output (Vis $r); Write-Output ""
if (-not $loggedIn) {
  Write-Output "!!! LOGIN FAILED after $LoginTries attempts (shell prompt '] >' never received)."
  Write-Output "!!! Check the credentials and that the telnet service is enabled. This probe is"
  Write-Output "!!! deliberately crude; the tik4net library is the reliable path."
  try { $c.Close() } catch {}
  return
}

foreach ($cmd in $Command) {
  Send ($cmd + "`r`n")
  # Fixed-duration read: RouterOS redraws the prompt BEFORE the data, so "read until ] >" would stop
  # too early on the redraw. Reading for a fixed window captures echo + data + final prompt.
  $r = ReadFor 2000
  Write-Output "=== CMD: $cmd ==="; Write-Output (Vis $r); Write-Output ""
}
$c.Close()
