using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace GoFish.Core;

/// <summary>Something that happened, in order, for the table to act out.</summary>
public abstract record GoFishEvent;

/// <summary>A player asked the other for a rank ("Got any sevens?").</summary>
public sealed record Asked(int Player, int Rank) : GoFishEvent;

/// <summary>The other player had some: they hand every card of that rank over.</summary>
public sealed record Gave(int From, int To, IReadOnlyList<Card> Cards) : GoFishEvent;

/// <summary>The other player had none: "Go fish!"</summary>
public sealed record GoFishTold(int Player) : GoFishEvent;

/// <summary>A player drew a card from the pond (the face-down cards in the middle).</summary>
public sealed record Drew(int Player, Card Card, int PondIndex, bool Lucky) : GoFishEvent;

/// <summary>A player completed four of a kind and laid it down.</summary>
public sealed record Book(int Player, int Rank, IReadOnlyList<Card> Cards) : GoFishEvent;

/// <summary>Whose turn it is now.</summary>
public sealed record TurnTo(int Player) : GoFishEvent;

/// <summary>All thirteen books are made.</summary>
public sealed record GameOver(int[] Books) : GoFishEvent;

public enum Phase { Asking, Fishing, Over }

/// <summary>
/// Go Fish for two players: seven cards each, the rest spread face down as the pond. On your
/// turn, ask for a rank you hold. If the other player has any, they give you all of them and
/// you ask again. If not, "Go fish!": draw from the pond; if you draw the rank you asked for, you
/// go again, otherwise the turn passes. Four of a kind make a book. A player with no cards draws
/// one at the start of their turn (or sits out if the pond is empty). When all thirteen books are
/// made, the most books wins.
/// </summary>
public sealed class GoFishGame
{
	public const int Players = 2;
	public const int HandSize = 7;

	private readonly List<Card>[] _hands = { new(), new() };
	private readonly List<int>[] _books = { new(), new() };
	private readonly List<Card> _pond;
	private int _askedRank;

	/// <summary>Deals a game from a deck (top card first); the deal alternates, starting with player 0.</summary>
	public GoFishGame(IReadOnlyList<Card> deck, int first = 0)
	{
		if (deck.Count != 52 || deck.Distinct().Count() != 52)
			throw new ArgumentException("Go Fish needs a full deck.", nameof(deck));
		Dealt = deck.Take(HandSize * Players).ToList();
		for (int i = 0; i < Dealt.Count; i++)
			_hands[(first + i) % Players].Add(Dealt[i]);
		_pond = deck.Skip(HandSize * Players).ToList();
		Current = first;
		// Seven cards can't hold a book, so no one starts with one.
	}

	public GoFishGame(Random random, int first = 0) : this(Deck.Shuffled(random), first) { }

	/// <summary>The cards dealt, in dealing order (alternating, starting with the first player).</summary>
	public IReadOnlyList<Card> Dealt { get; }

	public int Current { get; private set; }
	public Phase Phase { get; private set; } = Phase.Asking;
	public IReadOnlyList<Card> Hand(int player) => _hands[player];
	public IReadOnlyList<int> Books(int player) => _books[player];
	public IReadOnlyList<Card> Pond => _pond;
	public int BooksMade => _books[0].Count + _books[1].Count;
	public bool IsOver => Phase == Phase.Over;

	/// <summary>The rank the current player asked for and is fishing for.</summary>
	public int FishingFor => Phase == Phase.Fishing ? _askedRank : 0;

	/// <summary>Ranks the current player may ask for: the ones in their hand.</summary>
	public IReadOnlyList<int> AskableRanks(int player) => _hands[player].Select(c => c.Rank).Distinct().OrderBy(r => r).ToList();

	/// <summary>0 or 1 once the game is over (13 books can't split evenly, so there's no tie); -1 before.</summary>
	public int Winner => !IsOver ? -1 : _books[0].Count > _books[1].Count ? 0 : 1;

	/// <summary>The current player asks the other for a rank they hold.</summary>
	public List<GoFishEvent> Ask(int rank)
	{
		if (Phase != Phase.Asking)
			throw new InvalidOperationException($"Can't ask now ({Phase}).");
		if (!_hands[Current].Any(c => c.Rank == rank))
			throw new InvalidOperationException("You can only ask for a rank you hold.");
		var events = new List<GoFishEvent> { new Asked(Current, rank) };
		int other = 1 - Current;
		var given = _hands[other].Where(c => c.Rank == rank).ToList();
		if (given.Count > 0)
		{
			_hands[other].RemoveAll(c => c.Rank == rank);
			_hands[Current].AddRange(given);
			events.Add(new Gave(other, Current, given));
			MakeBooks(Current, events);
			ContinueTurn(events);
			return events;
		}
		events.Add(new GoFishTold(Current));
		if (_pond.Count == 0)
		{
			PassTurn(events);
			return events;
		}
		_askedRank = rank;
		Phase = Phase.Fishing;
		return events;
	}

	/// <summary>After "Go fish!", the current player draws the pond card at an index.</summary>
	public List<GoFishEvent> Fish(int pondIndex)
	{
		if (Phase != Phase.Fishing)
			throw new InvalidOperationException($"Can't fish now ({Phase}).");
		var card = _pond[pondIndex];
		_pond.RemoveAt(pondIndex);
		_hands[Current].Add(card);
		bool lucky = card.Rank == _askedRank;
		var events = new List<GoFishEvent> { new Drew(Current, card, pondIndex, lucky) };
		MakeBooks(Current, events);
		Phase = Phase.Asking;
		if (lucky)
			ContinueTurn(events);
		else
			PassTurn(events);
		return events;
	}

	/// <summary>The same player goes again (after getting cards, or a lucky draw).</summary>
	private void ContinueTurn(List<GoFishEvent> events) => StartTurn(Current, events, tries: 0);

	private void PassTurn(List<GoFishEvent> events) => StartTurn(1 - Current, events, tries: 0);

	/// <summary>
	/// Starts a player's turn: a player without cards draws one (or, with the pond empty, is
	/// skipped). The game ends once every book is made.
	/// </summary>
	private void StartTurn(int player, List<GoFishEvent> events, int tries)
	{
		if (BooksMade == 13)
		{
			Phase = Phase.Over;
			events.Add(new GameOver(new[] { _books[0].Count, _books[1].Count }));
			return;
		}
		Current = player;
		Phase = Phase.Asking;
		if (_hands[player].Count == 0)
		{
			if (_pond.Count > 0)
			{
				var card = _pond[^1];
				_pond.RemoveAt(_pond.Count - 1);
				_hands[player].Add(card);
				events.Add(new Drew(player, card, _pond.Count, Lucky: false));
				MakeBooks(player, events);
			}
			else if (tries < Players)
			{
				StartTurn(1 - player, events, tries + 1);
				return;
			}
		}
		events.Add(new TurnTo(player));
	}

	private void MakeBooks(int player, List<GoFishEvent> events)
	{
		foreach (var group in _hands[player].GroupBy(c => c.Rank).Where(g => g.Count() == 4).ToList())
		{
			var cards = group.ToList();
			_hands[player].RemoveAll(c => c.Rank == group.Key);
			_books[player].Add(group.Key);
			events.Add(new Book(player, group.Key, cards));
		}
	}
}
