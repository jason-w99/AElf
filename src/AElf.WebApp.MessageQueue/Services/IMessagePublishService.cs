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
    Task<bool> PublishListAsync(List<BlockEto> Blocks);
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

    public async Task<bool> PublishListAsync(List<BlockEto> blocks)
    {
        _logger.LogInformation($"PublishList Start publish block: {blocks.First().Height} ~{blocks.Last().Height }.");
        try
        {
            var block = blocks.Last(); 
            Dictionary<string, PreBlock> blocksHash = new Dictionary<string, PreBlock>();
            List<BlockEto> mainBlocks = new List<BlockEto>();
            List<BlockEto> forkBlocks = new List<BlockEto>();
            List<BlockEto> saveForkBlocks = new List<BlockEto>();
            
            var  blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
            if (blockSyncState.SentBlockHashs.ContainsKey(block.BlockHash) 
                || block.Height< _messageQueueOptions.StartPublishMessageHeight)
            {
                return true;
            }
            mainBlocks.Add(block);
            blocksHash.Add(block.BlockHash,new PreBlock(){BlockHash = block.PreviousBlockHash,Height = block.Height-1});

            var preHash = block.PreviousBlockHash;
            var preBlockId = block.PreviousBlockId;
            int i = 1;
            //check datalist
            while (true)
            {
                if (mainBlocks.Count>=blocks.Count)
                {
                    break;
                }
                if (blockSyncState.SentBlockHashs.ContainsKey(preHash) 
                    || block.Height-i< _messageQueueOptions.StartPublishMessageHeight 
                    || blocksHash.ContainsKey(preHash))
                {
                    break;
                }
                i += 1;
                var preBlock = blocks.Find(c => c.BlockHash == preHash);
                if (preBlock!=null)
                {
                    mainBlocks.Add(preBlock);
                    blocksHash.Add(preBlock.BlockHash,new PreBlock(){BlockHash = preBlock.PreviousBlockHash,Height = preBlock.Height-1});
                    preHash = preBlock.BlockHash;
                    preBlockId = preBlock.PreviousBlockId;
                    continue;
                }
                var dbBlock = await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(preBlockId);
                
                var dataBlock=blocks.Find(c => c.Height == preBlock.Height);
                if (dataBlock!=null)
                {
                    forkBlocks.Add(dataBlock);
                    blocksHash.Add(dataBlock.BlockHash,new PreBlock(){BlockHash = dataBlock.PreviousBlockHash,Height = dataBlock.Height-1});
                }
                mainBlocks.Add(dbBlock);
                blocksHash.Add(dbBlock.BlockHash,new PreBlock(){BlockHash = dbBlock.PreviousBlockHash,Height = dbBlock.Height-1});
                preHash = dbBlock.PreviousBlockHash;
                preBlockId = dbBlock.PreviousBlockId;
            }
            
            //check forklist
            foreach (var forkBlock in forkBlocks)
            {
                if (blockSyncState.SentBlockHashs.ContainsKey(forkBlock.BlockHash) 
                    || forkBlock.Height< _messageQueueOptions.StartPublishMessageHeight 
                    || blocksHash.ContainsKey(forkBlock.BlockHash)
                    || saveForkBlocks.Contains(forkBlock))
                {
                    continue;
                }
                saveForkBlocks.Add(forkBlock);
                blocksHash.Add(forkBlock.BlockHash,new PreBlock(){BlockHash = forkBlock.PreviousBlockHash,Height = forkBlock.Height-1});
                int j = 1;
                var preForkHash = forkBlock.PreviousBlockHash;
                var preForkBlockId = forkBlock.PreviousBlockId;
                while (true)
                {
                    
                    if (blockSyncState.SentBlockHashs.ContainsKey(preForkHash) 
                        || forkBlock.Height-j< _messageQueueOptions.StartPublishMessageHeight 
                        || blocksHash.ContainsKey(preForkHash))
                    {
                        break;
                    }
                    j += 1;
                    var preBlock = blocks.Find(c => c.BlockHash == preHash);
                    if (preBlock!=null)
                    {
                        saveForkBlocks.Add(preBlock);
                        blocksHash.Add(preBlock.BlockHash,new PreBlock(){BlockHash = preBlock.PreviousBlockHash,Height = preBlock.Height-1});
                        preForkHash = preBlock.BlockHash;
                        preForkBlockId = preBlock.PreviousBlockId;
                        continue;
                    }
                    var dbBlock = await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(preForkBlockId);
                    
                    saveForkBlocks.Add(dbBlock);
                    blocksHash.Add(dbBlock.BlockHash,new PreBlock(){BlockHash = dbBlock.PreviousBlockHash,Height = dbBlock.Height-1});
                    preForkHash = dbBlock.PreviousBlockHash;
                    preForkBlockId = dbBlock.PreviousBlockId;
                }
            }
            
            await _syncBlockStateProvider.AddBlocksHashAsync(blocksHash);
            var blockChainDataEto = new BlockChainDataEto();
            blockChainDataEto.ChainId = blocks.First().ChainId;
            if (saveForkBlocks.Count>0)
            {
                mainBlocks.AddRange(saveForkBlocks);
            }
            blockChainDataEto.Blocks = mainBlocks;
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
            /*while (IsContinue(message,blockSyncState))
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
            }*/
            while (true)
            {
                if (!blockSyncState.SentBlockHashs.ContainsKey(message.BlockHash)
                    && message.BlockHash != Hash.Empty.ToString()
                    && message.Height>= _messageQueueOptions.StartPublishMessageHeight)
                {
                    break;
                }
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
        return !blockSyncState.SentBlockHashs.ContainsKey(blockEto.BlockHash) &&
               blockEto.BlockHash != Hash.Empty.ToString() && blockEto.Height>= _messageQueueOptions.StartPublishMessageHeight;
    }
}