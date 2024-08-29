using System.Collections.Generic;
using System.Linq;
using AElf.Kernel.Blockchain;
using AElf.Kernel.FeeCalculation.Extensions;
using AElf.Types;
using AElf.WebApp.MessageQueue.Helpers;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Volo.Abp.DependencyInjection;
using Volo.Abp.ObjectMapping;

namespace AElf.WebApp.MessageQueue;

public class BlockChainDataMapper: IObjectMapper<BlockExecutedSet, BlockEto>,
    ITransientDependency
{
    private readonly IAutoObjectMappingProvider _mapperProvider;
    private readonly ILogger<BlockChainDataMapper> _logger;
    private readonly ITransformEtoHelper _transformEtoHelper;

    public BlockChainDataMapper(IAutoObjectMappingProvider mapperProvider, ILogger<BlockChainDataMapper> logger, ITransformEtoHelper transformEtoHelper)
    {
        _mapperProvider = mapperProvider;
        _logger = logger;
        _transformEtoHelper = transformEtoHelper;
    }
    
    public BlockEto Map(BlockExecutedSet source)
    {
        var block = source.Block;
        var blockEto =  _transformEtoHelper.ToBlockEtoAsync(block);

        List<TransactionEto> transactions = new List<TransactionEto>();
        if (source.TransactionResultMap!=null)
        {
            int eventIndex = 0;
            var transactionIdList = block.TransactionIds.ToList();
            foreach (var transactionResultKeyPair in source.TransactionResultMap)
            {
                var txId = transactionResultKeyPair.Key.ToHex();
                var transactionResult = transactionResultKeyPair.Value;
                if (!source.TransactionMap.TryGetValue(transactionResultKeyPair.Key, out var transaction))
                {
                    continue;
                }
                var transactionIndex = transactionIdList.IndexOf(transactionResultKeyPair.Key);
                var transactionEto =
                    _transformEtoHelper.ToTransactionEtoAsync(transaction, transactionResult, transactionIndex,eventIndex, txId,block.Header.Version.ToString());
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