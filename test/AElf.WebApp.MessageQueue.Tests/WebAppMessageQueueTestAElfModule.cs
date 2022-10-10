using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf.Kernel;
using AElf.Kernel.Account.Application;
using AElf.Kernel.Blockchain.Application;
using AElf.OS;
using AElf.OS.Node.Application;
using AElf.Types;
using AElf.WebApp.MessageQueue;
using AElf.WebApp.MessageQueue.Helpers;
using AElf.WebApp.MessageQueue.Provider;
using AElf.WebApp.MessageQueue.Services;
using AElf.WebApp.Web;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Volo.Abp;
using Volo.Abp.AspNetCore.TestBase;
using Volo.Abp.Autofac;
using Volo.Abp.EventBus;
using Volo.Abp.Modularity;
using Volo.Abp.Threading;

namespace AElf.WebApp.Application.MessageQueue.Tests;


[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreTestBaseModule),
    typeof(MessageQueueAElfModule),
    typeof(KernelCoreTestAElfModule),
    typeof(AbpEventBusModule)
)]
public class WebAppMessageQueueTestAElfModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        base.ConfigureServices(context);
        var services = context.Services;
        //需要mock chain
        //services.AddSingleton(p => Mock.Of<>());
        services.AddDistributedMemoryCache();
    }
    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var kernelTestHelper = context.ServiceProvider.GetService<KernelTestHelper>();
        var chain = AsyncHelper.RunSync(() => kernelTestHelper.MockChainAsync());
    }
}