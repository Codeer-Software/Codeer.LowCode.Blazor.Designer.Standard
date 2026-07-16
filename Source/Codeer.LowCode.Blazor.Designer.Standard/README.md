# Codeer.LowCode.Blazor.Designer.Standard

The standard implementation for the **Codeer.LowCode.Blazor** Designer. It packages everything a designer application normally ships — project templates, tool menus, icon candidates, and an AI chat assistant — so the Visual Studio template output stays thin and updates arrive as a NuGet version bump instead of regenerated code.

Distributed as open source under the **MIT License**.

## Features

- **Project templates** (`StandardTemplates`) — empty / empty with auth / getting started / pattern showcase / auth patterns / inventory / SFA / project management, with bundled sample SQLite databases.
- **Tool menus** (`StandardMenus`) — Import Modules from Database, Create DDL, Create Field Class, Create FieldData Class, Create C# Enum, Export Excel Print CheatSheet, Export PageObject (Selenium), plus the standard DB column transform (PostgreSQL `xmin` → optimistic locking).
- **Icon candidates** (`StandardIcons`) — Bootstrap Icons list for the icon picker.
- **Claude Code workspace** (`ClaudeWorkspaceDeploy` / Tools > Claude Code Workspace) — deploys and updates the Claude Code workspace (CLAUDE.md, docs, hooks, permission settings with the designer exe path baked in). The content ships inside this package (`ClaudeWorkspace/`), so the deployed docs always match the running designer version. Also available headless via the `claude-workspace` CLI verb.
- **DDL generation** (`DbMapping`) — deterministic CREATE TABLE / diff ALTER TABLE generation from module designs.
- **AI chat assistant** (`AIChat/`) — natural-language editing of CSS, scripts, SQL, module settings, fields, layouts (detail / list / search), and page frames.
  - *Task-oriented*: an intent router splits a request into steps and dispatches each to the right editor function.
  - *Self-correcting*: generated changes run through the Designer's design checks and are regenerated on errors before being applied.
  - *Model-swappable*: built on the [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/) `IChatClient` abstraction — inject any provider.
  - *Custom-field aware*: recognizes custom field types registered via the Designer's `FieldCatalog`.
  - *Editable prompts*: specifications are loaded from the Designer's `SpecDocCatalog` and guidelines from the embedded Claude Code workspace docs; fork and rebuild with your own.

## Installation

```
dotnet add package Codeer.LowCode.Blazor.Designer.Standard
```

Requires the Designer package (`Codeer.LowCode.Blazor.Designer`, matching version).

## Getting started

Register everything with two calls in your designer app — `SetupHeadless` **before** `base.OnStartup(e)` (project templates and CLI verbs are used by the headless CLI, which runs inside `base.OnStartup`), and `Setup` after it:

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    // ... app-specific service / script type registrations ...
    DesignerStandard.SetupHeadless(); // templates + claude-workspace CLI verb

    base.OnStartup(e);

    DesignerStandard.Setup(DesignerEnvironment, new DesignerStandardOptions
    {
        // null disables the AI chat. Provider choice and credentials stay in your app.
        CreateAiChatClient = () => new AzureOpenAIClient(endpoint, credential)
            .GetChatClient(model).AsIChatClient(),
    });
}
```

Or pick features individually — `Setup` is just the sum of public building blocks:

```csharp
StandardIcons.AddBootstrapIcons();
DesignerTemplateCandidate.Templates.Add(StandardTemplates.PatternShowcase()); // 一部だけ
StandardMenus.AddCreateDdl(DesignerEnvironment);                              // メニューも個別に
DesignerChatRegistration.RegisterScreenChats(
    new DesignerEnvironmentChatHost(DesignerEnvironment), createAiChatClient);
```

## AI chat architecture

```
Screen chats (ScreenChats)          per-editor entry points
        |
   AiOrchestrator + IntentRouter    split a request into ordered steps
        |
   Functions/ (IAiFunction)         field.create / field.edit / layout.* /
        |                           script.edit / sql.* / css.edit / db.update / ...
   IDesignerChatHost                bridge to the designer
                                    (DesignerEnvironmentChatHost is the standard implementation)
```

Prompts are single-sourced: framework specifications come from the Designer package (`SpecDocCatalog`, same assembly and version as the implementation), and authoring guidelines come from the embedded Claude Code workspace (`ClaudeWorkspace/ClaudeCodeForDesigner/Docs/`) — the AI chat and Claude Code read the same documents.

## License

MIT License. See [LICENSE](LICENSE).
