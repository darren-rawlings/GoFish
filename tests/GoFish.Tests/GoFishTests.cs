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

	/// <summary>A dealt and opened game.</summary>
	private static GoFishGame Open(IReadOnlyList<Card> deck, int setSize, out List<GoFishEvent> opening)
	{
		var game = new GoFishGame(deck, setSize: setSize);
		opening = game.Opening();
		return game;
	}

	private static GoFishGame Open(IReadOnlyList<Card> deck, int setSize) => Open(deck, setSize, out _);

	// ------------------------------------------------------------------ Both versions

	[Fact]
	public void SevenCardsEachAreDealtAlternatelyAndTheRestMakeThePond()
	{
		var deck = Deck.Shuffled(new Random(3));
		var game = new GoFishGame(deck, setSize: 4);
		Assert.Equal(7, game.Hand(0).Count);
		Assert.Equal(7, game.Hand(1).Count);
		Assert.Equal(38, game.Pond.Count);
		Assert.Equal(deck[0], game.Hand(0)[0]);
		Assert.Equal(deck[1], game.Hand(1)[0]);
		Assert.Equal(deck.Take(14), game.Dealt);
	}

	[Fact]
	public void NothingHappensBeforeTheOpening()
	{
		var game = new GoFishGame(Deck.Standard());
		Assert.Throws<InvalidOperationException>(() => game.Ask(Card.Ace));
		game.Opening();
		Assert.Throws<InvalidOperationException>(() => game.Opening());
	}

	[Fact]
	public void YouCanOnlyAskForWhatYouHold()
	{
		var game = Open(Deck.Standard(), 4);
		Assert.Throws<InvalidOperationException>(() => game.Ask(2));
		Assert.Throws<InvalidOperationException>(() => game.Fish(0));
	}

	[Theory]
	[InlineData(2, 1)]
	[InlineData(2, 2)]
	[InlineData(2, 3)]
	[InlineData(4, 4)]
	[InlineData(4, 5)]
	public void WholeGamesEndWithEveryCardInASet(int setSize, int seed)
	{
		var random = new Random(seed);
		var game = new GoFishGame(random, setSize: setSize);
		var ais = new[] { new GoFishAi(0, Difficulty.Medium, random), new GoFishAi(1, Difficulty.Hard, random) };
		var opening = game.Opening();
		foreach (var ai in ais)
			ai.Watch(opening);
		int moves = 0;
		while (!game.IsOver && moves++ < 2000)
		{
			var ai = ais[game.Current];
			var events = game.Phase == Phase.Fishing ? game.Fish(ai.ChoosePondCard(game)) : game.Ask(ai.ChooseRank(game));
			foreach (var watcher in ais)
				watcher.Watch(events);
			int cards = game.Hand(0).Count + game.Hand(1).Count + game.Pond.Count + game.BooksMade * setSize;
			Assert.Equal(52, cards);
		}
		Assert.True(game.IsOver);
		Assert.Equal(52 / setSize, game.BooksMade);
	}

	// ------------------------------------------------------------------ Books of four

	[Fact]
	public void AskingForARankTheOtherHoldsGetsAllOfThemAndYouGoAgain()
	{
		// Player 0: 7♣ 7♦ ...; player 1: 7♥ ...
		var deck = DeckStarting(C(7, Suit.Clubs), C(7, Suit.Hearts), C(7, Suit.Diamonds), C(2, Suit.Hearts));
		var game = Open(deck, 4);
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
		var game = Open(deck, 4);
		var events = game.Ask(3);
		Assert.IsType<GoFishTold>(events[^1]);
		Assert.Equal(Phase.Fishing, game.Phase);
		// The pond starts at 2♦; draw it: not a three, so the turn passes.
		var draw = game.Fish(0);
		Assert.False(((Drew)draw[0]).Lucky);
		Assert.Equal(1, game.Current);
		Assert.Equal(8, game.Hand(0).Count);
	}

	[Fact]
	public void DrawingTheRankYouAskedForMeansYouGoAgain()
	{
		var game = Open(Deck.Standard(), 4);
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
		var game = Open(deck, 4);
		var events = game.Ask(Card.King);
		var book = Assert.Single(events.OfType<Book>());
		Assert.Equal(Card.King, book.Rank);
		Assert.Equal(4, book.Cards.Count);
		Assert.Equal(new[] { Card.King }, game.Books(0));
		Assert.DoesNotContain(game.Hand(0), c => c.Rank == Card.King);
	}

	[Fact]
	public void FourOfAKindDealtIsLaidDownAtTheOpening()
	{
		// Player 0 is dealt all four queens (cards 0, 2, 4, 6 of the deal).
		var deck = DeckStarting(C(12, Suit.Clubs), C(2, Suit.Clubs), C(12, Suit.Diamonds), C(3, Suit.Clubs), C(12, Suit.Hearts), C(4, Suit.Clubs), C(12, Suit.Spades));
		var game = Open(deck, 4, out var opening);
		var book = Assert.Single(opening.OfType<Book>());
		Assert.Equal(0, book.Player);
		Assert.Equal(3, game.Hand(0).Count);
	}

	// ------------------------------------------------------------------ Pairs

	[Fact]
	public void PairsDealtAreLaidDownAtTheOpening()
	{
		// Player 0: 5♣ 5♦ 9♣ ...; player 1: 8♣ 8♦ ...
		var deck = DeckStarting(C(5, Suit.Clubs), C(8, Suit.Clubs), C(5, Suit.Diamonds), C(8, Suit.Diamonds), C(9, Suit.Clubs));
		var game = Open(deck, 2, out var opening);
		var pairs = opening.OfType<Book>().ToList();
		Assert.Contains(pairs, p => p.Player == 0 && p.Rank == 5 && p.Cards.Count == 2);
		Assert.Contains(pairs, p => p.Player == 1 && p.Rank == 8);
		// No one keeps a pair in hand.
		foreach (int p in new[] { 0, 1 })
			Assert.All(game.Hand(p).GroupBy(c => c.Rank), g => Assert.Single(g));
		Assert.IsType<TurnTo>(opening[^1]);
	}

	[Fact]
	public void AskingWithPairsGetsTheMatchAndLaysThePairDown()
	{
		// Player 0 holds 6♣; player 1 holds 6♥.
		var deck = DeckStarting(C(6, Suit.Clubs), C(6, Suit.Hearts));
		var game = Open(deck, 2);
		var events = game.Ask(6);
		Assert.Single(events.OfType<Gave>());
		var pair = Assert.Single(events.OfType<Book>());
		Assert.Equal(2, pair.Cards.Count);
		Assert.DoesNotContain(game.Hand(0), c => c.Rank == 6);
		Assert.Equal(0, game.Current);
	}

	[Fact]
	public void ThreeOfAKindWithPairsLaysOnePairAndKeepsOne()
	{
		var deck = DeckStarting(C(4, Suit.Clubs), C(2, Suit.Clubs), C(4, Suit.Diamonds), C(3, Suit.Clubs), C(4, Suit.Hearts));
		var game = Open(deck, 2, out var opening);
		Assert.Single(opening.OfType<Book>(), b => b.Player == 0 && b.Rank == 4);
		Assert.Single(game.Hand(0), c => c.Rank == 4);
	}

	[Fact]
	public void WithPairsATiedGameIsATie()
	{
		var random = new Random(11);
		int ties = 0;
		for (int n = 0; n < 200; n++)
		{
			var game = new GoFishGame(random, setSize: 2);
			var ai = new GoFishAi(0, Difficulty.Easy, random);
			game.Opening();
			while (!game.IsOver)
				_ = game.Phase == Phase.Fishing ? game.Fish(ai.ChoosePondCard(game)) : game.Ask(game.AskableRanks(game.Current)[0]);
			if (game.Books(0).Count == game.Books(1).Count)
			{
				ties++;
				Assert.Equal(-1, game.Winner);
			}
			else
			{
				Assert.InRange(game.Winner, 0, 1);
			}
		}
		Assert.True(ties > 0, "13–13 should come up now and then");
	}

	[Fact]
	public void HardBeatsEasyMoreOftenThanNot()
	{
		int hardWins = 0, decided = 0;
		const int games = 1000;
		for (int seed = 0; seed < games; seed++)
		{
			var random = new Random(seed);
			var game = new GoFishGame(random, first: seed % 2, setSize: 4);
			var ais = new[] { new GoFishAi(0, Difficulty.Easy, random), new GoFishAi(1, Difficulty.Hard, random) };
			var opening = game.Opening();
			foreach (var ai in ais)
				ai.Watch(opening);
			while (!game.IsOver)
			{
				var ai = ais[game.Current];
				var events = game.Phase == Phase.Fishing ? game.Fish(ai.ChoosePondCard(game)) : game.Ask(ai.ChooseRank(game));
				foreach (var watcher in ais)
					watcher.Watch(events);
			}
			if (game.Winner >= 0)
				decided++;
			if (game.Winner == 1)
				hardWins++;
		}
		// Go Fish is mostly luck, so remembering the table is worth a modest, steady edge.
		Assert.True(hardWins > decided * 0.53, $"Hard won {hardWins} of {decided}");
	}
}

public class GoFishGroupTests
{
	[Theory]
	[InlineData(3, 7)]
	[InlineData(4, 5)]
	[InlineData(5, 5)]
	public void BiggerTablesDealSevenOrFiveEach(int players, int each)
	{
		var game = new GoFishGame(new Random(1), players: players);
		Assert.All(Enumerable.Range(0, players), p => Assert.Equal(each, game.Hand(p).Count));
		Assert.Equal(52 - players * each, game.Pond.Count);
	}

	[Fact]
	public void YouAskAParticularPlayerAndTheTurnGoesLeft()
	{
		// Three players, dealt round: player 0 gets cards 0, 3, 6...; player 2 gets 2, 5, 8...
		var deck = Deck.Standard();
		var game = new GoFishGame(deck, players: 3);
		game.Opening();
		var rank = game.Hand(0)[0].Rank;
		Assert.Throws<ArgumentOutOfRangeException>(() => game.Ask(0, rank));
		var events = game.Ask(2, rank);
		var asked = Assert.IsType<Asked>(events[0]);
		Assert.Equal(2, asked.Target);
		if (events.OfType<GoFishTold>().Any())
		{
			game.Fish();
			Assert.Contains(game.Current, new[] { 0, 1 });
		}
	}

	[Theory]
	[InlineData(3, 1)]
	[InlineData(4, 2)]
	[InlineData(5, 3)]
	[InlineData(5, 4)]
	public void WholeGroupGamesEndWithEveryCardInABook(int players, int seed)
	{
		var random = new Random(seed);
		var game = new GoFishGame(random, players: players);
		var ais = Enumerable.Range(0, players).Select(p => new GoFishAi(p, (Difficulty)(p % 3), random)).ToArray();
		var opening = game.Opening();
		foreach (var ai in ais)
			ai.Watch(opening);
		int moves = 0;
		while (!game.IsOver && moves++ < 3000)
		{
			var ai = ais[game.Current];
			List<GoFishEvent> events;
			if (game.Phase == Phase.Fishing)
				events = game.Fish();
			else
			{
				var (target, rank) = ai.ChooseAsk(game);
				events = game.Ask(target, rank);
			}
			foreach (var watcher in ais)
				watcher.Watch(events);
			int cards = Enumerable.Range(0, players).Sum(p => game.Hand(p).Count) + game.Pond.Count + game.BooksMade * 4;
			Assert.Equal(52, cards);
		}
		Assert.True(game.IsOver);
		Assert.Equal(13, game.BooksMade);
		Assert.NotEmpty(game.Leaders);
	}
}
