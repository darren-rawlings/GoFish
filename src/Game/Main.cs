using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GoFish.Core;
using Godot;
using Renegade.Cards;
using Renegade.Core;
using Renegade.Play;
using Renegade.Ui;

namespace GoFish.Game;

/// <summary>
/// Go Fish against one to four computer players, played with real-looking cards: the deck is
/// shuffled with a riffle and a bridge and dealt, glass hands pick the cards up and fan them, and
/// every ask, throw, draw and book is acted out with a pinch of thumb and finger. Whoever asks has
/// their hands turn deep blue, and whoever is asked deep yellow; everything said goes in the log.
/// Tap a card in your fan to ask for its rank (then, with more than one opponent, tap whom to
/// ask); after "Go fish!", tap the deck (or let Settings › Fish automatically do it).
/// </summary>
public partial class Main : CardGame
{
	private readonly Dictionary<Card, Card3D> _nodes = new();
	private readonly List<Card3D>[] _books = Enumerable.Range(0, CardTable.MaxSeats).Select(_ => new List<Card3D>()).ToArray();
	private GoFishGame? _game;
	private readonly GoFishAi?[] _ais = new GoFishAi?[CardTable.MaxSeats];
	private int _selectedRank;
	private int _first;

	protected override string Title => "Go Fish";
	protected override string Hint => "Tap a card to ask for it";
	protected override int MinPlayers => 2;
	protected override int MaxPlayers => 5;
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => _game is { IsOver: false };

	/// <summary>4 for books of four (the standard), 2 to play with pairs (chosen in Settings).</summary>
	private int SetSize => Settings.Extra.TryGetValue("sets", out var sets) && sets == "pairs" ? 2 : 4;

	private bool AutoFish => Flag("autofish");

	protected override void AddSettings()
	{
		// Books of four (the standard rules) or pairs (a popular family version); from the next deal.
		Hud.AddChoice("Lay down", new[] { "Books of four", "Pairs" }, SetSize == 2 ? 1 : 0, choice =>
		{
			Settings.Extra["sets"] = choice == 1 ? "pairs" : "books";
			Settings.Save();
			if (_game == null || _game.BooksMade == 0)
				Run(StartRound());
		});
		// Draw for me after "Go fish!" (or wait for my tap): a way to set the pace.
		Hud.AddToggle("Fish automatically", AutoFish, on =>
		{
			SetFlag("autofish", on);
			if (on && !Busy && _game is { Current: 0, Phase: Phase.Fishing })
				Run(Play(_game.Fish()));
		});
	}

	protected override void LevelChanged(Difficulty difficulty)
	{
		for (int s = 1; s < Players; s++)
			if (_ais[s] != null)
				_ais[s] = new GoFishAi(s, difficulty, Random);
	}

	/// <summary>Where cards thrown to a player land: on the felt in front of them.</summary>
	private Transform3D Landing(int to, int i)
	{
		float yaw = ((float)Random.NextDouble() - 0.5f) * 0.6f;
		return new Transform3D(Card3D.FaceUp(yaw), Table.Landing[to] + new Vector3(i * 0.12f, 0.004f * (i + 1), i * 0.04f));
	}

	// ------------------------------------------------------------------ A round

	protected override async Task NewRound()
	{
		int round = Round;
		_selectedRank = 0;
		_nodes.Clear();
		_game = null;
		foreach (var books in _books)
			books.Clear();
		SetScore();

		// A fresh deck, face down in the middle: shuffle it for real (on screen, riffles and a bridge).
		var order = Deck.Shuffled(Random);
		var nodes = await ShuffleOnTable(order);
		if (round != Round)
			return;
		for (int i = 0; i < order.Count; i++)
			_nodes[order[i]] = nodes[i];
		var game = new GoFishGame(order, _first, SetSize, Players);
		for (int s = 0; s < Players; s++)
			_ais[s] = s > 0 ? new GoFishAi(s, Settings.Difficulty, Random) : Demo ? new GoFishAi(0, Difficulty.Medium, Random) : null;

		// Deal round the table, one card at a time, starting with the first player. The rest
		// stays face down in the middle: the deck players fish from.
		await DealRound(game.Dealt.Select(c => _nodes[c]).ToList(), Enumerable.Range(0, game.Dealt.Count).Select(j => (_first + j) % Players).ToList());
		if (round != Round)
			return;
		_game = game;
		await PickUpAll(p => game.Hand(p).Select(c => _nodes[c]));
		if (round != Round)
			return;
		Hud.SetRestartVisible(true);
		// Anything dealt that already makes a set goes straight down, then the first turn starts.
		await Play(game.Opening());
	}

	/// <summary>Acts out what just happened, then hands over to whoever moves next.</summary>
	private async Task Play(List<GoFishEvent> events)
	{
		var game = _game!;
		int round = Round;
		Busy = true;
		foreach (var ai in _ais)
			ai?.Watch(events);
		foreach (var e in events)
		{
			await Show(e);
			if (round != Round)
				return;
		}
		SetScore();
		if (game.IsOver)
		{
			ShowTurn(-1);
			return;
		}
		ShowTurn(game.Current);
		var mover = _ais[game.Current];
		if (game.Current == 0 && mover == null)
		{
			if (game.Phase == Phase.Fishing && AutoFish)
			{
				await Pause(0.5f);
				if (round == Round)
					await Play(game.Fish());
				return;
			}
			Busy = false;
			_selectedRank = 0;
			Hud.SetStatus(game.Phase == Phase.Fishing ? $"Go fish! Tap the deck for {(game.FishingFor is Card.Ace or 8 ? "an" : "a")} {Card.RankName(game.FishingFor).ToLowerInvariant()}." : "Your turn: tap a card to ask for it.", Bright);
			return;
		}
		// A computer player (or, in the demo, you) takes its turn after a short think.
		await Pause(game.Phase == Phase.Fishing ? 0.5f : 0.9f);
		if (round != Round)
			return;
		if (game.Phase == Phase.Fishing)
		{
			await Play(game.Fish(mover!.ChoosePondCard(game)));
		}
		else
		{
			var (target, rank) = mover!.ChooseAsk(game);
			await Play(game.Ask(target, rank));
		}
	}

	private async Task Show(GoFishEvent e)
	{
		ClipMoment("ask", 6);
		var game = _game!;
		switch (e)
		{
			case Asked asked:
			{
				string ranks = Card.RankName(asked.Rank, plural: true).ToLowerInvariant();
				string to = Players > 2 ? $"{(asked.Target == 0 ? "you" : Names[asked.Target])}, " : "";
				string question = $"{to}got any {ranks}?";
				question = char.ToUpperInvariant(question[0]) + question[1..];
				if (asked.Player == 0)
				{
					Fans[0].Raise(asked.Rank, true);
					Fans[0].Settle(0.15f);
				}
				Say(asked.Player, question, asked.Target);
				await Pause(1.1f);
				break;
			}
			case Gave gave:
			{
				// The one who has them throws them down on the felt; the one who asked picks them up.
				string what = $"{Count(gave.Cards.Count)} {Card.RankName(gave.Cards[0].Rank, gave.Cards.Count > 1).ToLowerInvariant()}";
				Say(gave.From, gave.Cards.Count == 1 ? $"Yes, here's {what}." : $"Yes, here are {what}.", gave.To);
				var nodes = gave.Cards.Select(c => _nodes[c]).ToList();
				foreach (var node in nodes)
					Fans[gave.From].Remove(node);
				Fans[gave.From].LowerAll();
				Fans[gave.From].Settle();
				await Hands[gave.From].Throw(nodes, nodes.Select((_, i) => Landing(gave.To, i)).ToList());
				await Pause(0.25f);
				await IntoFan(gave.To, nodes);
				Fans[0].LowerAll();
				Fans[0].Settle(0.15f);
				await Pause(0.5f);
				break;
			}
			case GoFishTold told:
				Say(told.By, "Go fish!", told.Player);
				if (told.Player == 0)
				{
					Fans[0].LowerAll();
					Fans[0].Settle(0.15f);
				}
				await Pause(0.9f);
				break;
			case Drew drew:
			{
				await IntoFan(drew.Player, new[] { _nodes[drew.Card] });
				if (drew.Lucky)
				{
					string rank = Card.RankName(drew.Card.Rank).ToLowerInvariant();
					Say(drew.Player, $"Lucky! I fished {(rank[0] is 'a' or 'e' ? "an" : "a")} {rank}.");
					await Pause(0.8f);
				}
				break;
			}
			case Book book:
				await LayBook(book);
				break;
			case TurnTo turn:
				SayNone();
				ShowTurn(turn.Player);
				for (int s = 0; s < Players; s++)
					Fans[s].LowerAll();
				if (turn.Player != 0)
					Hud.SetStatus($"{Names[turn.Player]}'s turn.", Muted);
				break;
			case GameOver over:
				EndGame(over);
				break;
		}
	}

	private async Task LayBook(Book book)
	{
		ClipMoment("book", 5);
		var nodes = book.Cards.Select(c => _nodes[c]).ToList();
		var game = _game!;
		int p = book.Player;
		int k = _books[p].Count / game.SetSize; // sets laid so far
		// Your books of four: a little grid beside your place setting, two across (three would run off
		// the screen). Pairs, and everyone else's books: one loose pile that grows, each set a touch
		// askew, the newest on top.
		bool grid = p == 0 && game.SetSize == 4;
		var at = grid
			? Table.Winnings[0] + new Vector3((k % 2) * 0.36f, 0, (k / 2) * -0.3f)
			: Table.Winnings[p] + new Vector3((k % 3) * 0.04f, 0, (k % 2) * -0.035f);
		float yaw = (p == 0 ? 0.1f : 0.6f * p) + (grid ? 0 : ((float)Random.NextDouble() - 0.5f) * 0.3f);
		var targets = new List<Transform3D>();
		for (int i = 0; i < nodes.Count; i++)
		{
			Fans[p].Remove(nodes[i]);
			targets.Add(new Transform3D(Card3D.FaceUp(yaw), at + new Vector3(i * (grid ? 0.05f : 0.09f), 0.004f * (i + 1) + 0.0085f * k * (grid ? 0 : 2) + 0.001f * k, 0)));
			_books[p].Add(nodes[i]);
		}
		Fans[p].Settle();
		await Hands[p].Carry(nodes, targets, seconds: 0.4f, lift: 0.35f);
		Sparkle.Burst(this, at + Vector3.Up * 0.2f);
		string ranks = Card.RankName(book.Rank, plural: true).ToLowerInvariant();
		string set = game.SetSize == 4 ? "book" : "pair";
		News($"{NameOf(p)} {(p == 0 ? "lay" : "lays")} down a {set} of {ranks}.");
		SetScore();
		await Pause(0.7f);
	}

	private void EndGame(GameOver over)
	{
		var game = _game!;
		string sets = game.SetSize == 4 ? "books" : "pairs";
		var leaders = game.Leaders;
		int most = over.Books.Max();
		_first = (_first + 1) % Players;
		if (leaders.Count > 1)
		{
			Finish($"A tie: {string.Join(" and ", leaders.Select(Who))}, {most} {sets} each!", -1);
			return;
		}
		int winner = leaders[0];
		int next = over.Books.Where((_, i) => i != winner).Max();
		Finish(winner == 0 ? $"You win, {most} {sets} to {next}!" : $"{Names[winner]} wins, {most} {sets} to {next}.",
			winner, _books[winner], Table.Winnings[winner]);
	}

	private static string Count(int n) => n switch { 1 => "a", 2 => "two", 3 => "three", _ => n.ToString() };

	private void SetScore()
	{
		var game = _game;
		string sets = SetSize == 4 ? "Books" : "Pairs";
		var scores = Enumerable.Range(0, Players).Select(p => $"{(p == 0 ? "you" : Names[p])} {game?.Books(p).Count ?? 0}");
		Hud.SetScore($"{sets}: {string.Join(" · ", scores)}   ·   Wins {Wins.First}");
	}

	// ------------------------------------------------------------------ Input

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_game is not { Current: 0 } game)
			return;
		if (game.Phase == Phase.Asking)
		{
			// Tap one of your cards: its rank is what you'll ask for.
			if (Fans[0].At(origin, direction) is { } card)
			{
				Hud.HideHint();
				if (Players == 2)
				{
					Run(Play(game.Ask(1, card.Card.Rank)));
					return;
				}
				Fans[0].LowerAll();
				_selectedRank = card.Card.Rank;
				Fans[0].Raise(_selectedRank, true);
				Fans[0].Settle(0.12f);
				Hud.SetStatus($"Ask whom for {Card.RankName(_selectedRank, plural: true).ToLowerInvariant()}? Tap their cards.", Bright);
				return;
			}
			// Then tap whom to ask.
			if (_selectedRank != 0)
				for (int s = 1; s < Players; s++)
					if (game.AskablePlayers(0).Contains(s) && (Fans[s].At(origin, direction) != null || NearSeat(screen, s)))
					{
						int rank = _selectedRank;
						_selectedRank = 0;
						Run(Play(game.Ask(s, rank)));
						return;
					}
		}
		else if (game.Phase == Phase.Fishing)
		{
			// A tap anywhere on the deck draws its top card.
			if (game.Pond.Any(c => _nodes[c].Hit(origin, direction) != null))
				Run(Play(game.Fish()));
		}
	}

	/// <summary>True if a tap lands close to a seat's fan on screen (so even a small fan is easy to pick).</summary>
	private bool NearSeat(Vector2 screen, int seat) =>
		Table.Camera.UnprojectPosition(Table.FanPivot[seat].Origin + Table.FanPivot[seat].Basis.Y * 0.45f).DistanceTo(screen) < 130;
}
