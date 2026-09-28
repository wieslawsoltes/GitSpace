#if __WASM__
using System.Text.Json;
using GitSpace.Core;

namespace GitSpace.App;

internal sealed class BrowserGitBackend : IGitBackend
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;
    private int _nextId;
    public string DisplayName => "Browser Git · IndexedDB worker";
    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.Ordinal)
    { "demo", "init", "open", "clone", "refresh", "diff", "commitFiles", "read", "write", "stage", "unstage", "commit", "amend", "discard", "branch", "checkout", "renameBranch", "deleteBranch", "remote", "fetch", "pull", "push", "merge", "tag", "deleteTag", "stash", "stashApply", "stashDrop", "export" };
    private void Start()
    {
        if (_started) return;
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("""
            (() => {
                if (globalThis.GitSpaceWorker) return 'ready';
                const results = new Map();
                const worker = new Worker(new URL('git/worker.js', document.baseURI), {type:'module', name:'GitSpace Git backend'});
                let failure = '';
                worker.onmessage = e => results.set(e.data.id, JSON.stringify(e.data));
                worker.onerror = e => { failure = 'Git worker failed: ' + (e.message || 'Check worker deployment and browser storage support.'); };
                worker.onmessageerror = () => { failure = 'Git worker response could not be decoded.'; };
                globalThis.GitSpaceWorker = {
                    send: (id, request) => { if (failure) throw new Error(failure); worker.postMessage({id,request}); },
                    take: id => { if (failure) return JSON.stringify({id,error:failure}); const result = results.get(id) || ''; results.delete(id); return result; },
                    stop: () => { worker.terminate(); results.clear(); delete globalThis.GitSpaceWorker; }
                };
                return 'ready';
            })()
            """);
        _started = true;
    }
    public async Task<GitResult> ExecuteAsync(GitRequest request, CancellationToken cancellation = default)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested(); Start(); var id = ++_nextId;
            var json = JsonSerializer.Serialize(GitJson.Serialize(request));
            Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.GitSpaceWorker.send(" + id + ", JSON.parse(" + json + ")); 'sent'");
            // Poll only during an active operation. No permanent dispatcher timer or idle animation.
            while (true)
            {
                await Task.Delay(25, cancellation);
                var result = Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.GitSpaceWorker.take(" + id + ")");
                if (string.IsNullOrEmpty(result)) continue;
                using var document = JsonDocument.Parse(result);
                if (document.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
                return GitJson.Deserialize<GitResult>(document.RootElement.GetProperty("result").GetRawText());
            }
        }
        finally { _gate.Release(); }
    }
    public ValueTask DisposeAsync()
    {
        if (_started) Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.GitSpaceWorker?.stop(); 'stopped'");
        _started = false; return ValueTask.CompletedTask;
    }
}
#endif
