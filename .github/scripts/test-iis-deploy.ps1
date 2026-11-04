# Tests for iis-deploy/WebDeploy.psm1 (no network, no msdeploy). Run: pwsh .github/scripts/test-iis-deploy.ps1
# Called by test-scripts.sh when pwsh is available (it is on GitHub's ubuntu runners).
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'iis-deploy/WebDeploy.psm1') -Force
$script:failed = 0; $script:passed = 0
function Check([string]$name, [scriptblock]$test) {
    try { if (& $test) { $script:passed++ } else { $script:failed++; Write-Host "FAIL: $name" } }
    catch { $script:failed++; Write-Host "FAIL: $name -> $($_.Exception.Message)" }
}
function Throws([scriptblock]$block) { try { & $block | Out-Null; return $false } catch { return $true } }

# Endpoint normalisation (bare host, host:port, full URL) and site escaping
Check 'bare host' { (Get-MsDeployEndpoint 'site1.siteasp.net' 'site1') -eq 'https://site1.siteasp.net:8172/msdeploy.axd?site=site1' }
Check 'host:port' { (Get-MsDeployEndpoint 'h.example:8443' 's') -eq 'https://h.example:8443/msdeploy.axd?site=s' }
Check 'full url' { (Get-MsDeployEndpoint 'https://h.example:8172/msdeploy.axd?site=x' ' s ') -eq 'https://h.example:8172/msdeploy.axd?site=s' }
Check 'http refused' { Throws { Get-MsDeployEndpoint 'http://h.example' 's' } }

# Comma / empty values
Check 'comma refused' { Throws { Assert-MsDeployValue 'MONSTER_PASSWORD' 'a,b' } }
Check 'empty refused' { Throws { Assert-MsDeployValue 'MONSTER_SITE' '' } }
Check 'quote refused' { Throws { Assert-MsDeployValue 'MONSTER_PASSWORD' 'a"b' } }
Check 'newline refused' { Throws { Assert-MsDeployValue 'MONSTER_USER' "u`n" } }
Check 'special chars ok' { -not (Throws { Assert-MsDeployValue 'MONSTER_PASSWORD' 'p@ss;w0rd!$%&^=' }) }
Check 'plain ok' { -not (Throws { Assert-MsDeployValue 'MONSTER_SITE' 'site1' }) }

# Content paths
Check 'root content path' { (Get-ContentPath 'site1' '') -eq 'site1' }
Check 'sub-app content path' { (Get-ContentPath 'site1/' '/api/') -eq 'site1/api' }
Check 'nested app refused' { Throws { Get-ContentPath 'site1' 'api/../x' } }

# Skip rules: protected folders always, the sub-application only when asked
$root = Get-MsDeploySkipArgs -ProtectSubApp 'api'
$sub = Get-MsDeploySkipArgs
function Skipped([string[]]$rules, [string]$kind, [string]$path) {
    foreach ($r in $rules) { if ($r -match "objectName=$kind,absolutePath=(.*)$" -and $path -match $Matches[1]) { return $true } }
    return $false
}
Check 'web skips api dir' { Skipped $root 'dirPath' 'C:\home\site1\wwwroot\api' }
Check 'web skips api file' { Skipped $root 'filePath' 'C:\home\site1\wwwroot\api\NeedleBase.Api.dll' }
Check 'web skips API (case)' { Skipped $root 'filePath' 'C:\home\site1\wwwroot\API\web.config' }
Check 'web does not skip apifoo' { -not (Skipped $root 'filePath' 'C:\home\site1\wwwroot\apifoo.js') }
Check 'web does not skip web.config' { -not (Skipped $root 'filePath' 'C:\home\site1\wwwroot\web.config') }
Check 'api deploy does not skip api' { -not (Skipped $sub 'filePath' 'C:\home\site1\wwwroot\api\NeedleBase.Api.dll') }
Check 'App_Data kept' { (Skipped $sub 'dirPath' 'C:\s\api\App_Data') -and (Skipped $sub 'filePath' 'C:\s\api\App_Data\keys\key-1.xml') }
Check 'logs kept' { Skipped $sub 'filePath' 'C:\s\logs\stdout_1.log' }
Check 'bad sub-app name' { Throws { Get-MsDeploySkipArgs -ProtectSubApp 'a|b' } }

# Package checks
$tmp = Join-Path ([IO.Path]::GetTempPath()) "iisdeploy-$([guid]::NewGuid().ToString('N'))"
function NewPackage([string]$config) {
    $dir = Join-Path $tmp ([guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $dir | Out-Null
    if ($config) { Set-Content -LiteralPath (Join-Path $dir 'web.config') -Value $config }
    return $dir
}
$good = '<?xml version="1.0"?><configuration><location path="." inheritInChildApplications="false"><system.webServer><handlers><add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" /></handlers><aspNetCore processPath="dotnet" arguments=".\x.dll" hostingModel="outofprocess" /></system.webServer></location></configuration>'
$inheriting = '<?xml version="1.0"?><configuration><system.webServer><aspNetCore processPath="dotnet" /></system.webServer></configuration>'
try {
    Check 'good package' { @(Test-DeployPackage (NewPackage $good) -ProtectSubApp 'api').Count -eq 0 }
    Check 'no web.config' { @(Test-DeployPackage (NewPackage '')).Count -eq 1 }
    Check 'inheriting web.config' { @(Test-DeployPackage (NewPackage $inheriting)).Count -ge 1 }
    Check 'App_Data refused' { $d = NewPackage $good; New-Item -ItemType Directory (Join-Path $d 'App_Data') | Out-Null; @(Test-DeployPackage $d).Count -eq 1 }
    Check 'dev settings refused' { $d = NewPackage $good; Set-Content (Join-Path $d 'appsettings.Development.json') '{}'; @(Test-DeployPackage $d).Count -eq 1 }
    Check 'nested api in web refused' { $d = NewPackage $good; New-Item -ItemType Directory (Join-Path $d 'wwwroot/Api') -Force | Out-Null; @(Test-DeployPackage $d -ProtectSubApp 'api').Count -eq 1 }
    Check 'nested logs refused' { $d = NewPackage $good; New-Item -ItemType Directory (Join-Path $d 'wwwroot/lib/logs') -Force | Out-Null; @(Test-DeployPackage $d).Count -eq 1 }
    Check 'data folder refused' { $d = NewPackage $good; New-Item -ItemType Directory (Join-Path $d 'Data') | Out-Null; @(Test-DeployPackage $d).Count -eq 1 }
    Check 'data file allowed' { $d = NewPackage $good; Set-Content (Join-Path $d 'data.json') '{}'; @(Test-DeployPackage $d).Count -eq 0 }
    Check 'api folder fine for api package' { $d = NewPackage $good; New-Item -ItemType Directory (Join-Path $d 'wwwroot/api') -Force | Out-Null; @(Test-DeployPackage $d).Count -eq 0 }

    # SITE_URL -> package environment (non-secret URLs only), kept inside the non-inheriting <location>
    $site = 'https://needlebase.runasp.net/'
    $webEnv = Get-SitePackageEnvironment -App Web -SiteUrl 'https://needlebase.runasp.net'
    $apiEnv = Get-SitePackageEnvironment -App Api -SiteUrl ' https://needlebase.runasp.net// '
    Check 'web env from SITE_URL' { $webEnv.Count -eq 1 -and $webEnv['Api__BaseUrl'] -ceq $site }
    Check 'api env from SITE_URL' { $apiEnv.Count -eq 1 -and $apiEnv['FrontendBaseUrl'] -ceq $site }
    $w = NewPackage $good
    Set-PackageEnvironment -Path $w -Variables $webEnv
    Check 'env written' { (Get-PackageEnvironment $w)['Api__BaseUrl'] -ceq $site }
    Check 'env keeps location rules' { @(Test-DeployPackage $w -ProtectSubApp 'api' -RequiredEnvironment $webEnv).Count -eq 0 }
    Check 'env inside location' { ([xml](Get-Content -Raw (Join-Path $w 'web.config'))).SelectNodes("/configuration/location[@path='.' and @inheritInChildApplications='false']/system.webServer/aspNetCore/environmentVariables/environmentVariable[@name='Api__BaseUrl']").Count -eq 1 }
    Set-PackageEnvironment -Path $w -Variables ([ordered]@{ Api__BaseUrl = 'https://other.example/' })
    Check 'env replaced, not duplicated' { $e = Get-PackageEnvironment $w; $e.Count -eq 1 -and $e['Api__BaseUrl'] -eq 'https://other.example/' }
    Check 'wrong env value reported' { @(Test-DeployPackage $w -RequiredEnvironment $webEnv).Count -eq 1 }
    Check 'missing env reported' { @(Test-DeployPackage (NewPackage $good) -RequiredEnvironment $apiEnv).Count -eq 1 }
    $existing = $good.Replace('hostingModel="outofprocess" />', 'hostingModel="outofprocess"><environmentVariables><environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" /></environmentVariables></aspNetCore>')
    $a = NewPackage $existing
    Set-PackageEnvironment -Path $a -Variables $apiEnv
    Check 'other env entries kept' { $e = Get-PackageEnvironment $a; $e['ASPNETCORE_ENVIRONMENT'] -eq 'Production' -and $e['FrontendBaseUrl'] -ceq $site }
    Check 'unlisted name refused' { Throws { Set-PackageEnvironment -Path (NewPackage $good) -Variables @{ Email__Password = 'x' } } }
    $srv = NewPackage $good
    Set-PackageEnvironment -Path $srv -Variables (Get-ApiServerEnvironment -DbConnection 'Host=h;Database=d;Username=u;Password="a;b"')
    Check 'server env written' { $e = Get-PackageEnvironment $srv; $e['ConnectionStrings__DefaultConnection'] -match 'Host=h' -and $e['JwtSettings__SecurityKey'].Length -ge 32 -and $e['Platform__RequireLicenseKey'] -eq 'false' }
    Check 'empty value refused' { Throws { Set-PackageEnvironment -Path (NewPackage $good) -Variables @{ FrontendBaseUrl = ' ' } } }
    Check 'inheriting config refused' { Throws { Set-PackageEnvironment -Path (NewPackage $inheriting) -Variables $apiEnv } }
    Check 'no web.config refused' { Throws { Set-PackageEnvironment -Path (NewPackage '') -Variables $apiEnv } }
} finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }

# Site root normalisation
Check 'site root slash added' { (Get-SiteRootUrl 'https://needlebase.runasp.net') -ceq 'https://needlebase.runasp.net/' }
Check 'site root port' { (Get-SiteRootUrl 'https://h.example:8443/') -ceq 'https://h.example:8443/' }
foreach ($bad in @('', 'http://needlebase.runasp.net/', 'https://needlebase.runasp.net/api/', 'https://x.invalid/', 'needlebase.runasp.net', 'https://h.example/?a=1', 'https://h ex/')) {
    Check "site root refused '$bad'" { Throws { Get-SiteRootUrl $bad } }
}

# Migration connection -> PG* variables
$n = ConvertTo-PgEnvironment 'Host=db.example;Port=5433;Database=nb;Username=u;Password=p=1;SSL Mode=VerifyFull'
Check 'npgsql parsed' { $n.PGHOST -eq 'db.example' -and $n.PGPORT -eq '5433' -and $n.PGDATABASE -eq 'nb' -and $n.PGUSER -eq 'u' -and $n.PGPASSWORD -eq 'p=1' -and $n.PGSSLMODE -eq 'verify-full' }
$d = ConvertTo-PgEnvironment 'Server=h;Database=d;User Id=u'
Check 'npgsql defaults' { $d.PGPORT -eq '5432' -and $d.PGSSLMODE -eq 'require' -and $d.PGUSER -eq 'u' -and -not $d.PGPASSWORD }
$u = ConvertTo-PgEnvironment 'postgresql://us%40r:p%3Aw@db.example:6543/nb?sslmode=disable'
Check 'uri parsed' { $u.PGHOST -eq 'db.example' -and $u.PGPORT -eq '6543' -and $u.PGDATABASE -eq 'nb' -and $u.PGUSER -eq 'us@r' -and $u.PGPASSWORD -eq 'p:w' -and $u.PGSSLMODE -eq 'disable' }
$q = ConvertTo-PgEnvironment 'Host=h;Database=d;Username=u;Password="pa;ss=word"'
Check 'quoted npgsql password' { $q.PGPASSWORD -eq 'pa;ss=word' }
$v6 = ConvertTo-PgEnvironment 'postgresql://u:p@[::1]:5432/d'
Check 'uri ipv6 host' { $v6.PGHOST -eq '::1' }
Check 'garbage refused' { Throws { ConvertTo-PgEnvironment 'Host="unterminated' } }
Check 'incomplete refused' { Throws { ConvertTo-PgEnvironment 'Host=h' } }

# The one database secret -> Api settings
$c = ConvertTo-NpgsqlConnectionString 'postgresql://us%40r:p%3Bw@db.example:6543/nb?sslmode=verify-full'
$cb = [System.Data.Common.DbConnectionStringBuilder]::new(); $cb.set_ConnectionString($c)
Check 'uri -> npgsql' { $cb['Host'] -eq 'db.example' -and $cb['Port'] -eq '6543' -and $cb['Database'] -eq 'nb' -and $cb['Username'] -eq 'us@r' -and $cb['Password'] -eq 'p;w' -and $cb['SSL Mode'] -eq 'VerifyFull' }
Check 'npgsql default ssl' { (ConvertTo-NpgsqlConnectionString 'Host=h;Database=d;Username=u') -match 'SSL Mode=Require' }
$k1 = Get-DerivedJwtKey 'Host=h;Database=d;Username=u;Password=p'
Check 'derived jwt stable and long' { $k1 -ceq (Get-DerivedJwtKey ' Host=h;Database=d;Username=u;Password=p ') -and $k1.Length -ge 32 }
Check 'derived jwt depends on secret' { $k1 -cne (Get-DerivedJwtKey 'Host=h;Database=d;Username=u;Password=q') }
$se = Get-ApiServerEnvironment -DbConnection 'Host=h;Database=d;Username=u' -JwtKey ('k' * 40) -PlatformBaseUrl 'https://p.example/' -PlatformKey 'pk' -PlatformIssuer 'iss'
Check 'explicit jwt + platform' { $se['JwtSettings__SecurityKey'] -eq ('k' * 40) -and $se['Platform__RequireLicenseKey'] -eq 'true' -and $se['Platform__BaseUrl'] -eq 'https://p.example/' -and $se['Platform__IntegrationKey'] -eq 'pk' -and $se['Platform__TokenIssuer'] -eq 'iss' }
Check 'short jwt refused' { Throws { Get-ApiServerEnvironment -DbConnection 'Host=h;Database=d;Username=u' -JwtKey 'short' } }
Check 'half platform refused' { Throws { Get-ApiServerEnvironment -DbConnection 'Host=h;Database=d;Username=u' -PlatformKey 'pk' } }

# Health URL of the sub-application
Check 'health url' { (Get-ApiHealthUrl 'https://site.example/') -eq 'https://site.example/api/health/ready' }
Check 'health url no slash' { (Get-ApiHealthUrl 'https://site.example') -eq 'https://site.example/api/health/ready' }
Check 'site url with path refused' { Throws { Get-ApiHealthUrl 'https://site.example/app/' } }
Check 'http site url refused' { Throws { Get-ApiHealthUrl 'http://site.example/' } }

# Wait-Healthy: https only, plain http only with -AllowHttp (SITE_HTTPS_PENDING first deploy). Port 9 on loopback is
# closed, so the allowed call fails fast with the "did not return 200" message rather than the https refusal.
$wait = Join-Path $PSScriptRoot 'iis-deploy/Wait-Healthy.ps1'
function WaitError([string]$url, [switch]$allow) {
    try { & $wait -Url $url -Attempts 1 -DelaySeconds 0 -AllowHttp:$allow 6>$null | Out-Null; return '' } catch { return $_.Exception.Message }
}
Check 'wait: http refused by default' { (WaitError 'http://127.0.0.1:9/') -like '*must be https*' }
Check 'wait: http allowed with -AllowHttp' { (WaitError 'http://127.0.0.1:9/' -allow) -like '*did not return 200*' }
Check 'wait: other schemes refused even with -AllowHttp' { (WaitError 'ftp://127.0.0.1:9/' -allow) -like '*must be https*' }

Write-Host "iis-deploy: $script:passed passed, $script:failed failed"
if ($script:failed) { exit 1 }
