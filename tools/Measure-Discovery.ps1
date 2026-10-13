param([ValidateSet('before','after')][string]$Phase = 'before', [string]$OutputDirectory = 'artifacts/discovery')
$ErrorActionPreference = 'Stop'
$queries = @('AI Dynamic Island','codex dynamic island','agent quota monitor','codex usage widget windows','AI agent status monitor','windows desktop ai widget')
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$records = @()
foreach ($query in $queries) {
    foreach ($suffix in @('', ' in:readme')) {
        $fullQuery = $query + $suffix
        $at = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('o')
        $raw = & gh api --method GET search/repositories -f "q=$fullQuery" -f per_page=50 -f page=1 2>&1
        if ($LASTEXITCODE -eq 0) {
            $response = ($raw -join "`n") | ConvertFrom-Json
            $names = @($response.items | ForEach-Object { $_.full_name })
            $rank = [array]::IndexOf($names, 'littletaro97-arch/AI-Dynamic-Island') + 1
            $records += [pscustomobject]@{ query=$fullQuery; observed_at=$at; endpoint='GET /search/repositories'; sort='default (best match)'; page=1; per_page=50; total_count=$response.total_count; incomplete_results=$response.incomplete_results; rank=$(if($rank -gt 0){$rank}else{$null}); result_names=$names; error=$null }
        } else {
            $records += [pscustomobject]@{query=$fullQuery; observed_at=$at; rank=$null; error=($raw -join "`n")}
        }
        $records | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $OutputDirectory "$Phase.json")
        Write-Output "$Phase | $fullQuery | rank=$($records[-1].rank) | error=$($records[-1].error)"
        # GitHub search has a separate rate limit; no parallel requests.
        Start-Sleep -Seconds 7
    }
}
