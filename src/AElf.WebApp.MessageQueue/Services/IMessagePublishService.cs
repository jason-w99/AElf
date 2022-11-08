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
    Task<long> PublishListAsync(List<BlockEto> Blocks);
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

    public async Task<long> PublishListAsync(List<BlockEto> blocks)
    {
        _logger.LogInformation($"PublishList Start publish block: {blocks.First().Height} ~{blocks.Last().Height }.");
        try
        {
            var block = blocks.Last(); 
            List<BlockEto> mainBlocks = new List<BlockEto>();
            List<BlockEto> forkBlocks = new List<BlockEto>();
            List<BlockEto> saveForkBlocks = new List<BlockEto>();
            
            var  blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
            
            //check datalist
            while (true)
            {
                _logger.LogDebug($"PublishList blockSyncState SentBlockHash is contains block hash : {blockSyncState.SentBlockHashs.ContainsKey(block.BlockHash) } | StartPublishMessageHeight : {_messageQueueOptions.StartPublishMessageHeight }.");
                if (blockSyncState.SentBlockHashs.ContainsKey(block.BlockHash) 
                    || block.Height < _messageQueueOptions.StartPublishMessageHeight )
                {
                    break;
                }
                mainBlocks.Add(block);
                //await _syncBlockStateProvider.AddBlockHashAsync(block.BlockHash,block.PreviousBlockHash, block.Height-1);
                blockSyncState.SentBlockHashs.Add(block.BlockHash,new PreBlock(){BlockHash = block.PreviousBlockHash,Height = block.Height-1});
                _logger.LogDebug($"PublishList blockSyncState SentBlockHash count: {blockSyncState.SentBlockHashs.Count} | Start publish block: {blocks.First().Height} ~{blocks.Last().Height }.");
                _logger.LogDebug($"PublishList blockSyncState mainBlocks count: {mainBlocks.Count} | Start publish block: {blocks.First().Height} ~{blocks.Last().Height }.");

                var preBlock = blocks.Find(c => c.BlockHash == block.PreviousBlockHash);
                if (preBlock != null)
                {
                    block = preBlock;
                    continue;
                }

               
                preBlock = await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(block.PreviousBlockId);
                if (preBlock==null)
                {
                    break;
                }
                var dataBlock=blocks.Find(c => c.Height == preBlock.Height);
                if (dataBlock!=null)
                {
                    forkBlocks.Add(dataBlock);
                }
                block = preBlock;
            }
            
            //check forklist
            foreach (var itemBlock in forkBlocks)
            {
                var forkBlock = itemBlock;
                while (true)
                {
                    if (blockSyncState.SentBlockHashs.ContainsKey(forkBlock.BlockHash) 
                        || forkBlock.Height< _messageQueueOptions.StartPublishMessageHeight 
                        || saveForkBlocks.Contains(forkBlock))
                    {
                        break;
                    }
                    saveForkBlocks.Add(forkBlock);
                    _logger.LogDebug($"PublishList blockSyncState saveForkBlocks count: {forkBlocks.Count} | Start publish block: {blocks.First().Height} ~{blocks.Last().Height }.");
                    blockSyncState.SentBlockHashs.Add(forkBlock.BlockHash,new PreBlock(){BlockHash = forkBlock.PreviousBlockHash,Height = forkBlock.Height-1});
                    var preBlock = blocks.Find(c => c.BlockHash == forkBlock.PreviousBlockHash);
                    if (preBlock != null)
                    {
                        forkBlock = preBlock;
                        continue;
                    }
                    preBlock = await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(forkBlock.PreviousBlockId);
                    if (preBlock==null)
                    {
                        break;
                    }
                    forkBlock = preBlock;
                }
            }
            
            long sendedHeight = blocks.Last().Height;
            _logger.LogInformation($"PublishList -- publish last block height: {sendedHeight} .");
            if (mainBlocks.Count==0)
            {
                await _syncBlockStateProvider.UpdateStateAsync(sendedHeight);
                _logger.LogInformation($"PublishList Success(mainBlocks Count is 0)  publish block: {blocks.First().Height} ~{blocks.Last().Height }.");
                return sendedHeight+1;
            }
            mainBlocks = mainBlocks.OrderBy(c => c.Height).ToList();
            sendedHeight=mainBlocks.Last().Height;
            var blockChainDataEto = new BlockChainDataEto();
            blockChainDataEto.ChainId = blocks.First().ChainId;
            if (saveForkBlocks.Count>0)
            {
                saveForkBlocks = saveForkBlocks.OrderBy(c => c.Height).ToList();
                saveForkBlocks.AddRange(mainBlocks);
                blockChainDataEto.Blocks = saveForkBlocks;
            }
            else
            {
                blockChainDataEto.Blocks = mainBlocks;
            }
            await _distributedEventBus.PublishAsync(blockChainDataEto);
            await _syncBlockStateProvider.UpdateBlocksHashAsync(blockSyncState.SentBlockHashs);
            await _syncBlockStateProvider.UpdateStateAsync(sendedHeight);
            
            _logger.LogInformation($"PublishList Success publish block: {blocks.First().Height} ~{blocks.Last().Height }.");
            return sendedHeight +1;

        }
        catch (Exception e)
        {
            _logger.LogError(e,$"Failed to publish events to mq service.\n{e.Message}");
            return -1;
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
            var blockSyncStateTemp = blockSyncState;
            List<BlockEto> blockEtos = new List<BlockEto>();

            var isContains = blockSyncState.SentBlockHashs.ContainsKey(message.BlockHash);
            _logger.LogDebug($"blockSyncState is contains the message height : {isContains}");
            
            if (isContains
                || message.BlockHash == Hash.Empty.ToString()
                || message.Height < _messageQueueOptions.StartPublishMessageHeight)
            {
                return true;
            }
            blockEtos.Add(message);
            blockSyncState.SentBlockHashs.Add(message.BlockHash,new PreBlock(){BlockHash=message.PreviousBlockHash,Height = message.Height-1});
            var preHash = message.PreviousBlockHash;
            var preBlockId = message.PreviousBlockId;
            int i = 1;
            while (true)
            {
                if (blockSyncState.SentBlockHashs.ContainsKey(preHash)
                    || preHash == Hash.Empty.ToString()
                    || message.Height-i < _messageQueueOptions.StartPublishMessageHeight)
                {
                    break;
                }
                
                _logger.LogDebug($" preHash hash: {preHash} and  height:{message.Height-i}");

                i += 1;
                var preBlock = await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(preBlockId );
                if (preBlock == null)
                {
                    break;
                }
                blockEtos.Add(preBlock);
                blockSyncState.SentBlockHashs.Add(preBlock.BlockHash,new PreBlock(){BlockHash=preBlock.PreviousBlockHash,Height = preBlock.Height-1});
                preHash = preBlock.PreviousBlockHash;
                preBlockId = preBlock.PreviousBlockId;
            }

            if (blockEtos.Count==0)
            {
                return true;
            }
            var blockChainDataEto = new BlockChainDataEto()
            {
                ChainId =message.ChainId,
                Blocks = blockEtos.OrderBy(c=>c.Height).ToList()
            };
          
            _logger.LogDebug($"{runningPattern}  publish block count: {blockEtos.Count}. the block height is {message.Height} ");

            if (blockEtos.Count>500)
            {
                foreach (var block in  blockSyncStateTemp.SentBlockHashs)
                {
                    _logger.LogDebug($" block hash: {block.Key}.and pre info height:{block.Value.Height} ___hash :{block.Value.BlockHash}");
                }
            }
            
            var k = 0;
            while (true)
            {
                if (k==5)
                {
                    break;
                }
                try
                {
                    await _distributedEventBus.PublishAsync(blockChainDataEto);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,$"Failed to publish events to mq service.\n {ex.Message}__________"+message.Height+"retry count:"+k+1 );
                    Thread.Sleep(1000);
                    k += 1;
                }
            }



            if (k==4)
            {
                return false;
            }
            await _syncBlockStateProvider.UpdateBlocksHashAsync(blockSyncState.SentBlockHashs);
            _logger.LogInformation($"{runningPattern} End publish block: {message.Height}.");
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e,$"Failed to publish events to mq service.\n{e.Message}");
            return false;
        }
    }

    private bool IsContinue(BlockEto blockEto,SyncInformation blockSyncState)
    {
        return !blockSyncState.SentBlockHashs.ContainsKey(blockEto.BlockHash) &&
               blockEto.BlockHash != Hash.Empty.ToString() && blockEto.Height>= _messageQueueOptions.StartPublishMessageHeight;
    }
}