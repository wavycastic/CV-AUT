param(
    [string]$BucketName = "simplimixi-storage",
    [string]$PagesProjectName = "simplimixi",
    [string]$R2PublicBaseUrl,
    [string]$Version = "0.6.2"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$setupFileName = "SimpliMixi-v$Version-Setup.exe"
$setupPath = Join-Path $repoRoot "publish\$setupFileName"
$pagesDir = Join-Path $repoRoot "pages"
$updatePath = Join-Path $pagesDir "update.json"

if (-not $R2PublicBaseUrl)
{
    throw "R2PublicBaseUrl is required, for example: -R2PublicBaseUrl https://downloads.simplimixi.com"
}

if (-not (Test-Path $setupPath))
{
    throw "Setup file not found: $setupPath"
}

if (-not $env:CLOUDFLARE_API_TOKEN)
{
    throw "CLOUDFLARE_API_TOKEN is not set. Create a Cloudflare API token and set it before deploying."
}

$downloadUrl = "$($R2PublicBaseUrl.TrimEnd('/'))/$setupFileName"
$manifest = [ordered]@{
    version = $Version
    url = $downloadUrl
    force_update = $true
    min_supported_version = "0.6.0"
    notes = "- Sửa lỗi nhận nhầm màn hình kết quả trận thành popup mất kết nối, khiến Clash of Clans bị khởi động lại sau battle.`n- Cải thiện luồng chờ kết thúc trận bằng cách ưu tiên xác nhận result screen trước khi chạy fallback reload_dialog_shape."
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $updatePath -Encoding UTF8

Get-ChildItem -Path $pagesDir -Filter "*.exe" -File | Remove-Item -Force

Write-Host "Uploading $setupFileName to R2 bucket $BucketName..."
npx wrangler r2 object put "$BucketName/$setupFileName" --file "$setupPath" --remote --content-type "application/vnd.microsoft.portable-executable"

Write-Host "Deploying update.json to Cloudflare Pages project $PagesProjectName..."
npx wrangler pages deploy "$pagesDir" --project-name "$PagesProjectName" --branch main --commit-dirty=true

Write-Host "Done. Manifest URL should be: https://$PagesProjectName.pages.dev/update.json"
Write-Host "Download URL: $downloadUrl"
