using AElf.Sdk.CSharp.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Consensus.ConsensusContracts.FieldMapCollections
{
    public class AElfPoWFieldMapCollection
    {
        public Map<UInt64Value, StringValue> ProducerMap;

        public Int32Field PrefixZeroCount;

        public UInt64Value Nonce;
    }
}