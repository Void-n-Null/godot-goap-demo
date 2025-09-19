using Godot;
using System;

namespace Game.Universe;

/// <summary>
/// Camera2D controller that provides pixel-perfect drag panning and cursor-anchored smooth zoom.
/// The world point under the cursor remains under the cursor while panning or zooming.
/// </summary>
public partial class CameraController2D : Camera2D
{
    [Export] public MouseButton PanButton = MouseButton.Middle;
    [Export] public float MinZoom = 0.1f;
    [Export] public float MaxZoom = 8.0f;
    [Export] public float ZoomStepFactor = 1.2f; // multiplicative per wheel notch
    [Export] public float ZoomLerpSpeed = 12.0f; // higher is snappier
    [Export] public float PanFriction = 6.0f; // higher slows quicker (per second)
    [Export] public float MaxPanSpeed = 50000.0f; // clamp world-units/sec
    [Export] public float InertiaFrequency = 6.0f; // 1/s, critically-damped momentum feel
    [Export] public float InertiaTravelSeconds = 0.35f; // lookahead distance = v * t on release
    [Export] public float InertiaStopDistance = 0.5f; // stop when within this distance (world units)
    [Export] public float InertiaStopSpeed = 5.0f; // stop when speed below (world-units/sec)

    private bool _isDragging = false;
    private Vector2 _dragAnchorWorld;
    private Vector2 _panVelocity = Vector2.Zero; // world-units/sec
    private Vector2 _lastMouseScreen;
    private bool _lastMouseValid = false;

    private bool _isZooming = false;
    private float _targetZoomScalar;
    private float _lastDelta = 1.0f;
    private float _lastZoomScalar = -1.0f;
    private Vector2 _lastAnchorScreen;
    private Vector2 _lastAnchorWorld;
    private bool _useFixedZoomAnchorDuringDrag = false;
    private Vector2 _zoomAnchorWorld;
    private Vector2 _zoomAnchorScreen;

    private bool _inertiaActive = false;
    private Vector2 _inertiaTarget = Vector2.Zero;

    public override void _Ready()
    {
        base._Ready();
        // Ensure uniform zoom
        if (!Mathf.IsEqualApprox(Zoom.X, Zoom.Y))
            Zoom = new Vector2(Zoom.X, Zoom.X);

        _targetZoomScalar = Zoom.X;
        _lastZoomScalar = Zoom.X;
        _lastAnchorScreen = GetViewport().GetMousePosition();
        _lastAnchorWorld = ScreenToWorld(_lastAnchorScreen);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Start/stop drag panning
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == PanButton)
            {
                if (mouseButton.Pressed)
                {
                    _isDragging = true;
                    _dragAnchorWorld = GetGlobalMousePosition();
                    _lastMouseScreen = GetViewport().GetMousePosition();
                    _lastMouseValid = true;
                    _panVelocity = Vector2.Zero; // reset inertia on grab
                    _inertiaActive = false; // cancel momentum when grabbing
                }
                else
                {
                    _isDragging = false;
                    _lastMouseValid = false;
                    // On release, compute a lookahead target for critically-damped momentum
                    if (_panVelocity.LengthSquared() > 0.000001f && InertiaTravelSeconds > 0.0f)
                    {
                        _inertiaTarget = GlobalPosition + _panVelocity * InertiaTravelSeconds;
                        _inertiaActive = true;
                    }
                }
            }

            // Wheel zoom with cursor anchor
            if (mouseButton.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var current = _targetZoomScalar;
                if (mouseButton.ButtonIndex == MouseButton.WheelUp)
                    current *= ZoomStepFactor; // zoom in
                else
                    current /= ZoomStepFactor; // zoom out

                _targetZoomScalar = Mathf.Clamp(current, MinZoom, MaxZoom);
                _isZooming = true;

                // If we're dragging, capture a fixed anchor (event-time)
                if (_isDragging)
                {
                    _useFixedZoomAnchorDuringDrag = true;
                    // When dragging, zoom should keep the original drag anchor under the current cursor
                    _zoomAnchorWorld = _dragAnchorWorld;
                }
                else
                {
                    _useFixedZoomAnchorDuringDrag = false;
                }
            }
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        _lastDelta = (float)delta;
        var currentMouseScreen = GetViewport().GetMousePosition();

        // If zoom changed externally (not via our smooth step), keep the last cursor world point fixed
        if (!_isZooming && !Mathf.IsEqualApprox(Zoom.X, _lastZoomScalar))
        {
            // Correct position so the cursor-anchored world point stays fixed
            var anchorAfter = GetGlobalMousePosition();
            GlobalPosition += _lastAnchorWorld - anchorAfter;
        }

        // Smooth zoom towards target; during drag use fixed event-time anchor, otherwise live cursor anchor
        if (_isZooming)
        {
            // Live anchor world point before zoom (used when not using fixed drag anchor)
            var anchorWorldBefore = GetGlobalMousePosition();

            float current = Zoom.X;
            float next = Mathf.Lerp(current, _targetZoomScalar, 1.0f - Mathf.Exp(-ZoomLerpSpeed * (float)delta));

            // Snap when close enough
            if (Mathf.Abs(next - _targetZoomScalar) < 0.0005f)
            {
                next = _targetZoomScalar;
                _isZooming = false;
            }

            Zoom = new Vector2(next, next);
            if (_isDragging && _useFixedZoomAnchorDuringDrag)
            {
                // During drag, keep the original drag anchor under the live cursor position
                // Calculate where the camera should be so that _zoomAnchorWorld appears at currentMouseScreen
                var currentMouseWorld = GetGlobalMousePosition();
                var offset = _zoomAnchorWorld - currentMouseWorld;
                GlobalPosition += offset;
            }
            else
            {
                // Correct any drift so the live world point under the cursor remains fixed
                var anchorWorldAfter = GetGlobalMousePosition();
                GlobalPosition += anchorWorldBefore - anchorWorldAfter;
            }
        }

        // Drag panning: keep the world point grabbed at press under the cursor exactly
        if (_isDragging)
        {
            // Track mouse movement for velocity calculation
            var mouseScreen = GetViewport().GetMousePosition();
            if (_lastMouseValid)
            {
                // For velocity, use world-space movement independent of current zoom
                var currentMouseWorld = GetGlobalMousePosition();
                var lastMouseWorld = ScreenToWorld(_lastMouseScreen);
                var worldDelta = currentMouseWorld - lastMouseWorld;
                var instantaneous = worldDelta / Math.Max(_lastDelta, 0.0001f);
                instantaneous = instantaneous.Length() > MaxPanSpeed
                    ? instantaneous.Normalized() * MaxPanSpeed
                    : instantaneous;
                _panVelocity = instantaneous;
            }
            _lastMouseScreen = mouseScreen;
            _lastMouseValid = true;

            // Only directly move camera if not in zoom correction path
            if (!(_isZooming && _useFixedZoomAnchorDuringDrag))
            {
                var currentMouseWorld = GetGlobalMousePosition();
                var worldDelta = _dragAnchorWorld - currentMouseWorld;
                if (worldDelta.LengthSquared() > 0.0f)
                {
                    GlobalPosition += worldDelta;
                    var instantaneous = worldDelta / Math.Max(_lastDelta, 0.0001f);
                    instantaneous = instantaneous.Length() > MaxPanSpeed
                        ? instantaneous.Normalized() * MaxPanSpeed
                        : instantaneous;
                    _panVelocity = instantaneous;
                }
            }
        }

        // Apply momentum when not dragging
        if (!_isDragging)
        {
            // Critically-damped spring toward a fixed lookahead target for natural ease-out
            if (_inertiaActive && InertiaFrequency > 0.0f && InertiaTravelSeconds > 0.0f)
            {
                float dt = (float)delta;
                float omega = InertiaFrequency; // 1/s
                float k = omega * omega;        // spring constant
                float c = 2.0f * omega;         // critical damping

                var toTarget = _inertiaTarget - GlobalPosition;
                _panVelocity += toTarget * k * dt - _panVelocity * c * dt;

                if (_panVelocity.Length() > MaxPanSpeed)
                    _panVelocity = _panVelocity.Normalized() * MaxPanSpeed;

                GlobalPosition += _panVelocity * dt;

                if (toTarget.Length() <= InertiaStopDistance && _panVelocity.Length() <= InertiaStopSpeed)
                {
                    GlobalPosition = _inertiaTarget;
                    _panVelocity = Vector2.Zero;
                    _inertiaActive = false;
                }
            }
            // Fallback to exponential friction if spring momentum disabled
            else if (_panVelocity.LengthSquared() > 0.000001f)
            {
                GlobalPosition += _panVelocity * (float)delta;
                float decay = Mathf.Exp(-PanFriction * (float)delta);
                _panVelocity *= decay;
                if (_panVelocity.LengthSquared() < 0.000001f)
                    _panVelocity = Vector2.Zero;
            }
        }

        // Update last-zoom and last anchor cache
        _lastZoomScalar = Zoom.X;
        _lastAnchorScreen = currentMouseScreen;
        _lastAnchorWorld = ScreenToWorld(currentMouseScreen);
    }

    private Vector2 ScreenToWorld(Vector2 screen)
    {
        // world = (screen - half_view) * zoom + camera_position
        return ScreenOffset(screen) * Zoom + GlobalPosition;
    }

    private Vector2 ScreenOffset(Vector2 screen)
    {
        var half = GetViewport().GetVisibleRect().Size * 0.5f;
        return screen - half;
    }
}


