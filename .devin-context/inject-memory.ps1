# inject-memory.ps1
# Hook helper: reads .devin-context/memory.md and emits a hookSpecificOutput
# JSON object on stdout so Devin CLI injects the memory into the agent's
# context. Invoked from .devin/hooks.v1.json for SessionStart, PostCompaction
# (full) and UserPromptSubmit (compact pointer).
#
# Args:
#   -Event <name>   The hook event name (SessionStart | UserPromptSubmit | PostCompaction)
#   -Mode  <mode>   "full" (inject whole file) | "compact" (pointer + first section)
#
# DEVIN_PROJECT_DIR is set by the Devin CLI to the project root.

param(
	[string]$Event = "SessionStart",
	[string]$Mode = "full"
)

$ErrorActionPreference = 'SilentlyContinue'

if (-not $env:DEVIN_PROJECT_DIR) { exit 0 }

$memFile = Join-Path $env:DEVIN_PROJECT_DIR ".devin-context\memory.md"
if (-not (Test-Path -LiteralPath $memFile)) { exit 0 }

$raw = [System.IO.File]::ReadAllText($memFile, [System.Text.Encoding]::UTF8)
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

if ($Mode -eq "compact") {
	$lines = $raw -split "`r?`n"
	$total = $lines.Count
	# Take through the first ~45 lines as a cheap per-turn anchor.
	$cap = 45
	$slice = ($lines | Select-Object -First $cap) -join "`n"
	$context = "Lute memory.md (rolling state, $total lines total - full file re-injected on SessionStart/PostCompaction; this is a compact anchor). Update .devin-context/memory.md as you work.`n`n$slice"
} else {
	$context = "Lute agent memory (re-injected from .devin-context/memory.md - keep this file current as you work; it survives compaction):`n`n$raw"
}

# Build the hookSpecificOutput JSON. ConvertTo-Json handles escaping.
$obj = [PSCustomObject]@{
	hookSpecificOutput = [PSCustomObject]@{
		hookEventName      = $Event
		additionalContext  = $context
	}
}

$json = $obj | ConvertTo-Json -Depth 5 -Compress
[Console]::Out.Write($json)
exit 0
