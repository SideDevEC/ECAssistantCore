# ECAssistant.Core.TestSupport

**Test harness** for [ECAssistant.Core](https://www.nuget.org/packages/ECAssistant.Core) — run agent scenarios without a real LLM.

> ⚠️ **Not for production use.** Development and diagnostics only.

## What's inside

- **`TestRunner`** — executes agent scenarios end-to-end and collects results
- **`TestScenario` / `TestResult`** — declarative scenario + outcome model
- **`MockEngine`** — fake inference engine; no model, no GPU, no server needed
- **`EcaTests`** — catalog of ready-made scenarios covering tools, memory, sessions

## Typical use

```csharp
using ECAssistant.Core.TestSupport;

var engine = new MockEngine();
var runner = new TestRunner(engine);
var result = await runner.RunAsync(myScenario);
Console.WriteLine(result.Passed);
```

It also powers the Console's built-in self-test mode (`ConsoleTestHost`), which runs these scenarios live against the configured agent for diagnostics.

## License

[MIT](../LICENSE) — © 2026 SideDevEC
