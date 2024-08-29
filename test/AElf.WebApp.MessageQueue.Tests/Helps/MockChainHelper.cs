using System.Collections.Generic;
using System.Threading.Tasks;
using AElf.Kernel;
using AElf.Kernel.Blockchain.Application;
using AElf.Kernel.Blockchain.Domain;
using AElf.Kernel.SmartContract.Domain;
using AElf.Types;

namespace AElf.WebApp.Application.MessageQueue.Tests.Helps;

public class MockChainHelper:KernelTestHelper
{
    private readonly IBlockchainService _blockchainService;
    private readonly IBlockStateSetManger _blockStateSetManger;
    private readonly IChainManager _chainManager;
    private readonly ITransactionResultService _transactionResultService;
    public MockChainHelper(IBlockchainService blockchainService, ITransactionResultService transactionResultService, 
        IChainManager chainManager, IBlockStateSetManger blockStateSetManger) 
        : base(blockchainService, transactionResultService, chainManager, blockStateSetManger)
    {
        BestBranchBlockList = new List<Block>();
        LongestBranchBlockList = new List<Block>();
        ForkBranchBlockList = new List<Block>();
        NotLinkedBlockList = new List<Block>();

        _blockchainService = blockchainService;
        _transactionResultService = transactionResultService;
        _chainManager = chainManager;
        _blockStateSetManger = blockStateSetManger;
    }
    
    /// <summary>
    ///     Mock a chain with a best branch, and some fork branches
    /// </summary>
    /// <returns>
    ///     Mock Chain
    ///     BestChainHeight: 11
    ///     LongestChainHeight: 13
    ///     LIB height: 5
    ///     Height: 51 -> 52 -> 53 -> 54 -> 55 -> 56 -> 57 -> 58 -> 59 -> 60 -> 61 -> 62 -> 63 -> 64 -> 65 -> 66 -> 67 -> 68 -> 19
    ///     Best Branch: a -> b -> c -> d -> e -> f -> g -> h -> i -> j  -> k
    ///     Longest Branch:                                   (h)-> l -> m  -> n  -> o  ->  p ->  q ->  r ->  s ->  t ->  u ->
    ///     v
    ///     Fork Branch:                    (e)-> q -> r -> s -> t -> u
    ///     Unlinked Branch:                                              v  -> w  -> x  -> y  -> z
    /// </returns>
    public  async Task<Kernel.Chain> MockOtherChainAsync()
    {
        var chain = await CreateChain();

        var genesisBlock = await _blockchainService.GetBlockByHashAsync(chain.GenesisBlockHash);
        BestBranchBlockList.Add(genesisBlock);

        BestBranchBlockList.AddRange(await AddBestBranch());

        LongestBranchBlockList =
            await AddForkBranch(BestBranchBlockList[57].Height, BestBranchBlockList[50].GetHash(), 11);

        foreach (var block in LongestBranchBlockList)
        {
            var chainBlockLink = await _chainManager.GetChainBlockLinkAsync(block.GetHash());
            await _chainManager.SetChainBlockLinkExecutionStatusAsync(chainBlockLink,
                ChainBlockLinkExecutionStatus.ExecutionFailed);
        }

        ForkBranchBlockList =
            await AddForkBranch(BestBranchBlockList[52].Height, BestBranchBlockList[52].GetHash());

        NotLinkedBlockList = await AddForkBranch(9, HashHelper.ComputeFrom("UnlinkBlock"));
        // Set lib
        chain = await _blockchainService.GetChainAsync();
        await _blockchainService.SetIrreversibleBlockAsync(chain, BestBranchBlockList[4].Height,
            BestBranchBlockList[4].GetHash());

        return chain;
    }
    
    private async Task<List<Block>> AddForkBranch(long previousHeight, Hash previousHash, int count = 5)
    {
        var forkBranchBlockList = new List<Block>();

        for (var i = 0; i < count; i++)
        {
            var newBlock = await AttachBlock(previousHeight, previousHash);
            forkBranchBlockList.Add(newBlock);

            previousHeight++;
            previousHash = newBlock.GetHash();
        }

        return forkBranchBlockList;
    }
    
    private async Task<List<Block>> AddBestBranch()
    {
        var bestBranchBlockList = new List<Block>();

        for (var i = 0; i < 60; i++)
        {
            var chain = await _blockchainService.GetChainAsync();
            var newBlock = await AttachBlock(chain.BestChainHeight, chain.BestChainHash);
            bestBranchBlockList.Add(newBlock);

            var chainBlockLink = await _chainManager.GetChainBlockLinkAsync(newBlock.GetHash());
            await _chainManager.SetChainBlockLinkExecutionStatusAsync(chainBlockLink,
                ChainBlockLinkExecutionStatus.ExecutionSuccess);

            chain = await _blockchainService.GetChainAsync();
            await _blockchainService.SetBestChainAsync(chain, newBlock.Height, newBlock.GetHash());
        }

        return bestBranchBlockList;
    }
}