# byte-tracker.ps1
# Optional PostToolUse hook: estimates cumulative tool output bytes per
# session and, when over a rough threshold, injects an approximate
# "≈90% — consider /compact" warning into the agent's context via
# hookSpecificOutput.additionalContext.
#
# This is CLEARLY APPROXIMATE — it only counts tool_response.output string
# length (not full context, not tokens, not images). It's a coarse nudge,
# not a real budget meter. Tune $Threshold and $BudgetBytes below.
#
# Stdin (from Devin CLI PostToolUse):
#   { tool_name, tool_input, tool_response: { success, output, error }, session_id, prompt_id }
#
# DEVIN_PROJECT_DIR is set to the project root. Tally files are per-session
# and gitignored (.devin-context/tally_<session_id>.txt).

$ErrorActionPreference = 'SilentlyContinue'

# --- Tunables (very approximate) ---
# Rough cumulative tool-output byte level that maps to "~90% of usable
# context before compaction is advisable". Default 500_000 bytes (~125K
# tokens at ~4 bytes/token) — conservative for a 1M-token window where you
# want to compact well before the hard limit. Adjust freely.
$Threshold  = 500000
$BudgetBytes = 560000  # what "100%" roughly maps to for the percentage display

if (-not $env:DEVIN_PROJECT_DIR) { exit 0 }

$stdin = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($stdin)) { exit 0 }

try {
	$data = $stdin | ConvertFrom-Json
} catch { exit 0 }

$sid = $data.session_id
if ([string]::IsNullOrWhiteSpace($sid)) { exit 0 }

# Tally this tool's output size.
$bytes = 0
$resp  = $data.tool_response
if ($resp) {
	if ($resp.output) { $bytes += $resp.output.Length }
	if ($resp.error)  { $bytes += "$($resp.error)".Length }
}

$ctxDir = Join-Path $env:DEVIN_PROJECT_DIR ".devin-context"
if (-not (Test-Path -LiteralPath $ctxDir)) {
	New-Item -ItemType Directory -Path $ctxDir -Force | Out-Null
}

$tallyFile = Join-Path $ctxDir "tally_$sid.txt"
$current = 0
if (Test-Path -LiteralPath $tallyFile) {
	$read = Get-Content -LiteralPath $tallyFile -Raw
	[long]::TryParse("$read".Trim(), [ref]$current) | Out-Null
}
$current += $bytes
Set-Content -LiteralPath $tallyFile -Value $current -NoNewline

if ($current -ge $Threshold) {
	$pct = [math]::Min(99, [math]::Round(($current / $BudgetBytes) * 100))
	$msg = "[byte-tracker] Approx cumulative tool output ~$current bytes (~$pct% of est. usable budget). Consider /compact soon. (Approximate - counts only tool_response.output length; tune via .devin-context/byte-tracker.ps1.)"
	$obj = [PSCustomObject]@{
		hookSpecificOutput = [PSCustomObject]@{
			hookEventName     = "PostToolUse"
			additionalContext = $msg
		}
	}
	$json = $obj | ConvertTo-Json -Depth 5 -Compress
	[Console]::Out.Write($json)
}

exit 0
