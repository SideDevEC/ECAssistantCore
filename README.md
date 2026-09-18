# ECAssistant.Core

**Local AI agent engine** — the brains of [ECAssistant](https://github.com/SideDevEC/ECAssistantLLM): tools, session orchestration, memory, and the setup wizard, all running against a local LLM server. No cloud, no API keys.

## What's inside

- **Agent engine** — multi-session orchestration, sub-agents, self-correction
- **12 built-in tools** — shell, file I/O, web fetch/search, and more (permission-gated)
- **Memory system** — vector (FAISS-style) + daily notes + curated long-term memory
- **Setup wizard** — first-run installer that provisions the LLM server, models, and backend runtimes (nothing is downloaded at chat time)
- **Model catalog** — data-driven (`model-catalog.json`); adding a model needs no code changes. Vision models always ship with their mmproj projector.

## Architecture boundary (by design)

Core consumes [ECAssistantLLM](https://github.com/SideDevEC/ECAssistantLLM) **exclusively via its OpenAI-compatible HTTP endpoints**. It knows nothing about how the server loads models or manages runtimes — the LLM server is a self-contained finished product.

## Installation

```bash
dotnet add package ECAssistant.Core
```

Packages are served from GitHub Packages (`nuget.pkg.github.com/SideDevEC`). You need a GitHub token with `read:packages`:

```bash
dotnet nuget add source \
  --username <your-github-username> \
  --password <your-token> \
  --store-password-in-clear-text \
  --name github \
  https://nuget.pkg.github.com/SideDevEC/index.json
```

## Related repos

| Repo | What it is |
|---|---|
| [ECAssistantLLM](https://github.com/SideDevEC/ECAssistantLLM) | OpenAI-compatible local LLM server (LLamaSharp + external backends) |
| [ECAssistantTUI](https://github.com/SideDevEC/ECAssistantTUI) | Terminal UI library |
| [ECAssistantConsole](https://github.com/SideDevEC/ECAssistantConsole) | Console application |

## License

[MIT](LICENSE) — © 2026 SideDevEC
