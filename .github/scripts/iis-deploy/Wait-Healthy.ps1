# Poll a URL until it answers 200 (post-deploy check, docs/ci-cd.md). Fails the job if it never does.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Url,
    [int]$Attempts = 18,
    [int]$DelaySeconds = 10,
    # Only for SITE_HTTPS_PENDING (first deploy before the host has issued the certificate): a plain-http smoke check.
    [switch]$AllowHttp
)
$ErrorActionPreference = 'Stop'
if ($Url -notmatch '^https://' -and -not ($AllowHttp -and $Url -match '^http://')) { throw "Health URL must be https (got '$Url')." }
for ($i = 1; $i -le $Attempts; $i++) {
    try {
        $r = Invoke-WebRequest -Uri $Url -TimeoutSec 20 -SkipHttpErrorCheck
        Write-Host "[$i/$Attempts] $Url -> $($r.StatusCode)"
        if ($r.StatusCode -eq 200) { return }
    } catch {
        Write-Host "[$i/$Attempts] $Url -> $($_.Exception.Message)"
    }
    Start-Sleep -Seconds $DelaySeconds
}
throw "$Url did not return 200 after $Attempts attempts. Check the site log and docs/ci-cd.md (Rollback section)."
