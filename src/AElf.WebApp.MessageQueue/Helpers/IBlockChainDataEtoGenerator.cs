using System.Threading;
using System.Threading.Tasks;
using AElf.Kernel.Blockchain;

namespace AElf.WebApp.MessageQueue.Helpers;

public interface IBlockChainDataEtoGenerator
{
    Task<BlockEto> GetBlockMessageEtoByHeightAsync(long height, CancellationToken cts);
    BlockEto GetBlockMessageEto(BlockExecutedSet blockExecutedSet);
}