using System;
using System.Threading.Tasks;
using AElf.Kernel.Blockchain.Events;
using AElf.WebApp.MessageQueue.Provider;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;

namespace AElf.WebApp.MessageQueue;

public class NewIrreversibleBlockFoundEventHandler  : ILocalEventHandler<NewIrreversibleBlockFoundEvent>, ITransientDependency
{
    private readonly ISyncBlockStateProvider _syncBlockStateProvider;


    public NewIrreversibleBlockFoundEventHandler(ISyncBlockStateProvider syncBlockStateProvider)
    {
        _syncBlockStateProvider = syncBlockStateProvider;
    }

    public async Task HandleEventAsync(NewIrreversibleBlockFoundEvent eventData)
    {
        //The cache data before lib needs to be deleted 
        string preLibHash = eventData.BlockHash.ToHex();
        //await CheckSendHash(preLibHash);
        var provider=await _syncBlockStateProvider.GetCurrentStateAsync();
        while (IsContinue(preLibHash,provider))
        {
            provider.SentBlockHashs.TryGetValue(preLibHash, out var preHash);
            await _syncBlockStateProvider.DeleteBlockHashAsync(preLibHash);
            preLibHash = preHash;
            provider=await _syncBlockStateProvider.GetCurrentStateAsync();
        }
        
    }

    private bool IsContinue( string preLibHash,SyncInformation provider )
    {
        return provider.SentBlockHashs.ContainsKey(preLibHash);
    }

}