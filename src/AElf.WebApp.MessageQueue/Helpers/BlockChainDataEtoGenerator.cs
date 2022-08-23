using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AElf.Kernel;
using AElf.Kernel.Blockchain;
using AElf.Kernel.Blockchain.Application;
using AElf.Kernel.Blockchain.Domain;
using AElf.Types;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.ObjectMapping;


namespace AElf.WebApp.MessageQueue.Helpers;

public class BlockChainDataEtoGenerator : IBlockMessageEtoGenerator
{
    
    private readonly IBlockchainService _blockchainService;
    private readonly ITransactionResultQueryService _transactionResultQueryService;
    private readonly ITransactionManager _transactionManager;
    private readonly IObjectMapper _objectMapper;
    private readonly ILogger<TransactionListEtoGenerator> _logger;
    
    public BlockChainDataEtoGenerator(IBlockchainService blockchainService,
        ITransactionResultQueryService transactionResultQueryService, ITransactionManager transactionManager,
        IObjectMapper objectMapper, ILogger<TransactionListEtoGenerator> logger)
    {
        _blockchainService = blockchainService;
        _transactionResultQueryService = transactionResultQueryService;
        _transactionManager = transactionManager;
        _objectMapper = objectMapper;
        _logger = logger;
    }
    
   
    public async Task<IBlockMessage> GetBlockMessageEtoByHeightAsync(long height, CancellationToken cts)
    {
        var blocks = await GetBlockByHeightAsync(height);
        if (blocks == null)
        {
            _logger.LogWarning($"Failed to find block information, height: {height + 1}");
            return null;
        }
        
        var blockChainDataEto = new BlockChainDataEto
        {
            ChainId = blocks[0].Header.ChainId.ToString()
        };
        List<BlockEto> blockEtos = new List<BlockEto>();
        foreach (var block in blocks)
        {
            var blockHash = block.Header.GetHash();
            var blockHashStr = blockHash.ToHex();
            var blockHeight = block.Height;
            var blockTime = block.Header.Time.ToDateTime();
            
            BlockEto blockEto = new BlockEto()
            {
                BlockHash=blockHashStr,
                BlockNumber= blockHeight,
                PreviousBlockHash= block.Header.PreviousBlockHash.ToHex(),
                BlockTime=blockTime,
                SignerPubkey=block.Header.SignerPubkey.ToHex(),
                Signature=block.Header.Signature.ToHex(),
            };
            //blockEto's extra properties
            blockEto.SetProperty("Version",block.Header.Version);
            blockEto.SetProperty("Bloom",block.Header.Bloom.ToHex());
            blockEto.SetProperty("ExtraData",block.Header.ExtraData);
            blockEto.SetProperty("MerkleTreeRootOfTransactions",block.Header.MerkleTreeRootOfTransactions.ToHex());
            blockEto.SetProperty("MerkleTreeRootOfTransactions",block.Header.MerkleTreeRootOfWorldState.ToHex());
            
            //blockEto.SetVersion();
            List<TransactionEto> transactions = new List<TransactionEto>();
            
            foreach (var txId in block.TransactionIds)
            {
                if (cts.IsCancellationRequested)
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
                TransactionEto transactionEto = new TransactionEto()
                {
                    TransactionId = txId.ToHex(),
                    From = transaction.From.ToBase58(),
                    To = transaction.To.ToBase58(),
                    MethodName= transaction.MethodName,
                    Params=transaction.Params.ToString(),
                    Signature=transaction.Signature.ToString(),
                    Status=(int)transactionResult.Status,

                };
                //TransactionEto's  extra properties
                transactionEto.SetProperty("RefBlockNumber",transaction.RefBlockNumber);
                transactionEto.SetProperty("RefBlockPrefix",transaction.RefBlockPrefix.ToHex());
                transactionEto.SetProperty("Bloom",transactionResult.Bloom.ToHex());
                transactionEto.SetProperty("ReturnValue",transactionResult.ReturnValue.ToHex());
                transactionEto.SetProperty("Error",transactionResult.Error);
                

                List<LogEventEto> logEvents = new List<LogEventEto>();
                int index = 0;
                foreach (var logEvent in transactionResult.Logs)
                {
         
                    LogEventEto logEventEto = new LogEventEto()
                    {
                        ContractAddress=logEvent.Address.ToBase58(),
                        EventName=logEvent.Name,
                        Index =index
                        
                    };
                    //logEventEto's  extra properties
                    logEventEto.SetProperty("Indexed", logEvent.Indexed);
                    logEventEto.SetProperty("NonIndexed", logEvent.NonIndexed.ToHex());
                    logEvents.Add(logEventEto);
                    index = index + 1;
                }

                transactionEto.LogEvents = logEvents;
                
                transactions.Add(transactionEto);
            }

            blockEto.Transactions = transactions;
            blockEtos.Add(blockEto);
        }


        blockChainDataEto.Blocks = blockEtos;
       
        return blockChainDataEto;
    }


    public IBlockMessage GetBlockMessageEto(BlockExecutedSet blockExecutedSet)
    {
        return _objectMapper.Map<BlockExecutedSet, BlockChainDataEto>(blockExecutedSet);
    }

    
    private async Task<List<Block>> GetBlockByHeightAsync(long height)
    {
        var chain = await _blockchainService.GetChainAsync();
        var hash = await _blockchainService.GetBlockHashByHeightAsync(chain, height, chain.LongestChainHash);
        var blocks = await _blockchainService.GetBlocksInLongestChainBranchAsync(hash, 2);
        return blocks.Any() ? blocks: null;
    }

   
}