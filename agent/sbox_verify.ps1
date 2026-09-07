<#
.SYNOPSIS
    Closes the "vision gap" for the Devin/GLM agent loop when building the
    Lute S&Box project. GLM has no native vision layer, so this script turns
    a running S&Box editor session into a structured TEXT report the agent
    CAN read: a coarse color-grid summary of the screenshot (proxy for
    "what's on screen"), the tail of the editor log (compile errors, runtime
    errors, Lute: ... status lines), and a flattened dump of the active
    scene file (object names + world positions + component types).

.USAGE
    powershell -File agent\sbox_verify.ps1
    powershell -File agent\sbox_verify.ps1 -ScenePath "sbox\Assets\scenes\sanctuary.scene" -LogTailLines 40

.OUTPUT
    Writes a timestamped report to scrap\verify_<timestamp>.txt and prints
    it to stdout. The agent should read the printed output (or the file)
    after every build/compile iteration.
#>

param(
    [string]$RepoRoot = "C:\Users\Shadow\Documents\lute",
    [string]$SboxLogDir = "C:\Users\Shadow\Documents\sbox-public-clean\game\logs",
    [string]$ScenePath = "",
    [int]$LogTailLines = 40
)

$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Drawing

$ts = Get-Date -Format "yyyyMMdd_HHmmss"
$outDir = Join-Path $RepoRoot "scrap"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$reportPath = Join-Path $outDir "verify_$ts.txt"
$screenshotPath = Join-Path $outDir "sbox_capture_$ts.png"

$report = New-Object System.Collections.Generic.List[string]
$report.Add("=== Lute S&Box Verification Report ($ts) ===")
$report.Add("")

# ---------------------------------------------------------------------------
# 1. Screenshot + coarse pixel analysis (poor man's vision for GLM)
# ---------------------------------------------------------------------------
try {
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
}

$bmp = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
$bmp.Save($screenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()

$w = $bmp.Width
$h = $bmp.Height

$report.Add("--- Screenshot ---")
$report.Add("File: $screenshotPath")
$report.Add("Size: ${w}x${h}")
$report.Add("")
$report.Add("5x5 color grid (R,G,B) top-left -> bottom-right:")
for ($gy = 0; $gy -lt 5; $gy++) {
    $row = ""
    for ($gx = 0; $gx -lt 5; $gx++) {
        $x = [Math]::Min($w - 1, [int]($w * ($gx / 4.0)))
        $y = [Math]::Min($h - 1, [int]($h * ($gy / 4.0)))
        $c = $bmp.GetPixel($x, $y)
        $row += "($($c.R),$($c.G),$($c.B)) "
    }
    $report.Add(("  Row {0}: {1}" -f $gy, $row))
}

# Brightness histogram across a downsampled grid — flags all-black (crash /
# nothing rendered) or all-white (shader/texture blowout) viewports.
$buckets = New-Object int[] 10
$stepX = [Math]::Max(1, [int]($w / 100))
$stepY = [Math]::Max(1, [int]($h / 100))
for ($y = 0; $y -lt $h; $y += $stepY) {
    for ($x = 0; $x -lt $w; $x += $stepX) {
        $c = $bmp.GetPixel($x, $y)
        $brightness = ($c.R + $c.G + $c.B) / 3
        $bucket = [Math]::Min(9, [int]($brightness / 25.6))
        $buckets[$bucket]++
    }
}
$report.Add("")
$report.Add("Brightness histogram (0=black .. 9=white):")
for ($i = 0; $i -lt 10; $i++) {
    $report.Add(("  bucket {0}: {1}" -f $i, $buckets[$i]))
}
$bmp.Dispose()
$report.Add("")

# ---------------------------------------------------------------------------
# 2. S&Box editor log tail (compile errors, "Lute: ..." status lines)
# ---------------------------------------------------------------------------
$report.Add("--- Editor Log Tail ---")
$latestLog = Get-ChildItem $SboxLogDir -Filter "sbox-dev*.log" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($latestLog) {
    $report.Add("File: $($latestLog.FullName)")
    $lines = Get-Content $latestLog.FullName -Tail $LogTailLines -ErrorAction SilentlyContinue
    foreach ($line in $lines) { $report.Add("  $line") }

    # Flag anything that looks like an error/warning for quick scanning.
    $flagged = $lines | Where-Object { $_ -match "error|Error|ERROR|exception|Exception|fail|Fail" }
    if ($flagged) {
        $report.Add("")
        $report.Add("  ^^ FLAGGED LINES (error/exception/fail):")
        foreach ($f in $flagged) { $report.Add("    ! $f") }
    }
} else {
    $report.Add("  No sbox-dev*.log found in $SboxLogDir")
}
$report.Add("")

# ---------------------------------------------------------------------------
# 3. Active scene dump (object names, positions, component types)
#    Ground truth for "where is everything", independent of rendering.
# ---------------------------------------------------------------------------
$report.Add("--- Scene Dump ---")
if (-not $ScenePath) {
    $ScenePath = Join-Path $RepoRoot "sbox\Assets\scenes\sanctuary.scene"
}
if (Test-Path $ScenePath) {
    $report.Add("File: $ScenePath")
    try {
        # NOTE: "__type" is a reserved polymorphic-type-discriminator key for
        # .NET JSON deserializers (ConvertFrom-Json silently drops it, and
        # JavaScriptSerializer throws trying to resolve it as a real .NET
        # type). Rename it before parsing so it comes through as plain data.
        $raw = (Get-Content $ScenePath -Raw) -replace '"__type"', '"_ObjType"'
        $json = $raw | ConvertFrom-Json

        function Walk-GameObject($obj, $depth) {
            $indent = "  " * ($depth + 1)
            $compTypes = @()
            if ($obj.Components) {
                $compTypes = $obj.Components | ForEach-Object { $_._ObjType }
            }
            $compStr = if ($compTypes.Count -gt 0) { $compTypes -join ", " } else { "(none)" }
            $script:report.Add("$indent- $($obj.Name) @ [$($obj.Position)] components: $compStr")
            if ($obj.Children) {
                foreach ($child in $obj.Children) { Walk-GameObject $child ($depth + 1) }
            }
        }

        foreach ($rootObj in $json.GameObjects) { Walk-GameObject $rootObj 0 }
    } catch {
        $report.Add("  Failed to parse scene JSON: $($_.Exception.Message)")
    }
} else {
    $report.Add("  Scene file not found: $ScenePath")
}
$report.Add("")
$report.Add("=== End of Report ===")

$report | Out-File -FilePath $reportPath -Encoding utf8
$report | ForEach-Object { Write-Output $_ }
Write-Output ""
Write-Output "Report saved to: $reportPath"
