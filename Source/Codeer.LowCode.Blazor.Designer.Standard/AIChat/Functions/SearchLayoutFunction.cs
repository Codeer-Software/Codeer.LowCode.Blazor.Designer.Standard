using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Microsoft.Extensions.AI;
using System.IO;
using System.Text;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // 検索レイアウト編集機能。旧 SearchLayoutChat のロジックをそのまま機能ユニットへ移したもの。
    //
    // 検索レイアウトは「詳細レイアウトとほぼ同じフィールド配置(ラベル・複数列・幅・ラベル左/上)」に加えて、
    // 検索固有の And/Or ネスト(SearchGridLayoutDesign.Operator を持つグループの入れ子)を表現できる。
    // 例: 担当者名 && (顧客コード || 顧客名)
    // この And/Or ネストが最も精度を落とす箇所なので、1つの巨大プロンプトで全部やらせず 2 段階に分けて実行する:
    //
    //   Stage1: 条件の論理構造(And/Or グループのネストと、どのフィールドがどのグループに属するか)だけを決める専用プロンプト。
    //           → 条件ツリー(ConditionNode)を得る → コードでネストした SearchGridLayoutDesign の「骨組み」を構築する。
    //           構造はコードが握るので、ここで決まったネスト/Operator は確実に反映される。
    //   Stage2: その骨組みの上で、各グループ内のフィールド配置(見出しラベル・複数列・幅・ラベル左/上)を
    //           詳細レイアウトとほぼ同じ要領で整えるプロンプト。レイアウト仕様 Docs(Layouts.md / LayoutGuidelines.md)を流用する。
    //
    // Stage2 が Stage1 の構造(グループのネスト・Operator・各グループの所属フィールド)を壊した場合は、
    // 骨組みをコードで整えたもの(既定のラベル左配置)にフォールバックし、構造は絶対に失わない。
    internal class SearchLayoutFunction : IAiFunction
    {
        readonly IModuleSearchLayoutEditor _editor;
        readonly IChatClient _chatClient;

        // Stage2(配置整え)の会話履歴。Stage1(条件構造)は毎回現在の状態から組み直すため履歴を持たない。
        readonly List<ChatMessage> _messages = new();

        // 直近適用で追加した見出しラベル・警告など(結果メッセージ用)。
        string _resultNote = string.Empty;

        public string Id => FunctionCatalog.LayoutSearch;
        public string DisplayName => FunctionCatalog.Entries[FunctionCatalog.LayoutSearch].DisplayName;
        public string RouterDescription => FunctionCatalog.Entries[FunctionCatalog.LayoutSearch].RouterDescription;

        public SearchLayoutFunction(Func<IChatClient> createChatClient, IModuleSearchLayoutEditor editor)
        {
            _editor = editor;
            _chatClient = createChatClient();
        }

        public void Clear() => _messages.Clear();

        public async Task<FunctionResult> ExecuteAsync(string instruction)
        {
            _resultNote = string.Empty;

            var search = (_editor.GetModuleDesign().SearchLayouts.GetValueOrDefault(_editor.GetLayoutName()) ?? new());
            if (search.Layout is not SearchGridLayoutDesign existingRoot)
                return FunctionResult.NothingToDo("レイアウトデータが不正です（SearchGridLayoutDesignが必要です）");

            var fields = _editor.GetModuleDesign().Fields;

            // ── Stage1: 条件の論理構造(And/Or ネスト)を決める ───────────────────────────────
            var (root, canDo, stage1Explanation) = await AnalyzeConditionStructureAsync(existingRoot, fields, instruction);
            if (!canDo)
                return FunctionResult.NothingToDo(string.IsNullOrWhiteSpace(stage1Explanation) ? "検索レイアウトは変更していません。" : stage1Explanation);
            if (root == null)
                return FunctionResult.NothingToDo("検索条件の構造を解釈できませんでした。もう一度、絞り込みたい項目と AND/OR の関係を指示してください。");

            // ツリー → ネストした SearchGridLayoutDesign の骨組み(構造はここで確定)。
            var skeleton = BuildSkeleton(root, existingRoot);

            // ── Stage2: 骨組みの上でフィールド配置を整える(詳細レイアウトとほぼ同じ) ───────────
            var arranged = await ArrangeLayoutAsync(skeleton, fields, instruction);

            // Stage2 が使えない/構造を壊した場合は、骨組みをコードで整えた既定配置にフォールバックする。
            if (arranged == null)
            {
                ApplyDefaultLabelsLeft(skeleton, fields);
                RestoreRootSettings(skeleton, existingRoot);
                search.Layout = skeleton;
                _editor.Update();
                var note = "検索条件の構造を更新し、既定の並べ方で配置しました。";
                return FunctionResult.Done(string.IsNullOrWhiteSpace(_resultNote) ? note : note + "\r\n" + _resultNote);
            }

            RestoreRootSettings(arranged, existingRoot);
            search.Layout = arranged;
            _editor.Update();

            var messages = new List<string> { "検索レイアウトを変更しました" };
            if (!string.IsNullOrEmpty(_resultNote)) messages.Add(_resultNote);
            return FunctionResult.Done(string.Join("\r\n", messages));
        }

        // ===================================================================================
        // Stage1: 条件の論理構造(And/Or ネスト)を決める専用プロンプト
        // ===================================================================================

        class ConditionNode
        {
            // 葉: 検索フィールド名(既存フィールドの Name)。グループのときは null。
            public string? Field { get; set; }

            // グループ: And / Or / UserSpecified。葉のときは null。
            public string? Operator { get; set; }

            // グループの子(葉 または さらにネストしたグループ)。
            public List<ConditionNode>? Children { get; set; }
        }

        class ConditionResponse
        {
            public ConditionNode? Root { get; set; }

            // レイアウト編集として実行できるか。検索と無関係な依頼・新規フィールド追加が必要な依頼などは false。
            public bool CanDo { get; set; } = true;

            // ユーザーへの説明。CanDo=false のときは理由と対処を書く。
            public string Explanation { get; set; } = string.Empty;
        }

        async Task<(ConditionNode? root, bool canDo, string explanation)> AnalyzeConditionStructureAsync(
            SearchGridLayoutDesign existingRoot, List<FieldDesignBase> fields, string message)
        {
            var searchableFieldNames = fields
                .Where(f => f is DbValueFieldDesignBase)
                .Select(f => f.Name)
                .ToHashSet(StringComparer.Ordinal);

            var fieldInfo = fields
                .Where(f => f is DbValueFieldDesignBase)
                .Select(f => $"  {f.Name} ({f.GetType().Name})")
                .ToList();

            var currentStructure = DescribeStructure(existingRoot);

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, ConditionSystemPrompt),
                new ChatMessage(ChatRole.User, 
                    $"検索に使えるフィールド一覧:\n{string.Join("\n", fieldInfo)}\n\n"
                    + $"現在の検索条件の構造:\n{currentStructure}\n\n"
                    + $"指示: {message}")
            };

            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ConditionResponse response;
                string resultText;
                try
                {
                    var result = await _chatClient.GetResponseAsync(messages,
                        new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
                    resultText = result.Text;
                    response = JsonConverterEx.DeserializeObject<ConditionResponse>(resultText)!;
                }
                catch (Exception ex)
                {
                    return (null, false, $"エラーリトライしてください\r\n{ex.Message}");
                }

                messages.Add(new ChatMessage(ChatRole.Assistant, resultText));

                if (!response.CanDo)
                    return (null, false, response.Explanation);
                if (response.Root == null)
                    return (null, false, response.Explanation);

                // ツリーが参照するフィールドがすべて実在するか検証(存在しない参照は再生成へ)。
                var unknown = CollectConditionFields(response.Root)
                    .Where(n => !searchableFieldNames.Contains(n))
                    .Distinct()
                    .ToList();
                if (unknown.Count > 0)
                {
                    if (attempt < maxAttempts)
                    {
                        messages.Add(new ChatMessage(ChatRole.User, 
                            $"存在しない、または検索に使えないフィールドを参照しています: {string.Join(", ", unknown)}。" +
                            "Field には上の『検索に使えるフィールド一覧』にある Name だけを使い、条件ツリーを再度出力してください。"));
                        continue;
                    }
                    return (null, false,
                        $"存在しないフィールド({string.Join(", ", unknown)})が指定されたため、変更していません。" +
                        "検索に使うフィールドは先に『全体設定』で追加してください。");
                }

                return (response.Root, true, response.Explanation);
            }

            return (null, false, "検索条件の構造を決められませんでした。もう一度指示してください。");
        }

        // 現在の SearchGridLayoutDesign を And/Or ネストの論理式として要約する(Stage1 が現状を保てるように渡す)。
        static string DescribeStructure(LayoutDesignBase? layout)
        {
            var logic = DescribeLogic(layout);
            return string.IsNullOrEmpty(logic) ? "(未設定)" : logic;
        }

        // レイアウトを "op(FieldA, op(FieldB, FieldC))" 形式の論理式文字列にする。
        static string DescribeLogic(LayoutDesignBase? node)
        {
            switch (node)
            {
                case SearchGridLayoutDesign grid:
                {
                    var parts = new List<string>();
                    foreach (var row in grid.Rows)
                        foreach (var col in row.Columns)
                        {
                            var s = DescribeLogic(col.Layout);
                            if (!string.IsNullOrEmpty(s)) parts.Add(s);
                        }
                    if (parts.Count == 0) return string.Empty;
                    return $"{grid.Operator}({string.Join(", ", parts)})";
                }
                case GridLayoutDesign grid:
                {
                    // 検索フィールドを囲むだけの通常 Grid(ラベル上ブロック等)は論理構造には影響しないので中の
                    // フィールド名だけを拾う。
                    var parts = new List<string>();
                    foreach (var row in grid.Rows)
                        foreach (var col in row.Columns)
                        {
                            var s = DescribeLogic(col.Layout);
                            if (!string.IsNullOrEmpty(s)) parts.Add(s);
                        }
                    return string.Join(", ", parts);
                }
                case FieldLayoutDesign f:
                    return string.IsNullOrEmpty(f.FieldName) ? string.Empty : f.FieldName;
                default:
                    return string.Empty;
            }
        }

        static List<string> CollectConditionFields(ConditionNode node)
        {
            var list = new List<string>();
            void Walk(ConditionNode n)
            {
                if (!string.IsNullOrEmpty(n.Field)) list.Add(n.Field!);
                foreach (var c in n.Children ?? new()) Walk(c);
            }
            Walk(node);
            return list;
        }

        // ===================================================================================
        // ツリー → ネストした SearchGridLayoutDesign の骨組み(構造はここで確定)
        // ===================================================================================

        SearchGridLayoutDesign BuildSkeleton(ConditionNode root, SearchGridLayoutDesign existingRoot)
        {
            var rootGrid = new SearchGridLayoutDesign
            {
                Name = existingRoot.Name,
                Operator = ParseOperator(root.Operator, SearchOperator.And),
            };
            RestoreRootSettings(rootGrid, existingRoot);

            // ルートが単一フィールド(葉)だけのときは And グループにその1件を入れる。
            var children = root.Children ?? new();
            if (children.Count == 0 && !string.IsNullOrEmpty(root.Field))
                children = new List<ConditionNode> { new() { Field = root.Field } };

            foreach (var child in children)
                rootGrid.Rows.Add(BuildRow(child));

            if (rootGrid.Rows.Count == 0)
                rootGrid.Rows.Add(GridRow.CreateEmptyRow());

            return rootGrid;
        }

        GridRow BuildRow(ConditionNode node)
        {
            var row = new GridRow();
            if (!string.IsNullOrEmpty(node.Field))
            {
                row.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign(node.Field!) });
                return row;
            }

            // グループ: ネストした SearchGridLayoutDesign(カード表示のため IsBordered=true)。
            var group = new SearchGridLayoutDesign
            {
                Operator = ParseOperator(node.Operator, SearchOperator.And),
                IsBordered = true,
            };
            foreach (var child in node.Children ?? new())
                group.Rows.Add(BuildRow(child));
            if (group.Rows.Count == 0)
                group.Rows.Add(GridRow.CreateEmptyRow());

            row.Columns.Add(new GridColumn { Layout = group });
            return row;
        }

        static SearchOperator ParseOperator(string? value, SearchOperator fallback)
            => Enum.TryParse<SearchOperator>(value, true, out var op) ? op : fallback;

        // ルートの折りたたみ・枠線・余白等の設定は構造編集で失わない(Stage2 が落としても復元する)。
        static void RestoreRootSettings(SearchGridLayoutDesign target, SearchGridLayoutDesign source)
        {
            target.IsBordered = source.IsBordered;
            target.IsExpandable = source.IsExpandable;
            target.ExpanderLabel = source.ExpanderLabel;
            target.IsExpanderDefaultOpened = source.IsExpanderDefaultOpened;
            target.Padding = source.Padding;
            target.BackgroundColor = source.BackgroundColor;
        }

        // ===================================================================================
        // Stage2: 骨組みの上でフィールド配置を整える(詳細レイアウトとほぼ同じ)
        // ===================================================================================

        class ArrangeResponse
        {
            public SearchGridLayoutDesign Layout { get; set; } = new();
            public List<LabelFieldDesign> NewLabels { get; set; } = new();
            public string Explanation { get; set; } = string.Empty;
        }

        async Task<SearchGridLayoutDesign?> ArrangeLayoutAsync(
            SearchGridLayoutDesign skeleton, List<FieldDesignBase> fields, string message)
        {
            // 骨組みの「構造の指紋」(Operator の並び・葉フィールドの集合)。Stage2 がこれを壊したら不採用。
            var skeletonOps = CollectOperators(skeleton);
            var skeletonLeaves = CollectFieldRefs(skeleton).ToHashSet(StringComparer.Ordinal);
            var originalNonLabel = fields.Where(f => f is not LabelFieldDesign).Select(f => f.Name)
                .ToHashSet(StringComparer.Ordinal);

            // ベースライン: 候補適用前に既にあるデザインチェックエラー。結果評価はここからの差分(新規エラー)だけを見る。
            var baseline = SafeCheckModule();

            if (_messages.Count == 0)
            {
                _messages.Add(new ChatMessage(ChatRole.System, ArrangeSystemPrompt));
                if (!string.IsNullOrEmpty(LayoutReference))
                    _messages.Add(new ChatMessage(ChatRole.System, 
                        "## レイアウト仕様（クラス定義・プロパティ・推奨ルール・IsViewOnly）\n\n" + LayoutReference));
            }

            var fieldInfo = fields.Select(f => $"  {f.Name} ({f.GetType().Name})").ToList();
            _messages.Add(new ChatMessage(ChatRole.User, 
                $"検索に使えるフィールド一覧:\n{string.Join("\n", fieldInfo)}\n\n"
                + "次の『骨組みレイアウト』は AND/OR の論理構造が確定済みです。"
                + "グループ(SearchGridLayoutDesign)のネスト・各グループの Operator・各グループに属する検索フィールドの割り当ては変更しないでください。"
                + "各グループの中で、フィールドの見出しラベル付け・1行あたりの項目数・幅・ラベル左/上 の見た目だけを整えてください。\n\n"
                + $"骨組みレイアウト:\n{JsonConverterEx.SerializeObject(skeleton)}\n\n指示: {message}"));

            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ArrangeResponse response;
                string resultText;
                try
                {
                    var result = await _chatClient.GetResponseAsync(_messages,
                        new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
                    resultText = result.Text;
                    response = JsonConverterEx.DeserializeObject<ArrangeResponse>(resultText)!;
                }
                catch
                {
                    // Stage2 が失敗したら骨組みフォールバック(構造は Stage1 で確定済みなので失わない)。
                    if (_messages.Count > 0 && _messages[^1].Role == ChatRole.User)
                        _messages.RemoveAt(_messages.Count - 1);
                    return null;
                }

                _messages.Add(new ChatMessage(ChatRole.Assistant, resultText));

                // 未知プロパティ検証(値が黙って捨てられるのを防ぐ)。
                var unmappedError = AiJsonValidation.GetUnmappedMemberError<ArrangeResponse>(resultText);
                if (unmappedError != null)
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            "生成されたJSONに定義に存在しないプロパティが含まれています。正しいプロパティ名・構造で全体を再度出力してください。\n" + unmappedError));
                        continue;
                    }
                    return null;
                }

                // 配置変更が不要(構造だけ変えた)なら空を返してよい → 骨組みの既定配置にフォールバック。
                if (response.Layout == null || response.Layout.Rows.Count == 0)
                    return null;

                var candidate = response.Layout;

                // 既知フィールド名(実在フィールド + 新規ラベル)。
                var knownFieldNames = new HashSet<string>(fields.Select(f => f.Name), StringComparer.Ordinal);
                foreach (var label in response.NewLabels ?? new())
                    if (!string.IsNullOrEmpty(label.Name)) knownFieldNames.Add(label.Name);

                if (HasEmptyRow(candidate))
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            "列(GridColumn)が1つも無い行(GridRow)があります。すべての行に最低1つの列を入れて全体を再度出力してください。"));
                        continue;
                    }
                    return null;
                }

                var unknownRefs = CollectFieldRefs(candidate).Where(n => !knownFieldNames.Contains(n)).Distinct().ToList();
                if (unknownRefs.Count > 0)
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            $"存在しないフィールドを参照する FieldLayoutDesign があります: {string.Join(", ", unknownRefs)}。" +
                            "FieldName には実在フィールド、または NewLabels で追加する見出しラベルの Name だけを指定して全体を再度出力してください。"));
                        continue;
                    }
                    return null;
                }

                var duplicateRefs = FindDuplicateFieldRefs(candidate);
                if (duplicateRefs.Count > 0)
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            $"同じフィールドが複数箇所に配置されています: {string.Join(", ", duplicateRefs)}。1フィールドは1箇所だけにして全体を再度出力してください。"));
                        continue;
                    }
                    return null;
                }

                // 構造の指紋チェック: Stage1 で確定した And/Or 構造・所属フィールドが保たれているか。
                var candidateOps = CollectOperators(candidate);
                var candidateLeaves = CollectFieldRefs(candidate).ToHashSet(StringComparer.Ordinal);
                var missing = skeletonLeaves.Where(n => !candidateLeaves.Contains(n)).ToList();
                var extraSearchFields = candidateLeaves
                    .Where(n => originalNonLabel.Contains(n) && !skeletonLeaves.Contains(n))
                    .ToList();
                var structureOk = candidateOps.SequenceEqual(skeletonOps)
                    && missing.Count == 0 && extraSearchFields.Count == 0;
                if (!structureOk)
                {
                    if (attempt < maxAttempts)
                    {
                        _messages.Add(new ChatMessage(ChatRole.User, 
                            "AND/OR の論理構造を変えてしまっています。骨組みレイアウトのグループ(SearchGridLayoutDesign)のネスト・各グループの Operator・" +
                            "各グループに属する検索フィールドの割り当ては変更しないでください(見た目の配置だけ整える)。骨組みの構造を保ったまま全体を再度出力してください。"));
                        continue;
                    }
                    return null;
                }

                // 適用候補を仕上げてデザインチェック。
                var addedLabels = AddNewLabels(fields, response.NewLabels);
                DetailLayoutFunction.NormalizeLabelLeftWidths(candidate, fields);

                var errors = ValidateLayout(candidate, baseline);
                if (errors.Count == 0)
                {
                    if (addedLabels.Count > 0)
                        _resultNote = $"見出しラベルを追加しました: {string.Join(", ", addedLabels)}";
                    if (!string.IsNullOrWhiteSpace(response.Explanation))
                        _resultNote = string.IsNullOrEmpty(_resultNote)
                            ? response.Explanation
                            : response.Explanation + "\r\n" + _resultNote;
                    return candidate;
                }

                if (attempt < maxAttempts)
                {
                    // 追加したラベルは巻き戻さない(再生成でまた同名参照するため既知のまま)。
                    _messages.Add(new ChatMessage(ChatRole.User, 
                        "生成されたレイアウトに次のデザインチェックエラーがあります。骨組みの AND/OR 構造は保ったまま修正して全体を再度出力してください。\n"
                        + FormatErrors(errors)));
                    continue;
                }

                return null;
            }

            return null;
        }

        List<string> AddNewLabels(List<FieldDesignBase> fields, List<LabelFieldDesign>? newLabels)
        {
            var added = new List<string>();
            if (newLabels == null) return added;
            var existingNames = new HashSet<string>(fields.Select(f => f.Name), StringComparer.Ordinal);
            foreach (var label in newLabels)
            {
                if (!string.IsNullOrEmpty(label.Name) && !existingNames.Contains(label.Name))
                {
                    fields.Add(label);
                    existingNames.Add(label.Name);
                    added.Add(label.Name);
                }
            }
            return added;
        }

        // ===================================================================================
        // フォールバック: 骨組みをコードで既定のラベル左配置に整える
        // ===================================================================================

        // 骨組みの各葉フィールド(単独セルの FieldLayoutDesign)を「ラベル左(左:見出し / 右:入力)」の行に整える。
        // BooleanField は自身が表示名を描画するので見出しは付けない。整えたあと NormalizeLabelLeftWidths で幅を揃える。
        void ApplyDefaultLabelsLeft(SearchGridLayoutDesign root, List<FieldDesignBase> fields)
        {
            var byName = fields.ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);
            var existingNames = new HashSet<string>(fields.Select(f => f.Name), StringComparer.Ordinal);
            var added = new List<string>();

            string Unique(string baseName)
            {
                if (!existingNames.Contains(baseName)) return baseName;
                var i = 2;
                while (existingNames.Contains(baseName + i)) i++;
                return baseName + i;
            }

            void Walk(GridLayoutDesign grid)
            {
                foreach (var row in grid.Rows)
                {
                    // 反復中にコレクションを変更しないよう、新しい列リストを組み立てて置き換える。
                    var newColumns = new List<GridColumn>();
                    foreach (var col in row.Columns)
                    {
                        if (col.Layout is GridLayoutDesign nested)  // SearchGridLayoutDesign も含む(GridLayoutDesign 派生)
                        {
                            Walk(nested);
                            newColumns.Add(col);
                            continue;
                        }

                        if (col.Layout is FieldLayoutDesign field
                            && !string.IsNullOrEmpty(field.FieldName)
                            && byName.TryGetValue(field.FieldName, out var d)
                            && d is DbValueFieldDesignBase and not BooleanFieldDesign)
                        {
                            // ラベル(Text:"" + RelativeField で表示名に追従)を作り、ラベル左の2列(見出し|入力)に組み替える。
                            var label = new LabelFieldDesign
                            {
                                Name = Unique(field.FieldName + "Label"),
                                Text = "",
                                RelativeField = field.FieldName,
                            };
                            fields.Add(label);
                            existingNames.Add(label.Name);
                            byName[label.Name] = label;
                            added.Add(label.Name);

                            newColumns.Add(new GridColumn { Layout = new FieldLayoutDesign(label.Name) });
                            newColumns.Add(new GridColumn { Layout = field });
                            continue;
                        }

                        newColumns.Add(col);
                    }
                    row.Columns.Clear();
                    row.Columns.AddRange(newColumns);
                }
            }

            Walk(root);
            DetailLayoutFunction.NormalizeLabelLeftWidths(root, fields);
            if (added.Count > 0)
                _resultNote = string.IsNullOrEmpty(_resultNote)
                    ? $"見出しラベルを追加しました: {string.Join(", ", added)}"
                    : _resultNote + $"\r\n見出しラベルを追加しました: {string.Join(", ", added)}";
        }

        // ===================================================================================
        // 共通ヘルパ(レイアウト走査)
        // ===================================================================================

        // SearchGridLayoutDesign の Operator を pre-order で列挙する(構造の指紋)。
        static List<SearchOperator> CollectOperators(LayoutDesignBase? layout)
        {
            var ops = new List<SearchOperator>();
            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case SearchGridLayoutDesign sg:
                        ops.Add(sg.Operator);
                        foreach (var row in sg.Rows)
                            foreach (var col in row.Columns) Walk(col.Layout);
                        break;
                    case GridLayoutDesign g:
                        foreach (var row in g.Rows)
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
            return ops;
        }

        // 配置されている FieldLayoutDesign.FieldName をすべて列挙する。
        static List<string> CollectFieldRefs(LayoutDesignBase? layout)
        {
            var names = new List<string>();
            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case FieldLayoutDesign f:
                        if (!string.IsNullOrEmpty(f.FieldName)) names.Add(f.FieldName);
                        break;
                    case GridLayoutDesign g:
                        foreach (var row in g.Rows)
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
            return names;
        }

        static List<string> FindDuplicateFieldRefs(LayoutDesignBase? layout)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var n in CollectFieldRefs(layout))
                counts[n] = counts.GetValueOrDefault(n) + 1;
            return counts.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToList();
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

        // 候補レイアウトをモジュールへ一時的に差し込んで CheckModule(レイアウト検証込み)し、
        // ベースラインからの新規エラーだけを返す。検証後は必ず元へ戻す(まだ適用段階ではないため)。
        List<DesignCheckInfo> ValidateLayout(SearchGridLayoutDesign layout, List<DesignCheckInfo> baseline)
        {
            try
            {
                var module = _editor.GetModuleDesign();
                var name = _editor.GetLayoutName();
                var target = module.SearchLayouts.GetValueOrDefault(name);
                var existed = target != null;
                target ??= new SearchLayoutDesign();
                var savedLayout = target.Layout;
                try
                {
                    target.Layout = layout;
                    if (!existed) module.SearchLayouts[name] = target;
                    return DesignCheckDiff.NewErrors(baseline, SafeCheckModule());
                }
                finally
                {
                    target.Layout = savedLayout;
                    if (!existed) module.SearchLayouts.Remove(name);
                }
            }
            catch
            {
                return new();
            }
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

        // ===================================================================================
        // レイアウト仕様 Docs(詳細レイアウトと共有)
        // ===================================================================================

        static readonly string LayoutReference = EmbeddedDocs.Spec("Layouts", "JsonAbstractTypeFullName")
            + EmbeddedDocs.Guideline("LayoutGuidelines.md");

        // Stage1 専用プロンプト: And/Or の論理構造だけを決める。見た目・ラベル・行列は一切考えない。
        const string ConditionSystemPrompt = @"
あなたはローコードの検索(Search)画面の「検索条件の論理構造」を設計するアシスタントです。
ユーザーの指示から、どのフィールドで絞り込むか、そしてそれらを AND / OR でどうグループ化(ネスト)するかだけを決め、
条件ツリーをJSONで返してください。**見た目・並び順・ラベル・行や列などのレイアウトは一切決めません(別の工程が行います)。**

## 検索条件のAND/ORネストについて
検索条件は AND と OR を入れ子(ネスト)で組み合わせられます。例:
  担当者名 && (顧客コード || 顧客名)
これは「担当者名 で絞り、かつ (顧客コード または 顧客名) で絞る」という意味です。
これを条件ツリーで表すと:
{
  ""Operator"": ""And"",
  ""Children"": [
    { ""Field"": ""担当者名に対応するフィールドName"" },
    { ""Operator"": ""Or"", ""Children"": [ { ""Field"": ""顧客コードのName"" }, { ""Field"": ""顧客名のName"" } ] }
  ]
}

## ルール
- ツリーのノードは2種類だけ:
  - **葉(フィールド)**: `{ ""Field"": ""<フィールドのName>"" }`
  - **グループ**: `{ ""Operator"": ""And""|""Or""|""UserSpecified"", ""Children"": [ ... ] }`
- **Field には『検索に使えるフィールド一覧』にある Name だけ**を使う(表示名ではなく Name)。一覧に無いフィールドは使わない。
- ユーザーが「または/or/どちらか」等と言った条件は `Or` グループ、「かつ/and/すべて」等は `And` グループにする。
- ユーザーが AND/OR を画面上で切り替えられるようにしたいと言ったときだけ `UserSpecified` を使う。
- **単純な検索(全部 AND)**のときは、ルートを `And` グループにして、全フィールドを葉として並べるだけでよい(ネスト不要)。
- ルートは必ずグループ(Operator と Children を持つ)にする。既定の Operator は `And`。
- **論理構造を変える指示でないとき(例: 見た目・並べ方だけの指示、ラベルの指示)は、現在の構造をそのまま維持**し、
  『現在の検索条件の構造』と同じツリーを返す(フィールドの増減・AND/OR の変更をしない)。
- 検索レイアウトと無関係な依頼や、まだ存在しないフィールドの新規追加が必要な依頼は `CanDo=false` にして、
  `Explanation` に理由と対処(『全体設定』でフィールドを追加する等)を簡潔に書く。

## 出力JSON形式
{
  ""Root"": { /* ルートの条件グループ(ConditionNode) */ },
  ""CanDo"": true,
  ""Explanation"": ""何をしたか。CanDo=false のときは理由と対処。""
}
";

        // Stage2 専用プロンプト: 骨組み(構造確定済み)の上でフィールド配置を整える。詳細レイアウトとほぼ同じ。
        const string ArrangeSystemPrompt = @"
あなたはローコードの検索(Search)画面レイアウトのデザイナです。
**AND/OR の論理構造が確定済みの『骨組みレイアウト』が渡されます。**その上で、各グループ内のフィールドの見た目(見出しラベル・
1行あたりの項目数・幅・ラベル左/上)を整え、結果を JSON で返してください。
レイアウトのクラス定義・プロパティ・配置パターン・推奨ルール・IsViewOnly などは、別途渡される「## レイアウト仕様」に必ず従ってください。

## 最重要: 論理構造を変えない
- 骨組みの **グループ(SearchGridLayoutDesign)のネスト構造・各グループの Operator(And/Or/UserSpecified)・各グループに属する検索フィールドの割り当ては、絶対に変更しない**でください。
- あなたがしてよいのは、**各グループの内側での見た目の整え**だけです: 見出しラベルを付ける、1行に複数項目を並べる、幅を付ける、ラベルを左/上にする。
- 検索フィールドを別のグループへ移したり、グループを増減したり、Operator を変えたりしないでください。
- ネストしたグループ(SearchGridLayoutDesign)はそのまま、その GridColumn.Layout に置いたままにします。

## 配置ルール(詳細レイアウトと同じ)
- **既定はラベル左**: ラベルと入力を同じ行に左右で置く(左の GridColumn に見出しラベル、右の GridColumn に入力)。ユーザーが「ラベル上/縦」と明示したときだけラベル上(ネストGrid: ラベル行→入力行)にする。
- **見出しラベルは NewLabels で追加**し、Layout 内では FieldLayoutDesign の FieldName でその Name を参照する。LabelFieldDesign を Layout に直接置かない。
- **新規ラベルは Text を空文字 """" にし、RelativeField に対象の入力フィールド名を設定**する(対象の表示名に自動追従する)。独立した見出しのときだけ Text に文字を入れてよい。
- **BooleanField には見出しラベルを付けない**(自身が表示名を描画するため)。単独セルに置く。
- ラベル左のラベル列 Width は付けても付けなくてもよい(ツールが文字数から推定して揃えます)。
- Layout 内の GridColumn.Layout に置けるのは FieldLayoutDesign / GridLayoutDesign / SearchGridLayoutDesign の3種類。フィールド定義そのものを直接置かない(必ず FieldLayoutDesign の FieldName で参照)。
- **このチャットで新規追加できるのは見出しラベルだけ**。検索フィールドそのものは追加しない(骨組みにあるものだけを配置する)。
- 行(GridRow)には必ず1つ以上の列(GridColumn)を入れる。空の行は出力しない。

## 出力JSON形式
{
  ""Layout"": { /* SearchGridLayoutDesign - ルートレイアウト全体。骨組みの構造を保ったまま各グループ内を整えたもの。TypeFullName も維持する */ },
  ""NewLabels"": [ /* 新規追加する見出しLabelFieldDesignの配列。無ければ空配列[] */ ],
  ""Explanation"": ""何をしたか""
}

見た目の変更が不要なとき(骨組みのままでよいとき)は、Layout を空(Rows 無し)にして返してください(ツールが既定の並べ方で配置します)。
";
    }
}
