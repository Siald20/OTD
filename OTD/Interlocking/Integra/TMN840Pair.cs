public sealed class TMN840Pair
{
    public TMN840_BS Block { get; }

    public TMN840Pair()
    {
        Block = new TMN840_BS();
    }

    public static void ConnectBlocks(TMN840Pair first, TMN840Pair second)
    {
        TMN840_BS.Connect(first.Block, second.Block);
    }
}
