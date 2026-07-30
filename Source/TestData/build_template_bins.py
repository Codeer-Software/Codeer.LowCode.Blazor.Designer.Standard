# テンプレートマスタ (このフォルダの <名前>/App) から、パッケージに埋め込む配布 bin (zip) を再生成する。
#   python build_template_bins.py
# マスタを編集したら必ずこれを実行して Templates/Resources/*.bin を更新すること。
# .ide (デザイナのIDE状態) はテンプレートに含めない。
# 反映はパッケージの repack (キャッシュ削除 → Rebuild → pack) が別途必要。
import os
import sys
import zipfile

sys.stdout.reconfigure(encoding='utf-8')

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(HERE, '..', 'Codeer.LowCode.Blazor.Designer.Standard', 'Templates', 'Resources')

# マスタフォルダ名 → 配布 bin 名
PAIRS = [
    ('EmptyTemplate', 'EmptyTemplate.bin'),
    ('EmptyAuthTemplate', 'EmptyAuthTemplate.bin'),
    ('GettingStartedTemplate', 'GettingStartedTemplate.bin'),
    ('InventoryManagementTemplate', 'InventoryManagementTemplate.bin'),
    ('PatternShowcase', 'PatternShowcaseTemplate.bin'),
    ('PatternShowcaseAuth', 'PatternShowcaseAuthTemplate.bin'),
    ('ProjectManagementTemplate', 'ProjectManagementTemplate.bin'),
    ('SFATemplate', 'SFATemplate.bin'),
]

for name, bin_name in PAIRS:
    master = os.path.join(HERE, name, 'App')
    if not os.path.isdir(master):
        print(f'!! master not found: {master}')
        continue
    out = os.path.abspath(os.path.join(RES, bin_name))
    count = 0
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        for dp, ds, fs in os.walk(master):
            ds[:] = [d for d in ds if d not in ('.ide', 'bin', 'obj')]
            for f in sorted(fs):
                p = os.path.join(dp, f)
                rel = os.path.relpath(p, master).replace(os.sep, '/')
                z.write(p, rel)
                count += 1
    print(f'{name}: {count} files -> {os.path.basename(out)} ({os.path.getsize(out)} bytes)')
