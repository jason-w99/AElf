namespace AElf.WebApp.MessageQueue;

public class MessageQueueOptions
{
    public bool Enable { get; set; } = true;
    public string ChainId { get; set; } = "AElf";
    public long StartPublishMessageHeight { get; set; } 
    public int Period { get; set; } = 1000;
    public int BlockCountPerPeriod { get; set; } 
    public int ParallelCount { get; set; } 
}