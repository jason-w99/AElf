using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AElf.WebApp.MessageQueue.Enum;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Threading;

namespace AElf.WebApp.MessageQueue.Provider;

public interface ISyncBlockStateProvider
{
    Task InitializeAsync();
    Task<SyncInformation> GetCurrentStateAsync();

    Task UpdateStateAsync(long? height, SyncState? state = null, SyncState? expectationState = null);

    Task AddBlockHashAsync(string blockHash, string preBlockHash,long preHeight);
    Task AddBlocksHashAsync(ConcurrentDictionary<string, PreBlock> blocksHash);
    Task UpdateBlocksHashAsync(ConcurrentDictionary<string, PreBlock> blocksHash);
    Task DeleteBlockHashAsync(long libHeight);
    
    
}

public class SyncBlockStateProvider : ISyncBlockStateProvider, ISingletonDependency
{
    private SyncInformation _blockSyncStateInformation;
    private const string BlockSyncState = "BlockSyncState";
    private readonly IDistributedCache<SyncInformation> _distributedCache;
    private readonly MessageQueueOptions _messageQueueOptions;
    private readonly ILogger<SyncBlockStateProvider> _logger;
    private readonly string _blockSynState;
    
    private SemaphoreSlim SyncSemaphore { get; }

    public SyncBlockStateProvider(IDistributedCache<SyncInformation> distributedCache,
        IOptionsSnapshot<MessageQueueOptions> messageQueueEnableOptions, ILogger<SyncBlockStateProvider> logger)
    {
        _messageQueueOptions = messageQueueEnableOptions.Value;
        _distributedCache = distributedCache;
        _logger = logger;
        SyncSemaphore = new SemaphoreSlim(1, 1);
        _blockSynState = $"{BlockSyncState}-{_messageQueueOptions.ChainId}";
    }

    public async Task InitializeAsync()
    {
        _blockSyncStateInformation = await _distributedCache.GetAsync(_blockSynState);
        if (_blockSyncStateInformation == null)
        {
            _blockSyncStateInformation = new SyncInformation
            {
                CurrentHeight = _messageQueueOptions.StartPublishMessageHeight >= 1
                    ? _messageQueueOptions.StartPublishMessageHeight-1
                    : 0
            };
        }

        else if (_blockSyncStateInformation.CurrentHeight <=
                 _messageQueueOptions.StartPublishMessageHeight )
        {
            _blockSyncStateInformation.CurrentHeight = _messageQueueOptions.StartPublishMessageHeight-1 ;
        }


        _blockSyncStateInformation.State = _messageQueueOptions.Enable ? SyncState.Prepared : SyncState.Stopped;
        if (_blockSyncStateInformation.SentBlockHashs==null || _blockSyncStateInformation.SentBlockHashs.Count==0)
        {
            _blockSyncStateInformation.SentBlockHashs = new ConcurrentDictionary<string, PreBlock>();
        }
        
        
        _logger.LogInformation(
            $"BlockSyncState initialized, State: {_blockSyncStateInformation.State}  CurrentHeight: {_blockSyncStateInformation.CurrentHeight +1}");
    }

    public async Task<SyncInformation> GetCurrentStateAsync()
    {
        var currentData = new SyncInformation();
        using (await SyncSemaphore.LockAsync())
        {
            currentData.State = _blockSyncStateInformation.State;
            currentData.CurrentHeight = _blockSyncStateInformation.CurrentHeight;
            currentData.SentBlockHashs = _blockSyncStateInformation.SentBlockHashs;
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
            await _distributedCache.SetAsync(_blockSynState, dataToUpdate);
        }

        _logger.LogInformation(
            $"BlockSyncState updated, State: {dataToUpdate.State}  CurrentHeight: {dataToUpdate.CurrentHeight}");
    }
    public async Task AddBlockHashAsync(string blockHash,string preBlockHash,long preHeight)
    {
        
        using (await SyncSemaphore.LockAsync())
        {
            _blockSyncStateInformation.SentBlockHashs.TryAdd(blockHash,new PreBlock(){BlockHash = preBlockHash,Height = preHeight});
            await _distributedCache.SetAsync(_blockSynState, _blockSyncStateInformation);
        }

        _logger.LogInformation(
            $"BlockSyncState AddBlockHashAsync, blockHash: {blockHash}  preBlockHash: {preBlockHash}");
    }
    
    public async Task AddBlocksHashAsync(ConcurrentDictionary<string, PreBlock> blocksHash)
    {
        using (await SyncSemaphore.LockAsync())
        {
            foreach (KeyValuePair<string, PreBlock> kvp in blocksHash)
            {
                if (!_blockSyncStateInformation.SentBlockHashs.ContainsKey(kvp.Key))
                {
                    _blockSyncStateInformation.SentBlockHashs.TryAdd(kvp.Key,kvp.Value);
                }
            }
            /*Dictionary<string, PreBlock> temp = new Dictionary<string, PreBlock>();
            temp= _blockSyncStateInformation.SentBlockHashs.Concat(blocksHash).ToDictionary(k => k.Key, v => v.Value);
            _blockSyncStateInformation.SentBlockHashs = temp;*/
            await _distributedCache.SetAsync(_blockSynState, _blockSyncStateInformation);
        }
        _logger.LogInformation(
            $"BlockSyncState AddBlocksHashAsync ");
    }
    
    public async Task UpdateBlocksHashAsync(ConcurrentDictionary<string, PreBlock> blocksHash)
    {
        using (await SyncSemaphore.LockAsync())
        {
            _blockSyncStateInformation.SentBlockHashs = blocksHash;
            await _distributedCache.SetAsync(_blockSynState, _blockSyncStateInformation);
        }
        _logger.LogInformation(
            $"BlockSyncState UpdateBlocksHashAsync blocksHash is {JsonConvert.SerializeObject(blocksHash)}");
    }
    public async Task DeleteBlockHashAsync(long libHeight)
    {
        _logger.LogInformation($"The current height of lib is: {libHeight} ");
        libHeight -= 1;
        using (await SyncSemaphore.LockAsync())
        {
            if (_blockSyncStateInformation.CurrentHeight< libHeight  )
            {
                if (_blockSyncStateInformation.CurrentHeight > _messageQueueOptions.ReservedCacheCount)
                {
                    libHeight = _blockSyncStateInformation.CurrentHeight - _messageQueueOptions.ReservedCacheCount;
                    _logger.LogDebug($"The current status is that the push has not caught up with the receive ,To delete the previous data .the height is: {libHeight} ");

                }
                else
                {
                    return;
                }
            }
            //List<string> keys = new List<string>();
            ConcurrentDictionary<string, PreBlock> deleteList = new ConcurrentDictionary<string, PreBlock>();
            foreach (var sendBolck in _blockSyncStateInformation.SentBlockHashs)
            {
                if (sendBolck.Value.Height<libHeight)
                {
                    //keys.Add(sendBolck.Key);
                    deleteList.TryAdd(sendBolck.Key,sendBolck.Value);
                }
            }

            foreach (var key in deleteList)
            {
                _blockSyncStateInformation.SentBlockHashs.TryRemove(key);
            }
            await _distributedCache.SetAsync(_blockSynState, _blockSyncStateInformation);
        }

        _logger.LogInformation(
            $"BlockSyncState delete SentBlockHashs before lib, libHeight: {libHeight} ");
    }
}

public class SyncInformation
{
    public long CurrentHeight { get; set; }
    public SyncState State { get; set; }
    public ConcurrentDictionary<string, PreBlock> SentBlockHashs { set; get; }
}

public class PreBlock
{
    public string BlockHash { set; get; }
    public long Height { set; get; }

}