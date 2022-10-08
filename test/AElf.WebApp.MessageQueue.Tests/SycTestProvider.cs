using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AElf.WebApp.MessageQueue;
using AElf.WebApp.MessageQueue.Enum;
using AElf.WebApp.MessageQueue.Provider;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Caching;
using Volo.Abp.Threading;
using CollectionExtensions = Castle.Core.Internal.CollectionExtensions;

namespace AElf.WebApp.Application.MessageQueue.Tests;

public class SycTestProvider : ISyncBlockStateProvider
{
    
    private SyncInformation _blockSyncStateInformation;
    private const string BlockSyncState = "BlockSyncStateTest";
    //private readonly ICacheManager _cacheManager;
    private readonly IDistributedCache<SyncInformation> _distributedCache;
    private readonly MessageQueueOptions _messageQueueOptions;
    private readonly string _blockSynState;
    private SemaphoreSlim SyncSemaphore { get; }

    public SycTestProvider(IDistributedCache<SyncInformation> distributedCache,
        IOptionsSnapshot<MessageQueueOptions> messageQueueEnableOptions)
    {
        _messageQueueOptions = messageQueueEnableOptions.Value;
        _distributedCache = distributedCache;
        SyncSemaphore = new SemaphoreSlim(1, 1);
        _blockSynState = $"{BlockSyncState}-{_messageQueueOptions.ClientName}";
    }

    public async Task InitializeAsync()
    {
        _blockSyncStateInformation = await _distributedCache.GetAsync(_blockSynState);
        if (_blockSyncStateInformation == null)
        {
            _blockSyncStateInformation = new SyncInformation
            {
                CurrentHeight = _messageQueueOptions.StartPublishMessageHeight >= 1
                    ? _messageQueueOptions.StartPublishMessageHeight - 1
                    : 1
            };
        }

        else if (_blockSyncStateInformation.CurrentHeight <
                 _messageQueueOptions.StartPublishMessageHeight - 1)
        {
            _blockSyncStateInformation.CurrentHeight = _messageQueueOptions.StartPublishMessageHeight - 1;
        }

        _blockSyncStateInformation.State = _messageQueueOptions.Enable ? SyncState.Prepared : SyncState.Stopped;
        _blockSyncStateInformation.SentBlockHashs = new Dictionary<string, string>();
        _blockSyncStateInformation.FirstSendBlockHash=String.Empty;
        
    }

    public async Task<SyncInformation> GetCurrentStateAsync()
    {
        var currentData = new SyncInformation();
        using (await SyncSemaphore.LockAsync())
        {
            currentData.State = _blockSyncStateInformation.State;
            currentData.CurrentHeight = _blockSyncStateInformation.CurrentHeight;
            currentData.SentBlockHashs = _blockSyncStateInformation.SentBlockHashs;
            currentData.FirstSendBlockHash = _blockSyncStateInformation.FirstSendBlockHash;
            
        }
        return currentData;
    }

    public async Task UpdateStateAsync(long? height, SyncState? state = null, SyncState? expectationState = null)
    {
        var dataToUpdate = new SyncInformation();
        using (await SyncSemaphore.LockAsync())
        {
            if (expectationState.HasValue && expectationState != _blockSyncStateInformation.State)
            {
                return;
            }
            _blockSyncStateInformation.State = state ?? _blockSyncStateInformation.State;
            _blockSyncStateInformation.CurrentHeight = height ?? _blockSyncStateInformation.CurrentHeight;
            dataToUpdate.State = _blockSyncStateInformation.State;
            dataToUpdate.CurrentHeight = _blockSyncStateInformation.CurrentHeight;
            dataToUpdate.SentBlockHashs = _blockSyncStateInformation.SentBlockHashs;
            dataToUpdate.FirstSendBlockHash = _blockSyncStateInformation.FirstSendBlockHash;
            await _distributedCache.SetAsync(_blockSynState, dataToUpdate);
        }

     
    }
    public async Task AddBlockHashAsync(string blockHash,string preBlockHash)
    {
        
        using (await SyncSemaphore.LockAsync())
        {
            if (_blockSyncStateInformation.SentBlockHashs.IsNullOrEmpty() && CollectionExtensions.IsNullOrEmpty(_blockSyncStateInformation.FirstSendBlockHash))
            {
                _blockSyncStateInformation.FirstSendBlockHash = blockHash;
            }
            _blockSyncStateInformation.SentBlockHashs.Add(blockHash,preBlockHash);
          
            await _distributedCache.SetAsync(_blockSynState, _blockSyncStateInformation);
        }

      
    }

    public async Task DeleteBlockHashAsync(string blockHash)
    {
        using (await SyncSemaphore.LockAsync())
        {
            _blockSyncStateInformation.SentBlockHashs.Remove(blockHash);
            await _distributedCache.SetAsync(_blockSynState, _blockSyncStateInformation);
        }

    }
}