# temporary/ 配下の AI 用生成物一式 (_field_catalog.md / _script_catalog.md / _defaults/ / _specs/ / _samples/) を
# 「必要なときだけ」デザイナ exe の ai-refresh で再生成するフック用スクリプト。
#
# 仕組み:
#   デザイナ exe は独自フィールド/スクリプトオブジェクトのライブラリを参照してビルドされるため、追加/削除は
#   「再ビルド」= exe とその配置フォルダの DLL のタイムスタンプ更新として現れる。
#   その最終更新時刻の最大値をシグネチャにして temporary/_ai_refresh.stamp に記録し、
#   前回と同じなら再生成をスキップ、違えば ai-refresh で全取得する (全置換 = 削除されたものも消える)。
#   生成に成功したときだけスタンプを更新する (失敗時に古い exe を「最新」と誤記録しないため)。
#
# ai-refresh はデザイナ 1.3.15 以降のサブコマンド。古い exe に未知の verb を渡すと GUI が起動してしまうため、
# exe と同フォルダの Codeer.LowCode.Blazor.Designer.dll のバージョンで対応可否を判定し、未満なら何もしない
# (このワークスペースはデザイナ 1.3.15 以降が前提。古い場合はデザイナを更新して claude-workspace を再実行する)。
#
# hook から:  powershell -NoProfile -ExecutionPolicy Bypass -File ".claude/refresh-ai-workspace.ps1" "<デザイナexeのパス>" "<デザインプロジェクトのフォルダ>"
# 常に exit 0 (セッション/プロンプトをブロックしない)。

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Project = 'Design'
)

$ErrorActionPreference = 'SilentlyContinue'

$outDir = 'temporary'
$stamp  = 'temporary/_ai_refresh.stamp'

# exe が未配置なら何もしない (パス未確定でもセッションは続行させる)。
if (-not (Test-Path -LiteralPath $Exe)) { exit 0 }

# ai-refresh 対応判定 (Designer 1.3.15 以降)。未満なら GUI が起動してしまうので叩かない。
$dir = [System.IO.Path]::GetDirectoryName($Exe)
$designerDll = [System.IO.Path]::Combine($dir, 'Codeer.LowCode.Blazor.Designer.dll')
if (-not (Test-Path -LiteralPath $designerDll)) { exit 0 }
try {
    $v = [version](Get-Item -LiteralPath $designerDll).VersionInfo.FileVersion
    if ($v -lt [version]'1.3.15.0') { exit 0 }
} catch { exit 0 }

# ビルド検知シグネチャ: exe と同フォルダの *.dll の LastWriteTimeUtc の最大値。
# 注: Split-Path は -LiteralPath と -Parent が別パラメータセットで両立しない (AmbiguousParameterSet) ため使わない。
$files = @(Get-Item -LiteralPath $Exe)
$files += Get-ChildItem -LiteralPath $dir -Filter *.dll -ErrorAction SilentlyContinue
# Ticks は Int64。Measure-Object -Maximum は double 化して精度を落とすので、Int64 のまま最大値を取る。
[long]$sig = 0
foreach ($f in $files) {
    [long]$t = $f.LastWriteTimeUtc.Ticks
    if ($t -gt $sig) { $sig = $t }
}

# 既存生成物があり、シグネチャが前回と同じなら再生成不要。
$prev = if (Test-Path -LiteralPath $stamp) { (Get-Content -LiteralPath $stamp -Raw).Trim() } else { '' }
if ((Test-Path -LiteralPath "$outDir/_field_catalog.md") -and ($prev -eq "$sig")) { exit 0 }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
& $Exe ai-refresh $Project --out-dir $outDir
if ($LASTEXITCODE -eq 0) {
    Set-Content -LiteralPath $stamp -Value "$sig" -NoNewline
}
exit 0
