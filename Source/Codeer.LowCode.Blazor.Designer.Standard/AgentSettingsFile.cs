using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// エージェントの許可とフックのファイル (.claude/settings.local.json。GitHub Copilot も同じファイルのフックを読む) を、
    /// exe パスを焼き込んだサンプルから生成・更新する。デザインワークスペース (ai-workspace) とホストのルート
    /// (developer-workspace) で同じ扱い:
    ///   無ければ: サンプルをそのまま書く。
    ///   あれば:   hooks 節と _README (ファイルの説明) をサンプル (= この exe を指すフック) で置き換え、permissions.allow に
    ///             サンプルの CLI 許可のうち足りないものを追加する。それ以外 (ユーザーが足した許可・その他のキー) はそのまま保持する。
    ///             JSON として読めないファイルは壊さないよう触らない。
    /// 既存を触らないままにすると、exe を移した・別の exe で展開し直した・旧フックのままだった、のどれでも自動更新が黙って止まる。
    /// </summary>
    static class AgentSettingsFile
    {
        /// <summary>
        /// path のファイルを sampleJson (exe パス焼き込み済み) で生成・更新する。
        /// 結果は created (新規生成・更新) / preserved (変更なし・未更新) に rel (表示用の相対パス) で入れる。
        /// </summary>
        public static void CreateOrUpdate(string path, string sampleJson, List<string> created, List<string> preserved, string rel)
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, sampleJson, new UTF8Encoding(false));
                created.Add(rel);
                return;
            }

            JsonObject existing;
            JsonObject sample;
            try
            {
                existing = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8), documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                }) as JsonObject ?? throw new JsonException("root is not an object");
                sample = JsonNode.Parse(sampleJson) as JsonObject ?? throw new JsonException("sample root is not an object");
            }
            catch (JsonException)
            {
                // 読めないものは上書きしない (ユーザーが手で直す)
                preserved.Add(rel + " (JSON として読めないため未更新)");
                return;
            }

            var changed = false;

            // hooks と _README: フレームワーク所有。サンプルのものに置き換える (exe パス・スクリプト名・説明の更新)
            foreach (var key in new[] { "hooks", "_README" })
            {
                var sampleValue = sample[key];
                if (sampleValue != null && (existing[key]?.ToJsonString() ?? "") != sampleValue.ToJsonString())
                {
                    existing[key] = sampleValue.DeepClone();
                    changed = true;
                }
            }

            // permissions.allow: サンプル由来 (この exe の CLI 許可) で足りないものを追加。既存の項目は消さない
            if (sample["permissions"]?["allow"] is JsonArray sampleAllow)
            {
                var permissions = existing["permissions"] as JsonObject;
                if (permissions == null)
                {
                    permissions = new JsonObject();
                    existing["permissions"] = permissions;
                }
                var allow = permissions["allow"] as JsonArray;
                if (allow == null)
                {
                    allow = new JsonArray();
                    permissions["allow"] = allow;
                }
                var present = allow.Select(x => x?.GetValue<string>()).Where(x => x != null).ToHashSet(StringComparer.Ordinal);
                foreach (var item in sampleAllow)
                {
                    var value = item?.GetValue<string>();
                    if (value == null || !present.Add(value)) continue;
                    allow.Add(value);
                    changed = true;
                }
            }

            if (!changed)
            {
                preserved.Add(rel);
                return;
            }
            File.WriteAllText(path, existing.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }), new UTF8Encoding(false));
            created.Add(rel + " (hooks / CLI 許可を更新)");
        }
    }
}
