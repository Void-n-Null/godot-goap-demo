using Godot;

namespace Game.Universe;

/// <summary>
/// Advanced, modular Camera2D controller built feature-by-feature with explicit requirements.
/// Each feature is implemented in isolation and verified against a compatibility matrix.
/// </summary>
public partial class CameraControllerAdvanced : Camera2D
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
    private bool _isDragging = false;
    private Vector2 _dragAnchorWorld;
    private Vector2 _dragStartMouseScreen;
    private bool _passedDeadZone = false;
    private Vector2 _lastDragInstantVelocity = Vector2.Zero;

    private bool _isZooming = false;
    private float _targetZoomScalar = 1.0f;

    // When zooming during a drag, fix the zoom anchor to the world point from the first wheel event
    private bool _useFixedZoomAnchorDuringDrag = false;
    private Vector2 _zoomAnchorWorld;

    // Inertia state (velocity decays exponentially)
    private bool _inertiaActive = false;
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
        if (@event is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == PanButton)
            {
                if (mouse.Pressed)
                {
                    _isDragging = true;
                    _dragAnchorWorld = GetGlobalMousePosition();
                    _dragStartMouseScreen = GetViewport().GetMousePosition();
                    _passedDeadZone = false;
                    _lastDragInstantVelocity = Vector2.Zero;
                }
                else
                {
                    _isDragging = false;
                    _useFixedZoomAnchorDuringDrag = false; // clear fixed anchor on drag end

                    // Start inertia if we have velocity, dead zone passed
                    float speed = _lastDragInstantVelocity.Length();
                    if (_passedDeadZone && speed >= InertiaMinSpeed)
                    {
                        _inertiaActive = true;
                        _inertiaVelocity = ClampSpeed(_lastDragInstantVelocity, InertiaMaxSpeed);
                    }
                    else
                    {
                        _inertiaActive = false;
                        _inertiaVelocity = Vector2.Zero;
                    }
                }
            }

            // Zoom: WheelUp zooms in (decrease scalar), WheelDown zooms out (increase scalar)
            if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                float next = _targetZoomScalar;
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
        }

        // Allow ESC to cancel an active drag (future: bind to a custom action)
        if (@event is InputEventKey key && !key.Echo && key.Pressed && key.Keycode == Key.Escape)
        {
            _isDragging = false;
            _useFixedZoomAnchorDuringDrag = false; // clear fixed anchor on cancel
            _passedDeadZone = false;
            _lastDragInstantVelocity = Vector2.Zero;
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        float dt = (float)delta;
        if (dt <= 0) return;

        // 1) Pixel-perfect drag panning: translate first and compute instantaneous velocity
        if (_isDragging)
        {
            var currentMouseWorld = GetGlobalMousePosition();
            var offset = _dragAnchorWorld - currentMouseWorld;
            if (offset.LengthSquared() > 0.0f)
            {
                GlobalPosition += offset;
                var v = offset / Mathf.Max(dt, MinDelta);
                _lastDragInstantVelocity = ClampSpeed(v, InertiaMaxSpeed);
            }
            else
            {
                _lastDragInstantVelocity = Vector2.Zero;
            }

            // Update dead zone state by screen distance from drag start
            var currentMouseScreen = GetViewport().GetMousePosition();
            if (!_passedDeadZone)
            {
                if ((currentMouseScreen - _dragStartMouseScreen).Length() >= InertiaDeadZonePixels)
                    _passedDeadZone = true;
            }
        }

        // 2) Smooth cursor-anchored zoom (use fixed anchor while dragging)
        if (_isZooming)
        {
            StepZoom(dt);
        }

        // 3) Inertia: only when not dragging and not zooming (zoom overrides inertia)
        if (!_isDragging && !_isZooming && _inertiaActive)
        {
            StepInertia(dt);
        }
    }

    private void StepZoom(float dt)
    {
        float current = Zoom.X;
        float next = Mathf.Lerp(current, _targetZoomScalar, 1.0f - Mathf.Exp(-ZoomLerpSpeed * dt));
        bool reached = Mathf.Abs(next - _targetZoomScalar) < SnapEpsilon;
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
        {
            _isZooming = false;
            // Keep fixed anchor until drag ends; it will be cleared on drag release
        }
    }

    private void StepInertia(float dt)
    {
        if (!_inertiaActive)
            return;

        // Apply velocity and decay exponentially
        GlobalPosition += _inertiaVelocity * dt;
        float decay = Mathf.Exp(-InertiaFriction * dt);
        _inertiaVelocity *= decay;

        // Clamp and floor
        _inertiaVelocity = ClampSpeed(_inertiaVelocity, InertiaMaxSpeed);
        if (_inertiaVelocity.Length() < InertiaMinSpeed)
        {
            _inertiaVelocity = Vector2.Zero;
            _inertiaActive = false;
        }
    }

    private Vector2 ClampSpeed(Vector2 v, float max)
    {
        float len = v.Length();
        if (len <= max) return v;
        return v / len * max;
    }
}
