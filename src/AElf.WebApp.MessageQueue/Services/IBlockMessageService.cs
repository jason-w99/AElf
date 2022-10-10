using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AElf.Kernel.Blockchain;
using AElf.WebApp.MessageQueue.Helpers;
using AElf.WebApp.MessageQueue.Provider;
using AutoMapper.Internal;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Volo.Abp.DependencyInjection;

namespace AElf.WebApp.MessageQueue.Services;

public interface IBlockMessageService
{
    Task<bool> SendMessageAsync(long height, CancellationToken cts = default);
    Task<bool> SendMessageAsync(BlockExecutedSet blockExecutedSet);
    Task<long> SendMessageAsync(long from, long to, CancellationToken cts);
}

public class BlockMessageService : IBlockMessageService, ITransientDependency
{
    private readonly IMessagePublishService _messagePublishService;
    private readonly ISyncBlockStateProvider _syncBlockStateProvider;
    private readonly IBlockChainDataEtoGenerator _blockChainDataEtoGenerator;
    private readonly ILogger _logger;

    public BlockMessageService(IMessagePublishService messagePublishService,
        ISyncBlockStateProvider syncBlockStateProvider, IBlockChainDataEtoGenerator blockChainDataEtoGenerator, ILogger<BlockMessageService> logger)
    {
        _messagePublishService = messagePublishService;
        _syncBlockStateProvider = syncBlockStateProvider;
        _blockChainDataEtoGenerator = blockChainDataEtoGenerator;
        _logger = logger;
    }


    public async Task<bool> SendMessageAsync(long height, CancellationToken cts)
    {
        var isSuccess = await _messagePublishService.PublishAsync(height, cts);
        if (!isSuccess)
            return false;
        await _syncBlockStateProvider.UpdateStateAsync(height + 1);
        return true;
    }

    public async Task<bool> SendMessageAsync(BlockExecutedSet blockExecutedSet)
    {
        var isSuccess = await _messagePublishService.PublishAsync(blockExecutedSet);
        if (!isSuccess)
            return false;
        await _syncBlockStateProvider.UpdateStateAsync(blockExecutedSet.Height);
        return true;
    }

    public async Task<long> SendMessageAsync(long from, long to, CancellationToken cts)
    {
        var queryTasks = new List<Task>();
        var blockMessageList = new ConcurrentBag<BlockEto>();
        for (var i = from; i <= to; i++)
        {
            queryTasks.Add(QueryBlockMessageAsync(i, blockMessageList, cts));
        }

        await queryTasks.WhenAll();
        if (!blockMessageList.Any())
        {
            _logger.LogError($"Failed to query message from: {from + 1} to: {to + 1}, 0 messages found");
            return -1;
        }

        var queryHeightLog = new StringBuilder($"Query height from: {from} to: {to}: ");
        blockMessageList.ForAll(b => queryHeightLog.Append($"|{b.Height}|"));
        _logger.LogInformation(queryHeightLog.ToString());
        var sortedMessageQuery = blockMessageList.OrderBy(b => b.Height);
        long heightIndex = 0;
        BlockChainDataEto blockChainDataEto = new BlockChainDataEto();
        List<BlockEto> blockEto = new List<BlockEto>();
        foreach (var message in sortedMessageQuery)
        {
            if (heightIndex == 0)
            {
                if (message.Height != from + 1)
                {
                    _logger.LogError($"Failed to query message from: {from + 1} to: {to + 1}");
                    return -1;
                }

                heightIndex = message.Height - 1;
            }

            if (cts.IsCancellationRequested)
            {
                break;
            }

            if (message.Height == heightIndex + 1 )
            {
                blockEto.Add(message);
                await _syncBlockStateProvider.UpdateStateAsync(message.Height);
                heightIndex = message.Height;
                continue;
            }
            

            break;
        }

        if (heightIndex!=-1)
        {
            blockChainDataEto.ChainId = blockEto.First().ChainId;
            blockChainDataEto.Blocks = blockEto;
            var isSuccess=await _messagePublishService.PublishListAsync(blockChainDataEto);
            if (!isSuccess)
            {
                return -1;
            }
        }

        return heightIndex;
    }
    
    private async Task QueryBlockMessageAsync(long height, ConcurrentBag<BlockEto> blockMessageList,
        CancellationToken cts)
    {
        var blockMessage = await _blockChainDataEtoGenerator.GetBlockMessageEtoByHeightAsync(height, cts);
        if (blockMessage == null)
            return;
        blockMessageList.Add(blockMessage);
    }
}