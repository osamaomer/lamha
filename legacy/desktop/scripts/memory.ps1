# How much memory the Windows app takes, measured from outside as Task Manager sees it: every process the app starts
# (Electron's own, and the sentence helper lamha-uia.exe, which app.getAppMetrics() doesn't count). Runs the app from
# source with a temporary profile (LAMHA_PROFILE in main.js): your data and the registry are left alone.
#   npm run memory                                            in the tray, after 20 s
#   npm run memory -- -Window                                 the main window open
#   npm run memory -- -Use                                    after a minute of use (scripts/memory-use.js), back in the tray
#   npm run memory -- -Gpu                                    with the graphics card (off by default; the same as -Local '{"useGpu":true}')
#   npm run memory -- -Local '{"clipboardEnabled":true}' -Sync '{"useContext":false}'   settings to start with
param([switch]$Window, [switch]$Gpu, [switch]$Use, [string]$Sync = "{}", [string]$Local = "{}", [int]$Wait = 20)
$ErrorActionPreference = "Stop"
# A PowerShell started from another strips plain quotes ({useGpu:false}), which the app can't read: it would quietly
# start with the defaults. Windows PowerShell's own ConvertFrom-Json accepts such keys, so they're looked for directly.
foreach ($j in @($Sync, $Local)) {
  if ($j -match '[{,]\s*[A-Za-z_$]') { throw ('keys need quotes: ' + $j + ' (from inside another PowerShell, write \"key\")') }
}
$desktop = Split-Path -Parent $PSScriptRoot
$electron = Join-Path $desktop "node_modules\electron\dist\electron.exe"
$dir = Join-Path $env:TEMP ("lamha-memory-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory $dir | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding $false # JSON.parse refuses a byte-order mark
[IO.File]::WriteAllText((Join-Path $dir "storage-sync.json"), $Sync, $utf8)
if ($Gpu) { $o = $Local | ConvertFrom-Json; $o | Add-Member -Force -NotePropertyName useGpu -NotePropertyValue $true; $Local = $o | ConvertTo-Json -Compress }
[IO.File]::WriteAllText((Join-Path $dir "storage-local.json"), $Local, $utf8)
[IO.File]::WriteAllText((Join-Path $dir "installed.flag"), "memory", $utf8) # not a first run: Settings doesn't open

Remove-Item Env:ELECTRON_RUN_AS_NODE -ErrorAction SilentlyContinue # set by some tools' shells: electron.exe would be plain Node
$env:LAMHA_PROFILE = $dir
$env:LAMHA_MEMORY_USE = $(if ($Use) { "1" } else { "" })
if ($Use -and $Wait -lt 35) { $Wait = 35 } # the use takes ~20 s, then the app settles
$appArgs = @("`"$desktop`"") + $(if ($Window) { @() } else { @("--hidden") })
$log = Join-Path $env:TEMP ("lamha-memory-" + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".log") # outside the profile, read after it's gone
$root = Start-Process -FilePath $electron -ArgumentList $appArgs -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"

# The app's process and every process under it.
function Get-Tree($procs) {
  $ids = @($root.Id)
  do {
    $more = @($procs | Where-Object { $ids -contains $_.ParentProcessId -and $ids -notcontains $_.ProcessId } | ForEach-Object { $_.ProcessId })
    $ids += $more
  } while ($more.Count)
  return $ids
}

try {
  Start-Sleep -Seconds $Wait
  $procs = Get-CimInstance Win32_Process
  $ids = Get-Tree $procs
  $perf = @{}
  Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | Where-Object { $ids -contains $_.IDProcess } | ForEach-Object { $perf[[int]$_.IDProcess] = $_ }
  $rows = foreach ($p in $procs | Where-Object { $ids -contains $_.ProcessId }) {
    $cl = [string]$p.CommandLine
    $kind = if ($p.ProcessId -eq $root.Id) { "main" }
      elseif ($cl -match "--utility-sub-type=([\w.]+)") { $Matches[1] -replace "^.*\.|Service$", "" }
      elseif ($cl -match "--type=([\w-]+)") { $Matches[1] }
      else { $p.Name -replace "\.exe$", "" }
    [pscustomobject]@{
      Process = $kind
      Pid = $p.ProcessId
      "Private MB" = [math]::Round($p.PrivatePageCount / 1MB) # private bytes (commit)
      "Task Manager MB" = $(if ($perf.ContainsKey([int]$p.ProcessId)) { [math]::Round($perf[[int]$p.ProcessId].WorkingSetPrivate / 1MB) } else { 0 }) # private working set
    }
  }
  $rows | Sort-Object "Task Manager MB" -Descending | Format-Table -AutoSize | Out-String -Width 120 | Write-Output
  $tm = ($rows | Measure-Object "Task Manager MB" -Sum).Sum
  $pv = ($rows | Measure-Object "Private MB" -Sum).Sum
  Write-Output ("TOTAL  Task Manager {0} MB  private {1} MB  ({2} processes)" -f $tm, $pv, @($rows).Count)
  if ($Use -and -not (Select-String -Path $log -Pattern "\[memory\] used" -Quiet)) { Write-Output "WARNING: the use didn't finish (see the app's log below)"; Get-Content $log, "$log.err" | Select-Object -Last 20 }
} finally {
  # the tree again: a process started after the measurement would keep the log open, and this script would wait for it
  $all = Get-CimInstance Win32_Process
  $ids = Get-Tree $all
  $all | Where-Object { $ids -contains $_.ProcessId } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 1
  Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
  Remove-Item -Force $log, "$log.err" -ErrorAction SilentlyContinue
}
