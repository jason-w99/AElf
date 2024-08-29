using System;
using System.Threading.Tasks;
using AElf.Modularity;
using AElf.WebApp.Application;
using AElf.WebApp.MessageQueue.Enum;
using AElf.WebApp.MessageQueue.Helpers;
using AElf.WebApp.MessageQueue.Provider;
using AElf.WebApp.MessageQueue.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.AutoMapper;
using Volo.Abp.Caching;
using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.Modularity;
using Volo.Abp.Threading;

namespace AElf.WebApp.MessageQueue;

[DependsOn(typeof(AbpAutoMapperModule), 
     //typeof(AbpEventBusRabbitMqModule), 
     typeof(CoreApplicationWebAppAElfModule),
     typeof(AbpCachingModule),
     typeof(AbpCachingStackExchangeRedisModule)
    )]
public class MessageQueueAElfModule : AElfModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        Configure<AbpAutoMapperOptions>(options => { options.AddMaps<MessageQueueAElfModule>(); });
        Configure<MessageQueueOptions>(options => { configuration.GetSection("MessageQueue").Bind(options); });
        ConfigureCache();
        context.Services.AddTransient<IBlockMessageEtoGenerator, TransactionListEtoGenerator>();
        context.Services.AddTransient<IBlockChainDataEtoGenerator, BlockChainDataEtoGenerator>();
        context.Services.AddTransient<ISendMessageService, SendMessageService>();
        context.Services.AddTransient<ITransformEtoHelper, TransformEtoHelper>();
        var chainId = configuration.GetSection("MessageQueue:ChainId").Value;
        Configure<AbpDistributedCacheOptions>(options =>
        {
            options.KeyPrefix =$"BlockSyncState-{chainId}";
            options.GlobalCacheEntryOptions = new DistributedCacheEntryOptions()
            {
                AbsoluteExpiration=DateTimeOffset.Now.AddYears(100)
            };
        });
        
    }

    
     

     private void ConfigureCache()
     {
         Configure<AbpDistributedCacheOptions>(options => { options.KeyPrefix = "EventFilterApp:"; });
     }

     public override void OnPreApplicationInitialization(ApplicationInitializationContext context)
     {
         AsyncHelper.RunSync(async () =>
         {
             var syncBlockStateProvider = context.ServiceProvider.GetRequiredService<ISyncBlockStateProvider>();
             await syncBlockStateProvider.InitializeAsync();
         });
     }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        AsyncHelper.RunSync(() => OnApplicationShutdownAsync(context));
    }

    public override async Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
    {
        var taskManageService = context.ServiceProvider.GetRequiredService<ISendMessageByDesignateHeightTaskManager>();
        await taskManageService.StopAsync(true);
        var syncBlockStateProvider = context.ServiceProvider.GetRequiredService<ISyncBlockStateProvider>();
        await syncBlockStateProvider.UpdateStateAsync(null, SyncState.Stopped);
    }
}