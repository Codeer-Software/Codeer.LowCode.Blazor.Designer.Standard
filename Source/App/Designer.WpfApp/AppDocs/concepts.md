# このアプリ構成の考え方

## 構成

Blazor WebAssembly クライアント + ASP.NET Core サーバーの標準構成。認証は組み込まない
(認証が必要な案件は Cookie / AAD バリアントのテンプレートから作る)。

## このアプリが追加しているスクリプトサービス

WebApp.Client.Shared 由来のサービス・型 (Excel / WebApi / Toaster / Mail / Loading) が
スクリプトから利用できる。正確なシグネチャと使用例は `_script_catalog.md` を参照
(概念だけここに書く: 帳票は Excel テンプレート方式、外部連携は WebApi サービス経由、
通知はトースト。これらの選定を変えたい場合はアプリ側の登録を差し替える)。

## AI 機能の前提

デザイナの AI チャットは環境変数 `AZURE_OPENAI_API_ENDPOINT` / `AZURE_OPENAI_API_KEY` /
`AZURE_OPENAI_API_MODEL` の 3 つが揃っているときだけ有効になる (欠けていれば AI チャット無効で他は通常動作)。
