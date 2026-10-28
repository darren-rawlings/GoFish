using System.Collections.Generic;
using Renegade.Ui;

namespace GoFish.Game;

/// <summary>
/// The guided tour behind the Demo button: a few short pages. (Clips recorded from the game
/// itself come once the table is final; a page without a clip just shows its text.)
/// </summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Ask for a card",
			"On your turn, tap a card in your hand to ask for that rank: \"Got any sevens?\" With more than one opponent, then tap whom to ask. If they have any, they throw them all down for you, and you go again. Whoever is asking has their hands glow blue, and whoever is asked, yellow; everything said is in the log at the top.",
			"res://tutorial/ask.ogv"),
		new TutorialPage("Go fish!",
			"If they have none, they say \"Go fish!\" Tap the deck in the middle to draw its top card. Fish the rank you asked for and you go again; otherwise the turn passes to your left. (Settings › Fish automatically draws for you.)",
			"res://tutorial/ask.ogv"),
		new TutorialPage("Make books",
			"Four of a kind make a book, laid down in front of you. When every card is in a book, whoever has the most wins. Prefer pairs? Choose them in Settings.",
			"res://tutorial/book.ogv"),
		new TutorialPage("Tips",
			"Play with up to four computer players in Settings › Players. On Hard they remember what everyone asks for, so think before you ask. Change the card backs and fronts in Style: every front has its own suits and face cards."),
	};
}
