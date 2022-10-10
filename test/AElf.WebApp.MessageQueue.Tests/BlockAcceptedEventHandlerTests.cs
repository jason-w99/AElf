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
using Volo.Abp.Uow;
using Xunit;
using NewIrreversibleBlockFoundEventHandler = AElf.WebApp.MessageQueue.NewIrreversibleBlockFoundEventHandler;

namespace AElf.WebApp.Application.MessageQueue.Tests;

public  class BlockAcceptedEventHandlerTests:AElfIntegratedTest<WebAppMessageQueueTestAElfModule>
{
    private readonly ILocalEventBus _eventBus;
    private readonly ISyncBlockStateProvider _syncBlockStateProvider;
    private readonly ISyncBlockLatestHeightProvider _syncBlockLatestHeightProvider;
    private readonly KernelTestHelper _kernelTestHelper;

    private readonly IBlockMessageService _blockMessageService;
    private readonly BlockAcceptedEventHandler _blockAcceptedEventHandler;
    private readonly SendMessage _sendMessageServer;
    private readonly NewIrreversibleBlockFoundEventHandler _newIrreversibleBlockFoundEventHandler;


    public BlockAcceptedEventHandlerTests()
    {
        _kernelTestHelper = GetRequiredService<KernelTestHelper>();
        _syncBlockStateProvider = GetRequiredService<ISyncBlockStateProvider>();
        _syncBlockLatestHeightProvider = GetRequiredService<ISyncBlockLatestHeightProvider>();
        _blockMessageService = GetRequiredService<IBlockMessageService>();
        _eventBus = GetRequiredService<ILocalEventBus>();
        _blockAcceptedEventHandler = GetRequiredService<BlockAcceptedEventHandler>();
        _sendMessageServer = GetRequiredService<SendMessage>();
        _newIrreversibleBlockFoundEventHandler = GetRequiredService<NewIrreversibleBlockFoundEventHandler>();
    }

    /// <summary>
    /// Node push status: Initialization status Confirm - Prepared
    /// </summary>
    [Fact]
    public async Task Test_01()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(100);
        BlockAcceptedEvent blockAcceptedEvent = new BlockAcceptedEvent();
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(100,presHash);
        blockExecutedSet.Block = block;
        blockAcceptedEvent.BlockExecutedSet = blockExecutedSet;
        
        await _blockAcceptedEventHandler.HandleEventAsync(blockAcceptedEvent); 
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.Prepared);
        //_syncBlockLatestHeightProvider.GetLatestHeight().ShouldBe(100);
    }
    
    [Fact]
    public async Task Test_03()
    {
        // 2.Prepared
        BlockAcceptedEvent blockAcceptedEvent = new BlockAcceptedEvent();
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(99,presHash);
        blockExecutedSet.Block = block;
        blockAcceptedEvent.BlockExecutedSet = blockExecutedSet;
        await _syncBlockStateProvider.UpdateStateAsync(99);
        /*var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.Prepared);
        */
        
        //await _syncBlockStateProvider.UpdateStateAsync(99);
        await _blockAcceptedEventHandler.HandleEventAsync(blockAcceptedEvent);
        
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncRunning);
        
        
    }

    [Fact]
    public async Task Test_04()
    {
        BlockAcceptedEvent blockAcceptedEvent = new BlockAcceptedEvent();
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(199,presHash);
        blockExecutedSet.Block = block;
        blockAcceptedEvent.BlockExecutedSet = blockExecutedSet;
        await _syncBlockStateProvider.UpdateStateAsync(99);
        await _blockAcceptedEventHandler.HandleEventAsync(blockAcceptedEvent);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.AsyncRunning);
        
        
    }
    
    /// <summary>
    /// Node push status: state switch -Asyncrunning -> SyncPrepared : Cache height >= incoming height -3
    /// </summary>
    [Fact]
    public async Task Test_05()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(189);
        await _syncBlockStateProvider.UpdateStateAsync(199,SyncState.AsyncRunning);
        await  _sendMessageServer.DoWorkAsync(3,5);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncPrepared);
    } 
    
    /// <summary>
    /// Node push status: state switch -Asyncrunning -> SyncPrepared : The height of the block to be sent is not equal to the height to be fetched
    /// </summary>
    [Fact]
    public async Task Test_06()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(189);
        await _syncBlockStateProvider.UpdateStateAsync(199,SyncState.AsyncRunning);
        await  _sendMessageServer.DoWorkAsync(3,5);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncPrepared);
    } 
    
    /// <summary>
    /// Node push status: state switch -Asyncrunning -> SyncPrepared : Number of blocks to send is 0
    /// </summary>
    [Fact]
    public async Task Test_07()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(200);
        await _syncBlockStateProvider.UpdateStateAsync(99,SyncState.AsyncRunning);
        await  _sendMessageServer.DoWorkAsync(3,5);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncPrepared);
    } 

    /// <summary>
    /// Node push status: state switch -Asyncrunning -> SyncPrepared :Cache height > The incoming height  -4
    /// </summary>
    [Fact]
    public async Task Test_08()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(_kernelTestHelper.BestBranchBlockList.Last().Height);
        await _syncBlockStateProvider.UpdateStateAsync(1,SyncState.AsyncRunning);
        await  _sendMessageServer.DoWorkAsync(3,5);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncPrepared);
    } 

    /*/// <summary>
    /// Node push status: state switch -Asyncrunning -> SyncPrepared :The received height is blockMessageEto == null
    /// </summary>
    [Fact]
    public async Task Test_09()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(_kernelTestHelper.BestBranchBlockList.Last().Height);
        await _syncBlockStateProvider.UpdateStateAsync(1,SyncState.AsyncRunning);
        await  _sendMessageServer.DoWorkAsync();
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncPrepared);
    } */
    
    [Fact]
    public async Task Test_10()
    {
        BlockAcceptedEvent blockAcceptedEvent = new BlockAcceptedEvent();
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(188,presHash);
        blockExecutedSet.Block = block;
        blockAcceptedEvent.BlockExecutedSet = blockExecutedSet;

        await _syncBlockStateProvider.UpdateStateAsync(99, SyncState.SyncPrepared);
        await _blockAcceptedEventHandler.HandleEventAsync(blockAcceptedEvent); 
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.Prepared);
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
        
    }

    [Fact]
    public async Task Test_11()
    {
        BlockAcceptedEvent blockAcceptedEvent = new BlockAcceptedEvent();
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(10,presHash);
        blockExecutedSet.Block = block;
        blockAcceptedEvent.BlockExecutedSet = blockExecutedSet;
        // 3.SyncPrepared
        await _syncBlockStateProvider.UpdateStateAsync(1, SyncState.SyncPrepared);
        await _blockAcceptedEventHandler.HandleEventAsync(blockAcceptedEvent); 
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncRunning);
    }
    
    [Fact]
    public async Task Test_12()
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

    
    /// <summary>
    /// Function test: The synchronous message is sent successfully
    /// </summary>
    [Fact]
    public async Task Test_13()
    {
        await _syncBlockStateProvider.UpdateStateAsync(100, SyncState.SyncRunning);
         _syncBlockLatestHeightProvider.SetLatestHeight(100);
        BlockAcceptedEvent blockAcceptedEvent = new BlockAcceptedEvent();
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(100,presHash);
        blockExecutedSet.Block = block;
        blockAcceptedEvent.BlockExecutedSet = blockExecutedSet;
        
        await _blockAcceptedEventHandler.HandleEventAsync(blockAcceptedEvent); 
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.CurrentHeight.ShouldBe(101);
        _syncBlockLatestHeightProvider.GetLatestHeight().ShouldBe(101);
    }
    
    
    /// <summary>
    /// Functional test: Delete the BlockHash cached below LIB after reaching LIB
    /// </summary>
    [Fact]
    public async Task Test_14()
    {
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(9,presHash);
        blockExecutedSet.Block = block;
        await _blockMessageService.SendMessageAsync(blockExecutedSet);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.SentBlockHashs.Count.ShouldBeGreaterThan(9);

        NewIrreversibleBlockFoundEvent _event = new NewIrreversibleBlockFoundEvent();
        _event.BlockHeight = 5;
        _newIrreversibleBlockFoundEventHandler.HandleEventAsync(_event);
        
        blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.SentBlockHashs.Count.ShouldBe(6);

    }
}