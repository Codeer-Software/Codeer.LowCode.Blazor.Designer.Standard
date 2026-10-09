# ホストソリューションのルートで Claude Code を起動したときに、配下のデザインプロジェクト
# (DesignProjects/<名>/<デザインのフォルダ>/) への Write / Edit / MultiEdit を、そのワークスペースの
# .claude/license-guard.ps1 (ライセンスで編集を止める PreToolUse フック) に委ねるスクリプト。
# 判定ロジックはそちらが持つ。デザインプロジェクトのフォルダ名は、ワークスペース直下で app.clprj を持つフォルダから求める。
#
# hook から:  powershell -NoProfile -ExecutionPolicy Bypass -File "ClaudeCodeForDeveloper/_hooks/license-guard-root.ps1" "<デザイナexeのパス>"
# 止めるときは委ねた先の deny (JSON) をそのまま返す。それ以外は何も出さず exit 0。デザイナの developer-workspace が生成する (手で編集しない)。

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$DesignProjectsDir = 'DesignProjects'
)

$ErrorActionPreference = 'SilentlyContinue'

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
try { $hook = $raw | ConvertFrom-Json } catch { exit 0 }
$filePath = $hook.tool_input.file_path
if ([string]::IsNullOrEmpty($filePath)) { exit 0 }

if (-not (Test-Path -LiteralPath $Exe)) { exit 0 }
if (-not (Test-Path -LiteralPath $DesignProjectsDir)) { exit 0 }

$full = [System.IO.Path]::GetFullPath($filePath)
foreach ($ws in Get-ChildItem -LiteralPath $DesignProjectsDir -Directory) {
    $wsDir = $ws.FullName.TrimEnd('\') + '\'
    if (-not $full.StartsWith($wsDir, [System.StringComparison]::OrdinalIgnoreCase)) { continue }

    $script = Join-Path $ws.FullName '.claude\license-guard.ps1'
    if (-not (Test-Path -LiteralPath $script)) { exit 0 }

    $project = $null
    foreach ($d in Get-ChildItem -LiteralPath $ws.FullName -Directory) {
        if (Test-Path -LiteralPath (Join-Path $d.FullName 'app.clprj')) { $project = $d.Name; break }
    }
    if ($null -eq $project) { exit 0 }

    Push-Location -LiteralPath $ws.FullName
    try {
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $raw | & powershell -NoProfile -ExecutionPolicy Bypass -File $script $Exe $project
    } finally {
        Pop-Location
    }
    exit 0
}
exit 0
