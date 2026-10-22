using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GoFish.Core;
using Godot;
using Renegade.Ads;
using Renegade.Audio;
using Renegade.Cards;
using Renegade.Core;
using Renegade.Dev;
using Renegade.Play;
using Renegade.Ui;

namespace GoFish.Game;

/// <summary>
/// Go Fish against one to four computer players, played with real-looking cards: the deck is
/// shuffled with a riffle and a bridge and dealt, glass hands pick the cards up and fan them, and
/// every ask, throw, draw and book is acted out with a pinch of thumb and finger. Tap a card in
/// your fan to ask for its rank (then, with more than one opponent, tap whom to ask); after "Go
/// fish!", tap the deck (or let Settings › Fish automatically do it).
/// </summary>
public partial class Main : Node3D
{
	public const string Title = "Go Fish";

	private static readonly AdMobConfig AdUnits = AdMobConfig.Test;
	private static readonly Color Muted = new("d9b99b");
	private static readonly Color Bright = new("fff3e0");
	private static readonly Color GlassTint = new(0.8f, 0.92f, 1f);
	private static readonly string[] Names = { "You", "Rosie", "Max", "Lena", "Theo" };

	private readonly Random _random = new();
	private GameSettings _settings = null!;
	private CardTable _table = null!;
	private CardLooks _looks = null!;
	private CardSounds _sounds = null!;
	private Dealer _dealer = null!;
	private GameHud _hud = null!;
	private CardStyleMenu _styleMenu = null!;
	private IAdService _ads = null!;
	private AdSchedule? _adSchedule;
	private readonly Scoreboard _wins = new();

	private int _players = 2;
	private readonly Dictionary<Card, Card3D> _nodes = new();
	private readonly CardFan[] _fans = new CardFan[CardTable.MaxSeats];
	private readonly SeatHands[] _hands = new SeatHands[CardTable.MaxSeats];
	private readonly Label3D[] _bubbles = new Label3D[CardTable.MaxSeats];
	private readonly List<Card3D>[] _books = Enumerable.Range(0, CardTable.MaxSeats).Select(_ => new List<Card3D>()).ToArray();
	private Flames? _fire;

	private GoFishGame? _game;
	private readonly GoFishAi?[] _ais = new GoFishAi?[CardTable.MaxSeats];
	private bool _busy = true;
	private int _selectedRank;
	private int _first;
	private int _round;

	private bool _demo;
	private bool _quitAfterDemo;
	private string? _art;
	private bool _handCheck;

	public override void _Ready()
	{
		DevLog.Tag = "gf";
		AudioWatch.Attach(this, Title);
		_settings = GameSettings.Load();
		_players = Math.Clamp(_settings.Extra.TryGetValue("players", out var n) && int.TryParse(n, out int p) ? p : 2, 2, 5);
		ReadDeveloperArgs();
		if (_art != null)
		{
			_ = ArtCheck.Run(this, _art);
			return;
		}
		if (_handCheck)
		{
			_ = ShowHands();
			return;
		}
		AudioServer.SetBusMute(0, _settings.Muted);
		Run(Start());
	}

	private async Task ShowHands() => HandCheck.Build(this, await CardLooks.Create(this, CardFront.Classic, CardBack.Crimson));

	private async Task Start()
	{
		_table = new CardTable { Seats = _players };
		AddChild(_table);
		var pool = new SoundPool();
		AddChild(pool);
		_sounds = new CardSounds(pool);
		_dealer = new Dealer(this, _sounds, _random);
		_looks = await CardLooks.Create(this, Saved("front", CardFront.Classic), Saved("back", CardBack.Crimson));

		for (int s = 0; s < _players; s++)
		{
			_fans[s] = s == 0
				? new CardFan(_table.FanPivot[0], sorted: true)
				: new CardFan(_table.FanPivot[s], sorted: false) { Radius = 0.5f, Spread = 9f, MaxArc = 80f };
			_hands[s] = new SeatHands(this, _sounds, _table.FanPivot[s], GlassTint);
			_bubbles[s] = new Label3D
			{
				FontSize = 88,
				PixelSize = 0.0042f,
				OutlineSize = 22,
				OutlineModulate = new Color(0.08f, 0.05f, 0.03f, 0.9f),
				Modulate = Bright,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				NoDepthTest = true,
				Position = _table.Bubble[s],
				Visible = false,
			};
			AddChild(_bubbles[s]);
		}

		_adSchedule ??= new AdSchedule(_settings.GamesSinceAd);
		var theme = HudTheme.Warm;
		_ads = AdServices.Create(this, AdUnits, _demo, new PlaceholderAds { TextColor = theme.Text });
		_hud = new GameHud(theme);
		AddChild(_hud);
		_hud.Build(new HudOptions(Title, "Tap a card to ask for it", Modes: false, LevelOnly: true,
			NewGameText: "Deal again", FreezeSetting: false, CustomStyle: () => _styleMenu.Open()), _settings);
		_hud.NewGamePressed += OnNewGamePressed;
		_hud.RestartPressed += OnRestartPressed;
		_hud.SetTutorial(Tutorial.Pages);
		_hud.ModeChosen += (mode, difficulty) =>
		{
			_settings.Difficulty = difficulty;
			_settings.Save();
			for (int s = 1; s < _players; s++)
				if (_ais[s] != null)
					_ais[s] = new GoFishAi(s, difficulty, _random);
		};
		var send = _hud.AddButton("Send", false, Control.LayoutPreset.TopLeft, 244, 344, 148, 196, 22);
		send.Pressed += () => _hud.Dialog("Send to a friend",
			"Once Go Fish is on Google Play, this opens your phone's share menu with a link to the game, so a friend can get it and play you.",
			("OK", true, null));

		// How many at the table: you and one to four computer players (a new table and deal).
		_hud.AddChoice("Players", new[] { "2", "3", "4", "5" }, _players - 2, choice =>
		{
			_settings.Extra["players"] = (choice + 2).ToString();
			_settings.Save();
			GetTree().ReloadCurrentScene();
		});
		// Books of four (the standard rules) or pairs (a popular family version); from the next deal.
		_hud.AddChoice("Lay down", new[] { "Books of four", "Pairs" }, SetSize == 2 ? 1 : 0, choice =>
		{
			_settings.Extra["sets"] = choice == 1 ? "pairs" : "books";
			_settings.Save();
			if (_game == null || _game.BooksMade == 0)
				Run(NewRound());
		});
		// Draw for me after "Go fish!" (or wait for my tap): a way to set the pace.
		_hud.AddToggle("Fish automatically", AutoFish, on =>
		{
			_settings.Extra["autofish"] = on ? "on" : "off";
			_settings.Save();
			if (on && !_busy && _game is { Current: 0, Phase: Phase.Fishing })
				Run(Play(_game.Fish()));
		});

		_styleMenu = new CardStyleMenu(theme, _looks.Back, _looks.Front);
		_styleMenu.BackChosen += back => { _ = _looks.SetBack(this, back); Save("back", back); };
		_styleMenu.FrontChosen += front => { _ = _looks.SetFront(this, front); Save("front", front); };
		_hud.AddChild(_styleMenu);
		if (!_demo)
			_hud.OfferTutorialOnce(_settings);
		_hud.ApplyDevFlags();
		await NewRound();
	}

	/// <summary>4 for books of four (the standard), 2 to play with pairs (chosen in Settings).</summary>
	private int SetSize => _settings.Extra.TryGetValue("sets", out var sets) && sets == "pairs" ? 2 : 4;

	private bool AutoFish => _settings.Extra.TryGetValue("autofish", out var on) && on == "on";

	private T Saved<T>(string key, T fallback) where T : struct, Enum =>
		_settings.Extra.TryGetValue(key, out var text) && Enum.TryParse<T>(text, out var value) ? value : fallback;

	private void Save<T>(string key, T value) where T : struct, Enum
	{
		_settings.Extra[key] = value.ToString();
		_settings.Save();
	}

	private async Task Await(Tween tween) => await ToSignal(tween, Tween.SignalName.Finished);

	/// <summary>Runs a task without waiting for it, but reports anything that goes wrong (otherwise it would vanish).</summary>
	private static async void Run(Task task)
	{
		try
		{
			await task;
		}
		catch (Exception error)
		{
			GD.PrintErr($"Go Fish: {error}");
			DevLog.Write($"error: {error.Message}");
		}
	}

	// ------------------------------------------------------------------ Moving cards with the hands

	/// <summary>A player's working hand picks cards up from wherever they are and puts them into their fan.</summary>
	private async Task IntoFan(int p, IReadOnlyList<Card3D> cards)
	{
		foreach (var node in cards)
		{
			for (int s = 0; s < _players; s++)
				_fans[s].Remove(node);
			_fans[p].Add(node);
		}
		var targets = cards.Select(node => _fans[p].Slot(_fans[p].Cards.ToList().IndexOf(node), _fans[p].Count)).ToList();
		await _hands[p].Carry(cards, targets);
		var settle = _fans[p].Settle();
		if (settle != null)
			await Await(settle);
	}

	/// <summary>Where cards thrown to a player land: on the felt in front of them.</summary>
	private Transform3D Landing(int to, int i)
	{
		float yaw = ((float)_random.NextDouble() - 0.5f) * 0.6f;
		return new Transform3D(Card3D.FaceUp(yaw), _table.Landing[to] + new Vector3(i * 0.12f, 0.004f * (i + 1), i * 0.04f));
	}

	// ------------------------------------------------------------------ A round

	private async Task NewRound()
	{
		_busy = true;
		_round++;
		int round = _round;
		_selectedRank = 0;
		_fire?.Extinguish();
		_fire = null;
		for (int s = 0; s < _players; s++)
		{
			_hands[s].Reset();
			_fans[s].Clear();
			_books[s].Clear();
			Say(s, null);
		}
		foreach (var node in _nodes.Values)
			node.QueueFree();
		_nodes.Clear();
		_hud.SetNewGameVisible(false);
		_hud.SetRestartVisible(false);
		SetScore();

		// A fresh deck, face down in the middle: shuffle it for real (on screen, riffles and a bridge).
		var order = Deck.Shuffled(_random);
		var deck = new List<Card3D>();
		for (int i = 0; i < 52; i++)
		{
			var node = new Card3D(order[51 - i], _looks);
			AddChild(node);
			deck.Add(node);
		}
		Dealer.Stack(deck, _table.Centre);
		_hud.SetStatus("Shuffling…", Muted);
		await Pause(0.4f);
		await _dealer.Riffle(deck, _table.Centre, times: 2);
		if (round != _round)
			return;

		// The shuffle moved the card objects around; give them the shuffled order, top first.
		_nodes.Clear();
		for (int i = 0; i < 52; i++)
		{
			deck[51 - i].Card = order[i];
			_nodes[order[i]] = deck[51 - i];
		}
		_game = new GoFishGame(order, _first, SetSize, _players);
		for (int s = 0; s < _players; s++)
			_ais[s] = s > 0 ? new GoFishAi(s, _settings.Difficulty, _random) : _demo ? new GoFishAi(0, Difficulty.Medium, _random) : null;

		// Deal round the table, one card at a time, starting with the first player. The rest
		// stays face down in the middle: the deck players fish from.
		_hud.SetStatus("Dealing…", Muted);
		var onPile = new int[_players];
		Tween? last = null;
		for (int j = 0; j < _game.Dealt.Count; j++)
		{
			int p = (_first + j) % _players;
			last = _dealer.Deal(_nodes[_game.Dealt[j]], _table.Pile[p], onPile[p]++, delay: j * (0.6f / _players + 0.05f));
		}
		await Await(last!);

		// Everyone picks up their cards and fans them, all at once.
		var pickups = new List<Task>();
		for (int p = 0; p < _players; p++)
		{
			var pile = _game.Hand(p).Select(c => _nodes[c]).OrderByDescending(n => n.GlobalPosition.Y).ToList();
			pickups.Add(IntoFan(p, pile));
		}
		await Task.WhenAll(pickups);
		if (round != _round)
			return;
		_hud.SetRestartVisible(true);
		// Anything dealt that already makes a set goes straight down, then the first turn starts.
		await Play(_game.Opening());
	}

	/// <summary>Acts out what just happened, then hands over to whoever moves next.</summary>
	private async Task Play(List<GoFishEvent> events)
	{
		var game = _game!;
		int round = _round;
		_busy = true;
		foreach (var ai in _ais)
			ai?.Watch(events);
		foreach (var e in events)
		{
			await Show(e);
			if (round != _round)
				return;
		}
		SetScore();
		if (game.IsOver)
			return;
		var mover = _ais[game.Current];
		if (game.Current == 0 && mover == null)
		{
			if (game.Phase == Phase.Fishing && AutoFish)
			{
				await Pause(0.5f);
				if (round == _round)
					await Play(game.Fish());
				return;
			}
			_busy = false;
			_selectedRank = 0;
			_hud.SetStatus(game.Phase == Phase.Fishing ? $"Go fish! Tap the deck for a {Card.RankName(game.FishingFor).ToLowerInvariant()}." : "Your turn: tap a card to ask for it.", Bright);
			return;
		}
		// A computer player (or, in the demo, you) takes its turn after a short think.
		await Pause(game.Phase == Phase.Fishing ? 0.5f : 0.9f);
		if (round != _round)
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

	private string Who(int p) => p == 0 ? "you" : Names[p];

	private async Task Show(GoFishEvent e)
	{
		var game = _game!;
		switch (e)
		{
			case Asked asked:
			{
				string ranks = Card.RankName(asked.Rank, plural: true).ToLowerInvariant();
				string to = _players > 2 ? (asked.Target == 0 ? "" : $"{Names[asked.Target]}, ") : "";
				string question = $"{to}got any {ranks}?";
				question = char.ToUpperInvariant(question[0]) + question[1..];
				if (asked.Player == 0)
				{
					_fans[0].Raise(asked.Rank, true);
					_fans[0].Settle(0.15f);
					_hud.SetStatus($"You: \"{question}\"", Bright);
				}
				else
				{
					Say(asked.Player, question);
					_hud.SetStatus(asked.Target == 0 ? $"{Names[asked.Player]} asks you for your {ranks}." : $"{Names[asked.Player]} asks {Names[asked.Target]} for {ranks}.", Muted);
				}
				await Pause(0.9f);
				break;
			}
			case Gave gave:
			{
				// The one who has them throws them down on the felt; the one who asked picks them up.
				var nodes = gave.Cards.Select(c => _nodes[c]).ToList();
				foreach (var node in nodes)
					_fans[gave.From].Remove(node);
				_fans[gave.From].LowerAll();
				_fans[gave.From].Settle();
				await _hands[gave.From].Throw(nodes, nodes.Select((_, i) => Landing(gave.To, i)).ToList());
				await Pause(0.25f);
				await IntoFan(gave.To, nodes);
				string what = $"{Count(nodes.Count)} {Card.RankName(gave.Cards[0].Rank, nodes.Count > 1).ToLowerInvariant()}";
				_hud.SetStatus(gave.To == 0 ? $"You got {what}! Ask again."
					: gave.From == 0 ? $"You give up {what}." : $"{Names[gave.From]} gives {Names[gave.To]} {what}.", Bright);
				_fans[0].LowerAll();
				_fans[0].Settle(0.15f);
				SayNone();
				await Pause(0.5f);
				break;
			}
			case GoFishTold told:
				SayNone();
				if (told.By == 0)
				{
					_hud.SetStatus("You: \"Go fish!\"", Bright);
				}
				else
				{
					Say(told.By, "Go fish!");
					if (told.Player == 0)
					{
						_fans[0].LowerAll();
						_fans[0].Settle(0.15f);
					}
				}
				await Pause(0.7f);
				break;
			case Drew drew:
			{
				await IntoFan(drew.Player, new[] { _nodes[drew.Card] });
				if (drew.Lucky)
				{
					string rank = Card.RankName(drew.Card.Rank).ToLowerInvariant();
					_hud.SetStatus(drew.Player == 0 ? $"Lucky! You fished a {rank}. Go again." : $"{Names[drew.Player]} fished the {rank} they wanted, and goes again.", Bright);
					await Pause(0.8f);
				}
				SayNone();
				break;
			}
			case Book book:
				await LayBook(book);
				break;
			case TurnTo turn:
				SayNone();
				for (int s = 0; s < _players; s++)
					_fans[s].LowerAll();
				if (turn.Player != 0)
					_hud.SetStatus($"{Names[turn.Player]}'s turn.", Muted);
				break;
			case GameOver over:
				Finish(over);
				break;
		}
	}

	private async Task LayBook(Book book)
	{
		var nodes = book.Cards.Select(c => _nodes[c]).ToList();
		var game = _game!;
		int p = book.Player;
		int k = _books[p].Count / game.SetSize; // sets laid so far
		// Your books of four: a little grid, three across. Pairs, and everyone else's books: one
		// loose pile that grows, each set a touch askew, the newest on top.
		bool grid = p == 0 && game.SetSize == 4;
		var at = grid
			? _table.Winnings[0] + new Vector3((k % 3) * 0.36f, 0, (k / 3) * -0.42f)
			: _table.Winnings[p] + new Vector3((k % 3) * 0.04f, 0, (k % 2) * -0.035f);
		float yaw = (p == 0 ? 0.1f : 0.6f * p) + (grid ? 0 : ((float)_random.NextDouble() - 0.5f) * 0.3f);
		var targets = new List<Transform3D>();
		for (int i = 0; i < nodes.Count; i++)
		{
			_fans[p].Remove(nodes[i]);
			targets.Add(new Transform3D(Card3D.FaceUp(yaw), at + new Vector3(i * (grid ? 0.05f : 0.09f), 0.004f * (i + 1) + 0.0085f * k * (grid ? 0 : 2) + 0.001f * k, 0)));
			_books[p].Add(nodes[i]);
		}
		_fans[p].Settle();
		await _hands[p].Carry(nodes, targets, seconds: 0.4f, lift: 0.35f);
		Sparkle.Burst(this, at + Vector3.Up * 0.2f);
		string ranks = Card.RankName(book.Rank, plural: true).ToLowerInvariant();
		string set = game.SetSize == 4 ? "book" : "pair";
		_hud.SetStatus(p == 0 ? $"A {set} of {ranks}!" : $"{Names[p]} lays down a {set} of {ranks}.", Bright);
		SetScore();
		await Pause(0.7f);
	}

	private void Finish(GameOver over)
	{
		var game = _game!;
		string sets = game.SetSize == 4 ? "books" : "pairs";
		var leaders = game.Leaders;
		int most = over.Books.Max();
		if (leaders.Count > 1)
		{
			_hud.SetStatus($"A tie: {string.Join(" and ", leaders.Select(Who))}, {most} {sets} each!", Bright);
		}
		else
		{
			int winner = leaders[0];
			_wins.Win(first: winner == 0);
			int next = over.Books.Where((_, i) => i != winner).Max();
			_hud.SetStatus(winner == 0 ? $"You win, {most} {sets} to {next}!" : $"{Names[winner]} wins, {most} {sets} to {next}.", Bright);
			var meshes = _books[winner].SelectMany(n => n.Meshes).ToList();
			if (meshes.Count > 0)
				_fire = Flames.SetAlight(this, _table.Winnings[winner], meshes, radius: 0.5f, size: 1.2f, withLight: true);
		}
		_hud.SetRestartVisible(false);
		_hud.SetNewGameVisible(true);
		_first = (_first + 1) % _players;
		_busy = true;
		if (_demo)
			Run(DemoNext());
	}

	private async Task DemoNext()
	{
		await Pause(3f);
		if (_quitAfterDemo)
			GetTree().Quit();
		else
			OnNewGamePressed();
	}

	private static string Count(int n) => n switch { 1 => "a", 2 => "two", 3 => "three", _ => n.ToString() };

	private void Say(int seat, string? text)
	{
		var bubble = _bubbles[seat];
		if (bubble == null)
			return;
		bubble.Visible = text != null;
		bubble.Text = text ?? "";
	}

	private void SayNone()
	{
		for (int s = 0; s < _players; s++)
			Say(s, null);
	}

	private async Task Pause(float seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	private void SetScore()
	{
		var game = _game;
		string sets = SetSize == 4 ? "Books" : "Pairs";
		var scores = Enumerable.Range(0, _players).Select(p => $"{(p == 0 ? "you" : Names[p])} {game?.Books(p).Count ?? 0}");
		_hud.SetScore($"{sets}: {string.Join(" · ", scores)}   ·   Wins {_wins.First}");
	}

	// ------------------------------------------------------------------ Input

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_busy || _hud.MenuOpen || _styleMenu.Visible || _game == null)
			return;
		if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
			return;
		var (origin, direction) = _table.Ray(click.Position);
		var game = _game;
		if (game.Current != 0)
			return;
		if (game.Phase == Phase.Asking)
		{
			// Tap one of your cards: its rank is what you'll ask for.
			if (_fans[0].At(origin, direction) is { } card)
			{
				_hud.HideHint();
				if (_players == 2)
				{
					Run(Play(game.Ask(1, card.Card.Rank)));
					return;
				}
				_fans[0].LowerAll();
				_selectedRank = card.Card.Rank;
				_fans[0].Raise(_selectedRank, true);
				_fans[0].Settle(0.12f);
				_hud.SetStatus($"Ask whom for {Card.RankName(_selectedRank, plural: true).ToLowerInvariant()}? Tap their cards.", Bright);
				return;
			}
			// Then tap whom to ask.
			if (_selectedRank != 0)
				for (int s = 1; s < _players; s++)
					if (game.AskablePlayers(0).Contains(s) && (_fans[s].At(origin, direction) != null || NearSeat(click.Position, s)))
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
		_table.Camera.UnprojectPosition(_table.FanPivot[seat].Origin + _table.FanPivot[seat].Basis.Y * 0.45f).DistanceTo(screen) < 130;

	private void OnNewGamePressed()
	{
		bool adDue = _adSchedule!.GameFinished();
		_settings.GamesSinceAd = _adSchedule.GamesSinceAd;
		_settings.Save();
		if (adDue)
		{
			DevLog.Write("ad: between-games interstitial");
			_ads.ShowInterstitial(() => Run(NewRound()));
		}
		else
		{
			Run(NewRound());
		}
	}

	/// <summary>Start over mid-game: against the computer it's a resignation unless an ad is watched.</summary>
	private void OnRestartPressed()
	{
		if (_game == null || _game.IsOver)
		{
			Run(NewRound());
			return;
		}
		_hud.AskRestart(!_demo, countLoss =>
		{
			if (countLoss)
				_wins.Win(first: false);
			Run(NewRound());
		}, then => _ads.ShowRewarded(rewarded: then, closed: () => { }));
	}

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo` (the
	/// computer plays every seat), `--quit-after-demo`, `--players=4`, `--games-since-ad=4`,
	/// `--art=user://art` (paints every card design to PNGs and quits), `--hands` (the glass hands up
	/// close), plus the HUD's `--tutorial=N` and `--settings`.
	/// </summary>
	private void ReadDeveloperArgs()
	{
		foreach (var (name, value) in DevArgs.Read())
		{
			switch (name)
			{
				case "--log":
					DevLog.Enabled = true;
					break;
				case "--demo":
					_demo = true;
					_settings.Muted = false;
					break;
				case "--quit-after-demo":
					_quitAfterDemo = true;
					break;
				case "--players":
					_players = Math.Clamp(int.Parse(value), 2, 5);
					break;
				case "--games-since-ad":
					_adSchedule = new AdSchedule(int.Parse(value));
					break;
				case "--art":
					_art = value;
					break;
				case "--hands":
					_handCheck = true;
					break;
			}
		}
	}
}
