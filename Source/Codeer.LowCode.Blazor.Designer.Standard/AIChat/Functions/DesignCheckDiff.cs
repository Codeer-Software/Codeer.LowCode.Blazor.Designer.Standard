using System.Collections.Generic;
using System.Linq;
using Codeer.LowCode.Blazor.DesignLogic.Check;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // デザインチェックの「ベースライン差分」共通ロジック。
    // AI 編集の可否には「AI が新たに増やしたエラーだけ」を使う(元からあった/ユーザー編集中のエラーは巻き添えにしない)。
    // モジュール編集(ModuleEditFunctionBase)・レイアウト編集(各 LayoutFunction)で共通に使う。
    static class DesignCheckDiff
    {
        // 同一エラー判定用シグネチャ(位置 + メッセージ)。
        public static string Signature(DesignCheckInfo info) => info.GetPositionText() + "" + info.Message;

        // after のうち baseline に無い(= 新たに増えた)エラーだけを返す。
        public static List<DesignCheckInfo> NewErrors(
            IEnumerable<DesignCheckInfo> baseline, IEnumerable<DesignCheckInfo> after)
        {
            var known = new HashSet<string>(baseline.Select(Signature));
            return after.Where(e => !known.Contains(Signature(e))).ToList();
        }
    }
}
