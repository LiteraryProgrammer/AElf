﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.TokenConverter
{
    public partial class TokenConverterContract : TokenConverterContractContainer.TokenConverterContractBase
    {
        #region Views

        public override Address GetTokenContractAddress(Empty input)
        {
            return State.TokenContract.Value;
        }

        public override Address GetFeeReceiverAddress(Empty input)
        {
            return State.FeeReceiverAddress.Value;
        }

        public override StringValue GetFeeRate(Empty input)
        {
            return new StringValue()
            {
                Value = State.FeeRate.Value
            };
        }

        public override Address GetManagerAddress(Empty input)
        {
            return State.ManagerAddress.Value;
        }

        public override TokenSymbol GetBaseTokenSymbol(Empty input)
        {
            return new TokenSymbol()
            {
                Symbol = State.BaseTokenSymbol.Value
            };
        }

        /// <summary>
        /// Query the connector details.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Connector GetConnector(TokenSymbol input)
        {
            return State.Connectors[input.Symbol];
        }

        #endregion Views

        #region Actions

        /// <summary>
        /// Initialize the contract information.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Empty Initialize(InitializeInput input)
        {
            Assert(IsValidSymbol(input.BaseTokenSymbol), $"Base token symbol is invalid. {input.BaseTokenSymbol}");
            Assert(State.TokenContract.Value == null, "Already initialized.");
            State.TokenContract.Value = input.TokenContractAddress != null
                ? input.TokenContractAddress
                : Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);

            State.FeeReceiverAddress.Value = input.FeeReceiverAddress != null
                ? input.FeeReceiverAddress
                : Context.GetContractAddressByName(SmartContractConstants.TreasuryContractSystemName);

            State.BaseTokenSymbol.Value = input.BaseTokenSymbol != string.Empty
                ? input.BaseTokenSymbol
                : Context.Variables.NativeSymbol;

            State.ManagerAddress.Value = input.ManagerAddress != null
                ? input.ManagerAddress
                : Context.GetContractAddressByName(SmartContractConstants.TreasuryContractSystemName);

            var feeRate = AssertedDecimal(input.FeeRate);
            Assert(IsBetweenZeroAndOne(feeRate), "Fee rate has to be a decimal between 0 and 1.");
            State.FeeRate.Value = feeRate.ToString(CultureInfo.InvariantCulture);

            var count = State.ConnectorCount.Value;
            foreach (var connector in input.Connectors)
            {
                AssertValidConnectorAndNormalizeWeight(connector);
                State.ConnectorSymbols[count] = connector.Symbol;
                State.Connectors[connector.Symbol] = connector;
                count = count.Add(1);
            }

            State.ConnectorCount.Value = count;

            return new Empty();
        }
        public override Empty UpdateConnector(Connector input)
        {
            AssertPerformedByManager();
            Assert(!string.IsNullOrEmpty(input.Symbol), "input symbol can not be empty'");
            var targetConnector = State.Connectors[input.Symbol];
            Assert(targetConnector != null, "Can't find target connector.");
            if (!string.IsNullOrEmpty(input.Weight))
            {
                AssertedDecimal(input.Weight);
                targetConnector.Weight = input.Weight;
            }
            if(input.VirtualBalance > 0)
                targetConnector.VirtualBalance = input.VirtualBalance;
            targetConnector.IsVirtualBalanceEnabled = input.IsVirtualBalanceEnabled;
            targetConnector.IsPurchaseEnabled = input.IsPurchaseEnabled;
            if(!string.IsNullOrEmpty(input.RelatedSymbol))
                targetConnector.RelatedSymbol = input.RelatedSymbol;
            return new Empty();
        }

        public override Empty AddPairConnectors(PairConnector pairConnector)
        {
            AssertPerformedByManager();
            Assert(!string.IsNullOrEmpty(pairConnector.ResourceConnectorSymbol),
                "resource token symbol should not be empty");
            Assert(!string.IsNullOrEmpty(pairConnector.NativeConnectorSymbol),
                "native token symbol symbol should not be empty");
            Assert(State.Connectors[pairConnector.ResourceConnectorSymbol]==null,
                "resource token symbol has been existed");
            Assert(State.Connectors[pairConnector.NativeConnectorSymbol]==null,
                "native token symbol has been existed");
            var resourceConnector = new Connector
            {
                Symbol = pairConnector.ResourceConnectorSymbol,
                VirtualBalance = pairConnector.ResourceVirtualBalance,
                IsVirtualBalanceEnabled = pairConnector.IsResourceVirtualBalanceEnabled,
                IsPurchaseEnabled = pairConnector.IsPurchaseEnabled,
                RelatedSymbol = pairConnector.NativeConnectorSymbol,
                Weight = pairConnector.ResourceWeight
            };
            AssertValidConnectorAndNormalizeWeight(resourceConnector);
            var nativeTokenToResourceConnector = new Connector
            {
                Symbol = pairConnector.NativeConnectorSymbol,
                VirtualBalance = pairConnector.NativeVirtualBalance,
                IsVirtualBalanceEnabled = pairConnector.IsNativeVirtualBalanceEnabled,
                IsPurchaseEnabled = pairConnector.IsPurchaseEnabled,
                RelatedSymbol = pairConnector.ResourceConnectorSymbol,
                Weight = pairConnector.NativeWeight
            };
            AssertValidConnectorAndNormalizeWeight(nativeTokenToResourceConnector);
            int count = State.ConnectorCount.Value;
            State.ConnectorSymbols[count+1] = resourceConnector.Symbol;
            State.Connectors[resourceConnector.Symbol] = resourceConnector;
            State.ConnectorSymbols[count+2] = nativeTokenToResourceConnector.Symbol;
            State.Connectors[nativeTokenToResourceConnector.Symbol] = nativeTokenToResourceConnector;
            State.ConnectorCount.Value = count + 2;
            return new Empty();
        }
        public override Empty Buy(BuyInput input)
        {
            Assert(IsValidSymbol(input.Symbol), "Invalid symbol.");
            
            var toConnector = State.Connectors[input.Symbol];
            Assert(toConnector != null, "Can't find to connector.");
            Assert(!string.IsNullOrEmpty(toConnector.RelatedSymbol), "can't find related symbol'");
            var fromConnector = State.Connectors[toConnector.RelatedSymbol];
            Assert(toConnector != null, "Can't find from connector.");
            var amountToPay = BancorHelper.GetAmountToPayFromReturn(
                GetSelfBalance(fromConnector), GetWeight(fromConnector),
                GetSelfBalance(toConnector), GetWeight(toConnector),
                input.Amount);
            var fee = Convert.ToInt64(amountToPay * GetFeeRate());

            var amountToPayPlusFee = amountToPay.Add(fee);
            Assert(input.PayLimit == 0 || amountToPayPlusFee <= input.PayLimit, "Price not good.");

            // Pay fee
            if (fee > 0)
            {
                HandleFee(fee);
            }

            // Transfer base token
            State.TokenContract.TransferFrom.Send(
                new TransferFromInput()
                {
                    Symbol = State.BaseTokenSymbol.Value,
                    From = Context.Sender,
                    To = Context.Self,
                    Amount = amountToPay,
                });
            State.DepositBalance[fromConnector.Symbol] = State.DepositBalance[fromConnector.Symbol].Add(amountToPay);
            // Transfer bought token
            State.TokenContract.Transfer.Send(
                new TransferInput
                {
                    Symbol = input.Symbol,
                    To = Context.Sender,
                    Amount = input.Amount
                });
            
            Context.Fire(new TokenBought
            {
                Symbol = input.Symbol,
                BoughtAmount = input.Amount,
                BaseAmount = amountToPay,
                FeeAmount = fee
            });
            return new Empty();
        }

        public override Empty Sell(SellInput input)
        {
            Assert(IsValidSymbol(input.Symbol), "Invalid symbol.");
            var fromConnector = State.Connectors[input.Symbol];
            Assert(fromConnector != null, "Can't find from connector.");
            Assert(!string.IsNullOrEmpty(fromConnector.RelatedSymbol), "can't find related symbol'");
            var toConnector = State.Connectors[fromConnector.RelatedSymbol];
            Assert(toConnector != null, "Can't find to connector.");
            var amountToReceive = BancorHelper.GetReturnFromPaid(
                GetSelfBalance(fromConnector), GetWeight(fromConnector),
                GetSelfBalance(toConnector), GetWeight(toConnector),
                input.Amount
            );

            var fee = Convert.ToInt64(amountToReceive * GetFeeRate());

            if (Context.Sender == Context.GetContractAddressByName(SmartContractConstants.TreasuryContractSystemName))
            {
                fee = 0;
            }

            var amountToReceiveLessFee = amountToReceive.Sub(fee);
            Assert(input.ReceiveLimit == 0 || amountToReceiveLessFee >= input.ReceiveLimit, "Price not good.");

            // Pay fee
            if (fee > 0)
            {
                HandleFee(fee);
            }

            // Transfer base token
            State.TokenContract.Transfer.Send(
                new TransferInput()
                {
                    Symbol = State.BaseTokenSymbol.Value,
                    To = Context.Sender,
                    Amount = amountToReceiveLessFee
                });
            State.DepositBalance[toConnector.Symbol] =
                State.DepositBalance[toConnector.Symbol].Sub(amountToReceiveLessFee);
            // Transfer sold token
            State.TokenContract.TransferFrom.Send(
                new TransferFromInput()
                {
                    Symbol = input.Symbol,
                    From = Context.Sender,
                    To = Context.Self,
                    Amount = input.Amount
                });
            Context.Fire(new TokenSold()
            {
                Symbol = input.Symbol,
                SoldAmount = input.Amount,
                BaseAmount = amountToReceive,
                FeeAmount = fee
            });
            return new Empty();
        }

        private void HandleFee(long fee)
        {
            var donateFee = fee.Div(2);
            var burnFee = fee.Sub(donateFee);

            // Transfer to fee receiver.
            State.TokenContract.TransferFrom.Send(
                new TransferFromInput
                {
                    Symbol = State.BaseTokenSymbol.Value,
                    From = Context.Sender,
                    To = State.FeeReceiverAddress.Value,
                    Amount = donateFee
                });

            // Transfer to self contract then burn
            State.TokenContract.TransferFrom.Send(
                new TransferFromInput
                {
                    Symbol = State.BaseTokenSymbol.Value,
                    From = Context.Sender,
                    To = Context.Self,
                    Amount = burnFee
                });
            State.TokenContract.Burn.Send(
                new BurnInput
                {
                    Symbol = State.BaseTokenSymbol.Value,
                    Amount = burnFee
                });
        }

        public override Empty SetFeeRate(StringValue input)
        {
            AssertPerformedByManager();
            var feeRate = AssertedDecimal(input.Value);
            Assert(IsBetweenZeroAndOne(feeRate), "Fee rate has to be a decimal between 0 and 1.");
            State.FeeRate.Value = feeRate.ToString(CultureInfo.InvariantCulture);
            return new Empty();
        }

        public override Empty SetManagerAddress(Address input)
        {
            AssertPerformedByManager();
            Assert(input != null && input != new Address(), "Input is not a valid address.");
            State.ManagerAddress.Value = input;
            return new Empty();
        }

        #endregion Actions

        #region Helpers

        private static decimal AssertedDecimal(string number)
        {
            try
            {
                return decimal.Parse(number);
            }
            catch (FormatException)
            {
                throw new InvalidValueException($@"Invalid decimal ""{number}""");
            }
        }

        private static bool IsBetweenZeroAndOne(decimal number)
        {
            return number > decimal.Zero && number < decimal.One;
        }

        private static bool IsValidSymbol(string symbol)
        {
            return symbol.Length > 0 &&
                   symbol.All(c => c >= 'A' && c <= 'Z');
        }

        private decimal GetFeeRate()
        {
            return decimal.Parse(State.FeeRate.Value);
        }

        private long GetSelfBalance(Connector connector)
        {
            long realBalance;
            if (connector.IsDepositAccount)
            {
                realBalance = State.DepositBalance[connector.Symbol];
            }
            else
            {
                realBalance = State.TokenContract.GetBalance.Call(
                    new GetBalanceInput()
                    {
                        Owner = Context.Self,
                        Symbol = connector.Symbol
                    }).Balance;
            }

            if (connector.IsVirtualBalanceEnabled)
            {
                return connector.VirtualBalance.Add(realBalance);
            }

            return realBalance;
        }

        private decimal GetWeight(Connector connector)
        {
            return decimal.Parse(connector.Weight);
        }

        private void AssertPerformedByManager()
        {
            Assert(Context.Sender == State.ManagerAddress.Value, "Only manager can perform this action.");
        }

        private void AssertValidConnectorAndNormalizeWeight(Connector connector)
        {
            Assert(IsValidSymbol(connector.Symbol), "Invalid symbol.");
            var weight = AssertedDecimal(connector.Weight);
            Assert(IsBetweenZeroAndOne(weight), "Connector Shares has to be a decimal between 0 and 1.");
            connector.Weight = weight.ToString(CultureInfo.InvariantCulture);
        }

        #endregion
    }
}