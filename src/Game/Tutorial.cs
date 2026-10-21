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
			"Pairs you're dealt go straight down. On your turn, tap a card in your hand to ask the computer for that rank: \"Got any sevens?\" If it has one, it hands it over, your pair goes down, and you go again."),
		new TutorialPage("Go fish!",
			"If it has none, it says \"Go fish!\" Tap a card in the pond in the middle to draw it. Fish the rank you asked for and you go again; otherwise it's the computer's turn."),
		new TutorialPage("Lay down pairs",
			"Every pair you make is laid down in front of you. When every card is in a pair, whoever has the most pairs wins. Prefer books of four? Choose them in Settings."),
		new TutorialPage("Tips",
			"The computer remembers what you ask for on Hard, so think before you ask. Change the card backs and fronts in Style: every front has its own suits and face cards."),
	};
}
