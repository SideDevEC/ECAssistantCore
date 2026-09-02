# ECAssistant Split Architecture — Core + LLM Server

**Date:** 2026-08-24  
**Updated:** 2026-09-02 (v14 — tag system removed, native JSON decisions, grammar + KV cache fixed)  
**Status:** ✅ Implemented — all 4 projects build, 0 errors, 0 LDC warnings

## Locked Decisions

1. **HTTP server:** `HttpListener` (built-in .NET, MIT, zero deps)
2. **Token counting:** `/eca/tokenize` HTTP endpoint (accurate, uses LLamaSharp tokenizer)
3. **Lifecycle:** `ECAssistantLLM` is a **standalone console app** (own process). `ECAssistantCore` launches it as a child process if no server is detected at the configured endpoint. Multiple ECAssistant instances can connect to the same server.
4. **HTTP-only:** No direct LLamaSharp fallback in Core. Clean cut.
5. **Config separation:** All server/LLM config lives in `ECAssistantLLM`'s own config file. Core only knows the endpoint URL + whether to auto-start.
6. **Multi-client:** Server supports multiple ECAssistant clients connecting simultaneously. Sessions namespaced by `clientId`.

---

## Goal

Split `ECAssistantCore` into 2 projects:
1. **`ECAssistantCore`** — engine, tools, session, memory, config — **no LLamaSharp dependency**
2. **`ECAssistantLLM`** — standalone console app, wraps LLamaSharp, exposes OpenAI-compatible local HTTP server with ECAssistant-specific control APIs

`ECAssistantCore` talks to `ECAssistantLLM` (or any OpenAI-compatible endpoint) via HTTP streaming.

---

## Architecture Overview

```
┌─ ECAssistantConsole/TUI (Process 1) ──────────────────────┐
│                                                          │
│  ┌──────────────────────────────────────────────────────┐ │
│  │              ECAssistantCore                          │ │
│  │            (no LLamaSharp dep)                        │ │
│  │                                                       │ │
│  │  Engine/          Tools/           Session/            │ │
│  │  - EAgentEngine   - EToolBase      - AgentSession    │ │
│  │  - ContextWindow  - EShellAgent    - SessionManager  │ │
│  │  - TokenCounter   - EGitTool       - ISessionContext │ │
│  │  - Orchestrator   - EWebSearch     - (no LLama types)│ │
│  │                                                       │ │
│  │  Services/         Memory/          Config/           │ │
│  │  - HttpStreaming   - EMemoryManager - EAgentConfig   │ │
│  │  - HttpEmbedder                     - LlmEndpoint    │ │
│  │  - RemoteKvCache                                    │ │
│  │  - RemoteModelLoader                                │ │
│  │  - ServerLauncher       (launches child process)      │ │
│  │                                                       │ │
│  │  Interfaces/                                          │ │
│  │  - IInferenceEngine  (streaming: IAsyncEnumerable)    │ │
│  │  - IVectorEmbedder   (HTTP-based)                     │ │
│  │  - IKvCacheController (HTTP-based)                    │ │
│  │  - IModelLoader       (HTTP-based)                    │ │
│  │                                                       │ │
│  │  Transport/                                            │ │
│  │  - OpenAIClient   (HttpClient wrapper)                │ │
│  │  - SseParser      (Server-Sent Events stream parser)  │ │
│  │                                                       │ │
│  │  LLamaSharp: ❌ none                                   │ │
│  │  NuGet: System.Net.Http, System.Text.Json (built-in)  │ │
│  └───────────────────────┬──────────────────────────────┘ │
└──────────────────────────┼───────────────────────────────┘
                           │ HTTP (localhost:port)
                           │ OpenAI-compatible + ECAssistant extensions
                           │
           ┌───────────────┼───────────────┐
           │               │               │
     Client 1       Client 2       Client N
           │               │               │
           ▼               ▼               ▼
┌──────────────────────────────────────────────────────────┐
│              ECAssistantLLM (Process 2)                    │
│           Standalone Console App — HTTP Server             │
│                                                          │
│  Server/                                                 │
│  - LlmHttpServer     (HttpListener — built-in, zero deps)│
│  - OpenAIEndpoints    (/v1/chat/completions, /v1/embeddings)│
│  - ECAssistantEndpoints (/eca/sessions/*, /eca/models/*) │
│  - SseStreamer        (token streaming response writer)   │
│  - ClientManager      (tracks connected clients, heartbeat)│
│                                                          │
│  Engine/                                                 │
│  - MultiModelHost     (manages 2+ LLamaWeights instances)│
│  - ModelSlot          (one model: weights + config)      │
│  - SessionRegistry    (all client sessions, namespaced)  │
│  - SessionContext     (per-session KV cache + inference)  │
│  - InferenceScheduler (serializes inference across clients)│
│  - VramBudget         (tracks total VRAM, rejects when full)│
│  - ClientManager      (client registration, heartbeat, eviction)│
│                                                          │
│  Config/ (server owns its own config — NOT EAgentConfig)  │
│  - LlmServerConfig    (ports, model paths, GPU layers)   │
│  - llm-server.json    (config file)                      │
│                                                          │
│  LLamaSharp: ✅ LLamaSharp + Backend (Cpu/Cuda/Vulkan)   │
│  NuGet: LLamaSharp 0.27.0                                │
└──────────────────────────────────────────────────────────┘
```

---

## Multi-Client Design

### Connection Flow

1. ECAssistantCore starts → reads `llm_server.endpoint` from its config
2. Pings `GET /eca/health` to check if server is running
3. If no response and `llm_server.auto_start` is true:
   - Launches `ECAssistantLLM` console app as child process (`Process.Start`)
   - Waits for health check to pass (poll up to N seconds)
4. Core registers as a client: `POST /eca/clients` → gets `clientId`
5. Core creates inference sessions under that client namespace

### Session Namespacing

- Each client gets a `clientId` (UUID from server)
- Sessions are keyed as `{clientId}:{sessionId}` internally
- API uses `X-Client-Id` header + `session_id` in body
- No collision possible between clients

### Heartbeat & Cleanup

- Clients send `POST /eca/clients/{id}/heartbeat` every 30s
- Server evicts sessions for clients that miss 3 consecutive heartbeats (~90s)
- Eviction = free KV cache, destroy `InteractiveExecutor`
- Server logs eviction for debugging

### VRAM Budget

- Server config sets `max_vram_mb` (or `max_sessions`)
- Server tracks total KV cache memory across all sessions
- `POST /eca/sessions` returns `503 Service Unavailable` if budget exceeded
- Client can retry or request smaller context size

### Inference Scheduling

- Single `SemaphoreSlim(1,1)` across ALL clients (one model, one compute unit)
- Client A generating → Client B waits
- Server returns `202 Accepted` with estimated wait time if queued
- Fair scheduling (FIFO queue)

---

## Config Separation

### ECAssistantLLM Config (`llm-server.json`)

Owned and read by the server. Core never touches this.

```json
{
  "server": {
    "host": "localhost",
    "port": 8420,
    "max_sessions": 8,
    "max_vram_mb": null,
    "heartbeat_timeout_sec": 90,
    "heartbeat_interval_sec": 30
  },
  "models": [
    {
      "id": "main",
      "path": "models/qwen3-8b-q4_k_m.gguf",
      "gpu_layers": 99,
      "context_size": 32768,
      "threads": -1
    },
    {
      "id": "embeddings",
      "path": "models/all-MiniLM-L6-v2-q4_k_m.gguf",
      "gpu_layers": 0,
      "context_size": 2048,
      "threads": -1
    }
  ],
  "inference": {
    "max_tokens": 512,
    "temperature": 0.3,
    "top_p": 0.95,
    "top_k": 40,
    "repeat_penalty": 1.1
  },
  "logging": {
    "level": "info",
    "file": "ecassistant-llm.log"
  }
}
```

### ECAssistantCore Config (additions to `EAgentConfig`)

Core only knows how to reach the server and whether to auto-start it.

```json
{
  "llm_server": {
    "endpoint": "http://localhost:8420",
    "auto_start": true,
    "server_executable_path": "../ECAssistantLLM/bin/Release/net8.0/ECAssistant.LLM",
    "startup_timeout_sec": 60,
    "heartbeat_interval_sec": 30,
    "model_id": "main",
    "embedding_model_id": "embeddings"
  }
}
```

That's it. No model paths, no GPU layers, no LLamaSharp params in Core.

---

## API Surface

### OpenAI-Compatible Endpoints

| Endpoint | Method | Purpose |
|---|---|---|
| `/v1/chat/completions` | POST | Chat completion with streaming (`stream: true`) |
| `/v1/completions` | POST | Text completion (legacy) |
| `/v1/embeddings` | POST | Text embeddings |
| `/v1/models` | GET | List loaded models |

### ECAssistant Extension Endpoints

**Health & Client Management:**

| Endpoint | Method | Purpose |
|---|---|---|
| `/eca/health` | GET | Server health check (used by Core to detect running server) |
| `/eca/clients` | POST | Register a new client → returns `clientId` |
| `/eca/clients/{id}/heartbeat` | POST | Client heartbeat (keeps sessions alive) |
| `/eca/clients/{id}` | DELETE | Disconnect client (frees all its sessions) |

**Session / KV Cache Control:**

| Endpoint | Method | Purpose |
|---|---|---|
| `/eca/sessions` | POST | Create a new inference session (own KV cache) |
| `/eca/sessions/{id}` | DELETE | Destroy a session (free KV cache) |
| `/eca/sessions/{id}/prefill` | POST | Prefill static prefix into KV cache |
| `/eca/sessions/{id}/rewind` | POST | Rewind KV cache to saved state |
| `/eca/sessions/{id}/save-state` | POST | Save current KV cache state (snapshot) |
| `/eca/sessions/{id}/reset` | POST | Reset KV cache + re-prefill |
| `/eca/sessions/{id}/status` | GET | KV cache usage, prefill status, token count |

**Model Management:**

| Endpoint | Method | Purpose |
|---|---|---|
| `/eca/models` | GET | List models + GPU/memory stats |
| `/eca/models/load` | POST | Load a model at runtime |
| `/eca/models/unload` | POST | Unload a model |

**Tokenization:**

| Endpoint | Method | Purpose |
|---|---|---|
| `/eca/tokenize` | POST | Tokenize text (returns token count + token IDs) |

### Request/Response Examples

```
# Register client
POST /eca/clients
{ "client_name": "ECAssistantConsole", "version": "10.25" }

→ 200 OK
{ "client_id": "a1b2c3d4-...", "server_version": "1.0.0" }

# Chat completion with streaming + session routing
POST /v1/chat/completions
Headers: X-Client-Id: a1b2c3d4-...
{
  "model": "main",
  "messages": [...],
  "stream": true,
  "temperature": 0.3,
  "top_p": 0.95,
  "top_k": 40,
  "max_tokens": 512,
  "repeat_penalty": 1.1,
  "session_id": "main",
  "stop": ["User:", "### User"]
}

→ SSE stream:
data: {"choices":[{"delta":{"content":"Hello"}}]}
data: {"choices":[{"delta":{"content":" world"}}]}
data: [DONE]

# Prefill KV cache
POST /eca/sessions/main/prefill
Headers: X-Client-Id: a1b2c3d4-...
{ "text": "system prompt + tool definitions..." }

→ 200 OK
{ "prefilled": true, "tokens": 1234, "elapsed_ms": 850 }

# Rewind KV cache
POST /eca/sessions/main/rewind
Headers: X-Client-Id: a1b2c3d4-...
{ "state_id": "last_saved" }

→ 200 OK
{ "rewound": true }

# Tokenize
POST /eca/tokenize
Headers: X-Client-Id: a1b2c3d4-...
{ "model": "main", "text": "Hello world" }

→ 200 OK
{ "tokens": 2, "token_ids": [1234, 5678] }

# Heartbeat
POST /eca/clients/a1b2c3d4-.../heartbeat
{ "active_sessions": 2 }

→ 200 OK
{ "ok": true, "sessions_alive": 2 }
```

---

## ECAssistantCore — What Changes

### New Interfaces

```csharp
// Replaces IInferenceEngine — now HTTP + streaming
public interface IInferenceEngine
{
    IAsyncEnumerable<string> StreamAsync(
        string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct = default);

    Task<string> GenerateAsync(
        string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct = default);

    string Endpoint { get; }    // e.g. "http://localhost:8420"
}

// New — KV cache control over HTTP
public interface IKvCacheController
{
    Task<string> CreateSessionAsync(string sessionId);
    Task<bool> PrefillAsync(string sessionId, string text);
    Task<bool> RewindAsync(string sessionId);
    Task<bool> SaveStateAsync(string sessionId);
    Task<bool> ResetAsync(string sessionId);
    Task<KvCacheStatus> GetStatusAsync(string sessionId);
    Task DestroySessionAsync(string sessionId);
}

// Replaces IModelLoader — no more LLamaWeights
public interface IModelLoader
{
    Task<bool> LoadModelAsync(string modelId, string path, ModelLoadOptions options);
    Task<bool> UnloadModelAsync(string modelId);
    Task<IReadOnlyList<ModelInfo>> GetLoadedModelsAsync();
}

// New — client lifecycle
public interface ILlmServerClient
{
    Task<string> ConnectAsync(string clientName);
    Task HeartbeatAsync();
    Task DisconnectAsync();
    string ClientId { get; }
}
```

### What Gets Removed from Core

- All `using LLama;` / `using LLama.Common;` / `using LLama.Native;` / `using LLama.Sampling;`
- `LlamaInferenceEngine` → replaced by `HttpStreamingEngine`
- `LlamaEmbedder` → replaced by `HttpEmbedder`
- `ModelLoader` (returns `LLamaWeights`) → replaced by `RemoteModelLoader`
- `InferenceParamsFactory` → replaced by `InferenceRequestMapper` (maps config → JSON)
- `Config/ContextParams.cs` → deleted (LLamaSharp-specific)
- `TokenCounter` using `LLamaContext` → calls `/eca/tokenize` endpoint
- `EAgentEngine` — all LLamaSharp fields/props replaced with `IKvCacheController` calls
- `SessionManager` — no `LLamaWeights` loading; uses `ServerLauncher` to start/connect to server
- `AgentSession` — no `LLamaWeights`/`ModelParams`/`InferenceParams`; takes `IInferenceEngine` + `IKvCacheController`
- `ISessionContext` — `SharedWeights` and `SharedModelParams` properties removed

### What Gets Added to Core

- `Transport/OpenAIClient` — `HttpClient` wrapper for OpenAI-compatible API
- `Transport/SseParser` — parses SSE `data:` lines into token stream
- `Services/HttpStreamingEngine` — implements `IInferenceEngine` via HTTP
- `Services/HttpEmbedder` — implements `IVectorEmbedder` via HTTP
- `Services/RemoteKvCacheController` — implements `IKvCacheController` via HTTP
- `Services/RemoteModelLoader` — implements `IModelLoader` via HTTP
- `Services/RemoteTokenizer` — calls `/eca/tokenize` endpoint
- `Services/ServerLauncher` — detects/starts ECAssistantLLM child process
- `Services/LlmServerClient` — implements `ILlmServerClient` (register, heartbeat, reconnect + 30s hysteresis, disconnect-before-dispose)
- `Services/LlmProviderRegistry` + `Interfaces/ILlmProviderRegistry` — multi-provider remote config (default provider, opt-in startup failover)
- `Services/SecureKeyStore` + `Interfaces/ISecureKeyStore` — cross-platform self-encrypting API key files (`keyfile:` scheme; Data Protection keyring, plaintext auto-migration, owner-only perms)
- `Config/LlmServerEndpointConfig` — endpoint URL, auto_start, server_executable_path

### EAgentEngine Refactor

Current flow:
```
Turn → BuildIncrementalInput → _executor.InferAsync → tokens → parse → orchestrate
KV cache: PrefillStaticPrefix / GetStateData / LoadState / ResetAndRebuild
```

New flow:
```
Turn → BuildIncrementalInput → _inferenceEngine.StreamAsync → tokens → parse → orchestrate
KV cache: _kvCacheController.PrefillAsync / RewindAsync / ResetAsync
```

Orchestration logic (format retry, tool calls, self-correction, context window) stays **unchanged**.

---

## ECAssistantLLM — Project Structure

```
ECAssistantLLM/
├── ECAssistant.LLM.csproj          (console app, LLamaSharp 0.27.0)
├── Program.cs                       (entry point — loads config, starts server)
├── llm-server.json                  (server config — models, ports, etc.)
│
├── Server/
│   ├── LlmHttpServer.cs            (HttpListener main loop)
│   ├── RequestRouter.cs            (all path/method routing + handlers)
│   └── SseStreamer.cs              (SSE response writer, JSON I/O helper)
│
├── Engine/
│   ├── MultiModelHost.cs          (manages 2+ LLamaWeights)
│   ├── ModelSlot.cs               (one model: weights + config + status)
│   ├── SessionRegistry.cs         (all sessions across all clients)
│   ├── SessionContext.cs          (per-session: executor + KV cache + prefill/rewind/reset)
│   ├── InferenceScheduler.cs      (SemaphoreSlim(1,1) FIFO gate)
│   ├── VramBudget.cs              (tracks total VRAM usage)
│   └── ClientManager.cs           (client registration, heartbeat, eviction)
│
├── Config/
│   ├── LlmServerConfig.cs         (deserializes llm-server.json)
│   └── Models/
│       ├── ServerSection.cs
│       ├── ModelConfig.cs
│       ├── InferenceDefaults.cs
│       └── LoggingSection.cs
│
├── Models/                         (request/response DTOs — 6 files)
│   ├── ChatCompletionRequest.cs   (chat request + ChatMessage)
│   ├── ChatCompletionChunk.cs     (SSE chunk + ChunkChoice + ChunkDelta)
│   ├── CompletionModels.cs        (text completion: req/resp/chunk/choice)
│   ├── EmbeddingModels.cs         (embedding request/response)
│   ├── TokenizeModels.cs          (tokenize request/response)
│   └── ApiModels.cs               (error/success/client/session/model DTOs)
│
├── ServerLogger.cs                (ILogger + LogLevel + ServerLogger impl)
└── ECAssistantLLM.Tests/          (64 integration tests — separate project)
    └── ECAssistant.LLM.Tests.csproj
```

**Note:** See `ECAssistantLLM/ARCHITECTURE.md` for the detailed, up-to-date architecture of the LLM server. This document captures the original design decisions; the per-project ARCHITECTURE.md files reflect the actual implementation.

---

## Current Coupling Points (what must change in Core)

| Coupling | File | LLamaSharp Usage | Replacement |
|---|---|---|---|
| `IModelLoader` returns `LLamaWeights` | Interfaces/IModelLoader.cs | `LLamaWeights`, `ModelParams` | `RemoteModelLoader` (HTTP) |
| `LlamaInferenceEngine` uses `StatelessExecutor` | Services/LlamaInferenceEngine.cs | `StatelessExecutor`, `InferenceParams` | `HttpStreamingEngine` (SSE) |
| `LlamaEmbedder` uses `LLamaEmbedder` | Services/LlamaEmbedder.cs | `LLamaEmbedder`, `LLamaWeights` | `HttpEmbedder` (HTTP) |
| `EAgentEngine` uses `InteractiveExecutor` | Engine/EAgentEngine.cs | `InteractiveExecutor`, `LLamaContext`, `ExecutorBaseState` | `IKvCacheController` (HTTP) |
| `SessionManager` loads `LLamaWeights` | Session/SessionManager.cs | `LLamaWeights.LoadFromFile` | `ServerLauncher` + `LlmServerClient` |
| `AgentSession` takes `LLamaWeights` | Session/AgentSession.cs | `LLamaWeights`, `ModelParams`, `InferenceParams` | `IInferenceEngine` + `IKvCacheController` |
| `ISessionContext` exposes `LLamaWeights` | Session/ISessionContext.cs | `LLamaWeights`, `ModelParams` | Properties removed |
| `TokenCounter` takes `LLamaContext` | Engine/TokenCounter.cs | `LLamaContext` | `RemoteTokenizer` (HTTP `/eca/tokenize`) |
| `InferenceParamsFactory` creates `InferenceParams` | Services/InferenceParamsFactory.cs | `InferenceParams`, `DefaultSamplingPipeline` | `InferenceRequestMapper` (→ JSON) |
| `Config/ContextParams.cs` | Config/ContextParams.cs | LLamaSharp mirror | Deleted |

---

## Migration Path

1. Create `ECAssistantLLM` project — csproj, Program.cs, config, server skeleton
2. Implement `MultiModelHost` + `ModelSlot` + `SessionRegistry` + `KvCacheManager`
3. Implement `LlmHttpServer` + all endpoints (OpenAI + ECAssistant extensions)
4. Implement `ClientManager` + heartbeat + VRAM budget
5. Add new interfaces to Core (`IKvCacheController`, `ILlmServerClient`, updated `IInferenceEngine`, `IModelLoader`)
6. Implement HTTP-backed services in Core (`HttpStreamingEngine`, `RemoteKvCacheController`, `ServerLauncher`, etc.)
7. Refactor `EAgentEngine` — replace LLamaSharp calls with HTTP interface calls
8. Refactor `SessionManager` — use `ServerLauncher` instead of loading weights
9. Refactor `AgentSession` constructor — remove LLamaSharp types
10. Refactor `TokenCounter` — use `RemoteTokenizer`
11. Remove LLamaSharp packages from Core csproj
12. Update solution file
13. Test end-to-end

**Risk:** KV cache rewind over HTTP is latency-sensitive. Current `LoadState` is in-memory (~ms). HTTP adds round-trip. For format retries this should be fine (sub-100ms localhost).

---

**Status:** ✅ Fully implemented (v11.7, 2026-08-25).
### Test Tiers

- ECAssistantLLM: unit/security suite via `ECAssistantLLM/scripts/run-unit-tests.sh` (no models); full-system E2E via `scripts/run-e2e.sh` (`Category=E2E`, loads real GGUFs). Default `dotnet test` excludes E2E.

---

## First-Run / Installation Subsystem (v12.1, 2026-08-27)

**Summary:** Wizard-driven AI setup — local vs remote choice, catalog downloads, config wiring, full verification.

### Components (Core/Setup)
- `ModelCatalogDocument` / `ModelCatalogEntry` — data-driven catalog (model-catalog.json, auto-created from defaults)
- `FirstRunDetector` — NeedsSetup when no GGUFs in models/ AND no valid llm-server.json entries
- `ModelInstallerService` — HF downloads (resume via .part), `ApplyToServerConfig`, `RegisterLocalModelFile(filename, isEmbedding)` for orphan GGUFs (gpu_layers 0, ctx 64k, batch 512, sibling mmproj auto-detect), internet probe, disk-space/list/remove helpers
- `RemoteProviderSetupWriter` — remote mode: llm_provider.mode=remote + llm_providers registry; API key encrypted immediately via `SecureKeyStore.SetKey` → config holds only `keyfile:` reference
- `VectorMemorySetupWriter` — persists vector_memory.enabled (in-place JSON edit)
- `SecureKeyStore` — self-encrypting key files (ECAKEY1:, DPAPI/DataProtection); `SetKey` encrypts at setup time, not on first read

### Components (TUI/Controller)
- `FirstRunWizard` — flow: vector memory opt-in → GPU preference → local/remote choice → remote (endpoint/key/model/embedding model + live connection test) OR catalog picks (internet + disk pre-flight, retry ×3, memory estimates) → embeddings ensure → post-install test → optional model removal
- `AppController.RunSetupFlowAsync(onlyIfNeeded)` — shared by auto first-run and `/reinstall`
- `/reinstall` — yes/no warning → StopAll + `SessionManager.StopLocalServerAsync()` → ResetAiSetup (keys/, llm-server.json deleted; models kept) → wizard re-runs
- `/menu [topic]` — layered help: overview + `/menu sessions|context|background|ai` submenus (`/help` alias)

### Config Flow
- appsettings.json: llm_provider.mode/endpoint/model/api_key(keyfile ref)/embedding_model_id + llm_providers registry
- llm/llm-server.json: model entries written by installer (path, gpu_layers, context_size, batch_size, mmproj_path)
- Embeddings: local → catalog/orphan GGUF wired automatically (if vector memory enabled); remote → provider embedding_model_id (default text-embedding-3-small); EAgentEngine falls back to TfidfEmbedder when no embedder
