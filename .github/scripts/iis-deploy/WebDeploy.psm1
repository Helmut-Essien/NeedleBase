# Helpers for deploying NeedleBase to ONE MonsterASP/IIS site (docs/deployment.md, docs/ci-cd.md): one package, one
# IIS application at the site root. The Web process hosts the Api under /api (Api:InProcess), like Platform, so
# there is no IIS sub-application to create.
# Pure functions (no network) so .github/scripts/test-iis-deploy.ps1 can test them; Deploy-Site.ps1 runs msdeploy.
Set-StrictMode -Version Latest

# Server folders a deploy must never delete or overwrite: Data Protection key rings, logs, host data, search-engine
# verification files (any folder of that name, e.g. api\App_Data left by the old /api sub-application).
function Get-MsDeploySkipArgs {
    return @(
        '-skip:objectName=dirPath,absolutePath=\\App_Data$',
        '-skip:objectName=filePath,absolutePath=\\App_Data\\',
        '-skip:objectName=dirPath,absolutePath=\\logs$',
        '-skip:objectName=filePath,absolutePath=\\logs\\',
        '-skip:objectName=dirPath,absolutePath=\\data$',
        '-skip:objectName=filePath,absolutePath=\\data\\',
        '-skip:objectName=filePath,absolutePath=google[^\\]*\.html$',
        '-skip:objectName=filePath,absolutePath=BingSiteAuth\.xml$'
    )
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

# IIS content path of the site root (the one application).
function Get-ContentPath {
    param([Parameter(Mandatory)][string]$Site)
    return $Site.Trim().TrimEnd('/')
}

# A package must be a dotnet publish output whose web.config does not leak into child applications, and must not
# carry anything that would replace server state.
function Test-DeployPackage {
    param([Parameter(Mandatory)][string]$Path, [System.Collections.IDictionary]$RequiredEnvironment)
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
            $problems += "web.config must wrap system.webServer in <location path=`".`" inheritInChildApplications=`"false`"> so the site's handler never leaks into a child application"
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

# The one database secret (URI or Npgsql string) -> the Npgsql string the Api runs with.
function ConvertTo-NpgsqlConnectionString {
    param([Parameter(Mandatory)][string]$Connection)
    $pg = ConvertTo-PgEnvironment -Connection $Connection
    $ssl = switch ($pg.PGSSLMODE) {
        'disable' { 'Disable' } 'allow' { 'Allow' } 'prefer' { 'Prefer' } 'require' { 'Require' }
        'verify-ca' { 'VerifyCA' } 'verify-full' { 'VerifyFull' } default { throw "Unknown sslmode '$($pg.PGSSLMODE)'." }
    }
    $b = [System.Data.Common.DbConnectionStringBuilder]::new()
    $b.set_Item('Host', $pg.PGHOST); $b.set_Item('Port', $pg.PGPORT); $b.set_Item('Database', $pg.PGDATABASE)
    $b.set_Item('Username', $pg.PGUSER)
    if ($pg.PGPASSWORD) { $b.set_Item('Password', $pg.PGPASSWORD) }
    $b.set_Item('SSL Mode', $ssl)
    return $b.get_ConnectionString()
}

# Stable JWT signing key when no JWT_SECURITY_KEY secret is set: HMAC-SHA256 of a fixed label keyed with the database
# secret (anyone holding that secret already owns the data). Same secret -> same key, so deploys keep sign-ins;
# changing the database password signs everyone out once.
function Get-DerivedJwtKey {
    param([Parameter(Mandatory)][string]$Connection)
    $hmac = [System.Security.Cryptography.HMACSHA256]::new([System.Text.Encoding]::UTF8.GetBytes($Connection.Trim()))
    try { return [Convert]::ToBase64String($hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes('NeedleBase JWT signing key v1'))) }
    finally { $hmac.Dispose() }
}

# Server settings the deploy writes into the Api package (nothing to set on the MonsterASP panel). Platform licensing
# is on only when both the Platform URL and key are given; otherwise shops register without a key (Api warns).
function Get-ApiServerEnvironment {
    param(
        [Parameter(Mandatory)][string]$DbConnection,
        [string]$JwtKey = '',
        [string]$PlatformBaseUrl = '',
        [string]$PlatformKey = '',
        [string]$PlatformIssuer = ''
    )
    $jwt = if ([string]::IsNullOrWhiteSpace($JwtKey)) { Get-DerivedJwtKey -Connection $DbConnection } else { $JwtKey.Trim() }
    if ($jwt.Length -lt 32) { throw 'JWT_SECURITY_KEY must be at least 32 characters.' }
    $result = [ordered]@{
        ConnectionStrings__DefaultConnection = ConvertTo-NpgsqlConnectionString -Connection $DbConnection
        JwtSettings__SecurityKey             = $jwt
    }
    $hasUrl = -not [string]::IsNullOrWhiteSpace($PlatformBaseUrl)
    $hasKey = -not [string]::IsNullOrWhiteSpace($PlatformKey)
    if ($hasUrl -ne $hasKey) { throw 'Set both PLATFORM_BASE_URL and PLATFORM_INTEGRATION_KEY, or neither.' }
    if ($hasUrl) {
        $result['Platform__RequireLicenseKey'] = 'true'
        $result['Platform__BaseUrl'] = $PlatformBaseUrl.Trim()
        $result['Platform__IntegrationKey'] = $PlatformKey.Trim()
        if (-not [string]::IsNullOrWhiteSpace($PlatformIssuer)) { $result['Platform__TokenIssuer'] = $PlatformIssuer.Trim() }
    } else {
        $result['Platform__RequireLicenseKey'] = 'false'
    }
    return $result
}

# Health URL of the Api (hosted by the Web under /api) from the site URL.
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

# Settings CI may write into a package's web.config. SITE_URL values are baked in at build time; the Api's server
# settings (connection string, JWT key, Platform) only by the deploy job, right before upload, so the uploaded
# artifacts never contain a secret. Any other name is refused.
$script:PackageEnvironmentAllowList = @('Api__BaseUrl', 'FrontendBaseUrl',
    'ConnectionStrings__DefaultConnection', 'JwtSettings__SecurityKey',
    'Platform__RequireLicenseKey', 'Platform__BaseUrl', 'Platform__IntegrationKey', 'Platform__TokenIssuer')

# The settings the site package gets from SITE_URL: the Web calls "api/..." and the Api builds e-mail links relative
# to the site root, so both values are the root (Web at /, Api at /api in the same process).
function Get-SitePackageEnvironment {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$SiteUrl)
    $root = Get-SiteRootUrl -SiteUrl $SiteUrl
    return [ordered]@{ Api__BaseUrl = $root; FrontendBaseUrl = $root }
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

Export-ModuleMember -Function Get-MsDeploySkipArgs, Get-MsDeployEndpoint, Assert-MsDeployValue, Get-ContentPath, Test-DeployPackage, ConvertTo-PgEnvironment, Get-ApiHealthUrl, Get-SiteRootUrl, Get-SitePackageEnvironment, Set-PackageEnvironment, Get-PackageEnvironment, ConvertTo-NpgsqlConnectionString, Get-DerivedJwtKey, Get-ApiServerEnvironment
