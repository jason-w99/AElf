using System;
using System.Collections.Generic;
using AElf.Types;
using Volo.Abp.Data;
using Volo.Abp.EventBus;

namespace AElf.WebApp.MessageQueue;

[EventName("AElf.WebMessage.BlockChainDataEto")]
public class BlockChainDataEto
{
    public string ChainId { get; set; }
    public List<BlockEto> Blocks {get;set;}
   
}
public class BlockEto
{
    public string ChainId { get; set; }
    public long Height { get; set; }
    public string BlockHash { get; set; }
    public Hash PreviousBlockId { get; set; }
    public long BlockNumber { get; set; }
    public string PreviousBlockHash { get; set; }
    public DateTime BlockTime { get; set; }
    public string SignerPubkey { get; set; }
    public string Signature { get; set; }
    public Dictionary<string, string> ExtraProperties {get;set;}
    public List<TransactionEto> Transactions{get;set;}
    
}

public class TransactionEto
{
    public string TransactionId { get; set; }
    
    public string From { get; set; }
    
    public string To { get; set; }
    
    public string MethodName { get; set; }
    
    public string Params { get; set; }
    
    public string Signature { get; set; }
    
    public int Status { get; set; }
    
    public  Dictionary<string, string>  ExtraProperties {get;set;}
    
    public List<LogEventEto> LogEvents { get; set; }
}
public class LogEventEto
{    
    public string ContractAddress { get; set; }
    
    public string EventName { get; set; }
    
    /// <summary>
    /// 事件在交易内的排序位置
    /// </summary>
    public int Index { get; set; }
    
    public  Dictionary<string, string>  ExtraProperties {get;set;}
}

