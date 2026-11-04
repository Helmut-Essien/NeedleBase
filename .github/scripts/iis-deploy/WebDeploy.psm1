# Helpers for deploying NeedleBase to ONE MonsterASP/IIS site (docs/deployment.md, docs/ci-cd.md):
#   site root  = Web           (IIS application "/")
#   /api       = Api           (IIS sub-application "/api" with its own application pool)
# Pure functions (no network) so .github/scripts/test-iis-deploy.ps1 can test them; Deploy-Site.ps1 runs msdeploy.
Set-StrictMode -Version Latest

# Server folders a deploy must never delete or overwrite: Data Protection key rings, logs, host data, search-engine
# verification files. With -ProtectSubApp the whole sub-application folder (the Api under the Web) is skipped too.
function Get-MsDeploySkipArgs {
    param([string]$ProtectSubApp = '')
    $skips = @(
        '-skip:objectName=dirPath,absolutePath=\\App_Data$',
        '-skip:objectName=filePath,absolutePath=\\App_Data\\',
        '-skip:objectName=dirPath,absolutePath=\\logs$',
        '-skip:objectName=filePath,absolutePath=\\logs\\',
        '-skip:objectName=dirPath,absolutePath=\\data$',
        '-skip:objectName=filePath,absolutePath=\\data\\',
        '-skip:objectName=filePath,absolutePath=google[^\\]*\.html$',
        '-skip:objectName=filePath,absolutePath=BingSiteAuth\.xml$'
    )
    if ($ProtectSubApp) {
        if ($ProtectSubApp -notmatch '^[A-Za-z0-9_-]+$') { throw "Sub-application name '$ProtectSubApp' must be one plain folder name." }
        $skips += "-skip:objectName=dirPath,absolutePath=(?i)\\$ProtectSubApp$"
        $skips += "-skip:objectName=filePath,absolutePath=(?i)\\$ProtectSubApp\\"
    }
    return $skips
}

# Accept a bare host ("site123.siteasp.net"), host:port or a full https://host:8172/msdeploy.axd URL (as Platform).
function Get-MsDeployEndpoint {
    param([Parameter(Mandatory)][string]$Server, [Parameter(Mandatory)][string]$Site)
    $s = $Server.Trim()
    if ($s -notmatch '^[a-z][a-z0-9+.-]*://') { $s = "https://$s" }
    $u = [Uri]$s
    if ($u.Scheme -ne 'https') { throw 'The Web Deploy server must use https.' }
    $port = if ($u.IsDefaultPort) { 8172 } else { $u.Port }
    return "https://$($u.Host):$port/msdeploy.axd?site=$([Uri]::EscapeDataString($Site.Trim()))"
}

# msdeploy splits provider settings on commas; refuse values it would mangle instead of failing obscurely.
function Assert-MsDeployValue {
    param([Parameter(Mandatory)][string]$Name, [AllowEmptyString()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { throw "$Name is not set (see docs/ci-cd.md)." }
    # msdeploy splits provider settings on ',' and its own command-line parser mangles '"'.
    if ($Value.Contains(',') -or $Value.Contains('"')) { throw "$Name contains a comma or double quote, which msdeploy cannot parse. Change it in the MonsterASP panel." }
    if ($Value -match '[\r\n]') { throw "$Name contains a line break (pasted with a trailing newline?)." }
}

# IIS content path of the target: "site" for the root app, "site/api" for the sub-application.
function Get-ContentPath {
    param([Parameter(Mandatory)][string]$Site, [string]$AppPath = '')
    $site = $Site.Trim().TrimEnd('/')
    $app = $AppPath.Trim().Trim('/')
    if ($app -and $app -notmatch '^[A-Za-z0-9_-]+$') { throw "App path '$AppPath' must be one plain folder name." }
    if ($app) { return "$site/$app" } else { return $site }
}

# A package must be a dotnet publish output whose web.config does not leak into child applications, and must not
# carry anything that would replace server state or collide with the sub-application.
function Test-DeployPackage {
    param([Parameter(Mandatory)][string]$Path, [string]$ProtectSubApp = '', [System.Collections.IDictionary]$RequiredEnvironment)
    $problems = @()
    if ($RequiredEnvironment) {
        $actual = Get-PackageEnvironment -Path $Path
        foreach ($name in $RequiredEnvironment.Keys) {
            if (-not $actual.Contains($name)) { $problems += "web.config is missing environment variable $name (expected '$($RequiredEnvironment[$name])')" }
            elseif ($actual[$name] -cne [string]$RequiredEnvironment[$name]) { $problems += "web.config has $name='$($actual[$name])', expected '$($RequiredEnvironment[$name])'" }
        }
    }
    $webConfig = Join-Path $Path 'web.config'
    if (-not (Test-Path -LiteralPath $webConfig)) {
        $problems += 'no web.config (not a dotnet publish output?)'
    } else {
        [xml]$xml = Get-Content -Raw -LiteralPath $webConfig
        $loc = $xml.SelectSingleNode("/configuration/location[@path='.' and @inheritInChildApplications='false']")
        if (-not $loc -or -not $loc.SelectSingleNode('system.webServer/aspNetCore')) {
            $problems += "web.config must wrap system.webServer in <location path=`".`" inheritInChildApplications=`"false`"> so the root app's handler never leaks into /api"
        }
        if ($xml.SelectSingleNode('/configuration/system.webServer')) {
            $problems += 'web.config has a system.webServer section outside the non-inheriting <location>'
        }
    }
    $all = @(Get-ChildItem -LiteralPath $Path -Recurse -Force)
    if ($all | Where-Object { $_.PSIsContainer -and $_.Name -ieq 'App_Data' }) { $problems += 'contains App_Data (server key rings must never be replaced)' }
    # The skip rules protect every folder named logs/data on the server, so such a folder in a package would be
    # silently left out of the deploy. Refuse it instead.
    $protected = @($all | Where-Object { $_.PSIsContainer -and ($_.Name -ieq 'logs' -or $_.Name -ieq 'data') })
    if ($protected) { $problems += "contains folder(s) named logs/data, which deploys always skip: $(($protected | ForEach-Object { $_.FullName.Substring($Path.Length) }) -join ', ')" }
    if ($all | Where-Object { -not $_.PSIsContainer -and $_.Name -ieq 'appsettings.Development.json' }) { $problems += 'contains appsettings.Development.json' }
    if ($ProtectSubApp -and ($all | Where-Object { $_.Name -ieq $ProtectSubApp })) {
        $problems += "contains a file or folder named '$ProtectSubApp', which would collide with the /$ProtectSubApp sub-application"
    }
    return $problems
}

# Npgsql "Host=..;Username=.." or postgres:// URI -> PG* variables for psql, so the connection never appears on a
# command line or in the log (Platform's approach).
function ConvertTo-PgEnvironment {
    param([Parameter(Mandatory)][string]$Connection)
    $conn = $Connection.Trim()
    $map = @{}
    if ($conn -match '^postgres(ql)?://') {
        $uri = [Uri]$conn
        $user, $pw = $uri.UserInfo.Split(':', 2)
        $map['host'] = $uri.DnsSafeHost
        if ($uri.Port -gt 0) { $map['port'] = "$($uri.Port)" }
        $map['database'] = [Uri]::UnescapeDataString($uri.AbsolutePath.TrimStart('/'))
        $map['username'] = [Uri]::UnescapeDataString($user)
        if ($pw) { $map['password'] = [Uri]::UnescapeDataString($pw) }
        foreach ($pair in $uri.Query.TrimStart('?').Split('&', [StringSplitOptions]::RemoveEmptyEntries)) {
            $k, $v = $pair.Split('=', 2)
            if ($k -ieq 'sslmode' -and $v) { $map['sslmode'] = [Uri]::UnescapeDataString($v) }
        }
    } else {
        # DbConnectionStringBuilder handles quoted values ("pa;ss") and escaping the way Npgsql does.
        $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
        # Method calls on purpose: PowerShell treats $builder.X = ... and .Keys as dictionary entries.
        try { $builder.set_ConnectionString($conn) } catch { throw 'The migration connection string is not a valid key=value list.' }
        foreach ($k in @($builder.get_Keys())) { $val = $null; [void]$builder.TryGetValue($k, [ref]$val); $map[($k.ToLowerInvariant() -replace '\s', '')] = "$val" }
    }
    function Pick([string[]]$keys) { foreach ($k in $keys) { if ($map.ContainsKey($k) -and $map[$k]) { return $map[$k] } }; return $null }
    $ssl = Pick @('sslmode')
    $result = [ordered]@{
        PGHOST     = Pick @('host', 'server')
        PGPORT     = (Pick @('port')) ?? '5432'
        PGDATABASE = Pick @('database', 'db')
        PGUSER     = Pick @('username', 'userid', 'user', 'uid')
        PGPASSWORD = Pick @('password', 'pwd')
        PGSSLMODE  = if ($ssl) { $ssl.ToLowerInvariant() -replace 'verifyfull', 'verify-full' -replace 'verifyca', 'verify-ca' } else { 'require' }
    }
    if (-not $result.PGHOST -or -not $result.PGDATABASE -or -not $result.PGUSER) {
        throw 'Could not read host, database and username from the migration connection string.'
    }
    return $result
}

# Health URL of the Api sub-application from the site URL.
function Get-ApiHealthUrl {
    param([Parameter(Mandatory)][string]$SiteUrl, [string]$AppPath = 'api')
    if ($SiteUrl -notmatch '^https://[^/\s]+/?$') { throw "SITE_URL must look like https://host/ (got '$SiteUrl')." }
    return $SiteUrl.TrimEnd('/') + '/' + $AppPath.Trim('/') + '/health/ready'
}

# SITE_URL -> normalised site root "https://host/" (one trailing slash). Throws unless it is an https site root.
function Get-SiteRootUrl {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$SiteUrl)
    $u = $SiteUrl.Trim()
    if ($u -notmatch '^https://[A-Za-z0-9.-]+(:[0-9]{1,5})?/*$' -or $u -match '\.invalid(:|/|$)') {
        throw "SITE_URL must be the https site root, e.g. https://needlebase.runasp.net/ (got '$SiteUrl')."
    }
    return $u.TrimEnd('/') + '/'
}

# Non-secret settings CI may bake into a package's web.config. Anything else is refused, so a secret can never be
# written into a package (secrets stay on the server).
$script:PackageEnvironmentAllowList = @('Api__BaseUrl', 'FrontendBaseUrl')

# The package settings each app gets from SITE_URL (single site: Web at /, Api at /api; the Web and the Android app
# call "api/..." relative to the site root, so both values are the root).
function Get-SitePackageEnvironment {
    param([Parameter(Mandatory)][ValidateSet('Api', 'Web')][string]$App, [Parameter(Mandatory)][AllowEmptyString()][string]$SiteUrl)
    $root = Get-SiteRootUrl -SiteUrl $SiteUrl
    if ($App -eq 'Web') { return [ordered]@{ Api__BaseUrl = $root } }
    return [ordered]@{ FrontendBaseUrl = $root }
}

function Get-AspNetCoreElement([xml]$Xml) {
    return $Xml.SelectSingleNode("/configuration/location[@path='.' and @inheritInChildApplications='false']/system.webServer/aspNetCore")
}

# Writes <environmentVariable name=.. value=..> under the package's aspNetCore element (inside the non-inheriting
# <location>, which is kept as is). Existing entries with the same name are replaced; other entries are untouched.
function Set-PackageEnvironment {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][System.Collections.IDictionary]$Variables)
    $webConfig = Join-Path $Path 'web.config'
    if (-not (Test-Path -LiteralPath $webConfig)) { throw "No web.config in '$Path'." }
    foreach ($name in $Variables.Keys) {
        if ($script:PackageEnvironmentAllowList -cnotcontains $name) { throw "'$name' may not be written into a package (only $($script:PackageEnvironmentAllowList -join ', '))." }
        if ([string]::IsNullOrWhiteSpace([string]$Variables[$name])) { throw "Empty value for '$name'." }
    }
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load((Resolve-Path -LiteralPath $webConfig).ProviderPath)
    $core = Get-AspNetCoreElement $xml
    if (-not $core) { throw "web.config in '$Path' has no aspNetCore element inside <location path=`".`" inheritInChildApplications=`"false`">." }
    $vars = $core.SelectSingleNode('environmentVariables')
    if (-not $vars) { $vars = $core.AppendChild($xml.CreateElement('environmentVariables')) }
    foreach ($name in $Variables.Keys) {
        foreach ($old in @($vars.SelectNodes('environmentVariable') | Where-Object { $_.GetAttribute('name') -ieq $name })) { $null = $vars.RemoveChild($old) }
        $e = $xml.CreateElement('environmentVariable')
        $e.SetAttribute('name', $name)
        $e.SetAttribute('value', [string]$Variables[$name])
        $null = $vars.AppendChild($e)
    }
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $writer = [System.Xml.XmlWriter]::Create($webConfig, $settings)
    try { $xml.Save($writer) } finally { $writer.Dispose() }
}

# name -> value of the package's web.config environment variables (empty when there are none).
function Get-PackageEnvironment {
    param([Parameter(Mandatory)][string]$Path)
    $result = [ordered]@{}
    $webConfig = Join-Path $Path 'web.config'
    if (-not (Test-Path -LiteralPath $webConfig)) { return $result }
    [xml]$xml = Get-Content -Raw -LiteralPath $webConfig
    $core = Get-AspNetCoreElement $xml
    if ($core) { foreach ($e in @($core.SelectNodes('environmentVariables/environmentVariable'))) { $result[$e.GetAttribute('name')] = $e.GetAttribute('value') } }
    return $result
}

Export-ModuleMember -Function Get-MsDeploySkipArgs, Get-MsDeployEndpoint, Assert-MsDeployValue, Get-ContentPath, Test-DeployPackage, ConvertTo-PgEnvironment, Get-ApiHealthUrl, Get-SiteRootUrl, Get-SitePackageEnvironment, Set-PackageEnvironment, Get-PackageEnvironment
