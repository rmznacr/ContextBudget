using System.Collections.Concurrent;
using System.Threading.Channels;
using ContextBudget.Application;

public sealed class ExperimentExecutionQueue(IServiceScopeFactory scopeFactory, ILogger<ExperimentExecutionQueue> logger) : BackgroundService, IExperimentExecutionQueue
{
    private readonly Channel<ExperimentJob> _channel = Channel.CreateBounded<ExperimentJob>(new BoundedChannelOptions(5) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();
    public bool Enqueue(ExperimentJob job)
    {
        var cts = new CancellationTokenSource();
        if (!_tokens.TryAdd(job.RunId, cts)) { cts.Dispose(); return false; }
        if (!_channel.Writer.TryWrite(job)) { _tokens.TryRemove(job.RunId, out _); cts.Dispose(); return false; }
        return true;
    }
    public bool Cancel(Guid runId) => _tokens.TryGetValue(runId, out var cts) && !cts.IsCancellationRequested && TryCancel(cts);
    private static bool TryCancel(CancellationTokenSource cts) { try { cts.Cancel(); return true; } catch (ObjectDisposedException) { return false; } }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            if (!_tokens.TryGetValue(job.RunId, out var cts)) continue;
            try { using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, stoppingToken); await using var scope = scopeFactory.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<ExperimentRunner>().Execute(job, linked.Token); }
            catch (Exception ex)
            {
                logger.LogError("Experiment run {RunId} failed with {ExceptionType}; request content was not logged.", job.RunId, ex.GetType().Name);
                try { await using var scope = scopeFactory.CreateAsyncScope(); var store = scope.ServiceProvider.GetRequiredService<IExperimentStore>(); var run = await store.Run(job.RunId, CancellationToken.None); var experiment = await store.Experiment(job.ExperimentId, CancellationToken.None); if (run is not null && experiment is not null) await store.SaveRunState(run, experiment, "Failed", "EVALUATION_INCOMPLETE", "{}", CancellationToken.None); } catch { }
            }
            finally { _tokens.TryRemove(job.RunId, out _); cts.Dispose(); }
        }
    }
}
