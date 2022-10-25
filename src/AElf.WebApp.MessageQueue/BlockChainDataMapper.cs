using System.Collections.Generic;
using AElf.Kernel.Blockchain;
using Volo.Abp.DependencyInjection;
using Volo.Abp.ObjectMapping;

namespace AElf.WebApp.MessageQueue;

public class BlockChainDataMapper: IObjectMapper<BlockExecutedSet, BlockEto>,
    ITransientDependency
{
    private readonly IAutoObjectMappingProvider _mapperProvider;

    public BlockChainDataMapper(IAutoObjectMappingProvider mapperProvider)
    {
        _mapperProvider = mapperProvider;
    }
    
    public BlockEto Map(BlockExecutedSet source)
    {
        var block = source.Block;
        BlockChainDataEto blockChainDataEto = new BlockChainDataEto()
        {
            ChainId =ChainHelper.ConvertChainIdToBase58(block.Header.ChainId)
        };

        var blockHash = block.Header.GetHash();
        var blockHashStr = blockHash.ToHex();
        var blockHeight = block.Height;
        var blockTime = block.Header.Time.ToDateTime();
            
        BlockEto blockEto = new BlockEto()
        {
            Height = blockHeight,
            ChainId = ChainHelper.ConvertChainIdToBase58(block.Header.ChainId),
            BlockHash=blockHashStr,
            BlockNumber= blockHeight,
            PreviousBlockId=block.Header.PreviousBlockHash,
            PreviousBlockHash= block.Header.PreviousBlockHash.ToHex(),
            BlockTime=blockTime,
            SignerPubkey=block.Header.SignerPubkey.ToByteArray().ToHex(false),
            Signature=block.Header.Signature.ToHex(),
        };
        //blockEto's extra properties
        Dictionary<string, string> blockExtraProperties = new Dictionary<string, string>();
        blockExtraProperties.Add("Version",block.Header.Version.ToString());
        blockExtraProperties.Add("Bloom",block.Header.Bloom.ToBase64());
        blockExtraProperties.Add("ExtraData",block.Header.ExtraData.ToString());
        blockExtraProperties.Add("MerkleTreeRootOfTransactions",block.Header.MerkleTreeRootOfTransactions.ToHex());
        blockExtraProperties.Add("MerkleTreeRootOfWorldState",block.Header.MerkleTreeRootOfWorldState.ToHex());
        blockEto.ExtraProperties = blockExtraProperties;

        List<TransactionEto> transactions = new List<TransactionEto>();
        if (source.TransactionResultMap!=null)
        {
            
            int transactionIndex = 0;
            int eventIndex = 0;
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
                    Params=transaction.Params.ToBase64(),
                    Signature=transaction.Signature.ToBase64(),
                    Status=transactionResult.Status,
                    Index = transactionIndex

                };
                transactionIndex += 1;
                //TransactionEto's  extra properties
                Dictionary<string, string> transactionExtraProperties = new Dictionary<string, string>();
                transactionExtraProperties.Add("Version",block.Header.Version.ToString());
                transactionExtraProperties.Add("RefBlockNumber",transaction.RefBlockNumber.ToString());
                transactionExtraProperties.Add("RefBlockPrefix",transaction.RefBlockPrefix.ToHex());
                transactionExtraProperties.Add("Bloom",transactionResult.Bloom.ToBase64());
                transactionExtraProperties.Add("ReturnValue",transactionResult.ReturnValue.ToHex());
                transactionExtraProperties.Add("Error",transactionResult.Error);

                transactionEto.ExtraProperties = transactionExtraProperties;

                List<LogEventEto> logEvents = new List<LogEventEto>();
                
                if (transactionResult.Logs!=null)
                {
                    foreach (var logEvent in transactionResult.Logs)
                    {
         
                        LogEventEto logEventEto = new LogEventEto()
                        {
                            ContractAddress=logEvent.Address.ToBase58(),
                            EventName=logEvent.Name,
                            Index =eventIndex
                        
                        };
                        //logEventEto's  extra properties
                        Dictionary<string, string> logEventEtoExtraProperties = new Dictionary<string, string>();
                        logEventEtoExtraProperties.Add("Indexed",logEvent.Indexed.ToString());
                        logEventEtoExtraProperties.Add("NonIndexed",logEvent.NonIndexed.ToString());
                        logEventEto.ExtraProperties = logEventEtoExtraProperties;
                   
                        logEvents.Add(logEventEto);
                        eventIndex += 1;
                    }

                    
                }
                
                transactionEto.LogEvents = logEvents;
                
                transactions.Add(transactionEto);
            }
        }
        
        blockEto.Transactions = transactions;
        return blockEto;
    }

    public BlockEto Map(BlockExecutedSet source, BlockEto destination)
    {
        throw new System.NotImplementedException();
    }
    
    
}