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
			"On your turn, tap a card in your hand to ask the computer for that rank: \"Got any sevens?\" If it has any, it hands them all over and you go again."),
		new TutorialPage("Go fish!",
			"If it has none, it says \"Go fish!\" Tap a card in the pond in the middle to draw it. Fish the rank you asked for and you go again; otherwise it's the computer's turn."),
		new TutorialPage("Make books",
			"Four of a kind make a book, laid down in front of you. When all thirteen books are made, whoever has the most wins."),
		new TutorialPage("Tips",
			"The computer remembers what you ask for on Hard, so think before you ask. Change the card backs and fronts in Style: every front has its own suits and face cards."),
	};
}
