# デザインを編集できないライセンス (サーバー / クライアント、ライセンス無しでトライアルも無効) のとき、
# デザインプロジェクトのフォルダへのファイル編集を止める PreToolUse フック。
# Claude Code (.claude/settings.local.json)・GitHub Copilot (同じファイルを読む)・Codex (.codex/hooks.json) が呼ぶ。
# 判定は .claude/license-status.ps1 (デザイナ exe の license-status。編集できる結果は 10 分キャッシュ)。
# デザインプロジェクトの外 (docs/ / ddl/ / Project.md 等) は止めない。
#
# 編集先は tool_input の file_path / path / filePath、または Codex の apply_patch のパッチ本文 (tool_input.command の
# "*** Add File:" / "*** Update File:" / "*** Delete File:" / "*** Move to:" 行) から取る。相対パスは入力の cwd から解決する。
#
# hook から:  powershell -NoProfile -ExecutionPolicy Bypass -File ".claude/license-guard.ps1" "<デザイナexeのパス>" "<デザインプロジェクトのフォルダ>"
# 止めるときは理由を標準エラーに出して exit 2 (どのエージェントでもツール呼び出しの拒否になる)。それ以外は何も出さず exit 0。

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Project = 'design'
)

$ErrorActionPreference = 'SilentlyContinue'

# 入力は UTF-8 (既定の OEM コードページで読むと日本語のパスが化けて判定が素通りする)
[Console]::InputEncoding = New-Object System.Text.UTF8Encoding $false
$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
try { $hook = $raw | ConvertFrom-Json } catch { exit 0 }

$paths = @()
$toolInput = $hook.tool_input
if ($null -eq $toolInput) { $toolInput = $hook.toolArgs }
if ($null -ne $toolInput) {
    foreach ($name in @('file_path', 'path', 'filePath')) {
        $value = $toolInput.$name
        if ($value -is [string] -and $value -ne '') { $paths += $value }
    }
    $patch = $toolInput.command
    if ($patch -is [string] -and $patch.Contains('*** ')) {
        foreach ($m in [regex]::Matches($patch, '(?m)^\*\*\* (?:Add File|Update File|Delete File|Move to): (.+?)\s*$')) {
            $paths += $m.Groups[1].Value
        }
    }
}
if ($paths.Count -eq 0) { exit 0 }

# 相対パスの基準: 入力の cwd (無ければこのフックの作業フォルダ)
$base = (Get-Location).Path
if ($hook.cwd -is [string] -and $hook.cwd -ne '') { $base = $hook.cwd }

# デザインプロジェクトの中か (大文字小文字を区別しない)
$projectDir = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Project)).TrimEnd('\') + '\'
$inProject = $false
foreach ($p in $paths) {
    try { $full = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($base, $p)) } catch { continue }
    if ($full.StartsWith($projectDir, [System.StringComparison]::OrdinalIgnoreCase)) { $inProject = $true; break }
}
if (-not $inProject) { exit 0 }

$status = & powershell -NoProfile -ExecutionPolicy Bypass -File '.claude/license-status.ps1' $Exe
$status = [string]$status
if (-not $status.StartsWith('blocked|')) { exit 0 }

$reason = $status.Substring(8) + ' デザインファイルは編集せず、ライセンス登録の手順 (CLAUDE.md の「ライセンス」) を案内してください。'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::Error.WriteLine($reason)
exit 2
