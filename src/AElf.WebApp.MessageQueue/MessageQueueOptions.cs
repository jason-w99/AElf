namespace AElf.WebApp.MessageQueue;

public class MessageQueueOptions
{
    public bool Enable { get; set; } = true;
    public string ChainId { get; set; } = "AElf";
    public long StartPublishMessageHeight { get; set; } = 1;
    public int Period { get; set; } = 1000;
    public int BlockCountPerPeriod = 3;
    public int ParallelCount = 5;
}