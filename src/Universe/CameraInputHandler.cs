using Godot;

namespace Game.Universe;

/// <summary>
/// Plain input helper for camera control. No nodes, no side-effects beyond per-frame intent flags.
/// Controller calls BeginFrame() once per frame, feeds InputEvents, then reads one-shot flags.
/// </summary>
public class CameraInputHandler
{
    private const float MinSpeedSqEpsilon = 0.000001f;

    // Configuration (owned by controller, mirrored here for input math)
    public MouseButton PanButton { get; set; } = MouseButton.Middle;
    public float MinZoom { get; set; } = 0.1f;
    public float MaxZoom { get; set; } = 8.0f;
    public float ZoomStepFactor { get; set; } = 1.2f;

    // Persistent input state
    public bool IsDragging { get; private set; }
    public Vector2 DragAnchorWorld { get; private set; }

    // One-shot intents (cleared by BeginFrame)
    public bool StartedDragging { get; private set; }
    public bool EndedDragging { get; private set; }
    public bool RequestedZoom { get; private set; }
    public float RequestedZoomTargetScalar { get; private set; } = 1.0f;
    public bool ShouldResetInertia { get; private set; }
    public bool ShouldCancelInertia { get; private set; }

    // Auxiliary input-derived data the controller may use
    public Vector2 PanVelocity { get; private set; } = Vector2.Zero;

    /// <summary>
    /// Initialize internal zoom scalar to match current camera zoom (call from controller _Ready).
    /// </summary>
    public void InitializeFromCurrentZoom(float currentZoom)
    {
        RequestedZoomTargetScalar = currentZoom;
    }

    /// <summary>
    /// Clear one-shot flags at the start of each frame.
    /// </summary>
    public void BeginFrame()
    {
        StartedDragging = false;
        EndedDragging = false;
        RequestedZoom = false;
        ShouldResetInertia = false;
        ShouldCancelInertia = false;
    }

    /// <summary>
    /// Feed an input event. Supply the current mouse world point (from controller) for anchoring.
    /// </summary>
    public void ProcessInput(InputEvent @event, Vector2 currentMouseWorld)
    {
        if (@event is InputEventMouseButton mouseButton)
            ProcessMouseButton(mouseButton, currentMouseWorld);
    }

    private void ProcessMouseButton(InputEventMouseButton mouseButton, Vector2 currentMouseWorld)
    {
        // Pan start/stop
        if (mouseButton.ButtonIndex == PanButton)
        {
            if (mouseButton.Pressed)
            {
                StartDragging(currentMouseWorld);
            }
            else
            {
                StopDragging();
            }
        }

        // Wheel zoom
        if (mouseButton.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            ProcessZoomInput(mouseButton);
        }
    }

    private void StartDragging(Vector2 currentMouseWorld)
    {
        IsDragging = true;
        StartedDragging = true;
        DragAnchorWorld = currentMouseWorld;
        PanVelocity = Vector2.Zero; // reset velocity on grab
        ShouldCancelInertia = true; // cancel momentum immediately
    }

    private void StopDragging()
    {
        IsDragging = false;
        EndedDragging = true;
        // Controller should have kept PanVelocity updated during drag
        ShouldResetInertia = PanVelocity.LengthSquared() > MinSpeedSqEpsilon;
    }

    private void ProcessZoomInput(InputEventMouseButton mouseButton)
    {
        float current = RequestedZoomTargetScalar;
        current = mouseButton.ButtonIndex == MouseButton.WheelUp
            ? current * ZoomStepFactor
            : current / ZoomStepFactor;

        RequestedZoomTargetScalar = Mathf.Clamp(current, MinZoom, MaxZoom);
        RequestedZoom = true;
    }

    /// <summary>
    /// Controller updates the latest pan velocity (world-units/sec) while dragging.
    /// </summary>
    public void UpdatePanVelocity(Vector2 newVelocity)
    {
        PanVelocity = newVelocity;
    }

    /// <summary>
    /// Hard reset of the handler (rarely needed).
    /// </summary>
    public void Reset()
    {
        IsDragging = false;
        StartedDragging = false;
        EndedDragging = false;
        RequestedZoom = false;
        PanVelocity = Vector2.Zero;
        ShouldResetInertia = false;
        ShouldCancelInertia = false;
    }
}


