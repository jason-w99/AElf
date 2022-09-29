using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AElf.Kernel;
using AElf.Kernel.Blockchain;
using AElf.Kernel.Blockchain.Events;
using AElf.TestBase;
using AElf.WebApp.MessageQueue;
using AElf.WebApp.MessageQueue.Enum;
using AElf.WebApp.MessageQueue.Provider;
using AElf.WebApp.MessageQueue.Services;
using Shouldly;
using Volo.Abp.EventBus.Local;
using Xunit;

namespace AElf.WebApp.Application.MessageQueue.Tests;

public  class BlockAcceptedEventHandlerTests:AElfIntegratedTest<TestModule>
{
    private readonly ILocalEventBus _eventBus;
    private readonly ISyncBlockStateProvider _syncBlockStateProvider;
    private readonly ISyncBlockLatestHeightProvider _syncBlockLatestHeightProvider;
    private readonly KernelTestHelper _kernelTestHelper;

    private readonly IBlockMessageService _blockMessageService;
    private readonly ISendMessageByDesignateHeightTaskManager _sendMessageByDesignateHeightTaskManager;
    private readonly BlockAcceptedEventHandler _blockAcceptedEventHandler;
    private readonly SendMessageServer _sendMessageServer;

    public BlockAcceptedEventHandlerTests()
    {
        _kernelTestHelper = GetRequiredService<KernelTestHelper>();
        _syncBlockStateProvider = GetRequiredService<ISyncBlockStateProvider>();
        _syncBlockLatestHeightProvider = GetRequiredService<ISyncBlockLatestHeightProvider>();
        _blockMessageService = GetRequiredService<IBlockMessageService>();
        _sendMessageByDesignateHeightTaskManager = GetRequiredService<ISendMessageByDesignateHeightTaskManager>(); 
        _eventBus = GetRequiredService<ILocalEventBus>();
        _blockAcceptedEventHandler = GetRequiredService<BlockAcceptedEventHandler>();
        _sendMessageServer = GetRequiredService<SendMessageServer>();
    }

   

    /// <summary>
    ///  from to test
    /// </summary>
    [Fact]
    public async Task Send_Message_Async_Test()
    {
        CancellationToken cancellationToken = default;
        var syncBlockHeight = await _blockMessageService.SendMessageAsync(2, 6, cancellationToken);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        syncBlockHeight.ShouldBe(7);
        /*List<BlockChainDataEto> eventData = new List<BlockChainDataEto>();
        _eventBus.Subscribe<BlockChainDataEto>(d =>
        {
            eventData.Add(d);
            return Task.CompletedTask;
        });
        foreach (var eto in eventData)
        {
            
        }*/
    }
    
   
    [Fact]
    public async Task Worker_Test()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(_kernelTestHelper.BestBranchBlockList.Last().Height);
        await _syncBlockStateProvider.UpdateStateAsync(1,SyncState.AsyncRunning);
        await  _sendMessageServer.DoWorkAsync();
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncPrepared);
        blockSyncState.SentBlockHashs.Count.ShouldBeGreaterThanOrEqualTo(90);
    } 
    
    [Fact]
    public async Task Handle_Event_TestA_Async()
    {
        // 2.Prepared
        BlockAcceptedEvent blockAcceptedEvent = new BlockAcceptedEvent();
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(15,presHash);
        blockExecutedSet.Block = block;
        blockAcceptedEvent.BlockExecutedSet = blockExecutedSet;

        await _syncBlockStateProvider.UpdateStateAsync(null, SyncState.Prepared);
        await _blockAcceptedEventHandler.HandleEventAsync(blockAcceptedEvent);
        
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.AsyncRunning);
        
        // 3.SyncPrepared
        
        await _syncBlockStateProvider.UpdateStateAsync(null, SyncState.SyncPrepared);
        await _blockAcceptedEventHandler.HandleEventAsync(blockAcceptedEvent); 
        blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.Prepared);
    }
   
    [Fact]
    public async Task Handle_Event_TestB_Async()
    {
        // 2.Prepared
        BlockAcceptedEvent blockAcceptedEvent = new BlockAcceptedEvent();
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(1,presHash);
        blockExecutedSet.Block = block;
        blockAcceptedEvent.BlockExecutedSet = blockExecutedSet;
        await _syncBlockStateProvider.UpdateStateAsync(null, SyncState.Prepared);
        await _blockAcceptedEventHandler.HandleEventAsync(blockAcceptedEvent);
        
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncRunning);
    
    }

    
    [Fact]
    public async Task Handle_Event_TestC_Async()
    {
        BlockAcceptedEvent blockAcceptedEvent = new BlockAcceptedEvent();
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(9,presHash);
        blockExecutedSet.Block = block;
        blockAcceptedEvent.BlockExecutedSet = blockExecutedSet;
        // 3.SyncPrepared
        await _syncBlockStateProvider.UpdateStateAsync(null, SyncState.SyncPrepared);
        await _blockAcceptedEventHandler.HandleEventAsync(blockAcceptedEvent); 
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncRunning);
        blockSyncState.SentBlockHashs.Count.ShouldBeGreaterThanOrEqualTo(9);
    }
    
    [Fact]
    public async Task Miss_send_Test()
    {
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(15,presHash);
        blockExecutedSet.Block = block;
        //发送一个同步的,分叉后另一条链上的某个高度的区块，判断是否将这条分叉的连上的 其他区块也发送出来
        await _blockMessageService.SendMessageAsync(blockExecutedSet);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        bool isTrue = true;
        foreach (var forkBlock in _kernelTestHelper.ForkBranchBlockList)
        {
            if (!blockSyncState.SentBlockHashs.ContainsKey(forkBlock.GetHash().ToHex()))
            {
                isTrue = false;
                break;
            }
        }
        isTrue.ShouldBe(true);
    }

}