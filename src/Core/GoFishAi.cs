using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Core;

namespace GoFish.Core;

/// <summary>
/// The computer's choices. Easy asks for a random rank it holds. Medium asks for the rank it holds
/// most of. Hard also remembers the table, the way a sharp player does: a rank you asked for is
/// one you hold (unless you've since given it away or booked it), and a rank you said "Go fish!"
/// to is one you didn't have (until you draw again).
/// </summary>
public sealed class GoFishAi
{
	private readonly Random _random;
	// What the computer believes about the other player's hand.
	private readonly HashSet<int> _theyHave = new();
	private readonly HashSet<int> _theyLack = new();
	private int _lastAsk;

	public GoFishAi(int player, Difficulty difficulty, Random random)
	{
		Player = player;
		Difficulty = difficulty;
		_random = random;
	}

	public int Player { get; }
	public Difficulty Difficulty { get; }

	/// <summary>Learns from everything that happens at the table.</summary>
	public void Watch(IEnumerable<GoFishEvent> events)
	{
		foreach (var e in events)
		{
			switch (e)
			{
				case Asked asked when asked.Player != Player:
					_theyHave.Add(asked.Rank);
					_theyLack.Remove(asked.Rank);
					break;
				case Asked asked:
					_lastAsk = asked.Rank;
					break;
				case Gave gave when gave.From != Player:
					// They gave us all of that rank: they have none left.
					_theyHave.Remove(gave.Cards[0].Rank);
					_theyLack.Add(gave.Cards[0].Rank);
					break;
				case Gave gave:
					// We gave them our cards of that rank: they hold it now.
					_theyHave.Add(gave.Cards[0].Rank);
					_theyLack.Remove(gave.Cards[0].Rank);
					break;
				case GoFishTold told when told.Player == Player:
					// They had none of what we asked for.
					_theyHave.Remove(_lastAsk);
					_theyLack.Add(_lastAsk);
					break;
				case Drew drew when drew.Player != Player:
					// A new unknown card: what they lacked, they might have now.
					_theyLack.Clear();
					break;
				case Book book:
					_theyHave.Remove(book.Rank);
					_theyLack.Remove(book.Rank);
					break;
			}
		}
	}

	/// <summary>The rank to ask for. The game must be in the Asking phase on this player's turn.</summary>
	public int ChooseRank(GoFishGame game)
	{
		var hand = game.Hand(Player);
		var ranks = game.AskableRanks(Player);
		if (ranks.Count == 0)
			throw new InvalidOperationException("No cards to ask with.");
		if (Difficulty == Difficulty.Easy)
			return ranks[_random.Next(ranks.Count)];

		int Count(int rank) => hand.Count(c => c.Rank == rank);
		if (Difficulty == Difficulty.Hard)
		{
			// A rank we know they hold wins outright: most cards first.
			var sure = ranks.Where(_theyHave.Contains).OrderByDescending(Count).ToList();
			if (sure.Count > 0)
				return sure[0];
			// Otherwise avoid ranks they've just said they don't have.
			var open = ranks.Where(r => !_theyLack.Contains(r)).ToList();
			if (open.Count > 0)
				ranks = open;
		}
		int most = ranks.Max(Count);
		var best = ranks.Where(r => Count(r) == most).ToList();
		return best[_random.Next(best.Count)];
	}

	/// <summary>Which card to draw: the top of the face-down stock.</summary>
	public int ChoosePondCard(GoFishGame game) => 0;
}
