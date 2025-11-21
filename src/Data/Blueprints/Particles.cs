using Game.Data;
using Game.Data.Components;
using Godot;

namespace Game.Data.Blueprints;

public static class Particles
{
	public static readonly EntityBlueprint Heart = Primordial.EmbodiedEntity.Derive(
		name: "HeartParticle",
		addComponents: () => [
			new SpriteParticleComponent
			{
				InitialVelocity = new Vector2(0f, -90f),
				Acceleration = new Vector2(0f, -30f),
				Lifetime = 1.4f,
				StartScale = 0.45f,
				EndScale = 0.25f
			}
		],
		addMutators: [
			EntityBlueprint.Mutate<VisualComponent>((visual) =>
			{
				visual.PendingSpritePath = "res://textures/Heart.png";
				visual.ScaleMultiplier = Vector2.One;
				visual.ZBiasOverride = 10000f;
			})
		]
	);
}

