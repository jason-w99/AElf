using System;
using System.Linq;
using AElf.Contracts.NFT;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Sdk.CSharp;
using Google.Protobuf.WellKnownTypes;
using GetAllowanceInput = AElf.Contracts.MultiToken.GetAllowanceInput;
using GetBalanceInput = AElf.Contracts.MultiToken.GetBalanceInput;
using TransferFromInput = AElf.Contracts.MultiToken.TransferFromInput;
using TransferInput = AElf.Contracts.MultiToken.TransferInput;

namespace AElf.Contracts.NFTMarket
{
    public partial class NFTMarketContract
    {
        /// <summary>
        /// There are 2 types of making offer.
        /// 1. Aiming a owner.
        /// 2. Only aiming nft. Owner will be null.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Empty MakeOffer(MakeOfferInput input)
        {
            var nftInfo = State.NFTContract.GetNFTInfo.Call(new GetNFTInfoInput
            {
                Symbol = input.Symbol,
                TokenId = input.TokenId
            });

            if (nftInfo.Quantity != 0 && input.OfferTo == null)
            {
                input.OfferTo = nftInfo.Creator;
            }

            if (nftInfo.Quantity == 0 && input.Quantity == 1)
            {
                // NFT not minted.
                PerformRequestNewItem(input.Symbol, input.TokenId, input.Price, input.ExpireTime);
                return new Empty();
            }

            Assert(Context.Sender != input.OfferTo, "Origin owner cannot be sender himself.");

            var listedNftInfoList = State.ListedNFTInfoListMap[input.Symbol][input.TokenId][input.OfferTo];

            if (listedNftInfoList == null || listedNftInfoList.Value.All(i => i.ListType == ListType.NotListed))
            {
                // NFT not listed by the owner.
                PerformMakeOffer(input);
                return new Empty();
            }

            var listedNftInfo = listedNftInfoList.Value.FirstOrDefault(i =>
                i.Price.Symbol == input.Price.Symbol && i.Price.Amount <= input.Price.Amount);
            var whiteListAddressPriceList =
                State.WhiteListAddressPriceListMap[input.Symbol][input.TokenId][input.OfferTo];

            var protocolInfo = State.NFTContract.GetNFTProtocolInfo.Call(new StringValue {Value = input.Symbol});
            if (protocolInfo.IsTokenIdReuse)
            {
                if (listedNftInfo == null || listedNftInfo.ListType == ListType.NotListed)
                {
                    if (whiteListAddressPriceList == null)
                    {
                        PerformMakeOffer(input);
                        return new Empty();
                    }

                    // For case pay balance for customized nft.

                    var maybeWhiteListAddressPrice =
                        whiteListAddressPriceList.Value.SingleOrDefault(p =>
                            p.Address == Context.Sender && p.Price.Amount <= input.Price.Amount && p.Price.Symbol == input.Price.Symbol);
                    Assert(listedNftInfoList.Value.Count == 1, "Incorrect listed nft info.");
                    if (maybeWhiteListAddressPrice != null)
                    {
                        listedNftInfo = new ListedNFTInfo
                        {
                            Symbol = input.Symbol,
                            TokenId = input.TokenId,
                            Price = input.Price,
                            ListType = ListType.FixedPrice,
                            Quantity = 1,
                            Owner = listedNftInfoList.Value.First().Owner,
                            Duration = listedNftInfoList.Value.First().Duration
                        };
                    }
                    else
                    {
                        throw new AssertionException("Cannot find correct listed nft info.");
                    }
                }
                
                var quantity = input.Quantity;
                if (quantity > listedNftInfo.Quantity)
                {
                    var makerOfferInput = input.Clone();
                    makerOfferInput.Quantity = quantity.Sub(listedNftInfo.Quantity);
                    PerformMakeOffer(makerOfferInput);
                    input.Quantity = listedNftInfo.Quantity;
                }
            }

            if (listedNftInfo == null)
            {
                listedNftInfo = listedNftInfoList.Value.First();
            }

            switch (listedNftInfo.ListType)
            {
                case ListType.FixedPrice when whiteListAddressPriceList != null &&
                                              whiteListAddressPriceList.Value.Any(p => p.Address == Context.Sender):
                    TryDealWithFixedPrice(input, listedNftInfo);
                    RemoveRequest(input.Symbol, input.TokenId);
                    break;
                case ListType.FixedPrice when input.Price.Symbol == listedNftInfo.Price.Symbol &&
                                              input.Price.Amount >= listedNftInfo.Price.Amount:
                    TryDealWithFixedPrice(input, listedNftInfo);
                    break;
                case ListType.FixedPrice:
                    PerformMakeOffer(input);
                    break;
                case ListType.EnglishAuction:
                    PerformPlaceBidForEnglishAuction(input);
                    break;
                case ListType.DutchAuction:
                    PerformMakeOfferToDutchAuction(input);
                    break;
            }

            return new Empty();
        }

        public override Empty CancelOffer(CancelOfferInput input)
        {
            OfferList offerList;
            var newOfferList = new OfferList();
            var requestInfo = State.RequestInfoMap[input.Symbol][input.TokenId];

            // Admin can remove expired offer.
            if (input.OfferFrom != null)
            {
                AssertSenderIsAdmin();

                offerList = State.OfferListMap[input.Symbol][input.TokenId][input.OfferFrom];
                foreach (var offer in offerList.Value)
                {
                    if (offer.ExpireTime >= Context.CurrentBlockTime)
                    {
                        newOfferList.Value.Add(offer);
                    }
                }

                State.OfferListMap[input.Symbol][input.TokenId][input.OfferFrom] = newOfferList;

                if (!requestInfo.IsConfirmed && requestInfo.ExpireTime > Context.CurrentBlockTime)
                {
                    RemoveRequest(input.Symbol, input.TokenId);
                    var protocolVirtualAddressFrom = CalculateTokenHash(input.Symbol);
                    var protocolVirtualAddress =
                        Context.ConvertVirtualAddressToContractAddress(protocolVirtualAddressFrom);
                    var balanceOfNftProtocolVirtualAddress = State.TokenContract.GetBalance.Call(new GetBalanceInput
                    {
                        Symbol = requestInfo.Price.Symbol,
                        Owner = protocolVirtualAddress
                    }).Balance;
                    State.TokenContract.Transfer.VirtualSend(protocolVirtualAddressFrom, new TransferInput
                    {
                        To = requestInfo.Requester,
                        Symbol = requestInfo.Price.Symbol,
                        Amount = balanceOfNftProtocolVirtualAddress
                    });
                    Context.Fire(new NFTRequestCancelled
                    {
                        Symbol = input.Symbol,
                        TokenId = input.TokenId,
                        Requester = Context.Sender
                    });
                }

                var bid = State.BidMap[input.Symbol][input.TokenId][input.OfferFrom];

                if (bid.ExpireTime > Context.CurrentBlockTime)
                {
                    State.BidMap[input.Symbol][input.TokenId].Remove(input.OfferFrom);
                    Context.Fire(new BidCanceled
                    {
                        Symbol = input.Symbol,
                        TokenId = input.TokenId,
                        BidFrom = bid.From,
                        BidTo = bid.To
                    });
                }

                return new Empty();
            }

            offerList = State.OfferListMap[input.Symbol][input.TokenId][Context.Sender];

            // Check Request Map first.
            if (requestInfo != null)
            {
                CancelRequest(input, requestInfo);
                // Only one request for each token id.
                State.OfferListMap[input.Symbol][input.TokenId].Remove(Context.Sender);
                return new Empty();
            }

            var nftInfo = State.NFTContract.GetNFTInfo.Call(new GetNFTInfoInput
            {
                Symbol = input.Symbol,
                TokenId = input.TokenId
            });
            if (nftInfo.Creator == null)
            {
                // This nft does not exist.
                State.OfferListMap[input.Symbol][input.TokenId].Remove(Context.Sender);
            }

            if (input.IsCancelBid)
            {
                var bid = State.BidMap[input.Symbol][input.TokenId][Context.Sender];
                if (bid != null)
                {
                    var auctionInfo = State.EnglishAuctionInfoMap[input.Symbol][input.TokenId];
                    var finishTime = auctionInfo.Duration.StartTime.AddHours(auctionInfo.Duration.DurationHours);
                    if (auctionInfo.DealTo != null || Context.CurrentBlockTime >= finishTime)
                    {
                        State.TokenContract.Transfer.VirtualSend(CalculateTokenHash(input.Symbol, input.TokenId),
                            new TransferInput
                            {
                                To = Context.Sender,
                                Symbol = auctionInfo.PurchaseSymbol,
                                Amount = auctionInfo.EarnestMoney
                            });
                    }

                    Context.Fire(new BidCanceled
                    {
                        Symbol = input.Symbol,
                        TokenId = input.TokenId,
                        BidFrom = Context.Sender,
                        BidTo = bid.To
                    });
                }
            }

            if (input.IndexList != null && input.IndexList.Value.Any())
            {
                for (var i = 0; i < offerList.Value.Count; i++)
                {
                    if (!input.IndexList.Value.Contains(i))
                    {
                        newOfferList.Value.Add(offerList.Value[i]);
                    }
                }

                Context.Fire(new OfferCanceled
                {
                    Symbol = input.Symbol,
                    TokenId = input.TokenId,
                    OfferFrom = Context.Sender,
                    IndexList = input.IndexList
                });
            }
            else
            {
                newOfferList.Value.Add(offerList.Value);
            }

            State.OfferListMap[input.Symbol][input.TokenId][Context.Sender] = newOfferList;

            return new Empty();
        }

        private void CancelRequest(CancelOfferInput input, RequestInfo requestInfo)
        {
            Assert(requestInfo.Requester == Context.Sender, "No permission.");
            var virtualAddress = CalculateNFTVirtuaAddress(input.Symbol, input.TokenId);
            var balanceOfNftVirtualAddress = State.TokenContract.GetBalance.Call(new GetBalanceInput
            {
                Symbol = requestInfo.Price.Symbol,
                Owner = virtualAddress
            }).Balance;

            var depositReceiver = requestInfo.Requester;

            if (requestInfo.IsConfirmed)
            {
                if (requestInfo.ConfirmTime.AddHours(requestInfo.WorkHours) < Context.CurrentBlockTime)
                {
                    // Creator missed the deadline.

                    var protocolVirtualAddressFrom = CalculateTokenHash(input.Symbol);
                    var protocolVirtualAddress =
                        Context.ConvertVirtualAddressToContractAddress(protocolVirtualAddressFrom);
                    var balanceOfNftProtocolVirtualAddress = State.TokenContract.GetBalance.Call(new GetBalanceInput
                    {
                        Symbol = requestInfo.Price.Symbol,
                        Owner = protocolVirtualAddress
                    }).Balance;
                    var deposit = balanceOfNftVirtualAddress.Mul(FeeDenominator).Div(DefaultDepositConfirmRate)
                        .Sub(balanceOfNftVirtualAddress);
                    if (balanceOfNftProtocolVirtualAddress > 0)
                    {
                        State.TokenContract.Transfer.VirtualSend(protocolVirtualAddressFrom, new TransferInput
                        {
                            To = requestInfo.Requester,
                            Symbol = requestInfo.Price.Symbol,
                            Amount = Math.Min(balanceOfNftProtocolVirtualAddress, deposit)
                        });
                    }
                }
                else if (requestInfo.ListTime != null)
                {
                    depositReceiver = State.NFTContract.GetNFTProtocolInfo.Call(new StringValue {Value = input.Symbol})
                        .Creator;
                }
            }

            var virtualAddressFrom = CalculateTokenHash(input.Symbol, input.TokenId);

            State.TokenContract.Transfer.VirtualSend(virtualAddressFrom, new TransferInput
            {
                To = depositReceiver,
                Symbol = requestInfo.Price.Symbol,
                Amount = balanceOfNftVirtualAddress
            });
            RemoveRequest(input.Symbol, input.TokenId);

            Context.Fire(new NFTRequestCancelled
            {
                Symbol = input.Symbol,
                TokenId = input.TokenId,
                Requester = Context.Sender
            });
        }

        /// <summary>
        /// Sender is buyer.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="listedNftInfo"></param>
        private void TryDealWithFixedPrice(MakeOfferInput input, ListedNFTInfo listedNftInfo)
        {
            var whiteList = State.WhiteListAddressPriceListMap[input.Symbol][input.TokenId][input.OfferTo] ??
                            new WhiteListAddressPriceList();
            var whiteListPrice = whiteList.Value.FirstOrDefault(p => p.Address == Context.Sender);
            var usePrice = input.Price;
            if (listedNftInfo.Duration.PublicTime > Context.CurrentBlockTime)
            {
                // Public time not reached.

                if (whiteListPrice != null)
                {
                    Assert(input.Price.Symbol == whiteListPrice.Price.Symbol,
                        $"Need to use token {whiteListPrice.Price.Symbol}, not {input.Price.Symbol}");
                    usePrice = whiteListPrice.Price;
                    whiteList.Value.Remove(whiteListPrice);
                    State.WhiteListAddressPriceListMap[input.Symbol][input.TokenId][input.OfferTo] = whiteList;
                }
                else
                {
                    throw new AssertionException(
                        $"Sender is not in the white list, please need until {listedNftInfo.Duration.PublicTime}");
                }
            }

            var totalAmount = usePrice.Amount.Mul(input.Quantity);
            PerformDeal(new PerformDealInput
            {
                NFTFrom = input.OfferTo,
                NFTTo = Context.Sender,
                NFTSymbol = input.Symbol,
                NFTTokenId = input.TokenId,
                NFTQuantity = input.Quantity,
                PurchaseSymbol = usePrice.Symbol,
                PurchaseAmount = totalAmount,
                PurchaseTokenId = input.Price.TokenId
            });
        }

        /// <summary>
        /// Will go to Offer List.
        /// </summary>
        /// <param name="input"></param>
        private void PerformMakeOffer(MakeOfferInput input)
        {
            var offerList = State.OfferListMap[input.Symbol][input.TokenId][Context.Sender] ?? new OfferList();
            var expireTime = input.ExpireTime ?? Context.CurrentBlockTime.AddDays(DefaultExpireDays);
            var maybeSameOffer = offerList.Value.SingleOrDefault(o =>
                o.Price.Symbol == input.Price.Symbol && o.Price.Amount == input.Price.Amount &&
                o.ExpireTime == expireTime && o.To == input.OfferTo && o.From == Context.Sender);
            if (maybeSameOffer == null)
            {
                offerList.Value.Add(new Offer
                {
                    From = Context.Sender,
                    To = input.OfferTo,
                    Price = input.Price,
                    ExpireTime = expireTime,
                    Quantity = input.Quantity
                });
            }
            else
            {
                maybeSameOffer.Quantity = maybeSameOffer.Quantity.Add(input.Quantity);
            }

            State.OfferListMap[input.Symbol][input.TokenId][Context.Sender] = offerList;

            var addressList = State.OfferAddressListMap[input.Symbol][input.TokenId] ?? new AddressList();

            if (!addressList.Value.Contains(Context.Sender))
            {
                addressList.Value.Add(Context.Sender);
                State.OfferAddressListMap[input.Symbol][input.TokenId] = addressList;
            }

            Context.Fire(new OfferMade
            {
                Symbol = input.Symbol,
                TokenId = input.TokenId,
                OfferFrom = Context.Sender,
                OfferTo = input.OfferTo,
                ExpireTime = expireTime,
                Price = input.Price,
                Quantity = input.Quantity
            });
        }

        private void PerformPlaceBidForEnglishAuction(MakeOfferInput input)
        {
            var auctionInfo = State.EnglishAuctionInfoMap[input.Symbol][input.TokenId];
            if (auctionInfo == null)
            {
                throw new AssertionException($"Auction info of {input.Symbol}-{input.TokenId} not found.");
            }

            var duration = auctionInfo.Duration;
            Assert(Context.CurrentBlockTime <= duration.StartTime.AddHours(duration.DurationHours),
                "Auction already finished.");
            Assert(input.Price.Symbol == auctionInfo.PurchaseSymbol, "Incorrect symbol.");
            Assert(input.Price.TokenId == 0, "Do not support use NFT to purchase auction.");

            if (input.Price.Amount < auctionInfo.StartingPrice)
            {
                PerformMakeOffer(input);
                return;
            }

            var bidList = GetBidList(new GetBidListInput
            {
                Symbol = input.Symbol,
                TokenId = input.TokenId
            });
            var sortedBitList = new BidList
            {
                Value =
                {
                    bidList.Value.OrderByDescending(o => o.Price.Amount)
                }
            };
            if (sortedBitList.Value.Any() && input.Price.Amount < sortedBitList.Value.First().Price.Amount)
            {
                PerformMakeOffer(input);
                return;
            }

            var bid = new Bid
            {
                From = Context.Sender,
                To = input.OfferTo,
                Price = new Price
                {
                    Symbol = input.Price.Symbol,
                    Amount = input.Price.Amount
                },
                ExpireTime = input.ExpireTime ?? Context.CurrentBlockTime.AddDays(DefaultExpireDays)
            };

            var bidAddressList = State.BidAddressListMap[input.Symbol][input.TokenId] ?? new AddressList();
            if (!bidAddressList.Value.Contains(Context.Sender))
            {
                bidAddressList.Value.Add(Context.Sender);
                State.BidAddressListMap[input.Symbol][input.TokenId] = bidAddressList;
                // Charge earnest if the Sender is the first time to place a bid.
                ChargeEarnestMoney(input.Symbol, input.TokenId, auctionInfo.PurchaseSymbol, auctionInfo.EarnestMoney);
            }

            State.BidMap[input.Symbol][input.TokenId][Context.Sender] = bid;

            var remainAmount = input.Price.Amount.Sub(auctionInfo.EarnestMoney);
            Assert(
                State.TokenContract.GetBalance.Call(new GetBalanceInput
                {
                    Symbol = auctionInfo.PurchaseSymbol,
                    Owner = Context.Sender
                }).Balance >= remainAmount,
                "Insufficient balance to bid.");
            Assert(
                State.TokenContract.GetAllowance.Call(new GetAllowanceInput
                {
                    Symbol = auctionInfo.PurchaseSymbol,
                    Owner = Context.Sender,
                    Spender = Context.Self
                }).Allowance >= remainAmount,
                "Insufficient allowance to bid.");

            Context.Fire(new BidPlaced
            {
                Symbol = input.Symbol,
                TokenId = input.TokenId,
                Price = bid.Price,
                ExpireTime = bid.ExpireTime,
                OfferFrom = bid.From,
                OfferTo = input.OfferTo
            });
        }

        private void ChargeEarnestMoney(string nftSymbol, long nftTokenId, string purchaseSymbol, long earnestMoney)
        {
            if (earnestMoney > 0)
            {
                var virtualAddress = CalculateNFTVirtuaAddress(nftSymbol, nftTokenId);
                State.TokenContract.TransferFrom.Send(new TransferFromInput
                {
                    From = Context.Sender,
                    To = virtualAddress,
                    Symbol = purchaseSymbol,
                    Amount = earnestMoney
                });
            }
        }

        private void PerformMakeOfferToDutchAuction(MakeOfferInput input)
        {
            var auctionInfo = State.DutchAuctionInfoMap[input.Symbol][input.TokenId];
            if (auctionInfo == null)
            {
                throw new AssertionException($"Auction info of {input.Symbol}-{input.TokenId} not found.");
            }

            var duration = auctionInfo.Duration;
            Assert(Context.CurrentBlockTime <= duration.StartTime.AddHours(duration.DurationHours),
                "Auction already finished.");
            Assert(input.Price.Symbol == auctionInfo.PurchaseSymbol, "Incorrect symbol");
            var currentBiddingPrice = CalculateCurrentBiddingPrice(auctionInfo.StartingPrice, auctionInfo.EndingPrice,
                auctionInfo.Duration);
            if (input.Price.Amount < currentBiddingPrice)
            {
                PerformMakeOffer(input);
            }
            else
            {
                PerformDeal(new PerformDealInput
                {
                    NFTFrom = auctionInfo.Owner,
                    NFTTo = Context.Sender,
                    NFTQuantity = 1,
                    NFTSymbol = input.Symbol,
                    NFTTokenId = input.TokenId,
                    PurchaseSymbol = input.Price.Symbol,
                    PurchaseAmount = input.Price.Amount,
                    PurchaseTokenId = 0
                });
            }
        }

        private long CalculateCurrentBiddingPrice(long startingPrice, long endingPrice, ListDuration duration)
        {
            var passedSeconds = (Context.CurrentBlockTime - duration.StartTime).Seconds;
            var durationSeconds = duration.DurationHours.Mul(3600);
            if (passedSeconds == 0)
            {
                return startingPrice;
            }

            var diffPrice = endingPrice.Sub(startingPrice);
            return Math.Max(startingPrice.Sub(diffPrice.Mul(durationSeconds).Div(passedSeconds)), endingPrice);
        }

        private void MaybeReceiveRemainDeposit(RequestInfo requestInfo)
        {
            if (requestInfo == null) return;
            Assert(Context.CurrentBlockTime > requestInfo.WhiteListDueTime, "Due time not passed.");
            var nftProtocolInfo = State.NFTContract.GetNFTProtocolInfo.Call((new StringValue {Value = requestInfo.Symbol}));
            Assert(nftProtocolInfo.Creator == Context.Sender, "Only NFT Protocol Creator can claim remain deposit.");

            var nftVirtualAddressFrom = CalculateTokenHash(requestInfo.Symbol, requestInfo.TokenId);
            var nftVirtualAddress = Context.ConvertVirtualAddressToContractAddress(nftVirtualAddressFrom);
            var balance = State.TokenContract.GetBalance.Call(new GetBalanceInput
            {
                Symbol = requestInfo.Price.Symbol,
                Owner = nftVirtualAddress
            }).Balance;
            if (balance > 0)
            {
                State.TokenContract.Transfer.VirtualSend(nftVirtualAddressFrom, new TransferInput
                {
                    To = nftProtocolInfo.Creator,
                    Symbol = requestInfo.Price.Symbol,
                    Amount = balance
                });
            }

            RemoveRequest(requestInfo.Symbol, requestInfo.TokenId);
        }
    }
}