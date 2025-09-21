using Godot;

namespace Game.Universe;

/// <summary>
/// Aggregates transient camera state to reduce boolean/field sprawl in the controller.
/// Owns zoom state, cached anchors, last input timing, and pan velocity.
/// </summary>
public readonly struct CameraState
{
    // Zoom state
    public bool IsZooming { get; init; }
    public float TargetZoomScalar { get; init; }
    public float LastZoomScalar { get; init; }

    // Timing
    public float LastDelta { get; init; }

    // Cached anchors (for cursor-anchored zoom correction)
    public Vector2 LastAnchorScreen { get; init; }
    public Vector2 LastAnchorWorld { get; init; }

    // Mouse tracking for drag panning velocity
    public Vector2 LastMouseScreen { get; init; }
    public bool LastMouseValid { get; init; }

    // Camera motion
    public Vector2 PanVelocity { get; init; } = Vector2.Zero; // world-units/sec

    public CameraState(bool isZooming, float targetZoomScalar, float lastZoomScalar, float lastDelta,
        Vector2 lastAnchorScreen, Vector2 lastAnchorWorld, Vector2 lastMouseScreen, bool lastMouseValid,
        Vector2 panVelocity)
    {
        IsZooming = isZooming;
        TargetZoomScalar = targetZoomScalar;
        LastZoomScalar = lastZoomScalar;
        LastDelta = lastDelta;
        LastAnchorScreen = lastAnchorScreen;
        LastAnchorWorld = lastAnchorWorld;
        LastMouseScreen = lastMouseScreen;
        LastMouseValid = lastMouseValid;
        PanVelocity = panVelocity;
    }
}


