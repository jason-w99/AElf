using AElf.Kernel.Blockchain.Events;
using AElf.WebApp.MessageQueue;
using Shouldly;
using Volo.Abp.AspNetCore.TestBase;
using Volo.Abp.EventBus.Local;
using Xunit;

namespace AElf.WebApp.Application.MessageQueue.Tests;

public class BlockAcceptedEventHandlerTests:AbpAspNetCoreIntegratedTestBase<TestModule>
{
    private readonly ILocalEventBus _eventBus;
    
    public BlockAcceptedEventHandlerTests()
    {
       
        _eventBus = GetRequiredService<ILocalEventBus>();
    }
    
    
    [Fact]
    public async Task Set_BestChain_Success()
    {
        BlockChainDataEto eventData = null;
        _eventBus.Subscribe<BlockChainDataEto>(d =>
        {
            eventData = d;
            return Task.CompletedTask;
        });
        //做验证的操作



        eventData.ShouldNotBeNull();
        
    }

}