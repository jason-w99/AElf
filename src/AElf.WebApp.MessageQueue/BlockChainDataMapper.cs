using System.Collections.Generic;
using AElf.Kernel;
using AElf.Kernel.Blockchain;
using AElf.Types;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.ObjectMapping;

namespace AElf.WebApp.MessageQueue;

public class BlockChainDataMapper: IObjectMapper<BlockExecutedSet, BlockChainDataEto>,
    ITransientDependency
{
    private readonly IAutoObjectMappingProvider _mapperProvider;

    public BlockChainDataMapper(IAutoObjectMappingProvider mapperProvider)
    {
        _mapperProvider = mapperProvider;
    }
    
    public BlockChainDataEto Map(BlockExecutedSet source)
    {
        var block = source.Block;
        BlockChainDataEto blockChainDataEto = new BlockChainDataEto()
        {
            ChainId = block.Header.ChainId.ToString()
        };
        List<BlockEto> blocks = new List<BlockEto>();


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
        
        List<TransactionEto> transactions = new List<TransactionEto>();
            
        foreach (var transactionResultKeyPair in source.TransactionResultMap)
        {
            var txId = transactionResultKeyPair.Key.ToHex();
            var transactionResult = transactionResultKeyPair.Value;
            if (!source.TransactionMap.TryGetValue(transactionResultKeyPair.Key, out var transaction))
            {
                continue;
            }
            TransactionEto transactionEto = new TransactionEto()
            {
                TransactionId = txId,
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
        
        blocks.Add(blockEto);
        blockChainDataEto.Blocks = blocks;
        return blockChainDataEto;
    }

    public BlockChainDataEto Map(BlockExecutedSet source, BlockChainDataEto destination)
    {
        throw new System.NotImplementedException();
    }
    
    
}