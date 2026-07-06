using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Microsoft.Extensions.AI;
using System.IO;
using System.Text;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // 詳細レイアウト編集機能。旧 DetailLayoutChat のロジックをそのまま機能ユニットへ移したもの。
    internal class DetailLayoutFunction : IAiFunction
    {
        readonly IModuleDetailLayoutEditor _editor;
        readonly IChatClient _chatClient;
        readonly List<ChatMessage> _messages = new();

        // 直近の ApplyResponse で EnsureStandardParts が追加した標準パーツの通知文(結果メッセージ用)。
        string _standardPartsNote = string.Empty;

        public string Id => FunctionCatalog.LayoutDetail;
        public string DisplayName => FunctionCatalog.Entries[FunctionCatalog.LayoutDetail].DisplayName;
        public string RouterDescription => FunctionCatalog.Entries[FunctionCatalog.LayoutDetail].RouterDescription;

        public DetailLayoutFunction(Func<IChatClient> createChatClient, IModuleDetailLayoutEditor editor)
        {
            _editor = editor;
            _chatClient = createChatClient();
        }

        public void Clear() => _messages.Clear();

        public async Task<FunctionResult> ExecuteAsync(string instruction)
        {
            _standardPartsNote = string.Empty;
            var detail = (_editor.GetModuleDesign().DetailLayouts.GetValueOrDefault(_editor.GetLayoutName()) ?? new());
            if (detail.Layout is not GridLayoutDesign)
                return FunctionResult.NothingToDo("レイアウトデータが不正です（GridLayoutDesignが必要です）");

            // ベースライン: この編集の前に既にあるデザインチェックエラー。結果評価はここからの差分(新規エラー)だけを見る。
            var baseline = SafeCheckModule();

            var fields = _editor.GetModuleDesign().Fields;

            // システムフィールドは原則、詳細レイアウトに配置しない。ただしユーザーが明示的に表示を求めた分は許可する。
            // 「明示要求」= 指示文にそのフィールドの Name か DisplayName が含まれること(例: 「作成日時も表示して」「CreatedAt を出して」)。
            var explicitlyRequestedSystemFields = fields
                .Where(f => SystemLayoutFieldNames.Contains(f.Name))
                .Where(f => instruction.Contains(f.Name, StringComparison.OrdinalIgnoreCase)
                    || (f is IDisplayName d && !string.IsNullOrEmpty(d.DisplayName) && instruction.Contains(d.DisplayName)))
                .Select(f => f.Name)
                .ToHashSet();

            // ラベル上(縦置き)を明示要求しているか。要求していなければ既定のラベル左に是正する。
            var labelAboveRequested = instruction.Contains("ラベル上") || instruction.Contains("縦")
                || (instruction.Contains("ラベル") && instruction.Contains("上"));

            // 標準パーツ(タイトル/戻る/サブミット)を追加するか。
            // 条件: 明示要求 OR 何もない(入力未配置)状態からの全体配置依頼。それ以外では足さない。
            var explicitBack = instruction.Contains("戻る");
            var explicitTitle = instruction.Contains("タイトル");
            var explicitSubmit = instruction.Contains("サブミット")
                || instruction.Contains("submit", StringComparison.OrdinalIgnoreCase)
                || instruction.Contains("登録ボタン") || instruction.Contains("保存ボタン");
            var arrangeWords = new[] { "並べ", "配置", "レイアウト", "いい感じ", "見やす", "整え", "フォーム" };
            var isWholeArrange = arrangeWords.Any(instruction.Contains);
            var fromScratch = !HasAnyInputPlaced(detail.Layout) && isWholeArrange;
            var wantBack = explicitBack || fromScratch;
            var wantTitle = explicitTitle || fromScratch;
            var wantSubmit = explicitSubmit || fromScratch;

            // 「タブに分けて」の要求。要求されたのに TabLayoutDesign を作らない誤りを検出して再生成へ回すため。
            var tabRequested = instruction.Contains("タブ") || instruction.Contains("tab", StringComparison.OrdinalIgnoreCase);

            // ── 一から全体配置するときはプラン方式(Stage1: 配置プランをAIが決める → コードが決定的にレイアウトを組む) ──
            // 検索レイアウトの2段化と同じ思想を一歩進めたもの。プラン(順序・グループ・タブ)だけAIに決めさせ、
            // 行・列・ラベル・幅・標準パーツはコードが組むため、空行・重複配置・ラベル上の誤りなどが構造的に起きない。
            // 既存レイアウトがある場合(個別編集)は従来の単段生成で最小変更する。
            // プランは「順序・グループ・タブ」しか表現できないため、罫線・色・幅・特定のGrid構造などの
            // 細かい見た目指定を含む指示は最初から従来の単段生成に任せる。
            var fineGrainedStyleWords = new[] { "罫線", "背景", "色", "幅", "グリッド", "Grid", "grid", "Border", "border" };
            var hasFineGrainedStyle = fineGrainedStyleWords.Any(w => instruction.Contains(w, StringComparison.OrdinalIgnoreCase));

            if (fromScratch && !hasFineGrainedStyle)
            {
                var planned = await TryArrangeByPlanAsync(
                    detail, instruction, explicitlyRequestedSystemFields, labelAboveRequested, baseline,
                    wantBack, wantTitle, wantSubmit);
                if (planned != null) return planned;
                //プランが得られなかったときは従来の単段生成にフォールバックする
            }

            // 仕様プロンプト(SystemPrompt + レイアウト仕様Docs)は会話の最初の1回だけ履歴に入れる。
            if (_messages.Count == 0)
            {
                _messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
                if (!string.IsNullOrEmpty(LayoutReference))
                    _messages.Add(new ChatMessage(ChatRole.System,
                        "## レイアウト仕様（クラス定義・プロパティ・推奨ルール・IsViewOnly）\n\n" + LayoutReference));
            }

            // フィールド一覧と現在のレイアウトは毎ターン追加する(編集で変わるため常に最新を渡す)。
            var fieldInfo = fields.Select(f => $"  {f.Name} ({f.GetType().Name})").ToList();
            var moduleName = _editor.GetModuleDesign().Name;
            _messages.Add(new ChatMessage(ChatRole.User,
                $"モジュール名: {moduleName}\n\n"
                + $"現在のモジュールに定義されているフィールド一覧:\n{string.Join("\n", fieldInfo)}\n\n"
                + $"現在のレイアウト:\n{JsonConverterEx.SerializeObject(detail.Layout)}\n\n指示: {instruction}"));

            // 全幅で置くべき大きいフィールド(複数行テキスト/File/List系/画像/MarkupString)。
            // これらをラベル左(横にラベルを並べる複数カラム行)に置くと全幅にならないため、検出して再生成へ回す。
            var fullWidthNames = fields.Where(IsFullWidthType).Select(f => f.Name).ToHashSet();

            // 生成 → 適用 → デザインチェック → エラーがあればAIに返して再生成、を上限まで繰り返す自己修正ループ。
            // レイアウトは壊れても表示が崩れる程度なので「適用しつつ」検証し、直らなければ警告する。
            const int maxAttempts = 3;
            var lastErrors = new List<DesignCheckInfo>();
            AIResponse? lastResponse = null;

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

                // 未知プロパティ検証: 定義に存在しないプロパティをAIが書くと、通常のデシリアライズでは黙って捨てられ、
                // 値が反映されないのに成功扱いになる。strict 検証で検出し、デザインチェックと同様に再生成へ回す。
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

                // レイアウト変更を伴わない応答(できない依頼の断り等)。Layout が空(行なし)なら適用せず説明だけ返す。
                // 既存レイアウトを保持し(空を適用するとフォームが消える)、断りを1回で完結させる(リトライせず速い)。
                if (response.Layout == null || response.Layout.Rows.Count == 0)
                {
                    // タイトル/戻る/サブミットの配置要求は、AIがレイアウトを返さなくても(空=現状維持)、
                    // 既存の該当フィールドがあればコードが現在のレイアウトの先頭/末尾へ配置し直す(作成はしない)。
                    if ((wantBack || wantTitle || wantSubmit) && detail.Layout is GridLayoutDesign currentRoot)
                    {
                        var note = EnsureStandardParts(currentRoot, _editor.GetModuleDesign().Fields, wantBack, wantTitle, wantSubmit);
                        _editor.Update();
                        return FunctionResult.Done("指定のパーツ(タイトル/戻る/サブミット)のうち、存在するものを配置しました。");
                    }
                    return FunctionResult.NothingToDo(string.IsNullOrWhiteSpace(response.Explanation)
                        ? "レイアウトは変更していません。"
                        : response.Explanation);
                }

                // 列(GridColumn)の無い行(GridRow)は何も描画されず不具合になる。デザインチェックでは検出されないため、
                // ここで検出して適用せず再生成ループに回す(ネストした Grid / Tab 内も再帰的に確認)。
                if (HasEmptyRow(response.Layout))
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            "生成されたレイアウトに、列(GridColumn)が1つも無い行(GridRow)が含まれています。" +
                            "列の無い行は画面に何も表示されず不具合になります。" +
                            "すべての行に最低1つの列(GridColumn)を入れ、フィールドを置く行はその列の Layout に FieldLayoutDesign を設定して、" +
                            "レイアウト全体を再度出力してください。"));
                        continue;
                    }
                    return FunctionResult.NothingToDo("列の無い行(GridRow)が生成され解消できなかったため、変更は適用していません。もう一度指示してください。");
                }

                // 存在しないフィールドを参照する FieldLayoutDesign(宙ぶらりん参照)は不具合になる(セルが壊れた参照で占有され、
                // 何も表示されず、上にドロップもできない)。この機能はフィールドを作成しないため、
                // 実在フィールドだけを既知として、未追加フィールドを参照させず、適用せず再生成ループに回す。
                var knownFieldNames = new HashSet<string>(_editor.GetModuleDesign().Fields.Select(f => f.Name));

                var unknownRefs = FindUnknownFieldRefs(response.Layout, knownFieldNames);
                if (unknownRefs.Count > 0)
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            $"レイアウトに、存在しないフィールドを参照する FieldLayoutDesign が含まれています: {string.Join(", ", unknownRefs)}。" +
                            "このチャットはラベル以外のフィールドを追加できません。存在しないフィールド名を FieldLayoutDesign.FieldName に指定しないでください。" +
                            "そのフィールドを後で置く場所は、GridColumn の Layout を省略した空のセルにして、レイアウト全体を再度出力してください。"));
                        continue;
                    }
                    return FunctionResult.NothingToDo($"存在しないフィールド({string.Join(", ", unknownRefs)})を参照するレイアウトが生成されたため、変更は適用していません。" +
                        "そのフィールドは『全体設定』で追加するか、デザイナで手動追加してから配置してください。");
                }

                // 同じフィールドを複数の場所に配置するのは不具合(1フィールドは1箇所のみ)。
                // ラベル上スタイルで入力フィールドを「ラベル行」と「入力行」に二重に置く誤りなどを検出して再生成へ回す。
                var duplicateRefs = FindDuplicateFieldRefs(response.Layout);
                if (duplicateRefs.Count > 0)
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            $"同じフィールドが複数の場所に配置されています: {string.Join(", ", duplicateRefs)}。" +
                            "1つのフィールドはレイアウト内で1箇所にしか置けません。" +
                            "ラベルを上に置く場合、ラベル行に入力フィールドを置くのではなく、その入力用の LabelFieldDesign(Text は \"\"、RelativeField に入力フィールド名を設定)を NewLabels に用意し、" +
                            "ラベル行ではそのラベルを FieldName で参照してください。入力フィールドは入力行に1回だけ置きます。重複を解消してレイアウト全体を再度出力してください。"));
                        continue;
                    }
                    return FunctionResult.NothingToDo($"同じフィールド({string.Join(", ", duplicateRefs)})が複数箇所に配置されたため、変更は適用していません。もう一度指示してください。");
                }

                // システムフィールド(Id/CreatedAt/... )は、明示要求が無い限り詳細レイアウトに置かない。
                // AIが指示を無視して置くことがあるため、検出したら再生成へ回し、最終的には決定的に取り除いて適用する。
                var unwantedSystemFields = FindPlacedFieldRefs(response.Layout, SystemLayoutFieldNames)
                    .Where(n => !explicitlyRequestedSystemFields.Contains(n)).ToList();
                if (unwantedSystemFields.Count > 0)
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            $"システムフィールドが詳細レイアウトに配置されています: {string.Join(", ", unwantedSystemFields)}。" +
                            "Id / CreatedAt / UpdatedAt / Creator / Updater / LogicalDelete / OptimisticLocking / DeletedAt / Deleter は、" +
                            "ユーザーが明示的に『表示して』と求めていない限り詳細レイアウトに置きません。これらの FieldLayoutDesign をレイアウトから取り除き(該当セルは Layout を省略した空セルにするか、行ごと削除する)、レイアウト全体を再度出力してください。"));
                        continue;
                    }
                    // 最終手段: AIが直さないので、該当する FieldLayoutDesign を決定的に空セル化して適用し、除外したことを伝える。
                    StripFieldRefs(response.Layout, unwantedSystemFields);
                    response.Explanation = (string.IsNullOrWhiteSpace(response.Explanation) ? "" : response.Explanation + "\r\n")
                        + $"（システムフィールド {string.Join(", ", unwantedSystemFields)} は詳細レイアウトに配置しない決まりのため除外しました。表示が必要なら『{unwantedSystemFields[0]} も表示して』のように指示してください。）";
                }

                // タブに分ける指示なのに TabLayoutDesign が無い場合は再生成へ回す(最終試行では諦めてそのまま適用する)。
                if (tabRequested && !HasTabLayout(response.Layout) && attempt < maxAttempts)
                {
                    _messages.Add(new ChatMessage(ChatRole.User, 
                        "タブに分ける指示ですが、生成されたレイアウトに TabLayoutDesign が含まれていません。" +
                        "**ルートは必ず GridLayoutDesign のまま**にし、その中の GridColumn.Layout に **TabLayoutDesign をネスト**してください(ルート直下に Tabs/Layouts を置いてはいけません。GridLayoutDesign に Tabs/Layouts プロパティはありません)。" +
                        "TabLayoutDesign は Tabs にタブ名の配列、Layouts に各タブの GridLayoutDesign を入れ、Tabs.Count と Layouts.Count を一致させます。" +
                        "各フィールドを対応するタブ内の GridLayoutDesign に配置して、レイアウト全体を再度出力してください。"));
                    continue;
                }

                // 全幅で置くべき大きいフィールドが、横にラベル/他項目と並ぶ複数カラム行に置かれていたら再生成へ回す(最終試行では諦めて適用)。
                var narrowFullWidth = FindFullWidthInMultiColumnRow(response.Layout, fullWidthNames);
                if (narrowFullWidth.Count > 0 && attempt < maxAttempts)
                {
                    _messages.Add(new ChatMessage(ChatRole.User, 
                        $"全幅で配置すべき大きいフィールドが、横にラベルや他項目と並ぶ複数カラムの行に置かれています: {string.Join(", ", narrowFullWidth)}。" +
                        "複数行テキスト・File・List/DetailList/TileList・画像・MarkupString などの大きいフィールドは、" +
                        "その行に**単独(1カラムだけの GridRow)**で置いて全幅にしてください(横にラベルや他フィールドを並べない)。" +
                        "見出しラベルが必要な場合は、その入力行の**上**に別の1カラム行としてラベルを置いてください。レイアウト全体を再度出力してください。"));
                    continue;
                }

                ApplyResponse(detail, response, labelAboveRequested, wantBack, wantTitle, wantSubmit, fromScratch);
                lastResponse = response;

                var errors = DesignCheckDiff.NewErrors(baseline, SafeCheckModule());
                if (errors.Count == 0)
                    return FunctionResult.Done(BuildResultMessage(response));

                lastErrors = errors;
                if (attempt < maxAttempts)
                    _messages.Add(new ChatMessage(ChatRole.User, 
                        "生成されたレイアウトに次のデザインチェックエラーがあります。修正してレイアウト全体を再度出力してください。\n"
                        + FormatErrors(errors)));
            }

            return FunctionResult.Done(BuildResultMessage(lastResponse!)
                + $"\r\n（注意: デザインチェックエラーが残っています。内容を確認してください）\r\n{FormatErrors(lastErrors)}");
        }

        // ===================================================================================
        // プラン方式(一から全体配置): Stage1 でAIが配置プラン(順序・セクション・タブ)だけを決め、
        // レイアウトJSONはコードが決定的に組み立てる。
        // ===================================================================================

        class ArrangePlan
        {
            public List<PlanTab> Tabs { get; set; } = new();
            public bool CanDo { get; set; } = true;
            public string Explanation { get; set; } = string.Empty;
        }

        class PlanTab
        {
            public string Title { get; set; } = string.Empty;
            public List<PlanSection> Sections { get; set; } = new();
        }

        class PlanSection
        {
            public string Title { get; set; } = string.Empty;
            public bool IsCard { get; set; }
            public int ItemsPerRow { get; set; } = 1;
            public List<string> Fields { get; set; } = new();
        }

        // プラン方式の実行。プランが得られなければ null(呼び出し元が従来の単段生成へフォールバック)。
        async Task<FunctionResult?> TryArrangeByPlanAsync(DetailLayoutDesign detail, string instruction,
            HashSet<string> explicitlyRequestedSystemFields, bool labelAboveRequested, List<DesignCheckInfo> baseline,
            bool wantBack, bool wantTitle, bool wantSubmit)
        {
            var fields = _editor.GetModuleDesign().Fields;

            // プランに載せてよいフィールド: 標準パーツ(タイトル/戻る/サブミット=コードが配置)とラベルを除く。
            // システムフィールドは明示要求分だけ。
            var placeable = fields
                .Where(f => f is not LabelFieldDesign)
                .Where(f => f is not SubmitButtonFieldDesign)
                .Where(f => f is not AnchorTagFieldDesign a || a.Target != AnchorTarget.HistoryBack)
                .Where(f => !SystemLayoutFieldNames.Contains(f.Name) || explicitlyRequestedSystemFields.Contains(f.Name))
                .ToList();
            if (placeable.Count == 0) return null;

            var placeableNames = placeable.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
            var fieldInfo = placeable.Select(f =>
                $"  {f.Name} ({f.GetType().Name}{(f is IDisplayName d && !string.IsNullOrEmpty(d.DisplayName) ? $", 表示名:{d.DisplayName}" : "")})");

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, PlanSystemPrompt),
                new ChatMessage(ChatRole.User,
                    $"モジュール名: {_editor.GetModuleDesign().Name}\n\n"
                    + $"配置できるフィールド一覧:\n{string.Join("\n", fieldInfo)}\n\n指示: {instruction}"),
            };

            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ArrangePlan plan;
                string resultText;
                try
                {
                    var result = await _chatClient.GetResponseAsync(messages,
                        new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
                    resultText = result.Text;
                    plan = JsonConverterEx.DeserializeObject<ArrangePlan>(resultText)!;
                }
                catch
                {
                    return null; //プランが取れない → 従来方式へ
                }
                messages.Add(new ChatMessage(ChatRole.Assistant, resultText));

                //CanDo=false は「プラン(順序・グループ・タブ)では扱えない指示」の申告。
                //ユーザーへ断りを返すのではなく、表現力の高い従来の単段生成へフォールバックする。
                if (!plan.CanDo) return null;

                var planFields = plan.Tabs.SelectMany(t => t.Sections).SelectMany(s => s.Fields).ToList();
                if (planFields.Count == 0) return null;

                // 実在しないフィールドは再生成へ(上限まで直らなければ従来方式へ)。
                var unknown = planFields.Where(n => !placeableNames.Contains(n)).Distinct().ToList();
                if (unknown.Count > 0)
                {
                    if (attempt < maxAttempts)
                    {
                        messages.Add(new ChatMessage(ChatRole.User,
                            $"一覧に無いフィールドが含まれています: {string.Join(", ", unknown)}。" +
                            "Fields には『配置できるフィールド一覧』にある Name だけを使い、プラン全体を再度出力してください。"));
                        continue;
                    }
                    return null;
                }

                // 重複はコードで決定的に除去する(最初の出現だけ残す)。
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var section in plan.Tabs.SelectMany(t => t.Sections))
                    section.Fields = section.Fields.Where(n => seen.Add(n)).ToList();
                plan.Tabs.ForEach(t => t.Sections.RemoveAll(s => s.Fields.Count == 0));
                plan.Tabs.RemoveAll(t => t.Sections.Count == 0);
                if (plan.Tabs.Count == 0) return null;

                // プラン → レイアウト(決定的)。
                var root = RenderPlan(plan, fields, labelAboveRequested);
                _standardPartsNote = EnsureStandardParts(root, fields, wantBack, wantTitle, wantSubmit);
                NormalizeLabelLeftWidths(root, fields);

                detail.Layout = root;
                _editor.Update();

                var errors = DesignCheckDiff.NewErrors(baseline, SafeCheckModule());
                var message = string.IsNullOrWhiteSpace(plan.Explanation) ? "レイアウトを配置しました。" : plan.Explanation;
                if (!string.IsNullOrEmpty(_standardPartsNote)) message += "\r\n" + _standardPartsNote;
                if (errors.Count > 0)
                    message += $"\r\n（注意: デザインチェックエラーが残っています。内容を確認してください）\r\n{FormatErrors(errors)}";
                return FunctionResult.Done(message);
            }
            return null;
        }

        // プランからレイアウトを決定的に組み立てる。
        // 空行・重複配置・ラベル上/左の取り違え・幅の付け忘れは、生成でなく構築なので起きない。
        GridLayoutDesign RenderPlan(ArrangePlan plan, List<FieldDesignBase> fields, bool labelAbove)
        {
            var byName = fields.ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);
            var existingNames = new HashSet<string>(fields.Select(f => f.Name), StringComparer.Ordinal);

            string Unique(string baseName)
            {
                if (!existingNames.Contains(baseName)) return baseName;
                var i = 2;
                while (existingNames.Contains(baseName + i)) i++;
                return baseName + i;
            }

            LabelFieldDesign AddLabel(string name, string text, string relativeField, LabelStyle style)
            {
                var label = new LabelFieldDesign { Name = Unique(name), Text = text, RelativeField = relativeField, Style = style };
                fields.Add(label);
                existingNames.Add(label.Name);
                byName[label.Name] = label;
                return label;
            }

            // 1項目(入力フィールド)を列の並びにする。ラベル左: [ラベル, 入力] / Boolean・ラベル不要: [入力]。
            List<GridColumn> ItemColumns(FieldDesignBase field)
            {
                if (field is DbValueFieldDesignBase and not BooleanFieldDesign)
                {
                    var label = AddLabel(field.Name + "Label", "", field.Name, LabelStyle.Default);
                    return new List<GridColumn>
                    {
                        new() { Layout = new FieldLayoutDesign(label.Name) },
                        new() { Layout = new FieldLayoutDesign(field.Name) },
                    };
                }
                return new List<GridColumn> { new() { Layout = new FieldLayoutDesign(field.Name) } };
            }

            GridLayoutDesign BuildSectionGrid(PlanSection section)
            {
                var grid = new GridLayoutDesign { IsBordered = section.IsCard };
                if (!string.IsNullOrWhiteSpace(section.Title))
                {
                    var heading = AddLabel(SanitizeName(section.Title) + "Heading", section.Title, "", LabelStyle.H5);
                    var headingRow = new GridRow();
                    headingRow.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign(heading.Name) });
                    grid.Rows.Add(headingRow);
                }

                var itemsPerRow = Math.Max(1, section.ItemsPerRow);
                GridRow? current = null;
                var itemsInCurrent = 0;

                foreach (var name in section.Fields)
                {
                    if (!byName.TryGetValue(name, out var field)) continue;

                    // 大きいフィールドは単独行・全幅(ラベルは上の1カラム行)。
                    if (IsFullWidthType(field))
                    {
                        current = null;
                        itemsInCurrent = 0;
                        var label = AddLabel(field.Name + "Label", "", field.Name, LabelStyle.Default);
                        var labelRow = new GridRow();
                        labelRow.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign(label.Name) });
                        grid.Rows.Add(labelRow);
                        var inputRow = new GridRow();
                        inputRow.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign(field.Name) });
                        grid.Rows.Add(inputRow);
                        continue;
                    }

                    if (labelAbove)
                    {
                        // ラベル上: 項目ごとに ネストGrid(ラベル行→入力行)。ItemsPerRow 分を1行に横に並べる。
                        var item = new GridLayoutDesign();
                        if (field is DbValueFieldDesignBase and not BooleanFieldDesign)
                        {
                            var label = AddLabel(field.Name + "Label", "", field.Name, LabelStyle.Default);
                            var lr = new GridRow();
                            lr.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign(label.Name) });
                            item.Rows.Add(lr);
                        }
                        var ir = new GridRow();
                        ir.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign(field.Name) });
                        item.Rows.Add(ir);

                        if (current == null || itemsInCurrent >= itemsPerRow)
                        {
                            current = new GridRow();
                            grid.Rows.Add(current);
                            itemsInCurrent = 0;
                        }
                        current.Columns.Add(new GridColumn { Layout = item });
                        itemsInCurrent++;
                        continue;
                    }

                    // ラベル左(既定)。ItemsPerRow 分の[ラベル,入力]ペアを1行に並べる。
                    if (current == null || itemsInCurrent >= itemsPerRow)
                    {
                        current = new GridRow();
                        grid.Rows.Add(current);
                        itemsInCurrent = 0;
                    }
                    current.Columns.AddRange(ItemColumns(field));
                    itemsInCurrent++;
                }
                return grid;
            }

            GridLayoutDesign BuildTabGrid(PlanTab tab)
            {
                //セクションが1つで見出しもカードも無いときは、ネストせず直接並べる
                if (tab.Sections.Count == 1 && !tab.Sections[0].IsCard && string.IsNullOrWhiteSpace(tab.Sections[0].Title))
                    return BuildSectionGrid(tab.Sections[0]);

                var grid = new GridLayoutDesign();
                foreach (var section in tab.Sections)
                {
                    var row = new GridRow();
                    row.Columns.Add(new GridColumn { Layout = BuildSectionGrid(section) });
                    grid.Rows.Add(row);
                }
                return grid;
            }

            if (plan.Tabs.Count > 1)
            {
                var tabLayout = new TabLayoutDesign();
                tabLayout.Tabs.Clear();    //コンストラクタが既定タブ("Tab 1")を作るため空にしてから組む
                tabLayout.Layouts.Clear();
                foreach (var tab in plan.Tabs)
                {
                    tabLayout.Tabs.Add(string.IsNullOrWhiteSpace(tab.Title) ? $"タブ{tabLayout.Tabs.Count + 1}" : tab.Title);
                    tabLayout.Layouts.Add(BuildTabGrid(tab));
                }
                var root = new GridLayoutDesign();
                var tabRow = new GridRow();
                tabRow.Columns.Add(new GridColumn { Layout = tabLayout });
                root.Rows.Add(tabRow);
                return root;
            }

            return BuildTabGrid(plan.Tabs[0]);
        }

        // セクション見出しからラベルNameに使える文字列を作る(記号・空白を除去)。
        static string SanitizeName(string title)
        {
            var sb = new StringBuilder();
            foreach (var c in title)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.Length == 0 ? "Section" : sb.ToString();
        }

        // Stage1 専用プロンプト: 配置プラン(順序・セクション・タブ)だけを決める。行・列・ラベル・幅は決めない。
        const string PlanSystemPrompt = @"
あなたはローコードのDetail(詳細)画面の配置プランナーです。
渡されたフィールドを「どの順序で・どんなグループ(セクション/タブ)に分けて」並べるかのプランだけを決めて、JSONで返してください。
行・列・見出しラベル・幅などの具体的なレイアウトは別工程(プログラム)が組み立てるので、一切考えません。

## ルール
- Fields には『配置できるフィールド一覧』にある Name だけを使う。**一覧の全フィールドを、業務的に自然な順序(コード→名前→属性→金額→日付→備考のような入力順)で並べる**。明らかに不要とユーザーが言ったもの以外は落とさない。
- **関連する項目をセクションにまとめる**。項目が少ない(目安8個以下)ならセクション1つ(Title空・IsCard=false)でよい。
  多いときは意味のまとまり(基本情報/金額/備考 等)ごとにセクションを分け、IsCard=true(枠線カード)にして Title を付ける。
- **タブ(Tabs を複数)にするのはユーザーがタブを求めたときだけ**。それ以外は Tabs は必ず1件(Title は空)。
- ItemsPerRow は「1行にN項目」の指定があったときだけその数。指定が無ければ 1。
- タイトル・戻るボタン・サブミットボタン・見出しラベルはプランに含めない(プログラムが自動配置する)。
- **プランで表現できるのは「順序・セクション分け・タブ分け・1行あたりの項目数」だけ**。それ以外の具体的な見た目指定
  (罫線・色・幅・特定のネスト構造など)が指示の主目的なら CanDo=false にする(別工程が対応する)。
- レイアウトと無関係な依頼や、存在しないフィールドの追加が必要な依頼も CanDo=false にして Explanation に理由を書く。

## 出力JSON形式
{
  ""Tabs"": [
    {
      ""Title"": """",
      ""Sections"": [
        { ""Title"": """", ""IsCard"": false, ""ItemsPerRow"": 1, ""Fields"": [""FieldName1"", ""FieldName2""] }
      ]
    }
  ],
  ""CanDo"": true,
  ""Explanation"": ""どう並べたか(セクション分けの意図)を簡潔に""
}
";

        // 列(GridColumn)が1つも無い行(GridRow)があるか。ネストした GridLayoutDesign / TabLayoutDesign も再帰確認する。
        // 全幅で配置すべき大きいフィールド型か(複数行テキスト/File/List系/画像/MarkupString)。
        static bool IsFullWidthType(FieldDesignBase f) =>
            (f is TextFieldDesign t && t.IsMultiline)
            || f is FileFieldDesign
            || f is ListFieldDesign
            || f is DetailListFieldDesign
            || f is TileListFieldDesign
            || f is MarkupStringFieldDesign
            || f is ImageViewerFieldDesign;

        // 全幅対象フィールドが、複数カラムの GridRow(横にラベル/他項目と並ぶ)に置かれているものを集める(ネスト再帰)。
        static List<string> FindFullWidthInMultiColumnRow(LayoutDesignBase? layout, HashSet<string> fullWidthNames)
        {
            var found = new List<string>();
            if (fullWidthNames.Count == 0) return found;

            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case GridLayoutDesign grid:
                        foreach (var row in grid.Rows)
                            foreach (var col in row.Columns)
                            {
                                if (row.Columns.Count > 1 && col.Layout is FieldLayoutDesign f
                                    && !string.IsNullOrEmpty(f.FieldName) && fullWidthNames.Contains(f.FieldName))
                                    found.Add(f.FieldName);
                                Walk(col.Layout);
                            }
                        break;
                    case TabLayoutDesign tab:
                        foreach (var t in tab.Layouts) Walk(t);
                        break;
                    case CanvasLayoutDesign canvas:
                        foreach (var e in canvas.Elements) Walk(e.Layout);
                        break;
                }
            }
            Walk(layout);
            return found.Distinct().ToList();
        }

        // レイアウトのどこかに TabLayoutDesign があるか(ネストGrid/Canvas も再帰確認)。
        static bool HasTabLayout(LayoutDesignBase? layout)
        {
            switch (layout)
            {
                case TabLayoutDesign:
                    return true;
                case GridLayoutDesign grid:
                    foreach (var row in grid.Rows)
                        foreach (var col in row.Columns)
                            if (HasTabLayout(col.Layout)) return true;
                    return false;
                case CanvasLayoutDesign canvas:
                    foreach (var e in canvas.Elements)
                        if (HasTabLayout(e.Layout)) return true;
                    return false;
                default:
                    return false;
            }
        }

        static bool HasEmptyRow(LayoutDesignBase? layout)
        {
            switch (layout)
            {
                case GridLayoutDesign grid:
                    foreach (var row in grid.Rows)
                    {
                        if (row.Columns.Count == 0) return true;
                        foreach (var col in row.Columns)
                            if (HasEmptyRow(col.Layout)) return true;
                    }
                    return false;
                case TabLayoutDesign tab:
                    return tab.Layouts.Any(HasEmptyRow);
                default:
                    return false;
            }
        }

        // FieldLayoutDesign が参照する FieldName が、実在フィールドにも新規ラベルにも無い(宙ぶらりん参照)を集める。
        // このチャットはラベル以外のフィールドを追加できないため、AIが存在しないフィールド(例: 未追加の SubmitButton)を
        // 参照する FieldLayoutDesign を作ってしまうのを防ぐ。ネストした Grid / Tab / Canvas も再帰確認する。
        static List<string> FindUnknownFieldRefs(LayoutDesignBase? layout, HashSet<string> knownFieldNames)
        {
            var unknown = new List<string>();

            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case FieldLayoutDesign field:
                        if (!string.IsNullOrEmpty(field.FieldName) && !knownFieldNames.Contains(field.FieldName))
                            unknown.Add(field.FieldName);
                        break;
                    case GridLayoutDesign grid:
                        foreach (var row in grid.Rows)
                            foreach (var col in row.Columns)
                                Walk(col.Layout);
                        break;
                    case TabLayoutDesign tab:
                        foreach (var t in tab.Layouts) Walk(t);
                        break;
                    case CanvasLayoutDesign canvas:
                        foreach (var e in canvas.Elements) Walk(e.Layout);
                        break;
                }
            }

            Walk(layout);
            return unknown.Distinct().ToList();
        }

        // 同じ FieldName を持つ FieldLayoutDesign が2回以上現れる(同一フィールドの多重配置)を集める。
        // ネストした Grid / Tab / Canvas も再帰確認する。
        static List<string> FindDuplicateFieldRefs(LayoutDesignBase? layout)
        {
            var counts = new Dictionary<string, int>();

            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case FieldLayoutDesign field:
                        if (!string.IsNullOrEmpty(field.FieldName))
                            counts[field.FieldName] = counts.GetValueOrDefault(field.FieldName) + 1;
                        break;
                    case GridLayoutDesign grid:
                        foreach (var row in grid.Rows)
                            foreach (var col in row.Columns)
                                Walk(col.Layout);
                        break;
                    case TabLayoutDesign tab:
                        foreach (var t in tab.Layouts) Walk(t);
                        break;
                    case CanvasLayoutDesign canvas:
                        foreach (var e in canvas.Elements) Walk(e.Layout);
                        break;
                }
            }

            Walk(layout);
            return counts.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToList();
        }

        // 詳細レイアウトに原則置かないシステムフィールド名(CurrentUser はフィールドではないので除外)。
        // コアの public 定数を参照して綴りを源泉と一致させる(SystemFieldNames.All は internal のため自前で列挙)。
        static readonly HashSet<string> SystemLayoutFieldNames = new(StringComparer.Ordinal)
        {
            SystemFieldNames.Id, SystemFieldNames.LogicalDelete, SystemFieldNames.CreatedAt,
            SystemFieldNames.UpdatedAt, SystemFieldNames.DeletedAt, SystemFieldNames.Creator,
            SystemFieldNames.Updater, SystemFieldNames.Deleter, SystemFieldNames.OptimisticLocking,
        };

        // 指定した名前集合のいずれかを FieldName に持つ FieldLayoutDesign がレイアウトに置かれているものを集める。
        static List<string> FindPlacedFieldRefs(LayoutDesignBase? layout, HashSet<string> targetNames)
        {
            var found = new List<string>();

            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case FieldLayoutDesign field:
                        if (!string.IsNullOrEmpty(field.FieldName) && targetNames.Contains(field.FieldName))
                            found.Add(field.FieldName);
                        break;
                    case GridLayoutDesign grid:
                        foreach (var row in grid.Rows)
                            foreach (var col in row.Columns)
                                Walk(col.Layout);
                        break;
                    case TabLayoutDesign tab:
                        foreach (var t in tab.Layouts) Walk(t);
                        break;
                    case CanvasLayoutDesign canvas:
                        foreach (var e in canvas.Elements) Walk(e.Layout);
                        break;
                }
            }

            Walk(layout);
            return found.Distinct().ToList();
        }

        // 指定した名前を FieldName に持つ FieldLayoutDesign を置いているセル(GridColumn.Layout / CanvasElement.Layout)を空にする。
        // セルや行の構造は壊さず(空セルは有効)、参照だけ取り除く。
        static void StripFieldRefs(LayoutDesignBase? layout, IEnumerable<string> namesToRemove)
        {
            var names = new HashSet<string>(namesToRemove, StringComparer.Ordinal);

            bool IsTarget(LayoutDesignBase? node)
                => node is FieldLayoutDesign f && !string.IsNullOrEmpty(f.FieldName) && names.Contains(f.FieldName);

            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case GridLayoutDesign grid:
                        foreach (var row in grid.Rows)
                            foreach (var col in row.Columns)
                            {
                                if (IsTarget(col.Layout)) col.Layout = null;
                                else Walk(col.Layout);
                            }
                        break;
                    case TabLayoutDesign tab:
                        foreach (var t in tab.Layouts) Walk(t);
                        break;
                    case CanvasLayoutDesign canvas:
                        foreach (var e in canvas.Elements)
                        {
                            if (IsTarget(e.Layout)) e.Layout = null;
                            else Walk(e.Layout);
                        }
                        break;
                }
            }

            Walk(layout);
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

        void ApplyResponse(DetailLayoutDesign detail, AIResponse response, bool labelAboveRequested,
            bool wantBack, bool wantTitle, bool wantSubmit, bool fromScratch)
        {
            // 既定はラベル左。ラベル上を明示要求していないのに AI がラベル上(縦置き)を作ったら、ラベル左へ是正する。
            if (!labelAboveRequested)
                ConvertLabelAboveToLabelLeft(response.Layout, _editor.GetModuleDesign().Fields);

            // ラベル左のラベル列幅をコードで確定的に推定設定する(AIが幅を付け忘れる/狭すぎるのを補正)。ラベル上の幅は外す。
            NormalizeLabelLeftWidths(response.Layout, _editor.GetModuleDesign().Fields);

            // 一から全体配置するときは、AIが作りがちな「全セルが空のゴミ行」を除去する(個別編集では消さない)。
            if (fromScratch)
                RemoveEmptyRows(response.Layout);

            // 標準パーツ(タイトル/戻る=先頭行、サブミット=末尾行)は「既存フィールドの中から見つけて」配置する(作成はしない=field.createの担当)。
            _standardPartsNote = EnsureStandardParts(response.Layout, _editor.GetModuleDesign().Fields, wantBack, wantTitle, wantSubmit);

            detail.Layout = response.Layout;
            _editor.Update();
        }

        // レイアウトに入力フィールド(DbValueFieldDesignBase)が1つでも配置されているか。フィールド型は名前では判断できないため、
        // ここでは「FieldName が付いた FieldLayoutDesign が1つでもあるか」で“何か配置済みか”を判定する(空/空セルだけなら false)。
        static bool HasAnyInputPlaced(LayoutDesignBase? layout)
        {
            var found = false;
            void Walk(LayoutDesignBase? node)
            {
                if (found) return;
                switch (node)
                {
                    case FieldLayoutDesign f:
                        if (!string.IsNullOrEmpty(f.FieldName)) found = true;
                        break;
                    case GridLayoutDesign grid:
                        foreach (var row in grid.Rows)
                            foreach (var col in row.Columns) Walk(col.Layout);
                        break;
                    case TabLayoutDesign tab:
                        foreach (var t in tab.Layouts) Walk(t);
                        break;
                    case CanvasLayoutDesign canvas:
                        foreach (var e in canvas.Elements) Walk(e.Layout);
                        break;
                }
            }
            Walk(layout);
            return found;
        }

        // 詳細画面の標準パーツ(タイトル/戻るボタン=先頭行、サブミット=末尾行)を、既存フィールドの中から見つけて配置し直す。
        // **新規作成はしない**(作成は field.create の担当)。placeX が真で対象が既存フィールドにあるときだけ、慣習位置へ置き直す。
        // 戻るボタンはアイコンのみ・HistoryBack に正規化する(配置時の体裁 know-how)。
        internal static string EnsureStandardParts(GridLayoutDesign root, List<FieldDesignBase> fields,
            bool placeBack, bool placeTitle, bool placeSubmit)
        {
            if (!placeBack && !placeTitle && !placeSubmit) return string.Empty;

            // 既存の戻るボタンは「HistoryBack の AnchorTag」または「Name が BackButton の AnchorTag」で拾う。
            var back = fields.OfType<AnchorTagFieldDesign>()
                .FirstOrDefault(a => a.Target == AnchorTarget.HistoryBack || a.Name == "BackButton");
            var title = fields.OfType<LabelFieldDesign>().FirstOrDefault(l => l.Style == LabelStyle.H4);
            var submit = fields.OfType<SubmitButtonFieldDesign>().FirstOrDefault();

            // 存在するものだけを対象にする(作成はしない)。
            var doBack = placeBack && back != null;
            var doTitle = placeTitle && title != null;
            var doSubmit = placeSubmit && submit != null;
            if (!doBack && !doTitle && !doSubmit) return string.Empty;

            // 戻るボタンは慣習に揃える: アイコンのみ(TitleText 空)・Style=Text・塗りつぶしアイコン(未設定時)。大きさは FontSize=30。
            if (doBack)
            {
                back!.Target = AnchorTarget.HistoryBack;
                back.Style = AnchorStyle.Text;
                back.TitleText = "";
                if (string.IsNullOrEmpty(back.Icon) || back.Icon == "bi bi-arrow-left-circle") back.Icon = "bi bi-arrow-left-circle-fill";
            }

            // 対象パーツの既存配置を一旦取り除いてから、正しい位置に置き直す(重複・誤配置を防ぐ)。
            var managed = new List<string>();
            if (doBack) managed.Add(back!.Name);
            if (doTitle) managed.Add(title!.Name);
            if (doSubmit) managed.Add(submit!.Name);
            RemovePlacements(root, managed);

            // 先頭行: 戻る(Width:60・アイコンを FontSize:30) | タイトル(中央) | 空セル(Width:60)
            if (doBack || doTitle)
            {
                var header = new GridRow();
                if (doBack)
                    header.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign { FieldName = back!.Name, FontSize = 30 }, Width = 60 });
                if (doTitle)
                    header.Columns.Add(new GridColumn
                    {
                        Layout = new FieldLayoutDesign { FieldName = title!.Name },
                        HorizontalAlignment = doBack ? HorizontalAlignment.Center : (HorizontalAlignment?)null
                    });
                if (doBack && doTitle)
                    header.Columns.Add(new GridColumn { Width = 60 });
                root.Rows.Insert(0, header);
            }

            // 末尾行: サブミット(1カラムのみなので左右中央に置く)
            if (doSubmit)
            {
                var footer = new GridRow();
                footer.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign { FieldName = submit!.Name }, HorizontalAlignment = HorizontalAlignment.Center });
                root.Rows.Add(footer);
            }

            return string.Empty;
        }

        // 中身(配置されたフィールド)が1つも無い行を取り除く(全セルが空のゴミ行)。ネストGrid/Tab も再帰。
        // 一から全体配置するとき専用(個別編集では『後で置く空セル行』を消さないよう呼ばない)。
        static void RemoveEmptyRows(GridLayoutDesign root)
        {
            void Walk(GridLayoutDesign g)
            {
                foreach (var row in g.Rows)
                    foreach (var col in row.Columns)
                    {
                        if (col.Layout is GridLayoutDesign ng) Walk(ng);
                        else if (col.Layout is TabLayoutDesign tab)
                            foreach (var t in tab.Layouts)
                                if (t is GridLayoutDesign tg) Walk(tg);
                    }
                g.Rows.RemoveAll(r => !r.Columns.Any(c => HasAnyInputPlaced(c.Layout)));
            }
            Walk(root);
        }

        // 指定名の FieldLayoutDesign を置いている列を取り除き、空になった行も取り除く(ネストGrid/Tab も再帰)。
        static void RemovePlacements(GridLayoutDesign root, List<string> names)
        {
            if (names.Count == 0) return;
            var set = new HashSet<string>(names, StringComparer.Ordinal);

            void Walk(GridLayoutDesign g)
            {
                foreach (var row in g.Rows)
                {
                    row.Columns.RemoveAll(c => c.Layout is FieldLayoutDesign f && set.Contains(f.FieldName));
                    foreach (var col in row.Columns)
                    {
                        if (col.Layout is GridLayoutDesign ng) Walk(ng);
                        else if (col.Layout is TabLayoutDesign tab)
                            foreach (var t in tab.Layouts)
                                if (t is GridLayoutDesign tg) Walk(tg);
                    }
                }
                // 管理対象を取り除いた結果、中身(配置フィールド)が無くなった行は丸ごと除去する
                // (例: AIが作ったヘッダ行から戻る/タイトルを抜いた後に残る空セルだけの行)。
                g.Rows.RemoveAll(r => !r.Columns.Any(c => HasAnyInputPlaced(c.Layout)));
            }
            Walk(root);
        }

        // ラベル上(ネストGrid: ラベル行→入力行)のブロックを、ラベル左(同じ行に[ラベル, 入力])へ変換する。
        // 既定ではラベル左がファーストチョイスなので、ユーザーがラベル上を明示要求していないのに AI がラベル上を作ったら是正する。
        // 厳密に「2行・各行1列・上が対応ラベル・下が入力」のネストGridだけを平坦化し、カード/タブ等は触らない。
        internal static void ConvertLabelAboveToLabelLeft(LayoutDesignBase? layout, List<FieldDesignBase> fields)
        {
            var byName = new Dictionary<string, FieldDesignBase>();
            foreach (var f in fields) byName[f.Name] = f;

            void TryFlatten(GridLayoutDesign g)
            {
                if (g.Rows.Count != 2) return;
                var r0 = g.Rows[0];
                var r1 = g.Rows[1];
                if (r0.Columns.Count != 1 || r1.Columns.Count != 1) return;
                if (r0.Columns[0].Layout is not FieldLayoutDesign lf0 || r1.Columns[0].Layout is not FieldLayoutDesign lf1) return;
                if (!byName.TryGetValue(lf0.FieldName, out var d0) || d0 is not LabelFieldDesign lbl) return;
                if (!byName.TryGetValue(lf1.FieldName, out var d1) || d1 is not DbValueFieldDesignBase) return;
                // ラベルが入力に対応している(RelativeField 一致 or 「<入力>Label」命名)。
                if (lbl.RelativeField != lf1.FieldName && lf0.FieldName != lf1.FieldName + "Label") return;

                var labelCol = r0.Columns[0];
                labelCol.Width = null; // 後段の NormalizeLabelLeftWidths が推定幅を設定する
                var inputCol = r1.Columns[0];
                var newRow = new GridRow();
                newRow.Columns.Add(labelCol);
                newRow.Columns.Add(inputCol);
                g.Rows.Clear();
                g.Rows.Add(newRow);
            }

            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case GridLayoutDesign grid:
                        TryFlatten(grid);
                        foreach (var row in grid.Rows)
                            foreach (var col in row.Columns) Walk(col.Layout);
                        break;
                    case TabLayoutDesign tab:
                        foreach (var t in tab.Layouts) Walk(t);
                        break;
                    case CanvasLayoutDesign canvas:
                        foreach (var e in canvas.Elements) Walk(e.Layout);
                        break;
                }
            }
            Walk(layout);
        }

        // ラベル左パターンのラベル列(入力と同じ行に並ぶ見出しラベルの列)の幅を、ラベル文字数から推定して統一する。
        // ラベル上(ラベルが入力と別の行)の列は対象外(幅を付けない)。構造は変えず Width だけ補正するので安全。
        // ユニットテストから直接検証するため internal。
        internal static void NormalizeLabelLeftWidths(LayoutDesignBase? layout, List<FieldDesignBase> fields)
        {
            var byName = new Dictionary<string, FieldDesignBase>();
            foreach (var f in fields) byName[f.Name] = f;

            bool IsLabelCol(GridColumn c) => c.Layout is FieldLayoutDesign f && !string.IsNullOrEmpty(f.FieldName)
                && byName.TryGetValue(f.FieldName, out var d) && d is LabelFieldDesign;
            bool IsInputCol(GridColumn c) => c.Layout is FieldLayoutDesign f && !string.IsNullOrEmpty(f.FieldName)
                && byName.TryGetValue(f.FieldName, out var d) && d is DbValueFieldDesignBase;

            // ラベル列を「ラベル左(入力と横並び)」と「それ以外(ラベル上/見出し)」に分類する。
            var labelLeftCols = new List<GridColumn>();
            var otherLabelCols = new List<GridColumn>();
            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case GridLayoutDesign grid:
                        foreach (var row in grid.Rows)
                        {
                            var hasInput = row.Columns.Any(IsInputCol);
                            foreach (var c in row.Columns.Where(IsLabelCol))
                                (hasInput ? labelLeftCols : otherLabelCols).Add(c);
                            foreach (var c in row.Columns) Walk(c.Layout);
                        }
                        break;
                    case TabLayoutDesign tab:
                        foreach (var t in tab.Layouts) Walk(t);
                        break;
                    case CanvasLayoutDesign canvas:
                        foreach (var e in canvas.Elements) Walk(e.Layout);
                        break;
                }
            }
            Walk(layout);

            // ラベル左でないラベル列(ラベル上のラベル行・見出し)に Width が付いていたら外す。
            // ラベル上では幅指定は不要で、AIが付けてしまうと縦置きなのに列幅が固定される不具合になる。
            foreach (var c in otherLabelCols) c.Width = null;

            if (labelLeftCols.Count == 0) return;

            // ラベルの表示文字を求める(Text 優先、無ければ RelativeField の対象フィールドの表示名)。
            string LabelText(GridColumn c)
            {
                var fieldName = ((FieldLayoutDesign)c.Layout!).FieldName;
                if (!byName.TryGetValue(fieldName, out var d) || d is not LabelFieldDesign lbl) return string.Empty;
                if (!string.IsNullOrEmpty(lbl.Text)) return lbl.Text;
                if (!string.IsNullOrEmpty(lbl.RelativeField)
                    && byName.TryGetValue(lbl.RelativeField, out var input) && input is IDisplayName dn
                    && !string.IsNullOrEmpty(dn.DisplayName))
                    return dn.DisplayName;
                return lbl.RelativeField ?? string.Empty;
            }

            var maxChars = labelLeftCols.Max(c => LabelText(c).Length);
            // 全角約18px + 余白40px、下限96px・上限240px。
            var estimate = Math.Clamp(maxChars * 18.0 + 40.0, 96.0, 240.0);
            // AI/ユーザーが付けた妥当(>=96)な幅があればそれも尊重し、大きい方に揃える。
            var existingMax = labelLeftCols.Where(c => c.Width is >= 96).Select(c => c.Width!.Value).DefaultIfEmpty(0).Max();
            var target = Math.Max(estimate, existingMax);
            foreach (var c in labelLeftCols) c.Width = target;
        }

        string BuildResultMessage(AIResponse response)
        {
            var messages = new List<string>
            {
                string.IsNullOrWhiteSpace(response.Explanation) ? "変更しました" : response.Explanation
            };
            if (!string.IsNullOrEmpty(_standardPartsNote)) messages.Add(_standardPartsNote);
            return string.Join("\r\n", messages);
        }

        // レイアウト仕様(クラス定義・プロパティ・推奨ルール・IsViewOnly)は Lib/AI の Layouts.md / LayoutGuidelines.md を
        // 埋め込みリソースとして読み込んで連結する。各レイアウト機能で共有(SystemPromptにハードコードしない)。
        static readonly string LayoutReference = EmbeddedDocs.Load(
            "Codeer.LowCode.Blazor.Designer.Standard.AIChat.Layouts.md",
            "Codeer.LowCode.Blazor.Designer.Standard.AIChat.LayoutGuidelines.md",
            "Codeer.LowCode.Blazor.Designer.Standard.AIChat.JsonAbstractTypeFullName.md");

        class AIResponse
        {
            public GridLayoutDesign Layout { get; set; } = new();

            // ユーザーへの説明。できたこと・できなかったこと(と理由・代替)を書く。できないことを成功扱いにしないため。
            public string Explanation { get; set; } = string.Empty;
        }

        // レイアウトのクラス定義・プロパティ・配置パターン・推奨ルール・IsViewOnly・Tab整合性・フィールド移動などは
        // 別途渡す「## レイアウト仕様」(Layouts.md / LayoutGuidelines.md)を参照。
        // ここには Detail レイアウトChat 固有の出力プロトコル(AIResponse の Layout / NewLabels 分離)のみを書く。
        const string SystemPrompt = @"
あなたはローコードでのDetail画面レイアウトのデザイナです。
ユーザーの指示に基づいて**既存フィールドの配置(レイアウト)だけ**を編集し、結果をJSONで返してください。
**あなたはフィールドを一切新規作成しません**(ラベル・ボタン等も含む)。配置できるのは渡された「フィールド一覧」にあるフィールドだけです(必要なフィールドは事前に作成されています)。
レイアウトのクラス定義・プロパティ・配置パターン・推奨ルール・IsViewOnly・Tab整合性・フィールド移動(重複禁止)などは、
別途渡される「## レイアウト仕様」に必ず従ってください。

## 基本ルール
- 元のレイアウトが渡されるので、ユーザーの指示に対して**必要最小限の変更**にしてください。
- 既存のプロパティ値は指示がない限り変更しないでください。
- **ただし「いい感じに/見やすく並べて」のような“全体を整える”依頼のときは、最小変更にこだわらず、既存レイアウト(空セルだけの初期状態や雑な並びを含む)を破棄して『おすすめ詳細レイアウトの作り方』レシピに沿って組み直してください。** ラベルを付け、関連項目をセクション化する。「最小変更」は具体的な個別編集(この行を移動・この列を広げる等)のときの原則です。
- **既定の並べ方は `全体配置-ラベル左`=1行に1項目(ラベル左＋入力右)をファーストチョイスにする**: 並べ方の指定が無く「いい感じに/見やすく並べて」だけのときは、横に複数項目を詰めず、1項目1行のラベル左で組む。
  - **ラベル左とは: ラベルと入力を「同じ行」に左右で置く**(同じ GridRow の中で、左の GridColumn にラベル、右の GridColumn に入力)。**ラベルを入力の「上の行」に置く縦置き(ラベル上)にしてはいけない。**
  - **ラベル上(縦置き・ネストGridでラベル行→入力行)にするのは、ユーザーが明示的に「ラベル上」「縦置き」「ラベルを上に」等と言ったときだけ。** 「いい感じに」「見やすく」だけのとき、また `全体配置-ラベル左` のときは、必ずラベル左(同じ行に左右)にする。迷ったらラベル左。
- **パターン名の指定に対応する**: パターン名は `やること-詳細1-詳細2` の階層命名(ハイフン区切り、左から解釈)。`全体配置-<ラベル左|ラベル上>[-<1行あたりの項目数>]` を指示されたら「## レイアウト仕様」の『詳細レイアウトのパターン』に従って組む。**末尾の数字＝1行あたりの項目数**(省略時は1。`全体配置-ラベル上-3`なら1行に3項目、ラベル上)。「カード分け」(枠線カードでセクション化)や自由形式の指示(この行を全幅に・罫線を引く・Notesを一番下に 等)にも従い、複数指定は組み合わせる。
- **既存ラベルを維持する(重要)**: レイアウト方式を切り替える依頼(縦↔横、ラベル左↔ラベル上、1項目1行↔複数列 等)は、配置の組み替えであってラベルの削除ではありません。**既にあるラベル(フィールド一覧の `XxxLabel` 等)は、新しい構造の中で必ず `FieldLayoutDesign.FieldName` で再配置**してください(ラベルを落として素並びに戻さない)。ユーザーがラベルの削除を明示的に求めた場合だけ外します。
- **BooleanField には見出しラベルを付けない**: `BooleanField`(チェックボックス/スイッチ/トグル)はフィールド自身が表示名を描画するため、別の見出しラベルは重複です。Boolean は1セルに単独で置き、ラベル左の2カラムにもラベル上のネストGridにもしないでください。組み直しのときも Boolean 用のラベルは作らない/置かない。
- **ラベル左のラベル列幅は文字数から推定する**: ラベル左のとき、ラベル列の `Width` は固定の決め打ちにせず、フォーム内で最も長い見出しラベルが折り返さず収まる幅にする。目安は `最長ラベルの文字数 × 18 + 40`(全角約18px)、**下限 96px(50px のような狭すぎは禁止)**、上限の目安 240px。ラベル列の幅はフォーム内で**全行同じ値に揃える**(左端を揃えるため)。
- **【最重要】システムフィールドの扱い**: `Id` / `CreatedAt` / `UpdatedAt` / `Creator` / `Updater` / `LogicalDelete` / `OptimisticLocking` の扱いは次の2点。判断は必ずこの順:
  1. **ユーザーがそのシステムフィールドの表示を明確に求めた場合は、求められたフィールドを必ず配置する(これが最優先)。** 「明確に求めた」= 指示文にそのフィールドの名前か表示名があり「表示して」「配置して」「並べて」「出して」等と言っているとき。例:「CreatedAt も表示して」「作成日時を出して」→ そのフィールドは必ず置く。求めていないことを理由に外さない。
  2. **上記で求められていないシステムフィールドは、詳細レイアウトに入れない。** 既存レイアウトに置かれていても組み直しのときに外す。「全部のフィールドを並べて」「いい感じに並べて」のような漠然とした指示は、システムフィールド表示の明示要求には**含めない**(=これらは置かない)。勝手に「作成日時・更新日時も並べる」をしない。
  - 要するに: **名指しで表示を頼まれたシステムフィールドだけ置き、それ以外のシステムフィールドは置かない。**
- Layout内のGridColumn.Layoutに配置できるのは FieldLayoutDesign / GridLayoutDesign / TabLayoutDesign / CanvasLayoutDesign の4種類のみです。**フィールド定義(SubmitButtonFieldDesign / TextFieldDesign 等の FieldDesignBase)を GridColumn.Layout に直接入れることは絶対に禁止**です(エラーになります)。フィールドは必ず FieldLayoutDesign の FieldName で参照します。
- FieldはFieldLayoutDesignの中でFieldNameで指定します。FieldNameは**渡されるフィールド一覧に実在するもの**だけを使います。
- **あなたはフィールドを新規作成しません(ラベル・ボタン等も含む)。** 一覧に無い名前を FieldName に書いてはいけません(セルが壊れ何も表示されません)。まだ無いフィールドを置く予定の場所は、**GridColumn の Layout を省略した空セル**にしてください。データフィールドの追加やプロパティ編集(最大長・必須等)もこの機能では行いません(それらは事前に別機能で作成・編集されています)。
- **戻るボタン・タイトル・サブミットボタン**: これらがフィールド一覧に**存在すれば**、ツールが自動で先頭行(戻る+タイトル)・末尾行(サブミット)へ慣習的に配置し直します。あなたは Layout に含めても含めなくても構いません(ツールが正規化)。**一覧に無いものは作らない**でください。
- **行(GridRow)には必ず1つ以上の列(GridColumn)を入れてください。** 空の行(Columns が空配列)は出力しないでください。

## 出力JSON形式（このチャット固有）

{
  ""Layout"": { /* GridLayoutDesign - ルートレイアウト全体。全フィールドはFieldLayoutDesignでFieldName参照 */ },
  ""Explanation"": ""ユーザーへの説明（何をしたか。できなかったことがあればそれと理由も書く）""
}

- Layout 内にフィールド定義(LabelFieldDesign 等)を直接置いてはいけません。**必ず FieldLayoutDesign の FieldName で既存フィールドを参照**します。
- **見出しラベルは既存のものを FieldName で参照**します(新規作成しない)。入力に付ける見出しは、フィールド一覧にある対応ラベル(`XxxLabel` 等、Text 空+RelativeField 設定)を参照してください。対応ラベルが無い入力はラベル無しで置きます。
- **ラベルを上に置く(縦置き)依頼のとき**: 「ラベル仕様」の『ラベルを上に配置する場合』の骨組みに従い、各項目を**ネストした GridLayoutDesign(上=ラベル行・下=入力行)**にします。ラベル行には、その入力用の**既存の見出しラベル**を FieldName で参照して置きます(入力フィールド自身を上の行に二重に置かない)。
";
    }
}
