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

    private bool _isDragging = false;
    private Vector2 _dragAnchorWorld;
    private Vector2 _panVelocity = Vector2.Zero; // world-units/sec

    private bool _isZooming = false;
    private float _targetZoomScalar;
    private Vector2 _zoomAnchorWorld;
    private Vector2 _zoomAnchorScreen;
    private float _lastDelta = 1.0f;

    public override void _Ready()
    {
        base._Ready();
        // Ensure uniform zoom
        if (!Mathf.IsEqualApprox(Zoom.X, Zoom.Y))
            Zoom = new Vector2(Zoom.X, Zoom.X);

        _targetZoomScalar = Zoom.X;
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
                    _panVelocity = Vector2.Zero; // reset inertia on grab
                }
                else
                {
                    _isDragging = false;
                }
            }

            // Wheel zoom with cursor anchor
            if (mouseButton.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var current = _targetZoomScalar;
                if (mouseButton.ButtonIndex == MouseButton.WheelUp)
                    current /= ZoomStepFactor; // zoom in
                else
                    current *= ZoomStepFactor; // zoom out

                _targetZoomScalar = Mathf.Clamp(current, MinZoom, MaxZoom);

                _zoomAnchorScreen = GetViewport().GetMousePosition();
                _zoomAnchorWorld = ScreenToWorld(_zoomAnchorScreen);
                _isZooming = true;
            }
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        _lastDelta = (float)delta;

        // Drag panning: keep the world point grabbed at press under the cursor exactly
        if (_isDragging)
        {
            var currentMouseWorld = GetGlobalMousePosition();
            var worldDelta = _dragAnchorWorld - currentMouseWorld;
            if (worldDelta.LengthSquared() > 0.0f)
            {
                GlobalPosition += worldDelta;
                // estimate velocity in world units/sec for inertia
                var instantaneous = worldDelta / Math.Max(_lastDelta, 0.0001f);
                instantaneous = instantaneous.Length() > MaxPanSpeed
                    ? instantaneous.Normalized() * MaxPanSpeed
                    : instantaneous;
                _panVelocity = instantaneous;
            }
        }

        // Smooth zoom towards target while keeping the anchor fixed under the cursor
        if (_isZooming)
        {
            float current = Zoom.X;
            float next = Mathf.Lerp(current, _targetZoomScalar, 1.0f - Mathf.Exp(-ZoomLerpSpeed * (float)delta));

            // Snap when close enough
            if (Mathf.Abs(next - _targetZoomScalar) < 0.0005f)
            {
                next = _targetZoomScalar;
                _isZooming = false;
            }

            Zoom = new Vector2(next, next);
            GlobalPosition = _zoomAnchorWorld - ScreenOffset(_zoomAnchorScreen) * Zoom;
        }

        // Apply inertia when not dragging
        if (!_isDragging && _panVelocity.LengthSquared() > 0.000001f)
        {
            GlobalPosition += _panVelocity * (float)delta;
            // exponential decay towards zero
            float decay = Mathf.Exp(-PanFriction * (float)delta);
            _panVelocity *= decay;
            // stop when very small to avoid drift
            if (_panVelocity.LengthSquared() < 0.000001f)
                _panVelocity = Vector2.Zero;
        }
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


