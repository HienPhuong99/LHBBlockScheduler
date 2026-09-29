param(
    [switch]$Install
)

# build.ps1 - Build Release + pack into .bundle (dung -Install de copy vao ApplicationPlugins)
# Run: powershell -ExecutionPolicy Bypass -File build.ps1 [-Install]

$ErrorActionPreference = "Stop"

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

Write-Host "1. Building Release..." -ForegroundColor Cyan
& $dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED - stopping." -ForegroundColor Red
    exit 1
}

$bundlePath = ".\Bundle\LHBBlockScheduler.bundle"
$contentsPath = "$bundlePath\Contents"
$distPath = ".\Dist\LHBBlockScheduler"

Write-Host "2. Copy files to Bundle/Contents and Dist folder..." -ForegroundColor Cyan
# Don sach Dist va Bundle/Contents truoc khi copy: tranh sot DLL cu (vd System.Text.Json.dll da bo tu 28/09/2026)
foreach ($p in @($contentsPath, $distPath)) {
    if (Test-Path $p) { Remove-Item "$p\*" -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $contentsPath | Out-Null
New-Item -ItemType Directory -Force -Path $distPath | Out-Null

# Copy LHB.lsp to bin
if (Test-Path ".\LHB.lsp") {
    Copy-Item ".\LHB.lsp" -Destination ".\bin\Release\net48\" -Force
}

# Copy all release files to Bundle/Contents
Copy-Item ".\bin\Release\net48\*" -Destination $contentsPath -Force -Recurse

# Copy release files to Dist folder for distribution to colleagues
Copy-Item ".\bin\Release\net48\*" -Destination $distPath -Force -Recurse
# LHBLoader chi dung tren may build (doc %APPDATA%\...\Runtime) -> khong dua vao ban phan phoi
Remove-Item "$distPath\LHBLoader.*" -Force -ErrorAction SilentlyContinue
if (Test-Path ".\LHB.lsp") {
    Copy-Item ".\LHB.lsp" -Destination $distPath -Force
}

# Dam bao build-info.txt co mat trong Dist va Bundle/Contents
$binInfo = ".\bin\Release\net48\build-info.txt"
if (Test-Path $binInfo) {
    Copy-Item $binInfo -Destination $distPath -Force
    Copy-Item $binInfo -Destination $contentsPath -Force
} else {
    $dllPath = "$distPath\LHBBlockScheduler.dll"
    if (Test-Path $dllPath) {
        $md5 = (Get-FileHash -Path $dllPath -Algorithm MD5).Hash
        $size = (Get-Item $dllPath).Length
        $time = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        $content = "Configuration: Release`nBuildTime: $time`nDllSize: $size`nMD5: $md5"
        [System.IO.File]::WriteAllText("$distPath\build-info.txt", $content, [System.Text.Encoding]::UTF8)
        [System.IO.File]::WriteAllText("$contentsPath\build-info.txt", $content, [System.Text.Encoding]::UTF8)
    }
}

# Ghi MD5 cua DLL vao LHB.lsp ban phan phoi: LISP chi NETLOAD thu muc co build-info.txt cung MD5
# (loi 29/09/2026: registry nho thu muc ban cu -> keo tha LHB.lsp ban moi van nap ban cu).
# Thay theo byte (Latin1 1:1) de giu nguyen ma hoa UTF-8 cua file LISP.
$distInfo = "$distPath\build-info.txt"
$md5Line = if (Test-Path $distInfo) { Get-Content $distInfo | Where-Object { $_ -match '^\s*MD5:' } | Select-Object -First 1 } else { $null }
if ($md5Line) {
    $md5 = ($md5Line -replace '^\s*MD5:\s*', '').Trim().ToUpperInvariant()
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    foreach ($lsp in @("$distPath\LHB.lsp", "$contentsPath\LHB.lsp")) {
        if (Test-Path $lsp) {
            $text = $latin1.GetString([System.IO.File]::ReadAllBytes($lsp))
            if ($text.Contains("@@LHB_BUILD_MD5@@")) {
                [System.IO.File]::WriteAllBytes($lsp, $latin1.GetBytes($text.Replace("@@LHB_BUILD_MD5@@", $md5)))
                Write-Host "   Da ghi MD5 $md5 vao $lsp" -ForegroundColor Green
            }
        }
    }
} else {
    Write-Host "   CANH BAO: khong doc duoc MD5 tu $distInfo -> LHB.lsp khong kiem tra ban" -ForegroundColor Yellow
}

$destApplicationPlugins = "$env:APPDATA\Autodesk\ApplicationPlugins\LHBBlockScheduler.bundle"

if ($Install) {
    Write-Host "3. Copy Bundle to ApplicationPlugins (-Install)..." -ForegroundColor Cyan
    Write-Host "   $destApplicationPlugins"

    # Create Autodesk\ApplicationPlugins if not exists
    $parentDir = "$env:APPDATA\Autodesk\ApplicationPlugins"
    if (-not (Test-Path $parentDir)) {
        New-Item -ItemType Directory -Force -Path $parentDir | Out-Null
    }

    if (Test-Path $destApplicationPlugins) {
        Remove-Item $destApplicationPlugins -Recurse -Force
    }
    Copy-Item $bundlePath -Destination $destApplicationPlugins -Recurse -Force
    Write-Host "Bundle da duoc cai dat vao Autodesk ApplicationPlugins tu dong nap." -ForegroundColor Yellow
} else {
    Write-Host "3. Bo qua copy ApplicationPlugins (Mac dinh KHONG copy. Dung 'build.ps1 -Install' neu muon cai dat)." -ForegroundColor Gray
}

Write-Host ""
Write-Host "DONE!" -ForegroundColor Green
Write-Host "1. Thu muc chia se dong nghiep: $distPath" -ForegroundColor Cyan
Write-Host "   (Chi can copy thu muc Dist\LHBBlockScheduler, keo tha LHB.lsp vao AutoCAD)"
if ($Install) {
    Write-Host "2. Bundle da duoc cai dat vao Autodesk ApplicationPlugins." -ForegroundColor Yellow
} else {
    Write-Host "2. Bundle duoc tao tai: $bundlePath (dung -Install de copy vao ApplicationPlugins)." -ForegroundColor Yellow
}
