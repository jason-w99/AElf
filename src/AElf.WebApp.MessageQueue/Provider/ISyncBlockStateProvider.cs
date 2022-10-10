using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AElf.WebApp.MessageQueue.Enum;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    Task AddBlocksHashAsync(Dictionary<string, PreBlock> blocksHash);
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
        _blockSyncStateInformation.SentBlockHashs = new Dictionary<string, PreBlock>();
        
        _logger.LogInformation(
            $"BlockSyncState initialized, State: {_blockSyncStateInformation.State}  CurrentHeight: {_blockSyncStateInformation.CurrentHeight}");
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
            _blockSyncStateInformation.SentBlockHashs.Add(blockHash,new PreBlock(){BlocHash = preBlockHash,Height = preHeight});
            await _distributedCache.SetAsync(_blockSynState, _blockSyncStateInformation);
        }

        _logger.LogInformation(
            $"BlockSyncState AddBlockHashAsync, blockHash: {blockHash}  preBlockHash: {preBlockHash}");
    }
    
    public async Task AddBlocksHashAsync(Dictionary<string, PreBlock> blocksHash)
    {
        using (await SyncSemaphore.LockAsync())
        {
            _blockSyncStateInformation.SentBlockHashs= _blockSyncStateInformation.SentBlockHashs.Union(blocksHash).ToDictionary(k => k.Key, v => v.Value);
          
            await _distributedCache.SetAsync(_blockSynState, _blockSyncStateInformation);
        }
        _logger.LogInformation(
            $"BlockSyncState AddBlocksHashAsync ");
    }
    public async Task DeleteBlockHashAsync(long libHeight)
    {
        using (await SyncSemaphore.LockAsync())
        {
            List<string> keys = new List<string>();
            foreach (var sendBolck in _blockSyncStateInformation.SentBlockHashs)
            {
                if (sendBolck.Value.Height<libHeight)
                {
                    keys.Add(sendBolck.Key);
                }
                
            }

            foreach (var key in keys)
            {
                _blockSyncStateInformation.SentBlockHashs.Remove(key);
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
    public Dictionary<string, PreBlock> SentBlockHashs { set; get; }
}

public class PreBlock
{
    public string BlocHash { set; get; }
    public long Height { set; get; }

}