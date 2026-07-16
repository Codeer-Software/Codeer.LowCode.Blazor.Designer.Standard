using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Microsoft.Extensions.AI;
using System.IO;
using System.Text;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // 一覧(List)レイアウト編集機能。旧 ListLayoutChat のロジックをそのまま機能ユニットへ移したもの。
    //
    // 一覧は「行×列の ListElement テーブル」というフラットな構造なので、検索(And/Or ネスト)のような 2 段階は不要。
    // 詳細レイアウト機能と同じ堅牢さ(自己修正ループ・未知プロパティ検証・存在しないフィールド参照の是正)を持たせた単一プロンプト。
    //
    // 一度諦めて削除(4e3abe963)した旧 ListLayoutChat を復活させつつ、詳細/検索Chatで得た知見で以下を改善:
    //  - 空応答でテーブルを消さない(旧実装は response.Elements を無条件適用 → 空を返されると一覧の列が全消えするバグ)。
    //  - Explanation を持たせ、できない依頼(列に出せないフィールドの新規追加・プロパティ編集等)は握りつぶさず丁寧に断る。
    //  - Id / LogicalDelete / OptimisticLocking は明示要求が無ければ列に出さない(Id 列は空セル描画になる = CLB の慣行)。決定的に除去する。
    //  - 存在しないフィールドを参照する列を検出して再生成へ回す。
    //  - AzureOpenAIClient 直接生成をやめ createChatClient() に統一。
    internal class ListLayoutFunction : IAiFunction
    {
        readonly IModuleListLayoutEditor _editor;
        readonly IChatClient _chatClient;
        readonly List<ChatMessage> _messages = new();

        // 直近適用で除去したシステム列などの通知(結果メッセージ用)。
        string _resultNote = string.Empty;

        public string Id => FunctionCatalog.LayoutList;
        public string DisplayName => FunctionCatalog.Entries[FunctionCatalog.LayoutList].DisplayName;
        public string RouterDescription => FunctionCatalog.Entries[FunctionCatalog.LayoutList].RouterDescription;

        public ListLayoutFunction(Func<IChatClient> createChatClient, IModuleListLayoutEditor editor)
        {
            _editor = editor;
            _chatClient = createChatClient();
        }

        public void Clear() => _messages.Clear();

        public async Task<FunctionResult> ExecuteAsync(string instruction)
        {
            _resultNote = string.Empty;

            var list = (_editor.GetModuleDesign().ListLayouts.GetValueOrDefault(_editor.GetLayoutName()) ?? new());
            // ベースライン: この編集の前に既にあるデザインチェックエラー。結果評価はここからの差分(新規エラー)だけを見る。
            var baseline = SafeCheckModule();
            var fields = _editor.GetModuleDesign().Fields;
            var knownFieldNames = new HashSet<string>(fields.Select(f => f.Name), StringComparer.Ordinal);

            // システム列でも、ユーザーが名指し(Name か DisplayName)で「出して/並べて」と求めたものは配置を許す。
            var explicitlyRequestedSystemFields = fields
                .Where(f => ListSystemExcludeFieldNames.Contains(f.Name))
                .Where(f => instruction.Contains(f.Name, StringComparison.OrdinalIgnoreCase)
                    || (f is IDisplayName d && !string.IsNullOrEmpty(d.DisplayName) && instruction.Contains(d.DisplayName)))
                .Select(f => f.Name)
                .ToHashSet(StringComparer.Ordinal);

            // 行番号(通し番号)列は「ツールが」既存の ListNumberField を配置する(作成はしない=field.createの担当)。
            var wantListNumber = ListNumberRequested(instruction, fields);
            var placeListNumberLast = wantListNumber && (instruction.Contains("末尾") || instruction.Contains("最後")
                || instruction.Contains("右端") || instruction.Contains("一番後ろ") || instruction.Contains("後ろ"));
            var listNumberName = FindListNumberName(fields);
            if (wantListNumber && listNumberName != null)
                knownFieldNames.Add(listNumberName);

            if (_messages.Count == 0)
            {
                _messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
                if (!string.IsNullOrEmpty(LayoutReference))
                    _messages.Add(new ChatMessage(ChatRole.System, 
                        "## レイアウト仕様（クラス定義・プロパティ・推奨ルール・IsViewOnly）\n\n" + LayoutReference));
            }

            var fieldInfo = fields.Select(f => $"  {f.Name} ({f.GetType().Name})").ToList();
            _messages.Add(new ChatMessage(ChatRole.User, 
                $"現在のモジュールに定義されているフィールド一覧:\n{string.Join("\n", fieldInfo)}\n\n"
                + $"現在の一覧レイアウト(列定義):\n{JsonConverterEx.SerializeObject(list.Elements)}\n\n指示: {instruction}"));

            const int maxAttempts = 3;
            var lastErrors = new List<DesignCheckInfo>();

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                AIResponse response;
                string resultText;
                try
                {
                    var result = await _chatClient.GetResponseAsync(_messages,
                        new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
                    resultText = result.Text;
                    response = JsonConverterEx.DeserializeObject<AIResponse>(resultText)!;
                }
                catch (Exception ex)
                {
                    if (_messages.Count > 0 && _messages[^1].Role == ChatRole.User)
                        _messages.RemoveAt(_messages.Count - 1);
                    return FunctionResult.Error($"エラーリトライしてください\r\n{ex.Message}");
                }

                _messages.Add(new ChatMessage(ChatRole.Assistant, resultText));

                // 未知プロパティ検証(値が黙って捨てられ、反映されないのに成功扱いになるのを防ぐ)。
                var unmappedError = AiJsonValidation.GetUnmappedMemberError<AIResponse>(resultText);
                if (unmappedError != null)
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            "生成されたJSONに、定義に存在しないプロパティが含まれています。" +
                            "存在しないプロパティに書いた値は無視され、設定に反映されません。" +
                            "次のエラーを解消し、正しいプロパティ名・構造で全体を再度出力してください。\n" + unmappedError));
                        continue;
                    }
                    return FunctionResult.NothingToDo($"生成されたJSONに定義外のプロパティが含まれており解消できなかったため、変更は適用していません。\r\n{unmappedError}");
                }

                // 列変更を伴わない応答(できない依頼の断り等)。空の Elements を適用すると一覧の列が全消えするので、
                // 現在のレイアウトを保持し、説明だけ返す(旧実装のバグ修正)。
                if (!HasAnyColumn(response.Elements))
                {
                    // 行番号の配置“だけ”を頼まれたとき(AIがレイアウトを返さない)は、既存の行番号列を現在の列に配置する。
                    if (wantListNumber)
                    {
                        if (listNumberName != null)
                        {
                            PlaceListNumberColumn(list.Elements, listNumberName, placeListNumberLast);
                            _editor.Update();
                            return FunctionResult.Done("行番号(ListNo)列を配置しました。");
                        }
                        return FunctionResult.NothingToDo("行番号(ListNo)フィールドがありません。先に『フィールド作成』で行番号フィールドを作成してください。");
                    }
                    return FunctionResult.NothingToDo(string.IsNullOrWhiteSpace(response.Explanation)
                        ? "一覧レイアウトは変更していません。"
                        : response.Explanation);
                }

                // 単一行(通常の一覧)で空セル(FieldName も DetailLayoutName も ListElementComponent も無い)があるのは不具合(空白列)。再生成へ。
                // ※二段以上(複数行)では、列数を揃えるための空セル(スペーサー)は正当なので許可する(矩形かどうかは後段の GetGridConsistencyError で担保)。
                if (response.Elements.Count == 1 && HasBlankCell(response.Elements))
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            "列(ListElement)の中に、FieldName も DetailLayoutName も ListElementComponent も無い空のセルがあります。" +
                            "空のセルは一覧に空白列として描画され不具合になります。不要な列は削除し、各列には表示するフィールド(FieldName)を設定して全体を再度出力してください。"));
                        continue;
                    }
                    return FunctionResult.NothingToDo("空の列が生成され解消できなかったため、変更は適用していません。もう一度指示してください。");
                }

                // 存在しないフィールドを参照する列は不具合(空セル描画)。再生成へ回す。
                var unknownRefs = CollectFieldRefs(response.Elements).Where(n => !knownFieldNames.Contains(n)).Distinct().ToList();
                if (unknownRefs.Count > 0)
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            $"一覧に、存在しないフィールドを参照する列があります: {string.Join(", ", unknownRefs)}。" +
                            "このチャットはフィールドを追加できません。FieldName には渡されたフィールド一覧にある Name だけを指定して全体を再度出力してください。"));
                        continue;
                    }
                    return FunctionResult.NothingToDo($"存在しないフィールド({string.Join(", ", unknownRefs)})を参照する列が生成されたため、変更は適用していません。" +
                        "そのフィールドは『全体設定』で追加してから列に出してください。");
                }

                // システム列(Id/LogicalDelete/OptimisticLocking)は、明示要求が無い限り一覧に出さない。
                var unwantedSystemFields = CollectFieldRefs(response.Elements)
                    .Where(n => ListSystemExcludeFieldNames.Contains(n) && !explicitlyRequestedSystemFields.Contains(n))
                    .Distinct().ToList();
                if (unwantedSystemFields.Count > 0)
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            $"一覧に出さないシステム列が含まれています: {string.Join(", ", unwantedSystemFields)}。" +
                            "Id / LogicalDelete / OptimisticLocking は、ユーザーが明示的に求めていない限り一覧の列に出しません(Id 列は空セルになります)。" +
                            "これらの列を取り除いて全体を再度出力してください。"));
                        continue;
                    }
                    // 最終手段: AIが直さないので決定的に取り除いて適用する。
                    RemoveColumns(response.Elements, unwantedSystemFields);
                    _resultNote = $"システム列 {string.Join(", ", unwantedSystemFields)} は一覧に出さない決まりのため除外しました。" +
                        $"表示が必要なら『{unwantedSystemFields[0]} も列に出して』のように指示してください。";
                }

                // 行番号列は「縦マージして左端」に置くのが望ましい(ユーザーが毎回指示しなくてもそうなるように)。
                // 既存の行番号フィールドがあるときだけ扱う(作成はしない)。要求が無くても、多段で既存の行番号列があれば自動で縦マージ対象にする。
                var listNoField = fields.OfType<ListNumberFieldDesign>().FirstOrDefault();
                var multiRow = response.Elements.Count > 1;
                var handleListNo = listNoField != null &&
                    (wantListNumber || (multiRow && CollectFieldRefs(response.Elements).Contains(listNoField.Name)));

                // 縦マージのため一旦取り除いてから、矩形化 → 左端(既定)/末尾に縦マージ配置する。
                if (handleListNo)
                    RemoveListNumberPlacements(response.Elements, listNoField!.Name);

                // 二段以上(複数行)は、行ごとの列数が揃わないと一覧テーブルの描画で例外になる。
                // AI に完璧に揃えさせるのは不安定なので、ツールが各行を ColumnSpan で端まで広げて確実に矩形化する(=二段を自動で見た目よく成立させる)。
                if (multiRow)
                    RectangularizeMultiRowGrid(response.Elements);

                // 行番号列は既存フィールドをツールが確定的に配置する(AIには作らせない)。多段なら全段を縦マージして先頭(既定)/末尾に置く。
                if (handleListNo)
                {
                    PlaceListNumberColumn(response.Elements, listNoField!.Name, placeListNumberLast);
                    if (wantListNumber)
                    {
                        var listNumberNote = "行番号(ListNo)列を配置しました。";
                        _resultNote = string.IsNullOrEmpty(_resultNote) ? listNumberNote : _resultNote + "\r\n" + listNumberNote;
                    }
                }

                // 保険: 上記でシステム列除去・矩形化・行番号追加まで済んだ最終形を検証する。矩形化済みなので通常は通る。
                var gridError = GetGridConsistencyError(response.Elements);
                if (gridError != null)
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            "生成された一覧の列構成が不正です。" + gridError +
                            "\n二段(複数行)にするときは各行にフィールドを並べてください(列数が揃わない分はツールが空セルで補います)。列定義全体を再度出力してください。"));
                        continue;
                    }
                    return FunctionResult.NothingToDo("一覧レイアウトの列構成を解消できなかったため、変更は適用していません。もう一度指示してください。\r\n" + gridError);
                }

                list.Elements = response.Elements;
                _editor.Update();

                var errors = DesignCheckDiff.NewErrors(baseline, SafeCheckModule());
                if (errors.Count == 0)
                    return FunctionResult.Done(BuildResultMessage(response));

                lastErrors = errors;
                if (attempt < maxAttempts)
                    _messages.Add(new ChatMessage(ChatRole.User, 
                        "生成された一覧レイアウトに次のデザインチェックエラーがあります。修正して列定義全体を再度出力してください。\n"
                        + FormatErrors(errors)));
            }

            return FunctionResult.Done(BuildResultMessage(null)
                + $"\r\n（注意: デザインチェックエラーが残っています。内容を確認してください）\r\n{FormatErrors(lastErrors)}");
        }

        // 一覧に出さないシステムフィールド(明示要求が無い限り)。Id 列は空セル描画になる = CLB の慣行。
        // CreatedAt/UpdatedAt/Creator/Updater 等の監査列は一覧に出してよいので除外対象に含めない。
        static readonly HashSet<string> ListSystemExcludeFieldNames = new(StringComparer.Ordinal)
        {
            SystemFieldNames.Id, SystemFieldNames.LogicalDelete, SystemFieldNames.OptimisticLocking,
        };

        // 列が1つでもあるか(表示するフィールド or ドリル用 DetailLayout or カスタムコンポーネントを持つセルが1つでもあるか)。
        static bool HasAnyColumn(List<List<ListElement>>? elements)
            => elements != null && elements.Any(row => row.Any(IsMeaningfulCell));

        // FieldName も DetailLayoutName も ListElementComponent も無い空セルがあるか。
        static bool HasBlankCell(List<List<ListElement>>? elements)
            => elements != null && elements.Any(row => row.Any(c => !IsMeaningfulCell(c)));

        static bool IsMeaningfulCell(ListElement c)
            => !string.IsNullOrEmpty(c.FieldName)
                || !string.IsNullOrEmpty(c.DetailLayoutName)
                || !string.IsNullOrEmpty(c.ListElementComponent);

        // 複数行(二段以上)の Elements が矩形(全行で列数が揃う)になっているかを ColumnSpan/RowSpan を考慮して検証する。
        // 揃っていない/セルが重なる/RowSpan が行数超過だと、一覧テーブルの描画で例外になる(旧実装はこれを未チェックで適用していた)。
        // 問題があれば直し方を含むエラーメッセージ、無ければ null を返す。internal はユニットテストから直接検証するため。
        internal static string? GetGridConsistencyError(List<List<ListElement>>? elements)
        {
            if (elements == null || elements.Count <= 1) return null; // 単一行(通常の一覧)は常に矩形

            var rows = elements.Count;
            var occupied = new List<HashSet<int>>();
            for (var i = 0; i < rows; i++) occupied.Add(new HashSet<int>());
            var maxCol = 0;

            for (var r = 0; r < rows; r++)
            {
                var c = 0;
                foreach (var cell in elements[r])
                {
                    var cs = Math.Max(1, cell.ColumnSpan);
                    var rs = Math.Max(1, cell.RowSpan);
                    if (cs > 100 || rs > 100)
                        return $"ColumnSpan / RowSpan が大きすぎます(行{r}付近)。1〜数程度の妥当な値にしてください。";
                    if (r + rs > rows)
                        return $"RowSpan がヘッダー/行の段数({rows})を超えています(行{r}付近)。RowSpan は段数以内にしてください。";

                    while (occupied[r].Contains(c)) c++;
                    for (var rr = r; rr < r + rs; rr++)
                        for (var cc = c; cc < c + cs; cc++)
                            if (!occupied[rr].Add(cc))
                                return $"セルが重なっています(行{rr}・列{cc}付近)。ColumnSpan / RowSpan を見直してください。";
                    c += cs;
                    maxCol = Math.Max(maxCol, c);
                }
            }

            // 全行が [0, maxCol) を隙間なく埋めているか(=行ごとの列数が揃っているか)。
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < maxCol; c++)
                    if (!occupied[r].Contains(c))
                        return $"行ごとの列数が揃っていません(行{r}に空き列があります)。" +
                            "二段以上にするときは、各段(行)の列を ColumnSpan で広げるなどして、全ての行が同じ列数(全体で矩形)になるようにしてください。";

            return null;
        }

        static List<string> CollectFieldRefs(List<List<ListElement>>? elements)
        {
            var names = new List<string>();
            if (elements == null) return names;
            foreach (var row in elements)
                foreach (var cell in row)
                    if (!string.IsNullOrEmpty(cell.FieldName)) names.Add(cell.FieldName);
            return names;
        }

        static void RemoveColumns(List<List<ListElement>> elements, List<string> fieldNames)
        {
            var set = new HashSet<string>(fieldNames, StringComparer.Ordinal);
            foreach (var row in elements)
                row.RemoveAll(c => !string.IsNullOrEmpty(c.FieldName) && set.Contains(c.FieldName));
        }

        // 行番号(通し番号)列の要求語。詳細レイアウトの「戻る/タイトル/サブミット」と同じく、ツールが列を追加する対象。
        static readonly string[] ListNumberKeywords =
        {
            "行番号", "行番", "連番", "通し番号", "行No", "行ナンバー", "レコード番号", "ListNumber", "ListNo",
        };

        // 「No」を単独語として検出する(Note / Now / number 等の英単語の一部は拾わない)。
        static readonly System.Text.RegularExpressions.Regex NoTokenRegex =
            new(@"(^|[^0-9A-Za-z])No([^0-9A-Za-z]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 行番号列の追加要求か。明確な語(行番号/連番/ListNo 等)は常に true。
        // 曖昧語「No」「番号」も行番号扱いにするが、既存フィールドの名前/表示名で説明できるとき(例: 電話番号/郵便番号/PhoneNumber)は
        // そのフィールド列の話とみなして行番号扱いにしない(誤検出防止)。internal はユニットテストから直接検証するため。
        internal static bool ListNumberRequested(string message, List<FieldDesignBase> fields)
        {
            if (ListNumberKeywords.Any(k => message.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return true;

            var hasBangou = message.Contains("番号");
            var hasNo = NoTokenRegex.IsMatch(message);
            if (!hasBangou && !hasNo) return false;

            // その曖昧語が、メッセージ中に現れる既存フィールドの名前/表示名の一部になっているか(=そのフィールドの指定)。
            bool ExplainedByField(string token) => fields.Any(f =>
            {
                if (!string.IsNullOrEmpty(f.Name) && f.Name.Length > token.Length
                    && f.Name.Contains(token, StringComparison.OrdinalIgnoreCase) && message.Contains(f.Name))
                    return true;
                return f is IDisplayName d && !string.IsNullOrEmpty(d.DisplayName) && d.DisplayName.Length > token.Length
                    && d.DisplayName.Contains(token) && message.Contains(d.DisplayName);
            });

            if (hasBangou && !ExplainedByField("番号")) return true;
            if (hasNo && !ExplainedByField("No")) return true;
            return false;
        }

        // 既存の ListNumberField の Name を返す(無ければ null)。**作成はしない**(作成は field.create の担当)。
        static string? FindListNumberName(List<FieldDesignBase> fields)
            => fields.OfType<ListNumberFieldDesign>().FirstOrDefault()?.Name;

        // 既存の行番号列(name)を配置し直す。既存配置を一旦取り除いてから、既定は左端(先頭列)、指定があれば末尾に置く。
        // 複数行ヘッダーのときは全ヘッダー行を縦にマージ(RowSpan)する。**新規作成はしない**(name は既存フィールド前提)。
        static void PlaceListNumberColumn(List<List<ListElement>> elements, string name, bool placeLast)
        {
            foreach (var row in elements)
                row.RemoveAll(c => c.FieldName == name);

            if (elements.Count == 0)
                elements.Add(new List<ListElement>());

            var col = new ListElement
            {
                FieldName = name,
                Width = 60,
                CanUserSort = false,
                RowSpan = Math.Max(1, elements.Count),
            };
            if (placeLast)
                elements[0].Add(col);
            else
                elements[0].Insert(0, col);
        }

        // 指定名(行番号列)の配置をすべての行から取り除く。
        static void RemoveListNumberPlacements(List<List<ListElement>> elements, string name)
        {
            foreach (var row in elements)
                row.RemoveAll(c => c.FieldName == name);
        }

        // 二段以上(複数行)の Elements を、行ごとの列数が揃う矩形にする(ユーザーが毎回細かく指示しなくても見た目が整うように)。
        // 各行のセルを ColumnSpan で均等に広げて端まで埋める(空セルを作らない=見た目が良い)。
        // データ列の縦マージ(RowSpan)は使わない方針なので RowSpan は 1 に正規化する
        // (行番号列の縦マージだけはこの後ツールが別途 RowSpan を付けて行う)。internal はユニットテストから直接検証するため。
        internal static void RectangularizeMultiRowGrid(List<List<ListElement>> elements)
        {
            var rows = elements.Count;
            if (rows <= 1) return;

            // AI が入れた空セル(スペーサー)は取り除く。埋めるのはツールが ColumnSpan で行うため不要。
            foreach (var row in elements)
                row.RemoveAll(c => !IsMeaningfulCell(c));

            foreach (var row in elements)
                foreach (var cell in row)
                {
                    cell.RowSpan = 1;
                    cell.ColumnSpan = Math.Max(1, cell.ColumnSpan);
                }

            // 結合(ColumnSpan>1)の明示指定が無い純粋な二段化では、フィールドを各段へ「なるべく同数ずつ」再配分して
            // 段の列数を揃える(2段目だけ列数が少ない、といったアンバランスを避けて見た目を整える)。順序は維持する。
            // 結合指定があるとき(住所を2セルに 等)は、その意図を壊さないよう再配分しない。
            var anyMerge = elements.Any(row => row.Any(c => c.ColumnSpan > 1));
            if (!anyMerge)
            {
                var all = elements.SelectMany(row => row).ToList();
                if (all.Count > 0)
                {
                    var idx = 0;
                    for (var r = 0; r < rows; r++)
                    {
                        var size = all.Count / rows + (r < all.Count % rows ? 1 : 0);
                        elements[r] = all.GetRange(idx, size);
                        idx += size;
                    }
                }
            }

            var target = elements.Max(row => row.Sum(c => c.ColumnSpan));
            if (target <= 0) return;

            foreach (var row in elements)
            {
                if (row.Count == 0)
                {
                    row.Add(new ListElement { ColumnSpan = target });
                    continue;
                }
                // 不足分の列は末尾セルの ColumnSpan に寄せて端まで埋める(空セルは作らない)。
                // 末尾に寄せることで、前方セルのユーザー明示指定(例: 顧客コードは1セル)を尊重する。
                var extra = target - row.Sum(c => c.ColumnSpan);
                if (extra > 0)
                    row[^1].ColumnSpan += extra;
            }
        }

        static string UniqueName(string baseName, List<FieldDesignBase> fields)
        {
            var names = new HashSet<string>(fields.Select(f => f.Name), StringComparer.Ordinal);
            if (!names.Contains(baseName)) return baseName;
            var i = 2;
            while (names.Contains(baseName + i)) i++;
            return baseName + i;
        }

        // レイアウトも含めてモジュール全体を検証する(CheckModule はレイアウト検証込み)。例外時は空。
        List<DesignCheckInfo> SafeCheckModule()
        {
            try
            {
                return _editor.CheckModule(_editor.GetModuleDesign());
            }
            catch
            {
                return new();
            }
        }

        static string FormatErrors(List<DesignCheckInfo> errors)
            => string.Join("\n", errors.Select(e => $"- {e.GetPositionText()}: {e.Message}"));

        string BuildResultMessage(AIResponse? response)
        {
            var messages = new List<string>
            {
                string.IsNullOrWhiteSpace(response?.Explanation) ? "一覧レイアウトを変更しました" : response!.Explanation
            };
            if (!string.IsNullOrEmpty(_resultNote)) messages.Add(_resultNote);
            return string.Join("\r\n", messages);
        }

        static readonly string LayoutReference = EmbeddedDocs.Spec("Layouts", "JsonAbstractTypeFullName")
            + EmbeddedDocs.Guideline("LayoutGuidelines.md");

        class AIResponse
        {
            // List<List<ListElement>> - テーブル列定義。外側=ヘッダー行(通常1行)、内側=その行の列。
            public List<List<ListElement>> Elements { get; set; } = new();

            // ユーザーへの説明。できたこと・できなかったこと(と理由・代替)を書く。できないことを成功扱いにしないため。
            public string Explanation { get; set; } = string.Empty;
        }

        // ListElement のプロパティ・Elements 構造・複数行ヘッダー・Id 列を出さない慣行・IsViewOnly などは
        // 別途渡す「## レイアウト仕様」(Layouts.md / LayoutGuidelines.md)を参照。
        // ここには List レイアウトChat 固有の出力プロトコル(AIResponse の Elements / Explanation)のみを書く。
        const string SystemPrompt = @"
あなたはローコードでのList（一覧）画面レイアウトのデザイナです。
ユーザーの指示に基づいて一覧テーブルの列定義を編集し、結果をJSONで返してください。
ListElement のプロパティ・Elements の構造([[列, 列, ...]] = 1行ヘッダー)・複数行ヘッダー(ColumnSpan/RowSpan)・Id 列を出さない慣行・IsViewOnly などは、
別途渡される「## レイアウト仕様」に必ず従ってください。

## 基本ルール
- 元の列定義が渡されるので、ユーザーの指示に対して**必要最小限の変更**にしてください(既存列のプロパティ値・並び順は指示がない限り変えない)。
- **列の幅変更・セルの結合(ColumnSpan)など見た目の調整だけを頼まれたときは、既存の列(フィールド)を絶対に減らさない・入れ替えない**。指定されたセルの ColumnSpan などだけを変え、他の列はフィールドも並びもそのまま残してください。フィールドを消したい/入れ替えたいと明示されたときだけ増減します。空セル(FieldName 空)でフィールドを置き換えないこと(ツールが列数を揃えるので、あなたが空セルを入れる必要はありません)。
- **ただし「いい感じに列を並べて/見やすくして」のような“全体を整える”依頼のときは**、主要な識別項目(コード・名称・区分・日付など)を見やすい順に列として並べ直してください。長い自由記述(メモ等)は右側か省略気味に。
- **列に出せるのは渡されたフィールド一覧にあるフィールドだけ**です。フィールド自体の新規追加やプロパティ編集はできません。
- FieldName には渡されたフィールド一覧にある Name を使います(表示名ではなく Name)。列見出しはフィールドの表示名が自動で使われるので、`Label` は既定の見出しを変えたいときだけ設定します。
- **【最重要】Id / LogicalDelete / OptimisticLocking の扱いは次の2点。判断は必ずこの順**:
  1. **ユーザーがそのフィールドの表示を明確に求めた場合は、必ず列に出す(これが最優先)。** 「明確に求めた」= 指示文にそのフィールドの Name か表示名があり「並べて」「出して」「表示して」「列にして」等と言っているとき。例:「Id・顧客コード・顧客名の列を並べて」「Id も出して」→ Id は必ず出す。慣行を理由に外さない。
  2. **上記で求められていない Id / LogicalDelete / OptimisticLocking は、一覧の列に出さない**(Id 列は空セルとして描画されます)。「いい感じに並べて」「全部の列を出して」のような漠然とした指示は、これらの表示の明示要求には**含めない**(=出さない)。
  - CreatedAt / UpdatedAt / Creator / Updater などの監査項目は、一覧に出して構いません(指示や文脈に応じて)。
- **【重要】行番号(通し番号)の列はツールが配置します(あなたは作らない・置かない)**: 「行番号」「連番」「通し番号」「No」「番号」「ListNo」等は行番号列の要求です。**行番号フィールド(ListNumberField)が存在すれば**、ツールが自動で左端(既定)に配置し、二段以上のときは全段を縦マージ(1セルに結合)します。あなたは行番号列を Elements に置かず、他のフィールドの並びだけを決めてください。**行番号フィールドは新規作成しません**(必要なものは事前に『フィールド作成』で作成されています。無ければツールは配置をスキップします)。
- **二段以上(複数行)にするとき**: Elements を2行以上にして、1レコードを複数の段(行)で見せられます。**各段(行)に表示したいフィールドを並べる**だけでよいです。**各段のフィールド数はできるだけ同数ずつに近づけて**ください(例: 全10項目なら1段目5・2段目5)。一部の段だけ極端に少なくしない。
  - 例: 1段目 `[顧客コード, 顧客名, 顧客名カナ, 有効, 作成日時]`、2段目 `[郵便番号, 住所, 電話番号, メール, 担当者名, 更新日時]` のように、段ごとに関連する項目を、数のバランスも見ながら並べる。
  - **特に幅・結合の指定が無いときは `ColumnSpan` を付けない(すべて1)**。行ごとの列数を揃える処理はツールが行います。
  - 特定のセルを広げたい/横結合したいと言われたときだけ `ColumnSpan` を使ってください(`RowSpan` は使わない。行番号列の縦マージはツールが行います)。
- Elements の各セル(ListElement)には表示対象(FieldName / DetailLayoutName / ListElementComponent のいずれか)を持たせます。**単一行(1行だけ)の一覧では、3つとも空のセルを作らないでください**(空白列になり不具合)。二段以上で列数を合わせる空セルはツールが入れるので、あなたが空セルを入れる必要はありません。
- **できない依頼は最初の応答で簡潔に断る**: 一覧に出せないフィールドの新規追加やフィールドのプロパティ編集(必須・最大長など)を求められたら、`Elements` を **空(要素なし)** にして現在の列を保持し、`Explanation` に簡潔な断りと対処(『全体設定』で追加/デザイナで手動)を書いてください。「変更しました」等のできたフリは禁止。

## 出力JSON形式（このチャット固有）

{
  ""Elements"": [ /* List<List<ListElement>> - テーブル列定義。[[列1, 列2, ...]] が1行ヘッダー(通常はこの形)。複数行ヘッダーのときだけ外側が2要素以上 */ ],
  ""Explanation"": ""ユーザーへの説明（何をしたか。できなかったことがあればそれと理由・対処も書く）""
}

列の変更が不要なとき(できない依頼の断りなど)は、Elements を空にして返してください(現在の一覧レイアウトはそのまま保たれます)。
";
    }
}
