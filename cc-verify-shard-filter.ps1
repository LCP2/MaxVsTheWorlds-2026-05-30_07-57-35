# cc-verify-shard-filter.ps1 (MV-1271)
#
# Resolves the -testFilter regex for one EditMode shard, read from the
# single data file cc-verify-shards.txt (repo root). Every shard is an
# explicit line in that file, in order - there is no synthesized "catch
# everything else" complement. An earlier version built the last shard as
# a negative-lookahead chain over all the other patterns; that is
# semantically correct (confirmed directly against .NET's own Regex engine)
# but Unity's -testFilter silently runs the WHOLE suite unfiltered - no
# error, just every test selected - for a filter whose own match can be
# zero-width or of variable length (a bare "(?!...)", or one followed by
# ".+" / "{1,80}" / any other unbounded-or-ranged quantifier all reproduced
# it; only an EXACT-length tail, like "[A-H]" or "[0-9]{2}", kept Unity's
# filter correctly selective - see MV-1271 fix-comment measurements). So
# instead, cc-verify-shards.txt enumerates every shard explicitly with
# exact-length patterns: the MV buckets use alternated exact-digit-count
# ranges up to 9999, and the legacy buckets jointly partition the full
# A-Z + "_" first-character space (the only characters a C# identifier can
# start with), so a test in a new namespace or fixture still always lands
# in exactly one shard without ever asking Unity to evaluate the regex
# shape that breaks it.
#
# MV-1468: -Half <k> (k = 1 or 2) instead of -Shard prints the alternation of the explicit shard
# patterns for shards 2,3,5,6 (k=1) or every other shard (k=2), each wrapped in (?:...). It
# only joins existing exact-length patterns - no negative-lookahead complement - and `finish`
# verifies the two halves together cover every test exactly once.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File cc-verify-shard-filter.ps1 -ShardsFile <path> -Shard <n>
# Prints the resolved regex to stdout on a single line. Exit 1 with a
# message on stderr if the file is missing or the shard index is out of range.

param(
    [Parameter(Mandatory = $true)][string]$ShardsFile,
    [int]$Shard = 0,
    [int]$Half = 0
)

if (-not (Test-Path -LiteralPath $ShardsFile)) {
    Write-Error "cc-verify-shard-filter: shards file not found: $ShardsFile"
    exit 1
}
if ($Half -eq 0 -and $Shard -lt 1) {
    Write-Error "cc-verify-shard-filter: shard index must be >= 1, got $Shard"
    exit 1
}

$patterns = @()
foreach ($line in Get-Content -LiteralPath $ShardsFile) {
    if ($line.Trim() -eq "") { continue }
    $parts = $line -split "`t", 2
    if ($parts.Count -ne 2) {
        Write-Error "cc-verify-shard-filter: malformed line (expected '<name><TAB><regex>'): $line"
        exit 1
    }
    $patterns += $parts[1]
}

if ($patterns.Count -eq 0) {
    Write-Error "cc-verify-shard-filter: $ShardsFile has no pattern lines"
    exit 1
}
if ($Half -ne 0) {
    if ($Half -ne 1 -and $Half -ne 2) {
        Write-Error "cc-verify-shard-filter: -Half must be 1 or 2, got $Half"
        exit 1
    }
    # Measured on the dev PC (MV-1468): shards 1-5 alone took 556s in one process, so a contiguous
    # split is badly unbalanced (the slow fixtures are in shards 2, 3 and 4). Half 1 is this explicit
    # list; half 2 is every other shard, so a shard added later lands in half 2 and is never dropped.
    $half1 = @(2, 3, 5, 6)
    $sel = @()
    for ($i = 1; $i -le $patterns.Count; $i++) {
        $inHalf1 = $half1 -contains $i
        if (($Half -eq 1) -eq $inHalf1) { $sel += $patterns[$i - 1] }
    }
    Write-Output (($sel | ForEach-Object { "(?:$_)" }) -join "|")
    exit 0
}
if ($Shard -gt $patterns.Count) {
    Write-Error "cc-verify-shard-filter: shard index $Shard is out of range (1..$($patterns.Count))"
    exit 1
}

Write-Output $patterns[$Shard - 1]
