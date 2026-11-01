<#
Spike: why does "#SpawnArmedNPC ... Location "X Y Z"" sometimes answer "Cannot spawn at the specified location."?
Hypothesis: the game traces down from Z and refuses when the ground is too far below (or Z is underground).

How it works
- Reads your position from the newest #ListPlayers output in SCUM's client log (run #ListPlayers in game first,
  standing on FLAT open ground), or takes -Origin "X Y Z".
- Sends one spawn per test point, in one chat session, and after each one counts new
  "Cannot spawn at the specified location." lines in SCUM.log. A successful NPC spawn logs nothing,
  so no refusal line within -WaitMs = spawned.
- Sweep 1 (height): at -Offset metres east of you, Z = your Z + each of -Heights (cm).
- Sweep 2 (distance): at your Z, -Distances metres east of you (tests "too far from the admin" instead).
- Writes results.csv and prints a table.

Run it with SCUM open, chat closed, standing still. It types into the game window; don't touch the keyboard.
Spawned NPCs are hostile: the default is one level-1 Drifter per point, -Offset 40 m away. -Template '#SpawnAnimal BP_Chicken 1 Location "{xyz}"'
gives a harmless control run.

Examples
  .\Run-Spike.ps1 -SteamId 76561198000000000 -DryRun     # print what it would send
  .\Run-Spike.ps1 -SteamId 76561198000000000
#>
param(
    [string]$SteamId,
    [string]$Origin,
    [string]$Template = '#SpawnArmedNPC BP_Drifter_Lvl_1 1 Location "{xyz}"',
    [double]$Offset = 40,
    [int[]]$Heights = @(-1000, -300, -100, -30, 0, 30, 100, 200, 300, 500, 1000, 2000, 5000),
    [double[]]$Distances = @(10, 50, 100, 250, 500, 1000),
    [switch]$SkipDistance,
    [int]$WaitMs = 2500,
    [switch]$DryRun,
    [string]$Log = (Join-Path $env:LOCALAPPDATA 'SCUM\Saved\Logs\SCUM.log')
)
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
$refusal = 'Cannot spawn at the specified location.'

function Read-LogFrom([long]$offset) {
    $fs = [IO.FileStream]::new($Log, 'Open', 'Read', 'ReadWrite, Delete')
    try { $fs.Seek([Math]::Min($offset, $fs.Length), 'Begin') | Out-Null; ([IO.StreamReader]::new($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

# ---- origin
if ($Origin) {
    $o = $Origin -split '\s+' | ForEach-Object { [double]::Parse($_, $inv) }
} else {
    if (-not $SteamId) { throw 'Give -SteamId (yours, to find you in #ListPlayers output) or -Origin "X Y Z".' }
    $text = Read-LogFrom 0
    $m = [regex]::Matches($text, "Platform ID: $SteamId\s*\r?\n(?:.*\r?\n){0,4}?Location: X=(-?[\d.]+) Y=(-?[\d.]+) Z=(-?[\d.]+)")
    if ($m.Count -eq 0) { throw "No #ListPlayers entry for $SteamId in $Log. Run #ListPlayers in game first." }
    $last = $m[$m.Count - 1]
    $o = 1..3 | ForEach-Object { [double]::Parse($last.Groups[$_].Value, $inv) }
}
"Origin: X={0} Y={1} Z={2}" -f $o[0], $o[1], $o[2]

# ---- test points (1 m = 100 world units; +X is east)
$points = [Collections.Generic.List[object]]::new()
foreach ($h in $Heights) { $points.Add([pscustomobject]@{ Sweep = 'height'; DistM = $Offset; DzCm = $h; X = $o[0] + $Offset * 100; Y = $o[1]; Z = $o[2] + $h }) }
if (-not $SkipDistance) {
    foreach ($d in $Distances) { $points.Add([pscustomobject]@{ Sweep = 'distance'; DistM = $d; DzCm = 0; X = $o[0] + $d * 100; Y = $o[1]; Z = $o[2] }) }
}
foreach ($p in $points) {
    $xyz = '{0} {1} {2}' -f $p.X.ToString('0.##', $inv), $p.Y.ToString('0.##', $inv), $p.Z.ToString('0.##', $inv)
    $p | Add-Member Line ($Template -replace '\{xyz\}', $xyz)
}
if ($DryRun) { $points | Format-Table Sweep, DistM, DzCm, Line -AutoSize; return }

# ---- input (same steps as SACC: focus with the Alt trick, T once, Ctrl+A / paste / Enter per line, Esc at the end)
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SpikeInput {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
  public static void Tap(byte vk) { byte sc = (byte)MapVirtualKey(vk, 0); keybd_event(vk, sc, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(40); keybd_event(vk, sc, 2, UIntPtr.Zero); System.Threading.Thread.Sleep(40); }
  public static void Chord(byte mod, byte vk) { byte ms = (byte)MapVirtualKey(mod, 0); keybd_event(mod, ms, 0, UIntPtr.Zero); Tap(vk); keybd_event(mod, ms, 2, UIntPtr.Zero); }
  public static uint FgPid() { uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p); return p; }
  public static void Request(IntPtr h) {
    uint pid; uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out pid); uint me = GetCurrentThreadId();
    bool att = fg != me && AttachThreadInput(me, fg, true);
    try { BringWindowToTop(h); SetForegroundWindow(h); } finally { if (att) AttachThreadInput(me, fg, false); } }
}
"@
$scum = Get-Process SCUM | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
if (-not $scum) { throw 'SCUM is not running.' }
$gpid = [uint32]$scum.Id
$clip = $null; try { $clip = [Windows.Forms.Clipboard]::GetText() } catch {}

$focused = $false
for ($a = 0; $a -lt 2 -and -not $focused; $a++) {
    if ($a -eq 1) { [SpikeInput]::Tap(0xA4) }
    [SpikeInput]::Request($scum.MainWindowHandle)
    for ($w = 0; $w -lt 1500; $w += 50) { if ([SpikeInput]::FgPid() -eq $gpid) { $focused = $true; break }; Start-Sleep -Milliseconds 50 }
}
if (-not $focused) { throw 'SCUM did not take focus; nothing sent.' }
Start-Sleep -Milliseconds 700
[SpikeInput]::Tap(0x54); Start-Sleep -Milliseconds 700   # T opens chat

try {
    foreach ($p in $points) {
        if ([SpikeInput]::FgPid() -ne $gpid) { Write-Warning 'SCUM lost focus; stopping.'; break }
        $before = (Get-Item $Log).Length
        [SpikeInput]::Chord(0xA2, 0x41)
        [Windows.Forms.Clipboard]::SetText($p.Line)
        [SpikeInput]::Chord(0xA2, 0x56); Start-Sleep -Milliseconds 250
        if ([SpikeInput]::FgPid() -ne $gpid) { Write-Warning 'SCUM lost focus before Enter; stopping.'; break }
        [SpikeInput]::Tap(0x0D)
        Start-Sleep -Milliseconds $WaitMs
        $new = Read-LogFrom $before
        $refused = ([regex]::Matches($new, [regex]::Escape($refusal))).Count
        $other = [regex]::Matches($new, 'for message (?!Cannot spawn)(.+?)!\s*$', 'Multiline') | ForEach-Object { $_.Groups[1].Value } | Select-Object -First 2
        $p | Add-Member Result ($(if ($refused -gt 0) { 'REFUSED' } else { 'spawned' }))
        $p | Add-Member Note (($other -join ' / '))
        '{0,-8} {1,6} m  dZ {2,6} cm  {3}  {4}' -f $p.Sweep, $p.DistM, $p.DzCm, $p.Result, $p.Note
    }
} finally {
    if ([SpikeInput]::FgPid() -eq $gpid) { [SpikeInput]::Tap(0x1B) }   # close chat
    if ($clip) { [Windows.Forms.Clipboard]::SetText($clip) }
}

$csv = Join-Path $PSScriptRoot ('results-{0:yyyyMMdd-HHmmss}.csv' -f (Get-Date))
$points | Where-Object { $_.PSObject.Properties['Result'] } | Select-Object Sweep, DistM, DzCm, X, Y, Z, Result, Note, Line | Export-Csv $csv -NoTypeInformation
"Saved $csv"
