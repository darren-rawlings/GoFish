using System.Threading.Tasks;
using Godot;
using Renegade.Cards;

namespace GoFish.Game;

/// <summary>Developer check (--art=folder): paints every card design and saves them as PNGs, then quits.</summary>
public static class ArtCheck
{
	public static async Task Run(Node host, string folder)
	{
		DirAccess.MakeDirRecursiveAbsolute(folder);
		foreach (var front in new[] { CardFront.Classic, CardFront.Ocean })
		{
			var texture = await CardArt.PaintFronts(host, front);
			texture.GetImage().SavePng($"{folder}/front-{front}.png");
		}
		foreach (var back in new[] { CardBack.Crimson, CardBack.Navy, CardBack.Emerald })
		{
			var texture = await CardArt.PaintBack(host, back);
			texture.GetImage().SavePng($"{folder}/back-{back}.png");
		}
		host.GetTree().Quit();
	}
}
