using AElf.Kernel.Account.Application;
using AElf.OS;
using AElf.WebApp.MessageQueue;
using AElf.WebApp.Web;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Volo.Abp.AspNetCore.TestBase;
using Volo.Abp.Autofac;
using Volo.Abp.EventBus;
using Volo.Abp.Modularity;

namespace AElf.WebApp.Application.MessageQueue.Tests;


[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreTestBaseModule),
    typeof(MessageQueueAElfModule),
    typeof(AbpEventBusModule)
)]
public class TestModule: AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        //需要mock chain
        services.AddSingleton(p => Mock.Of<>());
    }
}