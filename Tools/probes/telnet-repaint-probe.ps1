# telnet-repaint-probe.ps1 - raw RouterOS Telnet capture, timed per chunk, with a prompt-line check.
#
# Purpose: show where RouterOS puts each byte of an answer relative to its prompt repaints, the way the
# library's terminal sees it, but without the library. Unlike telnet-cli-probe.ps1 it logs in as
# <user>+c (the library's flags) and answers every cursor probe (ESC[6n) from a tracked cursor at the
# width given, so the router lays the screen out as it does for tik4net. Docs/findings-cli.md §7
# ("An answer starts on a line of its own") was measured with it.
#
# Each received chunk is printed with its time since the step was sent (ESC as \e, CR/LF as \r/\n).
# At the end every step's text is ANSI-stripped and split on CR/LF as CliOutputCleaner splits it, and
# each line that opens with the shell prompt is compared with what was typed: a line carrying anything
# else (answer text on a prompt line) is reported with "!!". A Tab completion is reported too, since the
# completed word is the router's - read those lines before taking a "!!" as evidence.
#
# Steps file, one step per line, '#' comments allowed:
#   <readMs>|<text>     send <text> and read for <readMs>. <text> may use \r \n \t \x03; no \r = typed, not entered.
#   -<ms>|<text>        the same, read for 14 s, and <ms> after the send a failed binary-API login for <user>
#                       is made on a second connection. 'login failure' is a critical topic, which the
#                       default logging rule echoes into every terminal session: this provokes a console log
#                       line on demand. It writes one entry to the router's log and changes nothing else.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools/probes/telnet-repaint-probe.ps1 `
#     -RouterHost <host> -StepsFile steps.txt [-Width 80]
#
# Take the router coordinates from tik4net.integrationtests/App.config. Omit -Pass for an empty password.

param(
  [Parameter(Mandatory = $true)][string] $RouterHost,
  [string] $User = 'admin',
  [string] $Pass = '',
  [Parameter(Mandatory = $true)][string] $StepsFile,
  [int]    $Width = 4096,
  [int]    $Height = 25,
  [int]    $ApiPort = 8728
)
$ErrorActionPreference = 'Stop'
$enc = [System.Text.Encoding]::GetEncoding(28591)   # one char per byte
$Esc = [char]27

# --- VT100 cursor tracking: enough to answer RouterOS's size probe truthfully ---
$script:row = 1; $script:col = 1; $script:st = 0; $script:par = ''
function Vt([string]$t) {
  $rep = @()
  foreach ($ch in $t.ToCharArray()) {
    $c = [int]$ch
    if ($script:st -eq 0) {
      if ($c -eq 27) { $script:st = 1 }
      elseif ($c -eq 13) { $script:col = 1 }
      elseif ($c -eq 10) { $script:row = [Math]::Min($script:row + 1, $Height) }
      elseif ($c -ge 32) { $script:col++; if ($script:col -gt $Width) { $script:col = 1; $script:row = [Math]::Min($script:row + 1, $Height) } }
    } elseif ($script:st -eq 1) {
      if ($ch -eq '[') { $script:st = 2; $script:par = '' } else { $script:st = 0 }
    } else {
      if (($c -ge 48 -and $c -le 57) -or $ch -eq ';' -or $ch -eq '?') { $script:par += $ch; continue }
      $ns = @($script:par.TrimStart('?').Split(';') | ForEach-Object { $v = 0; [void][int]::TryParse($_, [ref]$v); $v })
      $n1 = $ns[0]; $n2 = if ($ns.Count -gt 1) { $ns[1] } else { 0 }
      switch -CaseSensitive ($ch) {
        'A' { $script:row = [Math]::Max(1, $script:row - [Math]::Max(1, $n1)) }
        'B' { $script:row = [Math]::Min($Height, $script:row + [Math]::Max(1, $n1)) }
        'C' { $script:col = [Math]::Min($Width, $script:col + [Math]::Max(1, $n1)) }
        'D' { $script:col = [Math]::Max(1, $script:col - [Math]::Max(1, $n1)) }
        'H' { $script:row = if ($n1 -gt 0) { [Math]::Min($Height, $n1) } else { 1 }
              $script:col = if ($n2 -gt 0) { [Math]::Min($Width, $n2) } else { 1 } }
        'n' { if ($n1 -eq 6) { $rep += "$Esc[$($script:row);$($script:col)R" } }
      }
      $script:st = 0; $script:par = ''
    }
  }
  return ,$rep
}
function Vis([string]$t) {
  $sb = New-Object System.Text.StringBuilder
  foreach ($ch in $t.ToCharArray()) {
    $c = [int]$ch
    if ($c -eq 13) { [void]$sb.Append('\r') } elseif ($c -eq 10) { [void]$sb.Append('\n') }
    elseif ($c -eq 27) { [void]$sb.Append('\e') } elseif ($c -eq 9) { [void]$sb.Append('\t') }
    elseif ($c -lt 32 -or $c -ge 127) { [void]$sb.Append(('\x{0:X2}' -f $c)) } else { [void]$sb.Append($ch) }
  }
  $sb.ToString()
}
function Unesc([string]$t) { $t.Replace('\r', "`r").Replace('\n', "`n").Replace('\t', "`t").Replace('\x03', [string][char]3) }
function Squash([string]$s) { ($s -replace '\s', '').ToLowerInvariant() }

$c = New-Object System.Net.Sockets.TcpClient; $c.Connect($RouterHost, 23); $s = $c.GetStream()
$sw = [Diagnostics.Stopwatch]::StartNew()
function Send([string]$t) { $b = $enc.GetBytes($t); $s.Write($b, 0, $b.Length); $s.Flush() }
# Reads what is available, refuses every Telnet option, answers cursor probes. Prints when $show.
function Pump([bool]$show) {
  if (-not $s.DataAvailable) { return $null }
  $buf = New-Object byte[] 65536; $n = $s.Read($buf, 0, $buf.Length); if ($n -le 0) { return $null }
  $out = New-Object System.Collections.Generic.List[byte]
  for ($i = 0; $i -lt $n; $i++) {
    if ($buf[$i] -eq 0xFF -and $i + 2 -lt $n -and $buf[$i + 1] -ge 0xFB -and $buf[$i + 1] -le 0xFE) {
      $v = $buf[$i + 1]; $o = $buf[$i + 2]
      if ($v -eq 0xFD) { $s.Write([byte[]](0xFF, 0xFC, $o), 0, 3) } elseif ($v -eq 0xFB) { $s.Write([byte[]](0xFF, 0xFE, $o), 0, 3) }
      $i += 2; continue
    }
    $out.Add($buf[$i])
  }
  $t = $enc.GetString($out.ToArray())
  foreach ($r in (Vt $t)) { Send $r }
  # [Console] rather than Write-Output: this function's output is its return value.
  if ($show) { [Console]::Out.WriteLine(("  [{0,6} ms] {1}" -f $sw.ElapsedMilliseconds, (Vis $t))) }
  return $t
}
function ReadFor([int]$ms, [bool]$show) {
  $acc = ''; $end = $sw.ElapsedMilliseconds + $ms
  while ($sw.ElapsedMilliseconds -lt $end) { $t = Pump $show; if ($null -eq $t) { Start-Sleep -Milliseconds 5 } else { $acc += $t } }
  $acc
}
function FailedApiLogin {
  $x = New-Object System.Net.Sockets.TcpClient; $x.Connect($RouterHost, $ApiPort); $y = $x.GetStream()
  $m = New-Object System.IO.MemoryStream
  foreach ($w in @('/login', "=name=$User", '=password=telnet-repaint-probe-wrong', '')) {
    $wb = [Text.Encoding]::ASCII.GetBytes($w); $m.WriteByte([byte]$wb.Length); $m.Write($wb, 0, $wb.Length)
  }
  $a = $m.ToArray(); $y.Write($a, 0, $a.Length); $y.Flush()
  return $x
}

# --- Login: <user>+c, the change-password nag refused with Ctrl-C ---
$acc = ReadFor 2500 $false
Send "$User+c`r"; $acc = ReadFor 1500 $false
Send "$Pass`r"; $acc = ReadFor 3000 $false
$k = 0; while ($acc -match 'password>' -and $k -lt 3) { Send ([string][char]3); $acc = ReadFor 1500 $false; $k++ }
if ($acc -notmatch '\] >') { Write-Output "LOGIN FAILED: $(Vis $acc)"; $c.Close(); return }
Write-Output "=== logged in, width $Width, tail: $(Vis ($acc.Substring([Math]::Max(0, $acc.Length - 80))))"

$steps = @()
foreach ($line in (Get-Content $StepsFile)) {
  if ($line.Trim().Length -eq 0 -or $line.TrimStart().StartsWith('#')) { continue }
  $p = $line.IndexOf('|'); $ms = [int]$line.Substring(0, $p); $txt = Unesc $line.Substring($p + 1)
  Write-Output "=== SEND $(Vis $txt)"
  $sw.Restart(); Send $txt
  if ($ms -lt 0) {
    $rx = ReadFor (-$ms) $true
    $side = FailedApiLogin
    [Console]::Out.WriteLine(("  [{0,6} ms] --- failed API login sent on a second connection ---" -f $sw.ElapsedMilliseconds))
    $rx += ReadFor (14000 + $ms) $true
    $side.Close()
  } else {
    $rx = ReadFor $ms $true
  }
  $steps += @{ Sent = $txt; Rx = $rx }
}
Send "/quit`r"; Start-Sleep -Milliseconds 300; $c.Close()

# --- Check: does any prompt line carry text that was not typed? ---
Write-Output ""; Write-Output "=== CHECK"
$typed = ''; $bad = 0
foreach ($st in $steps) {
  $typed += $st.Sent
  $plain = [regex]::Replace($st.Rx, "$Esc\[[0-9;?]*[A-Za-z]", '')
  $sq = Squash ($typed -replace "`t", '')
  foreach ($ln in ($plain -split "`r`n|`r|`n")) {
    if ($ln -match '^\[[^\]]+@[^\]]+\] (<SAFE> )?> ?(.*)$') {
      $rest = $Matches[2]
      if ($rest.Trim().Length -gt 0 -and $sq.IndexOf((Squash $rest)) -lt 0) {
        $bad++; Write-Output "  !! prompt line with untyped text: [$ln]  (step: $(Vis $st.Sent))"
      }
    }
  }
  if ($st.Sent.EndsWith("`r")) { $typed = '' }
}
Write-Output "$($steps.Count) steps, $bad prompt line(s) carrying untyped text"
