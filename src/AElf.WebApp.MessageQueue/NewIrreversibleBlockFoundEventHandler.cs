using System;
using System.Threading.Tasks;
using AElf.Kernel.Blockchain.Events;
using AElf.WebApp.MessageQueue.Provider;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;

namespace AElf.WebApp.MessageQueue;

public class NewIrreversibleBlockFoundEventHandler  : ILocalEventHandler<NewIrreversibleBlockFoundEvent>, ITransientDependency
{
    private readonly ISyncBlockStateProvider _syncBlockStateProvider;
    private readonly ILogger<NewIrreversibleBlockFoundEventHandler> _logger;


    public NewIrreversibleBlockFoundEventHandler(ISyncBlockStateProvider syncBlockStateProvider, ILogger<NewIrreversibleBlockFoundEventHandler> logger)
    {
        _syncBlockStateProvider = syncBlockStateProvider;
        _logger = logger;
    }

    public async Task HandleEventAsync(NewIrreversibleBlockFoundEvent eventData)
    {
        _logger.LogDebug($" The new lib is: {eventData.BlockHeight}.");

        //The cache data before lib needs to be deleted 
        await _syncBlockStateProvider.DeleteBlockHashAsync(eventData.BlockHeight);
    }
}