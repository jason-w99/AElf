using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AElf.Kernel;
using AElf.Kernel.Blockchain;
using AElf.Kernel.Blockchain.Application;
using AElf.Kernel.Blockchain.Domain;
using AElf.Kernel.FeeCalculation.Extensions;
using AElf.Types;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Volo.Abp.ObjectMapping;


namespace AElf.WebApp.MessageQueue.Helpers;

public class BlockChainDataEtoGenerator : IBlockChainDataEtoGenerator
{
    
    private readonly IBlockchainService _blockchainService;
    private readonly ITransactionResultQueryService _transactionResultQueryService;
    private readonly ITransactionManager _transactionManager;
    private readonly IObjectMapper _objectMapper;
    private readonly ILogger<TransactionListEtoGenerator> _logger;
    private readonly ITransformEtoHelper _transformEtoHelper;

    
    public BlockChainDataEtoGenerator(IBlockchainService blockchainService,
        ITransactionResultQueryService transactionResultQueryService, ITransactionManager transactionManager,
        IObjectMapper objectMapper, ILogger<TransactionListEtoGenerator> logger, ITransformEtoHelper transformEtoHelper)
    {
        _blockchainService = blockchainService;
        _transactionResultQueryService = transactionResultQueryService;
        _transactionManager = transactionManager;
        _objectMapper = objectMapper;
        _logger = logger;
        _transformEtoHelper = transformEtoHelper;
    }
    
   
    public async Task<BlockEto> GetBlockMessageEtoByHeightAsync(long height, CancellationToken cts)
    {
        var block = await GetBlockByHeightAsync(height);
        if (block==null)
        {
            return null;
        }
        return await GetBlockMessageEtoByBlockAsync(block,cts.IsCancellationRequested);
    }

    public async Task<BlockEto> GetBlockMessageEtoByHashAsync(Hash blockId)
    {
        Block block = await _blockchainService.GetBlockByHashAsync(blockId);
        if (block==null)
        {
            return null;
        }
        return await GetBlockMessageEtoByBlockAsync(block,false);
    }

    private async Task<BlockEto> GetBlockMessageEtoByBlockAsync(Block  block, bool isCancellationRequested )
    {
       
        if (block == null)
        {
            _logger.LogWarning($"Failed to find block information, height: {block.Height}");
            return null;
        }
        var blockHash = block.Header.GetHash();
        var blockHashStr = blockHash.ToHex();
        var blockHeight = block.Height;
        var blockTime = block.Header.Time.ToDateTime();
        BlockEto blockEto = _transformEtoHelper.ToBlockEtoAsync(block);
        List<TransactionEto> transactions = new List<TransactionEto>();
        int transactionIndex = 0;
        int eventIndex = 0;
        foreach (var txId in block.TransactionIds)
        {
            if (isCancellationRequested)
            {
                return null;
            }

            var transactionResult = await _transactionResultQueryService.GetTransactionResultAsync(txId, blockHash);
            if (transactionResult == null)
            {
                _logger.LogWarning(
                    $"Failed to find transactionResult, block hash: {blockHash},  transaction ID: {txId}");
                continue;
            }

            var transaction = await _transactionManager.GetTransactionAsync(txId);
            if (transaction == null)
            {
                _logger.LogWarning($"Failed to find transaction, block hash: {blockHash},  transaction ID: {txId}");
                continue;
            }
            TransactionEto transactionEto =
                _transformEtoHelper.ToTransactionEtoAsync(transaction, transactionResult, transactionIndex,eventIndex, txId.ToHex(),block.Header.Version.ToString());
            transactionIndex += 1;
            transactions.Add(transactionEto);
        }
        blockEto.Transactions = transactions;
        return blockEto;
    }

    public BlockEto GetBlockMessageEto(BlockExecutedSet blockExecutedSet)
    {

       return _objectMapper.Map<BlockExecutedSet, BlockEto>(blockExecutedSet);
      
        
    }

    
    private async Task<Block> GetBlockByHeightAsync(long height)
    {
        var chain = await _blockchainService.GetChainAsync();
        var hash = await _blockchainService.GetBlockHashByHeightAsync(chain, height, chain.LongestChainHash);
        //var blocks = await _blockchainService.GetBlocksInLongestChainBranchAsync(hash, 1);
        return await _blockchainService.GetBlockByHashAsync(hash);
    }

   
}