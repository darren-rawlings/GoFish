using Godot;
using Renegade.Cards;

namespace GoFish.Game;

/// <summary>Developer check (--hands): the glass hands up close, relaxed and holding, left and right, over a card.</summary>
public static class HandCheck
{
	public static void Build(Node3D host, CardLooks? looks)
	{
		var env = new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color("2b3a2c"),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color(0.6f, 0.6f, 0.6f),
			},
		};
		host.AddChild(env);
		var sun = new DirectionalLight3D { RotationDegrees = new Vector3(-40, 30, 0), LightEnergy = 1.2f };
		host.AddChild(sun);
		var camera = new Camera3D { Position = new Vector3(0, 0, 7.5f), Fov = 40, KeepAspect = Camera3D.KeepAspectEnum.Width };
		host.AddChild(camera);
		var tint = new Color(0.8f, 0.92f, 1f);
		(bool Right, float Grip, Vector3 At)[] hands =
		{
			(false, 0f, new Vector3(-1.1f, 1.6f, 0)),
			(true, 0f, new Vector3(1.1f, 1.6f, 0)),
			(false, 1f, new Vector3(-1.1f, -1.6f, 0)),
			(true, 1f, new Vector3(1.1f, -1.6f, 0)),
		};
		foreach (var (right, grip, at) in hands)
		{
			var hand = new GlassHand(right, tint) { Grip = grip, Position = at };
			host.AddChild(hand);
		}
		if (looks != null)
		{
			var card = new Card3D(new Card(12, Suit.Hearts), looks) { Position = new Vector3(-0.2f, -1.2f, -0.12f), Rotation = new Vector3(0, 0, -0.3f) };
			host.AddChild(card);
		}
	}
}
