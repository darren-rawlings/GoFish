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
		// Seen palm-on, as a player sees their own hands: the left hand's thumb is on the left.
		(bool Right, HandPose Pose, Vector3 At)[] hands =
		{
			(false, HandPose.Hold, new Vector3(-1.1f, 1.6f, 0)),
			(true, HandPose.Relaxed, new Vector3(1.1f, 1.6f, 0)),
			(false, HandPose.Pinch, new Vector3(-1.1f, -1.6f, 0)),
			(true, HandPose.Pinch, new Vector3(1.1f, -1.6f, 0)),
		};
		foreach (var (right, pose, at) in hands)
		{
			// The pinches are turned side-on, the way they meet a card.
			var hand = new GlassHand(right, tint, pose) { Position = at, Rotation = pose == HandPose.Pinch ? new Vector3(0, right ? 1.25f : -1.25f, 0) : Vector3.Zero };
			host.AddChild(hand);
		}
		if (looks != null)
		{
			var right = new Vector3(1.1f, -1.6f, 0);
			var card = new Card3D(new Card(12, Suit.Hearts), looks);
			host.AddChild(card);
			// The card hangs from the right hand's pinch, top edge between thumb and finger.
			var turn = new Basis(Vector3.Up, 1.25f);
			card.Position = right + turn * HandShape.PinchPoint(true) + new Vector3(0, -Card3D.Height * 0.42f, 0);
			card.Rotation = new Vector3(0, 1.25f - Mathf.Pi / 2, 0);
			return;
		}
	}
}
