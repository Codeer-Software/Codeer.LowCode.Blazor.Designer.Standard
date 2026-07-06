using System.Threading.Tasks;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;

namespace Designer.WpfApp.Test
{
    // 機能(IAiFunction)を旧 Chat と同じ感覚でテストから呼ぶための薄いシム。
    // 旧テストは chat.ProcessMessage(msg) を呼んでいたので、機能に対しても同名で呼べるようにして移行の差分を最小化する。
    // ルーターを介さず 1 機能を直接叩く(=その機能のプロンプト品質を単体で検証する)。
    internal static class TestFunctionExtensions
    {
        public static async Task<string> ProcessMessage(this IAiFunction fn, string message)
            => (await fn.ExecuteAsync(message)).Message;
    }
}
