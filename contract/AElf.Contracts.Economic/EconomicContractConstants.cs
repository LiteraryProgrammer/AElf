namespace AElf.Contracts.Economic
{
    public static class EconomicContractConstants
    {
        public const long NativeTokenConnectorInitialVirtualBalance = 100_000_00000000;

        // Token Converter Contract related.
        public const string TokenConverterFeeRate = "0.005";
        public const string TokenConverterTokenSymbol = "AETC";
        public const long TokenConverterTokenTotalSupply = 1_000_000_000_00000000;
        public const int TokenConverterTokenDecimals = 8;
        public const long TokenConverterTokenConnectorInitialVirtualBalance = 100_000_00000000;

        // Resource token related.
        public const long ResourceTokenTotalSupply = 500_000_000_00000000;
        public const int ResourceTokenDecimals = 8;
        public const string ResourceTokenConnectorWeight = "0.2";
        public const long ResourceTokenConnectorInitialVirtualBalance = 100_000_00000000;

        public const long CpuUnitPrice = 100;
        public const long StoUnitPrice = 100;
        public const long NetUnitPrice = 100;

//        //resource to sell
        public const long ResourceTokenInitialVirtualBalance = 100_000;

        public const string NativeTokenPrefix = "NT";

        public const int TransactionSizeUnitPrice = 0;

        public const long NativeTokenToResourceBalance = 10_000_000_00000000;

        // Election related.
        public const string ElectionTokenSymbol = "VOTE";
        public const long ElectionTokenTotalSupply = long.MaxValue;
    }
}