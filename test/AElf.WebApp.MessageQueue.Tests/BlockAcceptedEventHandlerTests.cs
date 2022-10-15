using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AElf.Kernel;
using AElf.Kernel.Blockchain;
using AElf.Kernel.Blockchain.Events;
using AElf.TestBase;
using AElf.WebApp.Application.MessageQueue.Tests.Helps;
using AElf.WebApp.MessageQueue;
using AElf.WebApp.MessageQueue.Enum;
using AElf.WebApp.MessageQueue.Helpers;
using AElf.WebApp.MessageQueue.Provider;
using AElf.WebApp.MessageQueue.Services;
using JetBrains.Annotations;
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
    private readonly MockChainHelper _kernelTestHelper;

    private readonly IBlockMessageService _blockMessageService;
    private readonly BlockAcceptedEventHandler _blockAcceptedEventHandler;
    private readonly ISendMessageService _sendMessageServer;
    private readonly NewIrreversibleBlockFoundEventHandler _newIrreversibleBlockFoundEventHandler;
    private readonly IBlockChainDataEtoGenerator _blockChainDataEtoGenerator;
    private readonly IMessagePublishService _messagePublishService;

    protected CancellationToken CancellationToken = default;

    public BlockAcceptedEventHandlerTests()
    {
        _kernelTestHelper = GetRequiredService<MockChainHelper>();
        _syncBlockStateProvider = GetRequiredService<ISyncBlockStateProvider>();
        _syncBlockLatestHeightProvider = GetRequiredService<ISyncBlockLatestHeightProvider>();
        _blockMessageService = GetRequiredService<IBlockMessageService>();
        _eventBus = GetRequiredService<ILocalEventBus>();
        _blockAcceptedEventHandler = GetRequiredService<BlockAcceptedEventHandler>();
        _sendMessageServer = GetRequiredService<ISendMessageService>();
        _newIrreversibleBlockFoundEventHandler = GetRequiredService<NewIrreversibleBlockFoundEventHandler>();
        _blockChainDataEtoGenerator = GetRequiredService<IBlockChainDataEtoGenerator>();
        _messagePublishService = GetRequiredService<IMessagePublishService>();
    }

    /// <summary>
    /// 01  Node push status: Initialization status Confirm - Prepared
    /// </summary>
    [Fact]
    public async Task Prepared_Test()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(100);
        
        
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.Prepared);
    }
    /// <summary>
    /// 03  Prepare -> SyncRunning
    /// </summary>
    [Fact]
    public async Task PrepareToSyncRunning_Test()
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

    /// <summary>
    ///  04  Prepare -> AsyncRunning 
    /// </summary>
    [Fact]
    [CanBeNull]
    public async Task PrepareToAsyncRunning_Test ()
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
    ///05 Node push status: state switch -Asyncrunning -> SyncPrepared : Cache height >= incoming height -3
    /// </summary>
    [Fact]
    public async Task AsyncRunningToSyncPrepared_CacheHeight_Test()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(189);
        await _syncBlockStateProvider.UpdateStateAsync(199,SyncState.AsyncRunning);
        await  _sendMessageServer.DoWorkAsync(3,5,CancellationToken);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncPrepared);
    } 
    
    /*/// <summary>
    /// 06 Node push status: state switch -Asyncrunning -> SyncPrepared : The height of the block to be sent is not equal to the height to be fetched
    /// </summary>
    [Fact]
    public async Task Test_06()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(189);
        await _syncBlockStateProvider.UpdateStateAsync(199,SyncState.AsyncRunning);
        await  _sendMessageServer.DoWorkAsync(3,5,CancellationToken);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncPrepared);
    } */
    
    /// <summary>
    /// 07 Node push status: state switch -Asyncrunning -> SyncPrepared : Number of blocks to send is 0
    /// </summary>
    [Fact]
    public async Task AsyncRunningToSyncPrepared_SendNumberIs0_Test()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(200);
        await _syncBlockStateProvider.UpdateStateAsync(99,SyncState.AsyncRunning);
        await  _sendMessageServer.DoWorkAsync(3,5,CancellationToken);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncPrepared);
    } 

    /*/// <summary>
    /// Node push status: state switch -Asyncrunning -> SyncPrepared :Cache height > The incoming height  -4
    /// </summary>
    [Fact]
    public async Task Test_08()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(_kernelTestHelper.BestBranchBlockList.Last().Height);
        await _syncBlockStateProvider.UpdateStateAsync(null,SyncState.AsyncRunning);
        await  _sendMessageServer.DoWorkAsync(3,5,CancellationToken);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncPrepared);
    } */

    /// <summary>
    /// 09 Node push status: state switch -Asyncrunning -> SyncPrepared :The received height is blockMessageEto == null
    /// </summary>
    [Fact]
    public async Task AsyncRunningToSyncPrepared_ReceivedHeightIsNull_Test()
    {
        _syncBlockLatestHeightProvider.SetLatestHeight(_kernelTestHelper.BestBranchBlockList.Last().Height);
        await _syncBlockStateProvider.UpdateStateAsync(300,SyncState.AsyncRunning);
        await  _sendMessageServer.DoWorkAsync(100,10,CancellationToken);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.State.ShouldBe(SyncState.SyncPrepared);
    } 
    
    /// <summary>
    /// 10 Node push status:SyncPrepared -> Prepare
    /// </summary>
    [Fact]
    public async Task SyncPreparedToPrepare_Test()
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

    /// <summary>
    /// 11 Node push status:SyncPrepared -> SyncRunning
    /// </summary>
    [Fact]
    public async Task SyncPreparedToSyncRunning_Test()
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
    /// <summary>
    /// 12 Async send message
    /// </summary>
    [Fact]
    public async Task AsyncSend_Test()
    {
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        
        var presHash = _kernelTestHelper.ForkBranchBlockList[4].GetHash();
        Block block = _kernelTestHelper.GenerateBlock(15,presHash);
        blockExecutedSet.Block = block;
        //Send a synchronized block at a certain height on the other chain after the fork, and determine whether to send other blocks connected to the fork
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
    /// 13 Function test: The synchronous message is sent successfully
    /// </summary>
    [Fact]
    public async Task SyncSend_Test()
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
        blockSyncState.CurrentHeight.ShouldBe(100);
        _syncBlockLatestHeightProvider.GetLatestHeight().ShouldBe(101);
    }
    
    
    /// <summary>
    /// 14 Functional test: Delete the BlockHash cached below LIB after reaching LIB
    /// </summary>
    [Fact]
    public async Task DeleteUnderlibBlock_Test()
    {
        BlockExecutedSet blockExecutedSet = new BlockExecutedSet();
        var block=_kernelTestHelper.BestBranchBlockList[9];
        blockExecutedSet.Block = block;
        await _blockMessageService.SendMessageAsync(blockExecutedSet);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.SentBlockHashs.Count.ShouldBeGreaterThan(9);

        NewIrreversibleBlockFoundEvent _event = new NewIrreversibleBlockFoundEvent();
        _event.BlockHeight = 5;
        _newIrreversibleBlockFoundEventHandler.HandleEventAsync(_event);
        
        blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.SentBlockHashs.Count.ShouldBe(5);

    }
    
    /// <summary>
    /// 15 Batch Send blocks (PublishListAsync) - To receive a set of blocks without forks
    /// </summary>
    [Fact]
    public async Task BatchSend_Normal_Test()
    {
        CancellationToken cancellationToken = default;
        var syncBlockHeight = await _blockMessageService.SendMessageAsync(30, 39, cancellationToken);
        syncBlockHeight.ShouldBe(40);

    }
    /// <summary>
    /// 16 Batch Send blocks (PublishListAsync) - The received blocks have already been sent blocks
    /// </summary>
    [Fact]
    public async Task BatchSend_AlreadySend_Test()
    {
        CancellationToken cancellationToken = default;
        var syncBlockHeight = await _blockMessageService.SendMessageAsync(41, 50, cancellationToken);
        syncBlockHeight.ShouldBe(51);
        //send again
        syncBlockHeight = await _blockMessageService.SendMessageAsync(41, 50, cancellationToken);
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        var block=await  _blockChainDataEtoGenerator.GetBlockMessageEtoByHeightAsync(50, cancellationToken);
        var isTrue=blockSyncState.SentBlockHashs.ContainsKey(block.BlockHash);
        isTrue.ShouldBe(true);
    }
    /// <summary>
    /// 17 Batch Send blocks (PublishListAsync) - the block received has a continuous fork block  
    /// </summary>
    [Fact]
    public async Task BatchSend_HasContinuousFork_Test()
    { 
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        CancellationToken cancellationToken = default;
        List<BlockEto> Blocks = new List<BlockEto>();
        
        var firstBlock=_kernelTestHelper.BestBranchBlockList[47];
        var firstBlockEto=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(firstBlock.GetHash());
        await _syncBlockStateProvider.AddBlockHashAsync(firstBlockEto.BlockHash,
            firstBlockEto.PreviousBlockHash,firstBlockEto.Height - 1);
        
            
        for (int i = 48; i < 55; i++)
        {
            var block=_kernelTestHelper.BestBranchBlockList[i];
            
            var blockEto=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(block.GetHash());
            Blocks.Add(blockEto);
        }
        for (int i = 2; i < 5; i++)
        {
            var block=_kernelTestHelper.ForkBranchBlockList[i];
            
            var blockEto=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(block.GetHash());
            Blocks.Add(blockEto);
        }

        var nextHeight=await _messagePublishService.PublishListAsync(Blocks);
        blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.SentBlockHashs.Count.ShouldBeGreaterThan(10);
        nextHeight.ShouldBe(Blocks.Last().Height+1);
    }
    /// <summary>
    /// 18 Batch Send blocks (PublishListAsync) - the block received has a discontinuous fork block   
    /// </summary>
    [Fact]
    public async Task BatchSend_HasDiscontinuousFork_Test()
    {
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        CancellationToken cancellationToken = default;
        List<BlockEto> Blocks = new List<BlockEto>();
        
        var firstBlock=_kernelTestHelper.BestBranchBlockList[47];
        var firstBlockEto=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(firstBlock.GetHash());
        await _syncBlockStateProvider.AddBlockHashAsync(firstBlockEto.BlockHash,
            firstBlockEto.PreviousBlockHash,firstBlockEto.Height - 1);
        
            
        for (int i = 48; i < 54; i++)
        {
            var block=_kernelTestHelper.BestBranchBlockList[i];
            var blockEto=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(block.GetHash());
            Blocks.Add(blockEto);
        }
        // Add  two fork block
        var forkBlock=_kernelTestHelper.ForkBranchBlockList[1];
        var forkBlockEto=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(forkBlock.GetHash());
        
        var forkBlock2=_kernelTestHelper.ForkBranchBlockList[3];
        var forkBlockEto2=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(forkBlock2.GetHash());

        Blocks.Add(forkBlockEto);
        Blocks.Add(forkBlockEto2);
        // Add two best block
        var mainBlock1=_kernelTestHelper.BestBranchBlockList[55];
        var mainBlockEto1=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(mainBlock1.GetHash());
        var mainBlock2=_kernelTestHelper.BestBranchBlockList[57];
        var mainBlockEto2=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(mainBlock2.GetHash());
        Blocks.Add(mainBlockEto1);
        Blocks.Add(mainBlockEto2);

        var nextHeight=await _messagePublishService.PublishListAsync(Blocks);
        blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.SentBlockHashs.Count.ShouldBe(15);
        nextHeight.ShouldBe(Blocks.Last().Height+1);

    }
    
    /// <summary>
    /// 19 Batch Send blocks (PublishListAsync) -The received block has the fork block that has been sent
    /// </summary>
    [Fact]
    public async Task BatchSend_AlreadySendFork_Test()
    {
        var blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        CancellationToken cancellationToken = default;
        List<BlockEto> Blocks = new List<BlockEto>();
        
        var firstBlock=_kernelTestHelper.BestBranchBlockList[47];
        var firstBlockEto=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(firstBlock.GetHash());
        await _syncBlockStateProvider.AddBlockHashAsync(firstBlockEto.BlockHash,
            firstBlockEto.PreviousBlockHash,firstBlockEto.Height - 1);
        
            
        for (int i = 48; i < 54; i++)
        {
            var block=_kernelTestHelper.BestBranchBlockList[i];
            var blockEto=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(block.GetHash());
            Blocks.Add(blockEto);
        }
        for (int i = 1; i < 4; i++)
        {
            var forkBlock=_kernelTestHelper.ForkBranchBlockList[i];
            var forkBlockEto=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(forkBlock.GetHash());
            Blocks.Add(forkBlockEto);
            //add cache
            await _syncBlockStateProvider.AddBlockHashAsync(forkBlockEto.BlockHash,
                forkBlockEto.PreviousBlockHash,forkBlockEto.Height - 1);
        }

        // Add the last best block
        var mainBlock2=_kernelTestHelper.BestBranchBlockList[57];
        var mainBlockEto2=await _blockChainDataEtoGenerator.GetBlockMessageEtoByHashAsync(mainBlock2.GetHash());
        Blocks.Add(mainBlockEto2);

        var nextHeight=await _messagePublishService.PublishListAsync(Blocks);
        blockSyncState = await _syncBlockStateProvider.GetCurrentStateAsync();
        blockSyncState.SentBlockHashs.Count.ShouldBe(14);
        nextHeight.ShouldBe(Blocks.Last().Height+1);

    }
}