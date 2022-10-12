using System;
using System.Threading;
using System.Threading.Tasks;
using AElf.WebApp.MessageQueue.Enum;
using AElf.WebApp.MessageQueue.Provider;

namespace AElf.WebApp.MessageQueue.Services;

public interface ISendMessageService
{
     Task DoWorkAsync(int blockCount, int parallelCount,CancellationToken cancellationToken);
}

public class SendMessageService :ISendMessageService
{
    private readonly ISyncBlockStateProvider _syncBlockStateProvider;
    private readonly IBlockMessageService _blockMessageService;
    private readonly ISyncBlockLatestHeightProvider _latestHeightProvider;
    
    public SendMessageService(ISyncBlockStateProvider syncBlockStateProvider,
        ISyncBlockLatestHeightProvider latestHeightProvider, IBlockMessageService blockMessageService) 
    {
        _syncBlockStateProvider = syncBlockStateProvider;
        _latestHeightProvider = latestHeightProvider;
        _blockMessageService = blockMessageService;
    }
    public  async Task DoWorkAsync(int blockCount,int parallelCount,CancellationToken cancellationToken)
    {
        
        var currentState = await _syncBlockStateProvider.GetCurrentStateAsync();
        var nextHeight = currentState.CurrentHeight;

        var remainCount = blockCount;
        while (IsContinue(remainCount, currentState.State,cancellationToken))
        {
            var syncThreshold = GetSyncThresholdHeight();
            var startHeight = nextHeight;
            var endHeight = Math.Min(startHeight + parallelCount - 1, syncThreshold);
            if (startHeight >= syncThreshold)
            {
                await PreparedToSyncMessageAsync();
                break;
            }
            
            var syncBlockHeight = await _blockMessageService.SendMessageAsync(startHeight, endHeight, cancellationToken);
            if (syncBlockHeight <= 0)
            {
                await PreparedToSyncMessageAsync();
                break;
            }

            remainCount -= (int)(syncBlockHeight - startHeight + 1);
            nextHeight = syncBlockHeight;
            currentState = await _syncBlockStateProvider.GetCurrentStateAsync();
        }
        /*var startCount = 0;
        while (IsContinue(startCount++, currentState.State,cancellationToken))
        {
            var latestHeight = _latestHeightProvider.GetLatestHeight();
            if (nextHeight > latestHeight - 4)
            {
                await PreparedToSyncMessageAsync();
                break;
            }
            
            if (await _blockMessageService.SendMessageAsync(nextHeight, cancellationToken))
            {
                nextHeight++;
            }
            else
            {
                await PreparedToSyncMessageAsync();
                break;
            }

            currentState = await _syncBlockStateProvider.GetCurrentStateAsync();
        }*/
    } 
    
    private bool IsContinue(long remainCount, SyncState state,CancellationToken cancellationToken)
    {
        return remainCount > 0 && !cancellationToken.IsCancellationRequested &&
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