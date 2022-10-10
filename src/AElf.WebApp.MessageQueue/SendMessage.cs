using System;
using System.Threading;
using System.Threading.Tasks;
using AElf.WebApp.MessageQueue.Enum;
using AElf.WebApp.MessageQueue.Provider;
using AElf.WebApp.MessageQueue.Services;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BackgroundWorkers;

namespace AElf.WebApp.MessageQueue;

public class SendMessage
{
    private readonly ISyncBlockStateProvider _syncBlockStateProvider;
    private readonly IBlockMessageService _blockMessageService;
    private readonly ISyncBlockLatestHeightProvider _latestHeightProvider;
    protected CancellationToken CancellationToken { get; set; }
    
    public SendMessage(ISyncBlockStateProvider syncBlockStateProvider,
        ISyncBlockLatestHeightProvider latestHeightProvider, IBlockMessageService blockMessageService) 
    {
        _syncBlockStateProvider = syncBlockStateProvider;
        _latestHeightProvider = latestHeightProvider;
        _blockMessageService = blockMessageService;
    }
    
    
    public  async Task DoWorkAsync(int blockCount,int parallelCount)
    {
        
        var currentState = await _syncBlockStateProvider.GetCurrentStateAsync();
        var nextHeight = currentState.CurrentHeight;

        var remainCount = blockCount;
        while (IsContinue(remainCount, currentState.State))
        {
            var syncThreshold = GetSyncThresholdHeight();
            var startHeight = nextHeight;
            var endHeight = Math.Min(startHeight + parallelCount - 1, syncThreshold);
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
        
        var startCount = 0;
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