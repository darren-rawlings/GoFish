using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace GoFish.Core;

/// <summary>Something that happened, in order, for the table to act out.</summary>
public abstract record GoFishEvent;

/// <summary>A player asked another for a rank ("Rosie, got any sevens?").</summary>
public sealed record Asked(int Player, int Target, int Rank) : GoFishEvent;

/// <summary>The player asked had some: they hand every card of that rank over.</summary>
public sealed record Gave(int From, int To, IReadOnlyList<Card> Cards) : GoFishEvent;

/// <summary>The player asked had none: "Go fish!" (Player is the one who asked.)</summary>
public sealed record GoFishTold(int Player, int By) : GoFishEvent;

/// <summary>A player drew a card from the top of the stock (the "pond").</summary>
public sealed record Drew(int Player, Card Card, int PondIndex, bool Lucky) : GoFishEvent;

/// <summary>A player laid down a set: a book of four, or a pair, depending on the rules.</summary>
public sealed record Book(int Player, int Rank, IReadOnlyList<Card> Cards) : GoFishEvent;

/// <summary>Whose turn it is now.</summary>
public sealed record TurnTo(int Player) : GoFishEvent;

/// <summary>Every card is in a set. Books[p] is how many each player laid down.</summary>
public sealed record GameOver(int[] Books) : GoFishEvent;

public enum Phase { Asking, Fishing, Over }

/// <summary>
/// Go Fish for 2 to 5 players (the Bicycle rules): seven cards each for two or three players,
/// five each for four or five; the rest is left face down as the stock (the "pond"; index 0 is
/// the top). Players lay down sets as soon as they have them: books of four (the standard) or
/// pairs (a popular family version). On your turn, ask another player for a rank you hold. If
/// they have any, they give you all of them and you ask again. If not, "Go fish!": draw from the
/// stock; if you draw the rank you asked for, you go again, otherwise the turn passes to your
/// left. A player with no cards draws one at the start of their turn (or sits out if the stock is
/// empty). When every card is in a set, the most sets wins.
/// </summary>
public sealed class GoFishGame
{
	public const int MinPlayers = 2, MaxPlayers = 5;

	private readonly List<Card>[] _hands;
	private readonly List<int>[] _books;
	private readonly List<Card> _pond;
	private int _askedRank;
	private bool _opened;

	/// <summary>
	/// Deals a game from a deck (top card first): one card at a time round the table, starting
	/// with the first player. Call <see cref="Opening"/> next, to lay down any sets dealt.
	/// </summary>
	public GoFishGame(IReadOnlyList<Card> deck, int first = 0, int setSize = 4, int players = 2)
	{
		if (deck.Count != 52 || deck.Distinct().Count() != 52)
			throw new ArgumentException("Go Fish needs a full deck.", nameof(deck));
		if (setSize is not (2 or 4))
			throw new ArgumentOutOfRangeException(nameof(setSize), "Sets are pairs (2) or books (4).");
		if (players is < MinPlayers or > MaxPlayers)
			throw new ArgumentOutOfRangeException(nameof(players), "Go Fish is for 2 to 5 players.");
		SetSize = setSize;
		Players = players;
		_hands = Enumerable.Range(0, players).Select(_ => new List<Card>()).ToArray();
		_books = Enumerable.Range(0, players).Select(_ => new List<int>()).ToArray();
		Dealt = deck.Take(HandSize * players).ToList();
		for (int i = 0; i < Dealt.Count; i++)
			_hands[(first + i) % players].Add(Dealt[i]);
		_pond = deck.Skip(HandSize * players).ToList();
		Current = first;
	}

	public GoFishGame(Random random, int first = 0, int setSize = 4, int players = 2) : this(Deck.Shuffled(random), first, setSize, players) { }

	public int Players { get; }

	/// <summary>Seven cards each for two or three players, five for four or five.</summary>
	public int HandSize => Players <= 3 ? 7 : 5;

	/// <summary>4 for books, 2 for pairs.</summary>
	public int SetSize { get; }

	/// <summary>The cards dealt, in dealing order (round the table, starting with the first player).</summary>
	public IReadOnlyList<Card> Dealt { get; }

	public int Current { get; private set; }
	public Phase Phase { get; private set; } = Phase.Asking;
	public IReadOnlyList<Card> Hand(int player) => _hands[player];

	/// <summary>The ranks of the sets a player has laid down (a rank appears twice for two pairs).</summary>
	public IReadOnlyList<int> Books(int player) => _books[player];

	public IReadOnlyList<Card> Pond => _pond;
	public int BooksMade => _books.Sum(b => b.Count);
	public int SetsInDeck => 52 / SetSize;
	public bool IsOver => Phase == Phase.Over;

	/// <summary>The rank the current player asked for and is fishing for.</summary>
	public int FishingFor => Phase == Phase.Fishing ? _askedRank : 0;

	/// <summary>The player to someone's left (the next to play).</summary>
	public int Next(int player) => (player + 1) % Players;

	/// <summary>Ranks a player may ask for: the ones in their hand.</summary>
	public IReadOnlyList<int> AskableRanks(int player) => _hands[player].Select(c => c.Rank).Distinct().OrderBy(r => r).ToList();

	/// <summary>Who the current player may ask: anyone else holding cards (or, if no one does, anyone else).</summary>
	public IReadOnlyList<int> AskablePlayers(int player)
	{
		var others = Enumerable.Range(0, Players).Where(p => p != player).ToList();
		var holding = others.Where(p => _hands[p].Count > 0).ToList();
		return holding.Count > 0 ? holding : others;
	}

	/// <summary>The players with the most sets once the game is over (more than one is a tie).</summary>
	public IReadOnlyList<int> Leaders
	{
		get
		{
			int most = _books.Max(b => b.Count);
			return Enumerable.Range(0, Players).Where(p => _books[p].Count == most).ToList();
		}
	}

	/// <summary>The winner once the game is over, or -1 for a tie (or before the end).</summary>
	public int Winner => IsOver && Leaders.Count == 1 ? Leaders[0] : -1;

	/// <summary>
	/// After the deal, every player lays down the sets they were dealt (starting with the first
	/// player), then the first turn starts. Call once, before anything else.
	/// </summary>
	public List<GoFishEvent> Opening()
	{
		if (_opened)
			throw new InvalidOperationException("The game has already opened.");
		_opened = true;
		var events = new List<GoFishEvent>();
		for (int i = 0; i < Players; i++)
			MakeBooks((Current + i) % Players, events);
		StartTurn(Current, events, tries: 0);
		return events;
	}

	/// <summary>Two players: the current player asks the other.</summary>
	public List<GoFishEvent> Ask(int rank) => Ask(Next(Current), rank);

	/// <summary>The current player asks another for a rank they hold.</summary>
	public List<GoFishEvent> Ask(int target, int rank)
	{
		Opened();
		if (Phase != Phase.Asking)
			throw new InvalidOperationException($"Can't ask now ({Phase}).");
		if (target == Current || target < 0 || target >= Players)
			throw new ArgumentOutOfRangeException(nameof(target), "Ask another player.");
		if (!_hands[Current].Any(c => c.Rank == rank))
			throw new InvalidOperationException("You can only ask for a rank you hold.");
		var events = new List<GoFishEvent> { new Asked(Current, target, rank) };
		var given = _hands[target].Where(c => c.Rank == rank).ToList();
		if (given.Count > 0)
		{
			_hands[target].RemoveAll(c => c.Rank == rank);
			_hands[Current].AddRange(given);
			events.Add(new Gave(target, Current, given));
			MakeBooks(Current, events);
			StartTurn(Current, events, tries: 0);
			return events;
		}
		events.Add(new GoFishTold(Current, target));
		if (_pond.Count == 0)
		{
			StartTurn(Next(Current), events, tries: 0);
			return events;
		}
		_askedRank = rank;
		Phase = Phase.Fishing;
		return events;
	}

	/// <summary>After "Go fish!", the current player draws (normally the top card, index 0).</summary>
	public List<GoFishEvent> Fish(int pondIndex = 0)
	{
		Opened();
		if (Phase != Phase.Fishing)
			throw new InvalidOperationException($"Can't fish now ({Phase}).");
		var card = _pond[pondIndex];
		_pond.RemoveAt(pondIndex);
		_hands[Current].Add(card);
		bool lucky = card.Rank == _askedRank;
		var events = new List<GoFishEvent> { new Drew(Current, card, pondIndex, lucky) };
		MakeBooks(Current, events);
		Phase = Phase.Asking;
		StartTurn(lucky ? Current : Next(Current), events, tries: 0);
		return events;
	}

	private void Opened()
	{
		if (!_opened)
			throw new InvalidOperationException("Call Opening() after the deal first.");
	}

	/// <summary>
	/// Starts a player's turn: a player without cards draws one from the top of the stock (or,
	/// with the stock empty, is skipped). The game ends once every card is in a set.
	/// </summary>
	private void StartTurn(int player, List<GoFishEvent> events, int tries)
	{
		if (BooksMade == SetsInDeck)
		{
			Phase = Phase.Over;
			events.Add(new GameOver(_books.Select(b => b.Count).ToArray()));
			return;
		}
		Current = player;
		Phase = Phase.Asking;
		if (_hands[player].Count == 0)
		{
			if (_pond.Count > 0)
			{
				var card = _pond[0];
				_pond.RemoveAt(0);
				_hands[player].Add(card);
				events.Add(new Drew(player, card, 0, Lucky: false));
				MakeBooks(player, events);
			}
			else if (tries < Players)
			{
				StartTurn(Next(player), events, tries + 1);
				return;
			}
		}
		events.Add(new TurnTo(player));
	}

	/// <summary>Lays down every complete set in a hand (three of a kind with pairs: one pair, one card kept).</summary>
	private void MakeBooks(int player, List<GoFishEvent> events)
	{
		foreach (var group in _hands[player].GroupBy(c => c.Rank).Where(g => g.Count() >= SetSize).ToList())
		{
			var cards = group.ToList();
			for (int set = 0; set + SetSize <= cards.Count; set += SetSize)
			{
				var laid = cards.GetRange(set, SetSize);
				foreach (var card in laid)
					_hands[player].Remove(card);
				_books[player].Add(group.Key);
				events.Add(new Book(player, group.Key, laid));
			}
		}
	}
}
