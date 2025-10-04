using Game.Utils;
using Godot;

namespace Game.Universe;

/// <summary>
/// Advanced, modular Camera2D controller built feature-by-feature with explicit requirements.
/// Each feature is implemented in isolation and verified against a compatibility matrix.
/// </summary>
public partial class CameraController2D : Camera2D
{
	// Configuration (subject to refinement per feature)
	[Export] public MouseButton PanButton = MouseButton.Middle;

	// Zoom config
	[Export] public float MinZoom = 0.1f;
	[Export] public float MaxZoom = 8.0f;
	[Export] public float ZoomStepFactor = 1.2f; // multiplicative per wheel notch
	[Export] public float ZoomLerpSpeed = 12.0f; // exponential smoothing speed

	// Inertia config (exponential friction decay; v *= exp(-k * dt))
	[Export] public float InertiaFriction = 6.0f; // 1/s
	[Export] public float InertiaDeadZonePixels = 20.0f; // minimum cursor travel from drag start before inertia is allowed
	[Export] public float InertiaMinSpeed = 10.0f; // world-units/sec below which inertia is ignored
	[Export] public float InertiaMaxSpeed = 20000.0f; // clamp world-units/sec to avoid spikes

	private const float MinDelta = 1e-4f;
	private const float SnapEpsilon = 0.0005f;

	// Internal state
	private bool _isDragging;
	private Vector2 _dragAnchorWorld;
	private Vector2 _dragStartMouseScreen;
	private bool _passedDeadZone;
	private Vector2 _lastDragInstantVelocity = Vector2.Zero;

	private bool _isZooming;
	private float _targetZoomScalar = 1.0f;
	private bool _shouldApplyInertia => !_isDragging && !_isZooming && _inertiaActive;

	// When zooming during a drag, fix the zoom anchor to the world point from the first wheel event
	private bool _useFixedZoomAnchorDuringDrag;
	private Vector2 _zoomAnchorWorld;

	// Inertia state (velocity decays exponentially)
	private bool _inertiaActive;
	private Vector2 _inertiaVelocity = Vector2.Zero;

	public override void _Ready()
	{
		base._Ready();
		// Ensure uniform zoom scaling from the start
		if (!Mathf.IsEqualApprox(Zoom.X, Zoom.Y))
			Zoom = new Vector2(Zoom.X, Zoom.X);
		_targetZoomScalar = Zoom.X;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton mouse:
			{
				if (mouse.ButtonIndex == PanButton)
					if (mouse.Pressed) 
						HandleDragStart(); 
					else 
						HandleDragEnd();

				// Zoom: WheelUp zooms in (decrease scalar), WheelDown zooms out (increase scalar)
				if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
					HandleZooming(mouse);   
				break;
			}
			// Allow ESC to cancel an active drag (future: bind to a custom action)
			case InputEventKey { Echo: false, Pressed: true, Keycode: Key.Escape }:
				HandleHardDragEnd();
				break;
		}
	}

	private void HandleZooming(InputEventMouseButton mouse)
	{
		var next = _targetZoomScalar;
		next = mouse.ButtonIndex == MouseButton.WheelDown
			? next / ZoomStepFactor
			: next * ZoomStepFactor;
		_targetZoomScalar = Mathf.Clamp(next, MinZoom, MaxZoom);
		_isZooming = true;

		// Zoom during inertia overrides inertia
		if (_inertiaActive && !_isDragging)
		{
			_inertiaActive = false;
			_inertiaVelocity = Vector2.Zero;
		}

		// If we are dragging when zoom starts, capture a fixed anchor until drag ends
		if (_isDragging && !_useFixedZoomAnchorDuringDrag)
		{
			_useFixedZoomAnchorDuringDrag = true;
			_zoomAnchorWorld = GetGlobalMousePosition();
		}
	}

	private void HandleDragStart(){
		_isDragging = true;
		_dragAnchorWorld = GetGlobalMousePosition();
		_dragStartMouseScreen = GetViewport().GetMousePosition();
		_passedDeadZone = false;
		_lastDragInstantVelocity = Vector2.Zero;
	}

	private void HandleDragEnd(){
		_isDragging = false;
		_useFixedZoomAnchorDuringDrag = false; // clear fixed anchor on drag end

		// Start inertia if we have velocity, dead zone passed
		var speed = _lastDragInstantVelocity.Length();
		if (_passedDeadZone && speed >= InertiaMinSpeed)
		{
			_inertiaActive = true;
			_inertiaVelocity = _lastDragInstantVelocity.ClampMaxLength(InertiaMaxSpeed);
		}
		else
		{
			_inertiaActive = false;
			_inertiaVelocity = Vector2.Zero;
		}
	}

	private void HandleHardDragEnd(){
		_isDragging = false;
		_useFixedZoomAnchorDuringDrag = false;
		_inertiaActive = false;
		_inertiaVelocity = Vector2.Zero;
		_passedDeadZone = false;
		_lastDragInstantVelocity = Vector2.Zero;
	}

	public override void _Process(double delta)
	{
		var frameGlobalMousePosition = GetGlobalMousePosition();
		var frameScreenMousePosition = GetViewport().GetMousePosition();
		base._Process(delta);
		var dt = (float)delta;
		if (dt <= 0) return;
		if (_isDragging) // 1) Pixel-perfect drag panning: translate first and compute instantaneous velocity
			StepDrag(dt, frameGlobalMousePosition, frameScreenMousePosition);
		if (_isZooming) // 2) Smooth cursor-anchored zoom (use fixed anchor while dragging)
			StepZoom(dt, frameGlobalMousePosition);
		if (_shouldApplyInertia)  // 3) Inertia: only when not dragging and not zooming (zoom overrides inertia)
			StepInertia(dt);
	}

	private void StepDrag(float dt, Vector2 frameGlobalMousePosition, Vector2 frameScreenMousePosition)
	{
		var offset = _dragAnchorWorld - frameGlobalMousePosition;
		if (offset.LengthSquared() > 0.0f)
		{
			GlobalPosition += offset;
			var v = offset / Mathf.Max(dt, MinDelta);
			_lastDragInstantVelocity = v.ClampMaxLength(InertiaMaxSpeed);
		}
		else
		{
			_lastDragInstantVelocity = Vector2.Zero;
		}

		if (_passedDeadZone) return;
		if ((frameScreenMousePosition - _dragStartMouseScreen).Length() >= InertiaDeadZonePixels)
			_passedDeadZone = true;
	}

	private void StepZoom(float dt, Vector2 frameGlobalMousePosition)
	{
		var current = Zoom.X;
		var next = Mathf.Lerp(current, _targetZoomScalar, 1.0f - Mathf.Exp(-ZoomLerpSpeed * dt));
		var reached = Mathf.Abs(next - _targetZoomScalar) < SnapEpsilon;
		if (reached)
			next = _targetZoomScalar;

		// Compute correction
		Vector2 correction;
		if (_isDragging && _useFixedZoomAnchorDuringDrag)
		{
			// Keep the initially captured world point under the cursor while dragging
			var before = _zoomAnchorWorld;
			Zoom = new Vector2(next, next);
			var after = GetGlobalMousePosition();
			correction = before - after;
		}
		else
		{
			var before = GetGlobalMousePosition();
			Zoom = new Vector2(next, next);
			var after = GetGlobalMousePosition();
			correction = before - after;
		}

		GlobalPosition += correction;

		if (reached)
			// Keep fixed anchor until drag ends; it will be cleared on drag release
			_isZooming = false;
	}

	private void StepInertia(float dt)
	{
		if (!_inertiaActive)
			return;

		// Apply velocity and decay exponentially
		GlobalPosition += _inertiaVelocity * dt;
		var decay = Mathf.Exp(-InertiaFriction * dt);
		_inertiaVelocity *= decay;

		// Clamp and stop under threshold
		_inertiaVelocity = _inertiaVelocity.ClampMaxLength(InertiaMaxSpeed);
		if (!(_inertiaVelocity.Length() < InertiaMinSpeed)) return;
		
		_inertiaVelocity = Vector2.Zero;
		_inertiaActive = false;
	}
}
