param(
    [Parameter(Mandatory=$true)]
    [string]$TargetDir,

    [Parameter(Mandatory=$true)]
    [string]$Configuration,

    [Parameter(Mandatory=$false)]
    [string]$ProjectOutputDir = ""
)

$ErrorActionPreference = "Continue"

try {
    if (-not (Test-Path $TargetDir)) {
        Write-Warning "[PostBuild] TargetDir khong ton tai: $TargetDir"
        exit 0
    }

    $dllPath = Join-Path $TargetDir "LHBBlockScheduler.dll"
    if (Test-Path $dllPath) {
        $dllItem = Get-Item $dllPath
        $dllSize = $dllItem.Length
        $md5 = (Get-FileHash -Path $dllPath -Algorithm MD5).Hash
        $buildTime = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")

        $infoPath = Join-Path $TargetDir "build-info.txt"
        $content = @"
Configuration: $Configuration
BuildTime: $buildTime
DllSize: $dllSize
MD5: $md5
"@
        [System.IO.File]::WriteAllText($infoPath, $content, [System.Text.Encoding]::UTF8)
        Write-Host "[PostBuild] Da ghi build-info.txt ($Configuration | MD5: $md5 | Size: $dllSize bytes)" -ForegroundColor Green

        # Neu co ProjectOutputDir (bin\Debug\net48 hoac bin\Release\net48), ghi build-info.txt vao do luon
        if (-not [string]::IsNullOrEmpty($ProjectOutputDir) -and (Test-Path $ProjectOutputDir)) {
            $binInfoPath = Join-Path $ProjectOutputDir "build-info.txt"
            [System.IO.File]::WriteAllText($binInfoPath, $content, [System.Text.Encoding]::UTF8)
        }
    }

    # 1. Don dep thu muc cung cau hinh (Runtime\$Configuration), chi giu lai 3 ban moi nhat
    $configDir = Split-Path $TargetDir -Parent
    if (Test-Path $configDir) {
        $dirs = Get-ChildItem -Path $configDir -Directory |
            Where-Object { $_.Name -match '^\d{8}_\d{6}$' } |
            Sort-Object Name -Descending

        if ($dirs.Count -gt 3) {
            $toDelete = $dirs | Select-Object -Skip 3
            foreach ($d in $toDelete) {
                try {
                    Write-Host "[PostBuild] Xoa runtime cu ($Configuration): $($d.FullName)" -ForegroundColor Yellow
                    Remove-Item $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
                } catch { }
            }
        }
    }

    # 2. Don dep thu muc Runtime legacy kieu cu (neu co truc tiep duoi Runtime\ thay vi Runtime\$Configuration\)
    $runtimeRoot = Split-Path $configDir -Parent
    if (Test-Path $runtimeRoot) {
        $legacyDirs = Get-ChildItem -Path $runtimeRoot -Directory |
            Where-Object { $_.Name -match '^\d{8}_\d{6}$' }
        foreach ($d in $legacyDirs) {
            try {
                Write-Host "[PostBuild] Xoa runtime legacy: $($d.FullName)" -ForegroundColor Gray
                Remove-Item $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
            } catch { }
        }
    }

    # 3. Ghi de active-config.txt sau MOI lan build (build gan nhat quyet dinh cau hinh active)
    $appDataLHB = Split-Path $runtimeRoot -Parent
    if (-not (Test-Path $appDataLHB)) {
        New-Item -ItemType Directory -Path $appDataLHB -Force | Out-Null
    }
    $activeConfigFile = Join-Path $appDataLHB "active-config.txt"
    [System.IO.File]::WriteAllText($activeConfigFile, $Configuration.Trim(), [System.Text.Encoding]::UTF8)
    Write-Host "[PostBuild] Da ghi de active-config.txt -> $Configuration" -ForegroundColor Cyan

} catch {
    Write-Warning "[PostBuild] Loi trong PostBuild: $_"
}
