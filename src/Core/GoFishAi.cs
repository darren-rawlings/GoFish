using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Core;

namespace GoFish.Core;

/// <summary>
/// A computer player's choices. Easy asks a random player for a random rank it holds. Medium asks
/// for the rank it holds most of. Hard also remembers the table, the way a sharp player does: a
/// rank someone asked for is one they hold (until they give it away or book it), and a rank
/// someone said "Go fish!" to is one they don't have (until they draw again).
/// </summary>
public sealed class GoFishAi
{
	private readonly Random _random;
	private readonly Dictionary<int, HashSet<int>> _has = new();
	private readonly Dictionary<int, HashSet<int>> _lacks = new();
	private readonly Dictionary<int, int> _lastAsk = new();

	public GoFishAi(int player, Difficulty difficulty, Random random)
	{
		Player = player;
		Difficulty = difficulty;
		_random = random;
	}

	public int Player { get; }
	public Difficulty Difficulty { get; }

	private HashSet<int> Has(int player) => _has.TryGetValue(player, out var set) ? set : _has[player] = new HashSet<int>();
	private HashSet<int> Lacks(int player) => _lacks.TryGetValue(player, out var set) ? set : _lacks[player] = new HashSet<int>();

	/// <summary>Learns from everything that happens at the table.</summary>
	public void Watch(IEnumerable<GoFishEvent> events)
	{
		foreach (var e in events)
		{
			switch (e)
			{
				case Asked asked:
					_lastAsk[asked.Player] = asked.Rank;
					Has(asked.Player).Add(asked.Rank);
					Lacks(asked.Player).Remove(asked.Rank);
					break;
				case Gave gave:
					int rank = gave.Cards[0].Rank;
					Has(gave.From).Remove(rank);
					Lacks(gave.From).Add(rank);
					Has(gave.To).Add(rank);
					Lacks(gave.To).Remove(rank);
					break;
				case GoFishTold told when _lastAsk.TryGetValue(told.Player, out int asked):
					Has(told.By).Remove(asked);
					Lacks(told.By).Add(asked);
					break;
				case Drew drew:
					// A new unknown card: what they lacked, they might have now.
					Lacks(drew.Player).Clear();
					break;
				case Book book:
					foreach (var set in _has.Values)
						set.Remove(book.Rank);
					foreach (var set in _lacks.Values)
						set.Remove(book.Rank);
					break;
			}
		}
	}

	/// <summary>Two players: the rank to ask the other for.</summary>
	public int ChooseRank(GoFishGame game) => ChooseAsk(game).Rank;

	/// <summary>Whom to ask, and for what. The game must be in the Asking phase on this player's turn.</summary>
	public (int Target, int Rank) ChooseAsk(GoFishGame game)
	{
		var hand = game.Hand(Player);
		var ranks = game.AskableRanks(Player);
		var targets = game.AskablePlayers(Player);
		if (ranks.Count == 0)
			throw new InvalidOperationException("No cards to ask with.");
		int Pick(IReadOnlyList<int> from) => from[_random.Next(from.Count)];
		int Count(int r) => hand.Count(c => c.Rank == r);

		if (Difficulty == Difficulty.Easy)
			return (Pick(targets), Pick(ranks));

		if (Difficulty == Difficulty.Hard)
		{
			// A rank we know someone holds wins outright: most cards first.
			var sure = (from t in targets from r in ranks where Has(t).Contains(r) select (t, r))
				.OrderByDescending(x => Count(x.r)).ToList();
			if (sure.Count > 0)
				return sure[0];
			// Avoid asking someone for what they've just said they don't have; prefer whoever holds most.
			var options = (from t in targets from r in ranks where !Lacks(t).Contains(r) select (t, r)).ToList();
			if (options.Count > 0)
			{
				int bestCount = options.Max(o => Count(o.r));
				return options.Where(o => Count(o.r) == bestCount).OrderByDescending(o => game.Hand(o.t).Count).First();
			}
		}
		int most = ranks.Max(Count);
		var best = ranks.Where(r => Count(r) == most).ToList();
		return (Pick(targets), Pick(best));
	}

	/// <summary>Which card to draw: the top of the face-down stock.</summary>
	public int ChoosePondCard(GoFishGame game) => 0;
}
