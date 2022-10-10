using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AElf.Kernel.Blockchain;
using AElf.Types;
using AElf.WebApp.MessageQueue.Helpers;
using AElf.WebApp.MessageQueue.Provider;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace AElf.WebApp.MessageQueue.Services;

public interface IMessagePublishService
{
    Task<bool> PublishAsync(long height, CancellationToken cts);
    Task<bool> PublishAsync(BlockEto blockEto);
    Task<bool> PublishListAsync(BlockChainDataEto blockChainDataEto);
    Task<bool> PublishAsync(BlockExecutedSet blockExecutedSet);
}

public class MessagePublishService : IMessagePublishService, ITransientDependency
{
    private readonly IBlockChainDataEtoGenerator _blockChainDataEtoGenerator;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly ILogger<MessagePublishService> _logger;
    private readonly ISyncBlockStateProvider _syncBlockStateProvider;
    private const string Asynchronous = "Asynchronous";
    private const string Synchronous = "Synchronous";
    private readonly MessageQueueOptions _messageQueueOptions;
    public MessagePublishService(IDistributedEventBus distributedEventBus,
        IBlockChainDataEtoGenerator blockChainDataEtoGenerator, ILogger<MessagePublishService> logger, ISyncBlockStateProvider syncBlockStateProvider,  IOptionsSnapshot<MessageQueueOptions> messageQueueEnableOptions)
    {
        _distributedEventBus = distributedEventBus;
        _blockChainDataEtoGenerator = blockChainDataEtoGenerator;
        _logger = logger;
        _syncBlockStateProvider = syncBlockStateProvider;
        _messageQueueOptions = messageQueueEnableOptions.Value;
    }

    public async Task<bool> PublishAsync(long height, CancellationToken cts)
    {
        var blockMessageEto = await _blockChainDataEtoGenerator.GetBlockMessageEtoByHeightAsync(height, cts);
        if (blockMessageEto != null)
        {
            return await PublishAsync(blockMessageEto, Asynchronous);
        }

        return false;
    }

    public async Task<bool> PublishListAsync(BlockChainDataEto blockChainDataEto)
    {
        _logger.LogInformation($"PublishList Start publish block: {blockChainDataEto.Blocks.First().Height} ~{blockChainDataEto.Blocks.Last().Height }.");
        try
        {
            Dictionary<string, PreBlock> blocksHash = new Dictionary<string, PreBlock>();
            foreach (var block in blockChainDataEto.Blocks)
            {
                var  blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
                if (!blockSyncState.SentBlockHashs.ContainsKey(block.PreviousBlockHash) )
                {
                    var preBlock = await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(block.PreviousBlockId );
                    await PublishAsync(preBlock, Asynchronous);
                }
                blocksHash.Add(block.BlockHash,new PreBlock(){BlocHash = block.PreviousBlockHash,Height = block.Height-1});
            }
            await _syncBlockStateProvider.AddBlocksHashAsync(blocksHash);
            await _distributedEventBus.PublishAsync(blockChainDataEto);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError($"Failed to publish events to mq service.\n{e.Message}");
            return false;
        }
    }

    public async Task<bool> PublishAsync(BlockExecutedSet blockExecutedSet)
    {
        var blockMessageEto = _blockChainDataEtoGenerator.GetBlockMessageEto(blockExecutedSet);
        return await PublishAsync(blockMessageEto, Synchronous);
    }

    public async Task<bool> PublishAsync(BlockEto message)
    {
        return await PublishAsync(message, Asynchronous);
    }

    private async Task<bool> PublishAsync(BlockEto message, string runningPattern)
    {
        _logger.LogInformation($"{runningPattern} Start publish block: {message.Height}.");
        try
        {
            var  blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
            while (IsContinue(message,blockSyncState))
            {
                List<BlockEto> blockEtos = new List<BlockEto>();
                blockEtos.Add(message);
                var blockChainDataEto = new BlockChainDataEto()
                {
                    ChainId =message.ChainId,
                    Blocks = blockEtos
                };
                await _distributedEventBus.PublishAsync(blockChainDataEto);
                _logger.LogInformation($"{runningPattern} End publish block: {message.Height}.");
                //Added logic
                //The hash needs to be stored in the cache after each transmission ，
                await _syncBlockStateProvider.AddBlockHashAsync(message.BlockHash,message.PreviousBlockHash,message.Height-1);
                message = await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(message.PreviousBlockId );
                blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
            }
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError($"Failed to publish events to mq service.\n{e.Message}");
            return false;
        }
    }

    private bool IsContinue(BlockEto blockEto,SyncInformation blockSyncState)
    {
        return !blockSyncState.SentBlockHashs.ContainsKey(blockEto.PreviousBlockHash) &&
               blockEto.PreviousBlockId != Hash.Empty && blockEto.Height> _messageQueueOptions.StartPublishMessageHeight;
    }

    /*/// <summary>
    ///    It also needs to check whether the previous block exists in the cache by hash of the previous block, and if it does not
    ///    it keeps searching up (sending out every block that is not in the cache) until the query is reached
    /// </summary>
    /// <param name="blockEto"></param>
    /// <exception cref="NotImplementedException"></exception>
    private async Task CheckBlockContinuous(BlockEto blockEto)
    {
        var  blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        
        if (!blockSyncState.SentBlockHashs.ContainsKey(blockEto.PreviousBlockHash) && blockEto.PreviousBlockId!=Hash.Empty 
               // &&  blockEto.BlockHash!=blockSyncState.FirstSendBlockHash
            )
        {
            //This block needs to be queried and sent
            var blockMessageEto = await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(blockEto.PreviousBlockId );
            await PublishAsync(blockMessageEto, Asynchronous);
        }
        
    }*/
}