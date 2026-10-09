# デザインを編集できないライセンス (サーバー / クライアント、ライセンス無しでトライアルも無効) のとき、
# デザインプロジェクトのフォルダへの Write / Edit / MultiEdit を止める PreToolUse フック。
# 判定は .claude/license-status.ps1 (デザイナ exe の license-status。編集できる結果は 10 分キャッシュ)。
# デザインプロジェクトの外 (docs/ / ddl/ / Project.md 等) は止めない。
#
# hook から:  powershell -NoProfile -ExecutionPolicy Bypass -File ".claude/license-guard.ps1" "<デザイナexeのパス>" "<デザインプロジェクトのフォルダ>"
# 止めるときは PreToolUse の deny を JSON で返す。それ以外は何も出さず exit 0。

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Project = 'design'
)

$ErrorActionPreference = 'SilentlyContinue'

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
try { $hook = $raw | ConvertFrom-Json } catch { exit 0 }

$filePath = $hook.tool_input.file_path
if ([string]::IsNullOrEmpty($filePath)) { exit 0 }

# デザインプロジェクトの中か (大文字小文字を区別しない)
$projectDir = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Project)).TrimEnd('\') + '\'
$full = [System.IO.Path]::GetFullPath($filePath)
if (-not $full.StartsWith($projectDir, [System.StringComparison]::OrdinalIgnoreCase)) { exit 0 }

$status = & powershell -NoProfile -ExecutionPolicy Bypass -File '.claude/license-status.ps1' $Exe
$status = [string]$status
if (-not $status.StartsWith('blocked|')) { exit 0 }

$reason = $status.Substring(8) + ' デザインファイルは編集せず、ライセンス登録の手順 (CLAUDE.md の「ライセンス」) を案内してください。'
$decision = @{
    hookSpecificOutput = @{
        hookEventName = 'PreToolUse'
        permissionDecision = 'deny'
        permissionDecisionReason = $reason
    }
}
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Write-Output ($decision | ConvertTo-Json -Compress -Depth 3)
exit 0
