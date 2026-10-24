# How much memory Lamha 3 takes, measured from outside the way Task Manager sees it ("Memory" = private working set),
# plus commit (private bytes). Runs the app's --measure timeline: tray, card open, card closed, then Lamha quits.
#
#   pwsh tools/Measure/measure.ps1                          the published build (artifacts/publish/win-x64)
#   pwsh tools/Measure/measure.ps1 -Exe path\to\Lamha.exe   another build
#   pwsh tools/Measure/measure.ps1 -Budget                  fail (exit 1) above the budgets in docs/native-plan.md
#
# Lamha 2 may be running on the same PC (also Lamha.exe): this script only ever reads and stops the process it started.
param(
  [string]$Exe = (Join-Path $PSScriptRoot "..\..\artifacts\publish\win-x64\Lamha.exe"),
  [string[]]$AppArgs = @(),
  [switch]$Budget
)

$ErrorActionPreference = "Stop"
# MB. Task Manager's number drops to almost nothing once idle memory is handed back to Windows, so commit (the memory
# Lamha actually holds) has its own budget: it's the honest one. Set from phase 0's measurements (docs/native-plan.md).
$budgets = [ordered]@{
  "tray"        = @{ TaskManager = 35; Commit = 55 }
  "card open"   = @{ TaskManager = 70; Commit = 85 }
  "card closed" = @{ TaskManager = 45; Commit = 85 }
}
$exe = (Resolve-Path $Exe).Path

function Read-Memory($id) {
  $c = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess=$id"
  if (-not $c) { return $null }
  [pscustomobject]@{ TaskManager = [math]::Round($c.WorkingSetPrivate / 1MB); Commit = [math]::Round($c.PrivateBytes / 1MB) }
}

# The app's timeline (Services/MeasureTimeline.cs): card at 20 s, closed at 35 s, idle memory handed back 5 s later, quit at 60 s.
$samples = [ordered]@{ "tray" = 15; "card open" = 30; "card closed" = 55 }

# A profile of its own: the measurement never reads or writes the user's real data (lamha.db, keys, the startup entry).
$profile = Join-Path ([IO.Path]::GetTempPath()) ("lamha-measure-" + [guid]::NewGuid().ToString("N"))
$p = Start-Process -FilePath $exe -ArgumentList (@("--measure", "--profile=$profile") + $AppArgs) -PassThru
$started = Get-Date
$rows = @()
try {
  foreach ($stage in $samples.Keys) {
    $wait = $samples[$stage] - ((Get-Date) - $started).TotalSeconds
    if ($wait -gt 0) { Start-Sleep -Milliseconds ([int]($wait * 1000)) }
    $p.Refresh()
    if ($p.HasExited) { throw "Lamha exited early (code $($p.ExitCode)): see %LOCALAPPDATA%\Lamha\logs\lamha.log" }
    $m = Read-Memory $p.Id
    $rows += [pscustomobject]@{
      Stage = $stage
      "Task Manager MB" = $m.TaskManager
      "Commit MB" = $m.Commit
      "Budget MB" = "{0} / {1}" -f $budgets[$stage].TaskManager, $budgets[$stage].Commit
      OK = $m.TaskManager -le $budgets[$stage].TaskManager -and $m.Commit -le $budgets[$stage].Commit
    }
  }
} finally {
  if (-not $p.WaitForExit(15000)) { Stop-Process -Id $p.Id -Force } # only the process this script started
  Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue
}

$rows | Format-Table -AutoSize | Out-String -Width 120 | Write-Output
if ($Budget -and ($rows | Where-Object { -not $_.OK })) { Write-Output "Over budget."; exit 1 }
