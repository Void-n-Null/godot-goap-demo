using Godot;

namespace Game.Data.Components;

/// <summary>
/// Simple sprite-based particle that drifts, scales, and expires after a lifetime.
/// </summary>
public class SpriteParticleComponent : IActiveComponent
{
	public Entity Entity { get; set; }

	public Vector2 InitialVelocity { get; set; } = new(0f, -80f);
	public Vector2 Acceleration { get; set; } = new(0f, -20f);
	public float AngularVelocity { get; set; } = 0f;
	public float Lifetime { get; set; } = 1.2f;
	public float StartScale { get; set; } = 0.4f;
	public float EndScale { get; set; } = 0.2f;

	private TransformComponent2D _transform;
	private Vector2 _velocity;
	private float _elapsed;

	public void OnPostAttached()
	{
		_transform = Entity.GetComponent<TransformComponent2D>();
		_velocity = InitialVelocity;

		if (_transform != null)
		{
			_transform.Scale = new Vector2(StartScale, StartScale);
		}
	}

	public void SetVelocity(Vector2 velocity)
	{
		InitialVelocity = velocity;
		_velocity = velocity;
	}

	public void Update(double delta)
	{
		if (_transform == null)
			return;

		float dt = (float)delta;

		_elapsed += dt;
		if (_elapsed >= Lifetime)
		{
			Entity.Destroy();
			return;
		}

		_velocity += Acceleration * dt;
		_transform.Position += _velocity * dt;

		if (!Mathf.IsZeroApprox(AngularVelocity))
		{
			_transform.Rotation += AngularVelocity * dt;
		}

		if (!Mathf.IsZeroApprox(StartScale - EndScale))
		{
			float t = Mathf.Clamp(_elapsed / Lifetime, 0f, 1f);
			float scale = Mathf.Lerp(StartScale, EndScale, t);
			_transform.Scale = new Vector2(scale, scale);
		}
	}
}

