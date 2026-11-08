# このマシンのライセンスでデザインを編集できるかを、デザイナ exe の license-status で確かめるフック用の共通スクリプト。
# refresh-ai-workspace.ps1 (SessionStart / UserPromptSubmit) と license-guard.ps1 (PreToolUse) が呼ぶ。
#
# 出力 (1 行):
#   editable          編集できる (開発・メンテナンス・組込みのライセンス、またはトライアル中)
#   blocked|<理由>    編集できない (サーバー / クライアントのライセンス、ライセンス無しでトライアルも無効)
#   unknown           判定できない (exe が無い・license-status 未対応の版 1.3.42 以前・結果が読めない)。編集できる扱いにする
#
# 編集できる結果だけ .claude/license_status.json に 10 分キャッシュする (プロンプトごとに exe を起動しないため)。
# 編集できない結果はキャッシュしない (ライセンスを登録した直後に古い答えを返さないため)。
#
# 使い方: powershell -NoProfile -ExecutionPolicy Bypass -File ".claude/license-status.ps1" "<デザイナexeのパス>"

param(
    [Parameter(Mandatory = $true)][string]$Exe
)

$ErrorActionPreference = 'SilentlyContinue'

$cache = '.claude/license_status.json'
$cacheMinutes = 10

if (-not (Test-Path -LiteralPath $Exe)) { Write-Output 'unknown'; exit 0 }

# license-status はデザイナ 1.3.43 以降。それより前の exe に渡すと GUI が起動してしまうので判定しない
$dir = [System.IO.Path]::GetDirectoryName($Exe)
$designerDll = [System.IO.Path]::Combine($dir, 'Codeer.LowCode.Blazor.Designer.dll')
if (-not (Test-Path -LiteralPath $designerDll)) { Write-Output 'unknown'; exit 0 }
try {
    $v = [version](Get-Item -LiteralPath $designerDll).VersionInfo.FileVersion
    if ($v -lt [version]'1.3.43.0') { Write-Output 'unknown'; exit 0 }
} catch { Write-Output 'unknown'; exit 0 }

# キャッシュ (編集できる結果だけ)
if (Test-Path -LiteralPath $cache) {
    try {
        $cached = Get-Content -LiteralPath $cache -Raw | ConvertFrom-Json
        $checkedAt = [datetime]::Parse($cached.checkedAt)
        if ($cached.canEdit -eq $true -and ((Get-Date) - $checkedAt).TotalMinutes -lt $cacheMinutes) { Write-Output 'editable'; exit 0 }
    } catch { }
}

$out = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'clb_license_status_' + [System.Diagnostics.Process]::GetCurrentProcess().Id + '.json')
Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue
# デザイナ exe は GUI サブシステムのため & では待てない。Start-Process -Wait で完了まで待つ
Start-Process -FilePath $Exe -ArgumentList @('license-status', '--out', ('"' + $out + '"')) -Wait -WindowStyle Hidden
if (-not (Test-Path -LiteralPath $out)) { Write-Output 'unknown'; exit 0 }

try {
    $status = Get-Content -LiteralPath $out -Raw -Encoding UTF8 | ConvertFrom-Json
} catch { Write-Output 'unknown'; exit 0 }
Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue

if ($status.canEdit -eq $true) {
    $record = @{ canEdit = $true; licenseType = $status.licenseType; trial = $status.trial; checkedAt = (Get-Date).ToString('o') }
    try { $record | ConvertTo-Json -Compress | Set-Content -LiteralPath $cache -Encoding UTF8 } catch { }
    Write-Output 'editable'
    exit 0
}

$message = $status.message
if ([string]::IsNullOrEmpty($message)) { $message = 'このマシンのライセンスではデザインを編集できません。' }
Write-Output ('blocked|' + ($message -replace "[\r\n]+", ' '))
exit 0
