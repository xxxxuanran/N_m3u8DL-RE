param(
    [string]$ReleaseTag = ""
)

$ErrorActionPreference = 'Stop'

function Get-CommitDate([string]$Commit) {
    $previousTz = $env:TZ
    $env:TZ = 'Asia/Shanghai'
    try {
        return (git log -1 --format=%cd --date=format:%Y%m%d $Commit).Trim()
    }
    finally {
        if ($null -eq $previousTz) {
            Remove-Item Env:TZ -ErrorAction SilentlyContinue
        }
        else {
            $env:TZ = $previousTz
        }
    }
}

function Get-FallbackSuffix {
    $date = (Get-Date).ToString('yyyyMMdd')
    $version = $env:PRODUCT_VERSION
    if ([string]::IsNullOrWhiteSpace($version)) {
        $version = '0.0.0'
    }
    if (-not $version.StartsWith('v')) {
        $version = "v$version"
    }
    return "${date}+${version}"
}

try {
    $gitRoot = git rev-parse --show-toplevel 2>$null
    if (-not $gitRoot) {
        Write-Output (Get-FallbackSuffix)
        exit 0
    }

    $head = (git rev-parse HEAD).Trim()
    $releaseDate = Get-CommitDate $head

    if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
        $exactTag = git describe --tags --exact-match HEAD 2>$null
        if ($exactTag) {
            $ReleaseTag = $exactTag.Trim()
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleaseTag)) {
        $tagCommit = (git rev-parse "${ReleaseTag}^{commit}").Trim()
        if ($tagCommit -eq $head) {
            Write-Output "${releaseDate}+${ReleaseTag}"
            exit 0
        }
    }

    $shortCommit = (git rev-parse --short HEAD).Trim()
    Write-Output "${releaseDate}+${shortCommit}"
}
catch {
    Write-Output (Get-FallbackSuffix)
}
