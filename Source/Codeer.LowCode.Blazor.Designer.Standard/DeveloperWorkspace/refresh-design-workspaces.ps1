# ホストソリューションのルートで Claude Code を起動したときに、配下の全デザインプロジェクトの
# Claude Code ワークスペース (DesignProjects/<名>/) を最新化するフック用スクリプト。
#
# 仕組み:
#   各ワークスペースにはデザイナの claude-workspace が置いた .claude/refresh-ai-workspace.ps1 があり、
#   デザイナ exe の更新を検知して ClaudeCodeForDesigner/ を作り直す (判定ロジックはそちらが持つ)。
#   このスクリプトは DesignProjects/*/ を巡回し、そのスクリプトを各ワークスペースをカレントにして呼ぶだけ。
#   デザインプロジェクトのフォルダ名は、ワークスペース直下で app.clprj を持つフォルダから求める (既定 design)。
#
# hook から:  powershell -NoProfile -ExecutionPolicy Bypass -File "ClaudeCodeForDeveloper/_hooks/refresh-design-workspaces.ps1" "<デザイナexeのパス>"
# 常に exit 0 (セッション/プロンプトをブロックしない)。デザイナの developer-workspace が生成する (手で編集しない)。

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$DesignProjectsDir = 'DesignProjects'
)

$ErrorActionPreference = 'SilentlyContinue'

if (-not (Test-Path -LiteralPath $Exe)) { exit 0 }
if (-not (Test-Path -LiteralPath $DesignProjectsDir)) { exit 0 }

foreach ($ws in Get-ChildItem -LiteralPath $DesignProjectsDir -Directory) {
    $script = Join-Path $ws.FullName '.claude\refresh-ai-workspace.ps1'
    if (-not (Test-Path -LiteralPath $script)) { continue }

    $project = $null
    foreach ($d in Get-ChildItem -LiteralPath $ws.FullName -Directory) {
        if (Test-Path -LiteralPath (Join-Path $d.FullName 'app.clprj')) { $project = $d.Name; break }
    }
    if ($null -eq $project) { continue }

    Push-Location -LiteralPath $ws.FullName
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $script $Exe $project | Out-Null
    } finally {
        Pop-Location
    }
}
exit 0
