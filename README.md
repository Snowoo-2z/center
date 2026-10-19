# Center

A minimal, dark, Claude-inspired desktop chat client for a **local** Ollama model
(`qwen2.5:1.5b`), written in C# / .NET 9 with **Avalonia 11.3**, trimmed and
compiled with **Native AOT**.

- Streaming answers (token by token) over `POST /api/chat`
- Toggleable connectors — the first one is **Recherche Web via Chrome**
- Conversations history, cancel/stop, Markdown rendering, copyable code blocks
- Zero JSON reflection: streaming NDJSON + source-generated `JsonSerializerContext`
- Dark palette: `#121212` canvas, white accents, native Windows fonts

## Requirements

- Windows 10/11 x64
- .NET 9 SDK
- Visual Studio 2022 Build Tools with **Desktop development with C++**
  (MSVC linker + Windows SDK — required by Native AOT)
- [Ollama](https://ollama.com) with `qwen2.5:1.5b`

## Run (development)

```powershell
ollama serve
ollama pull qwen2.5:1.5b
dotnet run --project .\src\Center\Center.csproj
```

## Publish

```powershell
.\publish.ps1                    # Native AOT, fast startup
.\publish.ps1 -SingleFile        # literally one self-contained .exe (JIT)
```

Full step-by-step instructions (Ollama, toolchain, troubleshooting) are provided
in the conversation that generated this repository.

## Layout

```
src/Center/
├── Center.csproj          Native AOT + trimming configuration
├── Program.cs             AppBuilder entry point
├── App.axaml(.cs)         theme, resources, main window
├── Styles/Theme.axaml     palette, buttons, connector switch
├── Styles/AppStyles.axaml message bubbles, composer, animations
├── Views/MainWindow.axaml sidebar + chat + composer, auto-scroll, shortcuts
├── ViewModels/            chat loop, connectors, conversations
├── Services/              Ollama (NDJSON streaming), web search, Chrome
├── Controls/MarkdownView  tiny markdown renderer
└── Models/                ChatMessage, Conversation, Connector, WebSource
```
