# Web Deploy (msdeploy) of the site package (the Web, which hosts the Api under /api) to the root of the ONE
# MonsterASP.NET site (docs/ci-cd.md, docs/deployment.md).
# Direct msdeploy call as in Platform's deploy-monsterasp.yml (endpoint normalisation, comma check, retries,
# app_offline, masked password), plus: package checks, protected server folders, opt-in -allowUntrusted, and
# -Mode Backup/Restore so the workflow can roll a failed deploy back.
#
#   env: MONSTER_SERVER, MONSTER_SITE, MONSTER_USER, MONSTER_PASSWORD, optional MONSTER_ALLOW_UNTRUSTED=true
#   pwsh Deploy-Site.ps1 -Mode Deploy  -Path pkg/site
#   pwsh Deploy-Site.ps1 -Mode Backup  -Path backup/site     (server -> runner)
#   pwsh Deploy-Site.ps1 -Mode Restore -Path backup/site     (runner -> server)
[CmdletBinding()]
param(
    [ValidateSet('Deploy', 'Backup', 'Restore')][string]$Mode = 'Deploy',
    [Parameter(Mandatory)][string]$Path
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'WebDeploy.psm1') -Force

foreach ($name in 'MONSTER_SERVER', 'MONSTER_SITE', 'MONSTER_USER', 'MONSTER_PASSWORD') {
    Assert-MsDeployValue -Name $name -Value ([Environment]::GetEnvironmentVariable($name))
}
$endpoint = Get-MsDeployEndpoint -Server $env:MONSTER_SERVER -Site $env:MONSTER_SITE
$contentPath = Get-ContentPath -Site $env:MONSTER_SITE
$remote = "contentPath=$contentPath,computerName=$endpoint,userName=$($env:MONSTER_USER),password=$($env:MONSTER_PASSWORD),authType=Basic"

switch ($Mode) {
    'Deploy' {
        $local = (Resolve-Path -LiteralPath $Path).Path
        $problems = @(Test-DeployPackage -Path $local)
        if ($problems.Count) { throw "Refusing to deploy ${local}: $($problems -join '; ')." }
        $source = "-source:contentPath=$local"; $dest = "-dest:$remote"
    }
    'Restore' {
        $local = (Resolve-Path -LiteralPath $Path).Path
        if (-not (Test-Path -LiteralPath (Join-Path $local 'web.config'))) { throw "$local has no web.config; nothing to restore." }
        $source = "-source:contentPath=$local"; $dest = "-dest:$remote"
    }
    'Backup' {
        if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
        New-Item -ItemType Directory -Path $Path | Out-Null
        $local = (Resolve-Path -LiteralPath $Path).Path
        $source = "-source:$remote"; $dest = "-dest:contentPath=$local"
    }
}

$msdeploy = @("$env:ProgramFiles\IIS\Microsoft Web Deploy V3\msdeploy.exe",
    "${env:ProgramFiles(x86)}\IIS\Microsoft Web Deploy V3\msdeploy.exe") | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $msdeploy) { throw 'msdeploy.exe (Web Deploy V3) not found; use a windows runner.' }

$msArgs = @('-verb:sync', $source, $dest) + (Get-MsDeploySkipArgs) +
    @('-retryAttempts:3', '-retryInterval:3000')
if ($Mode -ne 'Backup') { $msArgs += '-enableRule:AppOffline' }
if ($env:MONSTER_ALLOW_UNTRUSTED -eq 'true') { $msArgs += '-allowUntrusted' }

Write-Host "$Mode $contentPath via $endpoint"
function Invoke-MsDeploy([string[]]$arguments) {
    $output = & $msdeploy @arguments 2>&1
    $exit = $LASTEXITCODE
    $output | ForEach-Object { "$_".Replace($env:MONSTER_PASSWORD, '***') } | Write-Host
    return $exit
}
$code = Invoke-MsDeploy $msArgs
if ($code -ne 0 -and $Mode -ne 'Backup') {
    # A sync that failed after retries can leave app_offline.htm behind (site shows "offline" forever). Remove it;
    # if that fails too, the rollback restore (which deletes files not in the backup) removes it.
    $offline = "-dest:filePath=$contentPath/app_offline.htm,computerName=$endpoint,userName=$($env:MONSTER_USER),password=$($env:MONSTER_PASSWORD),authType=Basic"
    $cleanup = @('-verb:delete', $offline, '-retryAttempts:2', '-retryInterval:3000')
    if ($env:MONSTER_ALLOW_UNTRUSTED -eq 'true') { $cleanup += '-allowUntrusted' }
    if ((Invoke-MsDeploy $cleanup) -ne 0) { Write-Host "::warning::Could not remove $contentPath/app_offline.htm; check the site." }
}
if ($code -ne 0) { throw "msdeploy $Mode of $contentPath failed (exit $code)." }
if ($Mode -eq 'Backup' -and -not (Test-Path -LiteralPath (Join-Path $local 'web.config'))) {
    throw "Nothing to back up at $contentPath (no web.config; first deploy?). This part has no automatic rollback."
}
