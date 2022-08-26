using System.Threading;
using System.Threading.Tasks;
using AElf.Kernel.Blockchain;
using AElf.Types;

namespace AElf.WebApp.MessageQueue.Helpers;

public interface IBlockChainDataEtoGenerator
{
    Task<BlockEto> GetBlockMessageEtoByHeightAsync(long height, CancellationToken cts);
    Task<BlockEto> GetBlockMessageEtoByHashAsync(Hash blockId);
    BlockEto GetBlockMessageEto(BlockExecutedSet blockExecutedSet);
}