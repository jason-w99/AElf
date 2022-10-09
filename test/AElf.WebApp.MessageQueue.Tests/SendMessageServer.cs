using System;
using System.Threading;
using System.Threading.Tasks;
using AElf.WebApp.MessageQueue;
using AElf.WebApp.MessageQueue.Enum;
using AElf.WebApp.MessageQueue.Provider;
using AElf.WebApp.MessageQueue.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AElf.WebApp.Application.MessageQueue.Tests;


public class SendMessageServer
{
     private readonly ISyncBlockStateProvider _syncBlockStateProvider;
     private readonly IBlockMessageService _blockMessageService;
    private readonly ISyncBlockLatestHeightProvider _latestHeightProvider;
    protected CancellationToken CancellationToken { get; set; }
    private int _blockCount;
    private int _parallelCount;

    public SendMessageServer(ISyncBlockStateProvider syncBlockStateProvider, AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory, IOptionsSnapshot<MessageQueueOptions> option,
        ISyncBlockLatestHeightProvider latestHeightProvider, IBlockMessageService blockMessageService)
    {
        _syncBlockStateProvider = syncBlockStateProvider;
        _latestHeightProvider = latestHeightProvider;
        _blockMessageService = blockMessageService;
        _blockCount = option.Value.BlockCountPerPeriod;
        _parallelCount = option.Value.ParallelCount;
    }


    public  async Task DoWorkAsync()
    {
        var currentState = await _syncBlockStateProvider.GetCurrentStateAsync();
        var nextHeight = currentState.CurrentHeight;

        var remainCount = _blockCount;
        while (IsContinue(remainCount, currentState.State))
        {
            var syncThreshold = GetSyncThresholdHeight();
            var startHeight = nextHeight;
            var endHeight = Math.Min(startHeight + _parallelCount - 1, syncThreshold);
            if (startHeight >= syncThreshold)
            {
                await PreparedToSyncMessageAsync();
                break;
            }
            
            var syncBlockHeight = await _blockMessageService.SendMessageAsync(startHeight, endHeight, CancellationToken);
            if (syncBlockHeight <= 0)
            {
                await PreparedToSyncMessageAsync();
                break;
            }

            remainCount -= (int)(syncBlockHeight - startHeight + 1);
            nextHeight = syncBlockHeight;
            currentState = await _syncBlockStateProvider.GetCurrentStateAsync();
        }
        
        var startCount = 1;
        while (IsContinue(startCount++, currentState.State))
        {
            var latestHeight = _latestHeightProvider.GetLatestHeight();
            if (nextHeight > latestHeight - 4)
            {
                await PreparedToSyncMessageAsync();
                break;
            }
            
            if (await _blockMessageService.SendMessageAsync(nextHeight, CancellationToken))
            {
                nextHeight++;
            }
            else
            {
                await PreparedToSyncMessageAsync();
                break;
            }

            currentState = await _syncBlockStateProvider.GetCurrentStateAsync();
        }
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