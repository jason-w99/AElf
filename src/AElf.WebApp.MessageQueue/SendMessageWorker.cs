using System.Threading;
using System.Threading.Tasks;
using AElf.WebApp.MessageQueue.Enum;
using AElf.WebApp.MessageQueue.Provider;
using AElf.WebApp.MessageQueue.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AElf.WebApp.MessageQueue;

public class SendMessageWorker : AsyncPeriodicBackgroundWorkerBase
{
    private readonly ISyncBlockStateProvider _syncBlockStateProvider;
    private readonly ISyncBlockLatestHeightProvider _latestHeightProvider;
    private readonly ISendMessageService _sendMessage;
    protected CancellationToken CancellationToken { get; set; }
    private int _blockCount;
    private int _parallelCount;

    public SendMessageWorker(ISyncBlockStateProvider syncBlockStateProvider, AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory, IOptionsSnapshot<MessageQueueOptions> option,
        ISyncBlockLatestHeightProvider latestHeightProvider, ISendMessageService sendMessage) : base(timer,
        serviceScopeFactory)
    {
        _syncBlockStateProvider = syncBlockStateProvider;
        _latestHeightProvider = latestHeightProvider;
        _sendMessage = sendMessage;
        _blockCount = option.Value.BlockCountPerPeriod;
        _parallelCount = option.Value.ParallelCount;
        Timer.Period = option.Value.Period;
        timer.RunOnStart = true;
    }

    public void SetWork(int? period, int? blockCountPerPeriod, int? parallelCount)
    {
        if (period.HasValue)
        {
            Timer.Period = period.Value;
        }

        if (blockCountPerPeriod.HasValue)
        {
            _blockCount = blockCountPerPeriod.Value;
        }

        if (parallelCount.HasValue)
        {
            _parallelCount = parallelCount.Value;
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await base.StartAsync(cancellationToken);
        CancellationToken = cancellationToken;
    }

    public Task StopTimerAsync(CancellationToken cancellationToken = default)
    {
        Timer.Stop(cancellationToken);
        return Task.CompletedTask;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        await _sendMessage.DoWorkAsync( _blockCount,_parallelCount,CancellationToken);
        
    }
    
    
    private bool IsContinue(long remainCount, SyncState state)
    {
        return remainCount > 0 && !CancellationToken.IsCancellationRequested &&
               state == SyncState.AsyncRunning;
    }

    private long GetSyncThresholdHeight()
    {
        return _latestHeightProvider.GetLatestHeight() - 3;
    }

    private async Task PreparedToSyncMessageAsync()
    {
        await _syncBlockStateProvider.UpdateStateAsync(null, SyncState.SyncPrepared,
            SyncState.AsyncRunning);
    }
}