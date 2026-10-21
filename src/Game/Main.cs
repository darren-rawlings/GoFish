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
/// Go Fish against the computer, played with real-looking cards: the deck is riffled and dealt,
/// glass hands pick up and fan the cards, and every ask, hand-over, draw and book is acted out.
/// Tap a card in your fan to ask for its rank; after "Go fish!", tap a card in the pond.
/// </summary>
public partial class Main : Node3D
{
	public const string Title = "Go Fish";

	private static readonly AdMobConfig AdUnits = AdMobConfig.Test;
	private static readonly Color Muted = new("d9b99b");
	private static readonly Color Bright = new("fff3e0");

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

	private readonly Dictionary<Card, Card3D> _nodes = new();
	private readonly CardFan[] _fans = new CardFan[2];
	private readonly GlassHand[] _holders = new GlassHand[2];
	private readonly GlassHand[] _helpers = new GlassHand[2];
	private readonly Transform3D[] _helperRest = new Transform3D[2];
	private readonly List<Card3D>[] _books = { new(), new() };
	private Label3D _bubble = null!;
	private Flames? _fire;

	private GoFishGame? _game;
	private GoFishAi? _ai;
	private GoFishAi? _autopilot;
	private bool _busy = true;
	private int _first;
	private int _round;

	private bool _demo;
	private bool _quitAfterDemo;
	private string? _art;

	public override void _Ready()
	{
		DevLog.Tag = "gf";
		AudioWatch.Attach(this, Title);
		_settings = GameSettings.Load();
		ReadDeveloperArgs();
		if (_art != null)
		{
			_ = ArtCheck.Run(this, _art);
			return;
		}
		AudioServer.SetBusMute(0, _settings.Muted);
		_ = Start();
	}

	private async Task Start()
	{
		_table = new CardTable();
		AddChild(_table);
		var pool = new SoundPool();
		AddChild(pool);
		_sounds = new CardSounds(pool);
		_dealer = new Dealer(this, _sounds, _random);
		_looks = await CardLooks.Create(this, Saved("front", CardFront.Classic), Saved("back", CardBack.Crimson));

		_fans[0] = new CardFan(_table.FanPivot[0], sorted: true);
		_fans[1] = new CardFan(_table.FanPivot[1], sorted: false) { Radius = 0.5f, Spread = 9f, MaxArc = 80f };
		BuildHands();
		_bubble = new Label3D
		{
			FontSize = 96,
			PixelSize = 0.0042f,
			OutlineSize = 22,
			OutlineModulate = new Color(0.08f, 0.05f, 0.03f, 0.9f),
			Modulate = Bright,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			Position = _table.FanPivot[1].Origin + new Vector3(0, 0.95f, 0.2f),
			Visible = false,
		};
		AddChild(_bubble);

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
			if (_game != null)
				_ai = new GoFishAi(1, difficulty, _random);
		};
		var send = _hud.AddButton("Send", false, Control.LayoutPreset.TopLeft, 244, 344, 148, 196, 22);
		send.Pressed += () => _hud.Dialog("Send to a friend",
			"Once Go Fish is on Google Play, this opens your phone's share menu with a link to the game, so a friend can get it and play you.",
			("OK", true, null));

		_styleMenu = new CardStyleMenu(theme, _looks.Back, _looks.Front);
		_styleMenu.BackChosen += back => { _ = _looks.SetBack(this, back); Save("back", back); };
		_styleMenu.FrontChosen += front => { _ = _looks.SetFront(this, front); Save("front", front); };
		_hud.AddChild(_styleMenu);
		if (!_demo)
			_hud.OfferTutorialOnce(_settings);
		_hud.ApplyDevFlags();
		await NewRound();
	}

	private T Saved<T>(string key, T fallback) where T : struct, Enum =>
		_settings.Extra.TryGetValue(key, out var text) && Enum.TryParse<T>(text, out var value) ? value : fallback;

	private void Save<T>(string key, T value) where T : struct, Enum
	{
		_settings.Extra[key] = value.ToString();
		_settings.Save();
	}

	// ------------------------------------------------------------------ Hands

	private void BuildHands()
	{
		var tint = new Color(0.8f, 0.92f, 1f);
		for (int p = 0; p < 2; p++)
		{
			// The left hand holds the fan from behind, thumb across the front at its base.
			_holders[p] = new GlassHand(mirrored: false, tint) { Grip = 0.85f };
			AddChild(_holders[p]);
			_holders[p].GlobalTransform = HolderPose(p);
			// The right hand does the work: picking up, handing over, fishing.
			_helpers[p] = new GlassHand(mirrored: true, tint) { Grip = 0.35f };
			AddChild(_helpers[p]);
			var pivot = _table.FanPivot[p];
			var rest = pivot.Origin + pivot.Basis * new Vector3(p == 0 ? 0.95f : -0.95f, -0.25f, -0.05f);
			_helperRest[p] = new Transform3D(pivot.Basis * new Basis(Vector3.Back, p == 0 ? 0.35f : -0.35f), rest);
			_helpers[p].GlobalTransform = _helperRest[p];
		}
	}

	private Transform3D HolderPose(int p)
	{
		var pivot = _table.FanPivot[p];
		// Behind the lower left of the fan, fingers reaching up and right behind the cards, the
		// palm and thumb showing below and across the base where the cards are pinched.
		var basis = pivot.Basis * new Basis(Vector3.Back, -0.45f) * new Basis(Vector3.Right, -0.2f);
		return new Transform3D(basis, pivot.Origin + pivot.Basis * new Vector3(-0.3f, 0.05f, -0.07f));
	}

	/// <summary>The helper hand reaches to a point (palm towards the card) and back again.</summary>
	private async Task Reach(int p, Vector3 at, float seconds = 0.22f)
	{
		var toward = new Transform3D(_helperRest[p].Basis, at + _helperRest[p].Basis * new Vector3(0, -0.32f, 0.05f));
		await ToSignal(CardMotion.Fly(_helpers[p], toward, seconds, lift: 0.1f), Tween.SignalName.Finished);
	}

	private Tween Rest(int p, float seconds = 0.3f) => CardMotion.Fly(_helpers[p], _helperRest[p], seconds, lift: 0.1f);

	/// <summary>Flies cards (carried by a hand) into a player's fan, then settles both fans.</summary>
	private async Task IntoFan(IReadOnlyList<Card3D> cards, int to, int carrier, float seconds = 0.45f)
	{
		Tween? last = null;
		for (int i = 0; i < cards.Count; i++)
		{
			var node = cards[i];
			foreach (var fan in _fans)
				fan.Remove(node);
			var slot = _fans[to].SlotFor(node);
			_fans[to].Add(node);
			last = CardMotion.Fly(node, slot, seconds, lift: 0.35f, delay: i * 0.06f, landed: () => _sounds.PlaySlap(node.GlobalPosition));
		}
		var handTarget = new Transform3D(_helperRest[carrier].Basis, _fans[to].Pivot.Origin + _fans[to].Pivot.Basis * new Vector3(0, 0.2f, 0.08f));
		CardMotion.Fly(_helpers[carrier], handTarget, seconds, lift: 0.35f);
		_sounds.PlaySwish(cards[0].GlobalPosition);
		if (last != null)
			await ToSignal(last, Tween.SignalName.Finished);
		Rest(carrier);
		_fans[0].Settle();
		var settle = _fans[1].Settle();
		if (settle != null)
			await ToSignal(settle, Tween.SignalName.Finished);
	}

	// ------------------------------------------------------------------ A round

	private async Task NewRound()
	{
		_busy = true;
		_round++;
		int round = _round;
		_fire?.Extinguish();
		_fire = null;
		foreach (var node in _nodes.Values)
			node.QueueFree();
		_nodes.Clear();
		foreach (var fan in _fans)
			fan.Clear();
		foreach (var books in _books)
			books.Clear();
		_hud.SetNewGameVisible(false);
		_hud.SetRestartVisible(false);
		SetScore();

		// A fresh deck, face down in the middle: shuffle it for real (on screen, a riffle).
		var order = Deck.Shuffled(_random);
		var deck = new List<Card3D>();
		for (int i = 0; i < 52; i++)
		{
			var node = new Card3D(order[51 - i], _looks);
			AddChild(node);
			deck.Add(node);
			_nodes[node.Card] = node;
		}
		Dealer.Stack(deck, _table.Centre);
		_hud.SetStatus("Shuffling…", Muted);
		await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
		await _dealer.Riffle(deck, _table.Centre, times: 2);
		if (round != _round)
			return;

		// The riffle moved the card objects around; give them the shuffled order again, top first.
		_nodes.Clear();
		for (int i = 0; i < 52; i++)
		{
			deck[51 - i].Card = order[i];
			_nodes[order[i]] = deck[51 - i];
		}
		_game = new GoFishGame(order, _first);
		_ai = new GoFishAi(1, _settings.Difficulty, _random);
		_autopilot = _demo ? new GoFishAi(0, Difficulty.Medium, _random) : null;

		// Deal seven each, one at a time, starting with the first player.
		_hud.SetStatus("Dealing…", Muted);
		var onPile = new int[2];
		Tween? last = null;
		for (int j = 0; j < _game.Dealt.Count; j++)
		{
			int p = (_first + j) % 2;
			last = _dealer.Deal(_nodes[_game.Dealt[j]], _table.Pile[p], onPile[p]++, delay: j * 0.13f);
		}
		await ToSignal(last!, Tween.SignalName.Finished);

		// The rest is spread face down as the pond.
		for (int i = 0; i < _game.Pond.Count; i++)
			last = CardMotion.Slide(_nodes[_game.Pond[i]], _dealer.Scatter(_table.Centre + new Vector3(0, 0, 0.05f), 1.05f, 0.5f, i), 0.32f, delay: i * 0.018f,
				landed: i % 6 == 0 ? () => _sounds.PlayFlick(_table.Centre) : null);
		await ToSignal(last!, Tween.SignalName.Finished);

		// Each player picks up their cards and fans them.
		for (int p = 0; p < 2; p++)
		{
			await Reach(p, _table.Pile[p], 0.25f);
			var mine = _game.Hand(p).Select(c => _nodes[c]).ToList();
			await IntoFan(mine, p, carrier: p, seconds: 0.4f);
		}
		if (round != _round)
			return;
		_hud.SetRestartVisible(true);
		await Play(new List<GoFishEvent> { new TurnTo(_game.Current) });
	}

	/// <summary>Acts out what just happened, then hands over to whoever moves next.</summary>
	private async Task Play(List<GoFishEvent> events)
	{
		var game = _game!;
		int round = _round;
		_busy = true;
		_ai?.Watch(events);
		_autopilot?.Watch(events);
		foreach (var e in events)
		{
			await Show(e);
			if (round != _round)
				return;
		}
		SetScore();
		if (game.IsOver)
			return;
		if (game.Current == 0 && _autopilot == null)
		{
			_busy = false;
			_hud.SetStatus(game.Phase == Phase.Fishing ? $"Go fish! Tap the pond for a {Card.RankName(game.FishingFor).ToLowerInvariant()}." : "Your turn: tap a card to ask for it.", Bright);
			return;
		}
		// The computer (or, in the demo, both sides) takes its turn after a short think.
		var ai = game.Current == 0 ? _autopilot! : _ai!;
		await ToSignal(GetTree().CreateTimer(game.Phase == Phase.Fishing ? 0.5f : 0.9f), SceneTreeTimer.SignalName.Timeout);
		if (round != _round)
			return;
		var next = game.Phase == Phase.Fishing ? game.Fish(ai.ChoosePondCard(game)) : game.Ask(ai.ChooseRank(game));
		await Play(next);
	}

	private async Task Show(GoFishEvent e)
	{
		var game = _game!;
		switch (e)
		{
			case Asked asked:
			{
				string ranks = Card.RankName(asked.Rank, plural: true);
				if (asked.Player == 0)
				{
					_fans[0].Raise(asked.Rank, true);
					_fans[0].Settle(0.15f);
					_hud.SetStatus($"You: \"Got any {ranks.ToLowerInvariant()}?\"", Bright);
				}
				else
				{
					Say($"Got any {ranks.ToLowerInvariant()}?");
					_hud.SetStatus($"The computer asks for your {ranks.ToLowerInvariant()}.", Muted);
				}
				await Pause(0.9f);
				break;
			}
			case Gave gave:
			{
				var nodes = gave.Cards.Select(c => _nodes[c]).ToList();
				_fans[gave.From].LowerAll();
				await Reach(gave.From, nodes[0].GlobalPosition);
				await IntoFan(nodes, gave.To, carrier: gave.From);
				string what = $"{Count(nodes.Count)} {Card.RankName(gave.Cards[0].Rank, nodes.Count > 1).ToLowerInvariant()}";
				_hud.SetStatus(gave.To == 0 ? $"You got {what}! Ask again." : $"You hand over {what}.", Bright);
				_fans[0].LowerAll();
				_fans[0].Settle(0.15f);
				Say(null);
				await Pause(0.6f);
				break;
			}
			case GoFishTold told:
				if (told.Player == 0)
				{
					Say("Go fish!");
					_fans[0].LowerAll();
					_fans[0].Settle(0.15f);
				}
				else
				{
					Say(null);
					_hud.SetStatus("You: \"Go fish!\"", Bright);
				}
				await Pause(0.7f);
				break;
			case Drew drew:
			{
				var node = _nodes[drew.Card];
				await Reach(drew.Player, node.GlobalPosition);
				await IntoFan(new[] { node }, drew.Player, carrier: drew.Player, seconds: 0.4f);
				if (drew.Lucky)
				{
					string rank = Card.RankName(drew.Card.Rank).ToLowerInvariant();
					_hud.SetStatus(drew.Player == 0 ? $"Lucky! You fished a {rank}. Go again." : $"The computer fished the {rank} it wanted. It goes again.", Bright);
					await Pause(0.8f);
				}
				Say(null);
				break;
			}
			case Book book:
				await LayBook(book);
				break;
			case TurnTo turn:
				Say(null);
				foreach (var fan in _fans)
					fan.LowerAll();
				if (turn.Player == 1)
					_hud.SetStatus("The computer's turn.", Muted);
				break;
			case GameOver over:
				Finish(over);
				break;
		}
	}

	private async Task LayBook(Book book)
	{
		var nodes = book.Cards.Select(c => _nodes[c]).ToList();
		int k = _books[book.Player].Count;
		float side = book.Player == 0 ? 1 : -1;
		var at = _table.Winnings[book.Player] + new Vector3(side * (k % 3) * 0.36f, 0, side * (k / 3) * -0.42f);
		Tween? last = null;
		for (int i = 0; i < nodes.Count; i++)
		{
			var node = nodes[i];
			_fans[book.Player].Remove(node);
			var target = new Transform3D(Card3D.FaceUp(side * 0.1f), at + new Vector3(i * 0.05f * side, 0.004f * (i + 1) + 0.001f * k, 0));
			last = CardMotion.Fly(node, target, 0.45f, lift: 0.4f, delay: i * 0.07f, landed: () => _sounds.PlaySlap(node.GlobalPosition));
			_books[book.Player].Add(node);
		}
		await ToSignal(last!, Tween.SignalName.Finished);
		Sparkle.Burst(this, at + Vector3.Up * 0.2f);
		_fans[book.Player].Settle();
		string ranks = Card.RankName(book.Rank, plural: true).ToLowerInvariant();
		_hud.SetStatus(book.Player == 0 ? $"A book of {ranks}!" : $"The computer makes a book of {ranks}.", Bright);
		SetScore();
		await Pause(0.8f);
	}

	private void Finish(GameOver over)
	{
		int winner = over.Books[0] > over.Books[1] ? 0 : 1;
		_wins.Win(first: winner == 0);
		_hud.SetStatus(winner == 0 ? $"You win, {over.Books[0]} books to {over.Books[1]}!" : $"The computer wins, {over.Books[1]} books to {over.Books[0]}.", Bright);
		var meshes = _books[winner].SelectMany(n => n.Meshes).ToList();
		if (meshes.Count > 0)
			_fire = Flames.SetAlight(this, _table.Winnings[winner], meshes, radius: 0.5f, size: 1.2f, withLight: true);
		_hud.SetRestartVisible(false);
		_hud.SetNewGameVisible(true);
		_first = 1 - _first;
		_busy = true;
		if (_demo)
			_ = DemoNext();
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

	private void Say(string? text)
	{
		_bubble.Visible = text != null;
		_bubble.Text = text ?? "";
	}

	private async Task Pause(float seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	private void SetScore()
	{
		var game = _game;
		int mine = game?.Books(0).Count ?? 0, theirs = game?.Books(1).Count ?? 0;
		_hud.SetScore($"Books: you {mine} · {theirs} computer   ·   Wins {_wins.First}–{_wins.Second}");
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
			var card = _fans[0].At(origin, direction);
			if (card == null)
				return;
			_hud.HideHint();
			_ = Play(game.Ask(card.Card.Rank));
		}
		else if (game.Phase == Phase.Fishing)
		{
			// Any card in the pond: the frontmost one under the tap.
			Card3D? best = null;
			float bestDistance = float.MaxValue;
			foreach (var c in game.Pond)
				if (_nodes[c].Hit(origin, direction) is { } d && d < bestDistance)
				{
					best = _nodes[c];
					bestDistance = d;
				}
			if (best == null)
				return;
			int index = game.Pond.ToList().IndexOf(best.Card);
			_ = Play(game.Fish(index));
		}
	}

	private void OnNewGamePressed()
	{
		bool adDue = _adSchedule!.GameFinished();
		_settings.GamesSinceAd = _adSchedule.GamesSinceAd;
		_settings.Save();
		if (adDue)
		{
			DevLog.Write("ad: between-games interstitial");
			_ads.ShowInterstitial(() => _ = NewRound());
		}
		else
		{
			_ = NewRound();
		}
	}

	/// <summary>Start over mid-game: against the computer it's a resignation unless an ad is watched.</summary>
	private void OnRestartPressed()
	{
		if (_game == null || _game.IsOver)
		{
			_ = NewRound();
			return;
		}
		_hud.AskRestart(!_demo, countLoss =>
		{
			if (countLoss)
				_wins.Win(first: false);
			_ = NewRound();
		}, then => _ads.ShowRewarded(rewarded: then, closed: () => { }));
	}

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo` (the
	/// computer plays both sides), `--quit-after-demo`, `--games-since-ad=4`, `--art=user://art`
	/// (paints every card design to PNGs and quits), plus the HUD's `--tutorial=N` and `--settings`.
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
				case "--games-since-ad":
					_adSchedule = new AdSchedule(int.Parse(value));
					break;
				case "--art":
					_art = value;
					break;
			}
		}
	}
}
