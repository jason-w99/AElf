using System.Collections.Generic;
using AElf.Kernel;
using AElf.Types;
using AElf.WebApp.MessageQueue.Extensions;
using Google.Protobuf;
using Newtonsoft.Json;

namespace AElf.WebApp.MessageQueue.Helpers;

public interface ITransformEtoHelper
{
    BlockEto ToBlockEtoAsync(Block block);
    TransactionEto ToTransactionEtoAsync(Transaction transaction,TransactionResult transactionResult,int transactionIndex,int eventIndex,string txId,string blockVersion);

}

public class TransformEtoHelper : ITransformEtoHelper
{
    public BlockEto ToBlockEtoAsync(Block block)
    {
        var blockHash = block.Header.GetHash();
        var blockHashStr = blockHash.ToHex();
        var blockHeight = block.Height;
        var blockTime = block.Header.Time.ToDateTime();
            
        BlockEto blockEto = new BlockEto()
        {
            Height = blockHeight,
            ChainId = ChainHelper.ConvertChainIdToBase58(block.Header.ChainId),
            BlockHash=blockHashStr,
            BlockHeight= blockHeight,
            PreviousBlockId=block.Header.PreviousBlockHash,
            PreviousBlockHash= block.Header.PreviousBlockHash.ToHex(),
            BlockTime=blockTime,
            SignerPubkey=block.Header.SignerPubkey.ToByteArray().ToHex(false),
            Signature=block.Header.Signature.ToHex(),
        };
        //blockEto's extra properties
        
        Dictionary<string, string> blockExtraProperties = new Dictionary<string, string>();
        blockExtraProperties.Add("Version",block.Header.Version.ToString());
        blockExtraProperties.Add("Bloom",block.Header.Bloom.Length == 0
            ? ByteString.CopyFrom(new byte[256]).ToBase64()
            : block.Header.Bloom.ToBase64());
        blockExtraProperties.Add("ExtraData",block.Header.ExtraData.ToString());
        blockExtraProperties.Add("MerkleTreeRootOfTransactions",block.Header.MerkleTreeRootOfTransactions.ToHex());
        blockExtraProperties.Add("MerkleTreeRootOfTransactionStatus",block.Header.MerkleTreeRootOfTransactionStatus.ToHex());
        blockExtraProperties.Add("MerkleTreeRootOfWorldState",block.Header.MerkleTreeRootOfWorldState.ToHex());
        blockExtraProperties.Add("BlockSize",block.CalculateSize().ToString());

        blockEto.ExtraProperties = blockExtraProperties;
        return blockEto;
    }

    public TransactionEto ToTransactionEtoAsync(Transaction transaction,TransactionResult transactionResult,int transactionIndex,int eventIndex,string txId,string blockVersion)
    {
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
        //TransactionEto's  extra properties
        Dictionary<string, string> transactionExtraProperties = new Dictionary<string, string>();
        transactionExtraProperties.Add("Version",blockVersion);
        transactionExtraProperties.Add("RefBlockNumber",transaction.RefBlockNumber.ToString());
        transactionExtraProperties.Add("RefBlockPrefix",transaction.RefBlockPrefix.ToBase64());
        transactionExtraProperties.Add("Bloom",transactionResult.Status == TransactionResultStatus.NotExisted
            ? null
            : transactionResult.Bloom.Length == 0
                ? ByteString.CopyFrom(new byte[256]).ToBase64()
                : transactionResult.Bloom.ToBase64());
        transactionExtraProperties.Add("ReturnValue",transactionResult.ReturnValue.ToHex());
        transactionExtraProperties.Add("Error",transactionResult.Error);
        transactionExtraProperties.Add("TransactionSize",transaction.CalculateSize().ToString());
        transactionExtraProperties.Add("TransactionFee", JsonConvert.SerializeObject(transactionResult.GetChargedTransactionFees()));
        transactionExtraProperties.Add("ResourceFee", JsonConvert.SerializeObject(transactionResult.GetConsumedResourceTokens()));

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
                logEventEtoExtraProperties.Add("NonIndexed",logEvent.NonIndexed.ToBase64());
                logEventEto.ExtraProperties = logEventEtoExtraProperties;
                   
                logEvents.Add(logEventEto);
                eventIndex += 1;
            }
        }
        transactionEto.LogEvents = logEvents;
        
        return transactionEto;
    }

   
}