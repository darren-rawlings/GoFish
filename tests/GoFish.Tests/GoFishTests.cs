using System;
using System.Collections.Generic;
using System.Linq;
using GoFish.Core;
using Renegade.Cards;
using Renegade.Core;
using Xunit;

namespace GoFish.Tests;

public class GoFishTests
{
	/// <summary>A deck whose first cards are dealt as given (alternating) and the rest in order.</summary>
	private static List<Card> DeckStarting(params Card[] top)
	{
		var rest = Deck.Standard().Where(c => !top.Contains(c));
		return top.Concat(rest).ToList();
	}

	private static Card C(int rank, Suit suit) => new(rank, suit);

	[Fact]
	public void SevenCardsEachAreDealtAlternatelyAndTheRestMakeThePond()
	{
		var deck = Deck.Shuffled(new Random(3));
		var game = new GoFishGame(deck);
		Assert.Equal(7, game.Hand(0).Count);
		Assert.Equal(7, game.Hand(1).Count);
		Assert.Equal(38, game.Pond.Count);
		Assert.Equal(deck[0], game.Hand(0)[0]);
		Assert.Equal(deck[1], game.Hand(1)[0]);
		Assert.Equal(deck.Take(14), game.Dealt);
		Assert.Equal(Phase.Asking, game.Phase);
	}

	[Fact]
	public void AskingForARankTheOtherHoldsGetsAllOfThemAndYouGoAgain()
	{
		// Player 0: 7♣ 7♦ ...; player 1: 7♥ ...
		var deck = DeckStarting(C(7, Suit.Clubs), C(7, Suit.Hearts), C(7, Suit.Diamonds), C(2, Suit.Hearts));
		var game = new GoFishGame(deck);
		var events = game.Ask(7);
		var gave = Assert.Single(events.OfType<Gave>());
		Assert.Equal(1, gave.From);
		Assert.Equal(new[] { C(7, Suit.Hearts) }, gave.Cards);
		Assert.Equal(3, game.Hand(0).Count(c => c.Rank == 7));
		Assert.Equal(0, game.Current);
		Assert.Equal(new TurnTo(0), events[^1]);
	}

	[Fact]
	public void NoneMeansGoFishAndAWrongDrawPassesTheTurn()
	{
		var deck = Deck.Standard(); // player 0 gets A♣ 3♣ 5♣ 7♣ 9♣ J♣ K♣; player 1 2♣ 4♣ 6♣ 8♣ 10♣ Q♣ A♦
		var game = new GoFishGame(deck);
		var events = game.Ask(3);
		Assert.IsType<GoFishTold>(events[^1]);
		Assert.Equal(Phase.Fishing, game.Phase);
		// The pond starts at 2♦; draw it: not a three, so the turn passes.
		var draw = game.Fish(0);
		var drew = Assert.IsType<Drew>(draw[0]);
		Assert.False(drew.Lucky);
		Assert.Equal(1, game.Current);
		Assert.Equal(8, game.Hand(0).Count);
	}

	[Fact]
	public void DrawingTheRankYouAskedForMeansYouGoAgain()
	{
		var deck = Deck.Standard();
		var game = new GoFishGame(deck);
		game.Ask(3);
		var draw = game.Fish(game.Pond.ToList().IndexOf(C(3, Suit.Diamonds)));
		Assert.True(((Drew)draw[0]).Lucky);
		Assert.Equal(0, game.Current);
	}

	[Fact]
	public void FourOfAKindMakeABook()
	{
		// Player 0 holds three kings; player 1 holds the fourth.
		var deck = DeckStarting(C(13, Suit.Clubs), C(13, Suit.Spades), C(13, Suit.Diamonds), C(2, Suit.Spades), C(13, Suit.Hearts));
		var game = new GoFishGame(deck);
		var events = game.Ask(Card.King);
		var book = Assert.Single(events.OfType<Book>());
		Assert.Equal(Card.King, book.Rank);
		Assert.Equal(4, book.Cards.Count);
		Assert.Equal(new[] { Card.King }, game.Books(0));
		Assert.DoesNotContain(game.Hand(0), c => c.Rank == Card.King);
	}

	[Fact]
	public void YouCanOnlyAskForWhatYouHold()
	{
		var game = new GoFishGame(Deck.Standard());
		Assert.Throws<InvalidOperationException>(() => game.Ask(2));
		Assert.Throws<InvalidOperationException>(() => game.Fish(0));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(5)]
	public void WholeGamesEndWithThirteenBooksAndEveryCardAccountedFor(int seed)
	{
		var random = new Random(seed);
		var game = new GoFishGame(random);
		var ais = new[] { new GoFishAi(0, Difficulty.Medium, random), new GoFishAi(1, Difficulty.Hard, random) };
		int moves = 0;
		while (!game.IsOver && moves++ < 1000)
		{
			var ai = ais[game.Current];
			var events = game.Phase == Phase.Fishing ? game.Fish(ai.ChoosePondCard(game)) : game.Ask(ai.ChooseRank(game));
			foreach (var watcher in ais)
				watcher.Watch(events);
			int cards = game.Hand(0).Count + game.Hand(1).Count + game.Pond.Count + game.BooksMade * 4;
			Assert.Equal(52, cards);
		}
		Assert.True(game.IsOver);
		Assert.Equal(13, game.BooksMade);
		Assert.InRange(game.Winner, 0, 1);
	}

	[Fact]
	public void HardBeatsEasyMoreOftenThanNot()
	{
		int hardWins = 0;
		const int games = 1000;
		for (int seed = 0; seed < games; seed++)
		{
			var random = new Random(seed);
			var game = new GoFishGame(random, first: seed % 2);
			var ais = new[] { new GoFishAi(0, Difficulty.Easy, random), new GoFishAi(1, Difficulty.Hard, random) };
			while (!game.IsOver)
			{
				var ai = ais[game.Current];
				var events = game.Phase == Phase.Fishing ? game.Fish(ai.ChoosePondCard(game)) : game.Ask(ai.ChooseRank(game));
				foreach (var watcher in ais)
					watcher.Watch(events);
			}
			if (game.Winner == 1)
				hardWins++;
		}
		// Go Fish is mostly luck, so remembering the table is worth a modest, steady edge.
		Assert.True(hardWins > games * 0.53, $"Hard won {hardWins} of {games}");
	}
}
